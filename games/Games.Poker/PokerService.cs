// ─────────────────────────────────────────────────────────────────────────────
// PokerService — application service for /poker.
//
// Port of src/CasinoShiz.Core/Services/Poker/Application/PokerService.cs.
// Differences vs. the monolith:
//   • Dapper stores (IPokerTableStore / IPokerSeatStore) replace EF Core.
//   • IEconomicsService is userId-based (no UserState entity round-trips).
//   • Analytics goes through IAnalyticsService instead of ClickHouseReporter.
//   • Domain events (PokerHandStarted / PokerHandEnded) are published on the
//     IDomainEventBus for cross-module subscribers.
//
// Concurrency: per-table SemaphoreSlim gates keyed by invite code. Table-creating
// operations gate by "u:{userId}" until a code is assigned.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Games.Poker.Domain;
using static Games.Poker.PokerResultHelpers;

namespace Games.Poker;

public interface IPokerService
{
    Task<(TableSnapshot? Snapshot, PokerSeat? MySeat)> FindMyTableAsync(
        long userId, long currentChatId, CancellationToken ct);
    Task<CreateResult> CreateTableAsync(long userId, string displayName, long chatId, CancellationToken ct);
    Task<JoinResult> JoinTableAsync(long userId, string displayName, long chatId, string code, CancellationToken ct);
    Task<StartResult> StartHandAsync(long userId, long currentChatId, CancellationToken ct);
    Task<ActionResult> ApplyPlayerActionAsync(
        long userId, long currentChatId, string verb, int amount, CancellationToken ct);
    Task<ActionResult?> RunAutoActionAsync(string inviteCode, CancellationToken ct);
    Task<LeaveResult> LeaveTableAsync(long userId, long currentChatId, CancellationToken ct);
    Task SetTableStateMessageIdAsync(string inviteCode, int messageId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct);
}

public sealed partial class PokerService(
    IPokerTableStore tables,
    IPokerSeatStore seats,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDomainEventBus events,
    IRuntimeTuningAccessor tuning,
    ILogger<PokerService> logger) : IPokerService
{
    private static readonly ConcurrentDictionary<string, Gate> _gates = new();

    private static SemaphoreSlim GetGate(string key)
    {
        var g = _gates.GetOrAdd(key, _ => new Gate());
        Volatile.Write(ref g.LastUsedTick, Environment.TickCount64);
        return g.Semaphore;
    }

    internal static void PruneGates(long idleMs)
    {
        var cutoff = Environment.TickCount64 - idleMs;
        foreach (var (key, g) in _gates)
            if (Volatile.Read(ref g.LastUsedTick) < cutoff && g.Semaphore.CurrentCount == 1)
                _gates.TryRemove(key, out _);
    }

    private sealed class Gate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public long LastUsedTick = Environment.TickCount64;
    }

    private PokerOptions CurrentOptions() => tuning.GetSection<PokerOptions>(PokerOptions.SectionName);

    public async Task<(TableSnapshot? Snapshot, PokerSeat? MySeat)> FindMyTableAsync(
        long userId, long currentChatId, CancellationToken ct)
    {
        var table = await tables.FindOpenByChatAsync(currentChatId, ct);
        if (table == null) return (null, null);
        var seat = await seats.FindByUserInTableAsync(userId, table.InviteCode, ct);
        if (seat == null) return (null, null);
        var list = await seats.ListByTableAsync(table.InviteCode, ct);
        return (new TableSnapshot(table, list), list.First(s => s.UserId == userId));
    }

    public async Task<CreateResult> CreateTableAsync(long userId, string displayName, long chatId, CancellationToken ct)
    {
        var lockKey = $"chat:{chatId}";
        var gate = GetGate(lockKey);
        await gate.WaitAsync(ct);
        try
        {
            var opts = CurrentOptions();
            var buyIn = opts.BuyIn;
            await economics.EnsureUserAsync(userId, chatId, displayName, ct);
            var existingTable = await tables.FindOpenByChatAsync(chatId, ct);
            if (existingTable != null) return Fail(PokerError.TableAlreadyExists);

            var balance = await economics.GetBalanceAsync(userId, chatId, ct);
            if (balance < buyIn)
            {
                LogPokerCreateNotEnoughCoins(userId, balance);
                return Fail(PokerError.NotEnoughCoins);
            }

            string code = await GenerateUniqueCodeAsync(ct);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var table = new PokerTable
            {
                InviteCode = code,
                ChatId = chatId,
                HostUserId = userId,
                Status = PokerTableStatus.Seating,
                Phase = PokerPhase.None,
                SmallBlind = opts.SmallBlind,
                BigBlind = opts.BigBlind,
                CreatedAt = now,
                LastActionAt = now,
            };
            var seat = new PokerSeat
            {
                InviteCode = code,
                Position = 0,
                UserId = userId,
                DisplayName = displayName,
                Stack = buyIn,
                ChatId = chatId,
                JoinedAt = now,
            };

            if (!await economics.TryDebitAsync(userId, chatId, buyIn, "poker.create", ct))
                return Fail(PokerError.NotEnoughCoins);

            try
            {
                await tables.InsertAsync(table, ct);
                await seats.InsertAsync(seat, ct);
            }
            catch (Exception ex)
            {
                await RefundBuyInAfterCreateFailureAsync(userId, chatId, code, buyIn, ex, ct);
                throw;
            }

            LogPokerCreated(code, userId, buyIn);
            analytics.Track("poker", "create", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["invite_code"] = code,
                ["buy_in"] = buyIn,
            });
            await events.PublishAsync(new PokerTableCreated(code, userId, buyIn, now), ct);

            return new CreateResult(PokerError.None, code, buyIn);
        }
        finally { gate.Release(); }
    }

    public async Task<JoinResult> JoinTableAsync(long userId, string displayName, long chatId, string code, CancellationToken ct)
    {
        code = code.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            var groupTable = await tables.FindOpenByChatAsync(chatId, ct);
            if (groupTable == null) return JoinFail(PokerError.NoTable);
            code = groupTable.InviteCode;
        }

        var gate = GetGate(code);
        await gate.WaitAsync(ct);
        try
        {
            var opts = CurrentOptions();
            var buyIn = opts.BuyIn;
            await economics.EnsureUserAsync(userId, chatId, displayName, ct);
            var table = await tables.FindAsync(code, ct);
            if (table == null || table.Status == PokerTableStatus.Closed) return JoinFail(PokerError.TableNotFound);
            if (table.ChatId != 0 && table.ChatId != chatId) return JoinFail(PokerError.TableNotFound);
            if (table.Status != PokerTableStatus.Seating && table.Status != PokerTableStatus.HandComplete)
                return JoinFail(PokerError.HandInProgress);

            var existingSeat = await seats.FindByUserInTableAsync(userId, table.InviteCode, ct);
            if (existingSeat != null) return JoinFail(PokerError.AlreadySeated);

            var balance = await economics.GetBalanceAsync(userId, chatId, ct);
            if (balance < buyIn) return JoinFail(PokerError.NotEnoughCoins);

            var list = await seats.ListByTableAsync(code, ct);
            if (list.Count >= opts.MaxPlayers) return JoinFail(PokerError.TableFull);

            int position = 0;
            var used = list.Select(s => s.Position).ToHashSet();
            while (used.Contains(position)) position++;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var seat = new PokerSeat
            {
                InviteCode = code,
                Position = position,
                UserId = userId,
                DisplayName = displayName,
                Stack = buyIn,
                ChatId = chatId,
                JoinedAt = now,
            };
            if (!await economics.TryDebitAsync(userId, chatId, buyIn, "poker.join", ct))
                return JoinFail(PokerError.NotEnoughCoins);
            try
            {
                await seats.InsertAsync(seat, ct);
            }
            catch (Exception ex)
            {
                await RefundBuyInAfterJoinFailureAsync(userId, chatId, code, buyIn, ex, ct);
                throw;
            }

            list.Add(seat);
            LogPokerJoined(code, userId, position, list.Count);
            analytics.Track("poker", "join", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["invite_code"] = code,
                ["seat"] = position,
                ["seated"] = list.Count,
                ["buy_in"] = buyIn,
            });
            await events.PublishAsync(new PokerPlayerJoined(code, userId, position, buyIn, now), ct);

            return new JoinResult(PokerError.None, new TableSnapshot(table, list), list.Count, opts.MaxPlayers);
        }
        finally { gate.Release(); }
    }

    public async Task<StartResult> StartHandAsync(long userId, long currentChatId, CancellationToken ct)
    {
        var precheckTable = await tables.FindOpenByChatAsync(currentChatId, ct);
        if (precheckTable == null) return StartFail(PokerError.NoTable);

        var gate = GetGate(precheckTable.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var table = await tables.FindOpenByChatAsync(currentChatId, ct);
            if (table == null) return StartFail(PokerError.NoTable);
            var mySeat = await seats.FindByUserInTableAsync(userId, table.InviteCode, ct);
            if (mySeat == null) return StartFail(PokerError.NoTable);
            if (table.HostUserId != userId) return StartFail(PokerError.NotHost);
            if (table.Status == PokerTableStatus.HandActive) return StartFail(PokerError.HandInProgress);

            var list = await seats.ListByTableAsync(table.InviteCode, ct);
            if (list.Count(s => s.Stack > 0) < 2) return StartFail(PokerError.NeedTwo);

            PokerDomain.StartHand(table, list);
            await tables.UpdateAsync(table, ct);
            foreach (var s in list) await seats.UpdateAsync(s, ct);

            var activeSeats = list.Count(s => s.Status == PokerSeatStatus.Seated || s.Status == PokerSeatStatus.AllIn);
            LogPokerHandStarted(table.InviteCode, table.ButtonSeat, table.CurrentSeat, table.Pot);
            analytics.Track("poker", "hand_start", new Dictionary<string, object?>
            {
                ["invite_code"] = table.InviteCode,
                ["seats"] = activeSeats,
            });
            await events.PublishAsync(
                new PokerHandStarted(table.InviteCode, activeSeats, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                ct);

            return new StartResult(PokerError.None, new TableSnapshot(table, list));
        }
        finally { gate.Release(); }
    }

    public async Task<ActionResult> ApplyPlayerActionAsync(
        long userId, long currentChatId, string verb, int amount, CancellationToken ct)
    {
        var precheckTable = await tables.FindOpenByChatAsync(currentChatId, ct);
        if (precheckTable == null) return ActionFail(PokerError.NoTable);

        var gate = GetGate(precheckTable.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var table = await tables.FindOpenByChatAsync(currentChatId, ct);
            if (table == null || table.Status != PokerTableStatus.HandActive) return ActionFail(PokerError.NotYourTurn);
            var seat = await seats.FindByUserInTableAsync(userId, table.InviteCode, ct);
            if (seat == null) return ActionFail(PokerError.NoTable);
            var list = await seats.ListByTableAsync(table.InviteCode, ct);

            var live = list.First(s => s.UserId == userId);
            if (live.Position != table.CurrentSeat || live.Status != PokerSeatStatus.Seated)
                return ActionFail(PokerError.NotYourTurn);

            var action = PokerAction.FromVerb(verb, amount);
            if (action is null) return ActionFail(PokerError.InvalidAction);

            var validation = PokerDomain.Validate(table, live, action.Value);
            if (validation != ValidationResult.Ok) return ActionFail(MapValidation(validation));

            PokerDomain.Apply(table, live, action.Value);
            live.HasActedThisRound = true;
            table.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            LogPokerAction(table.InviteCode, userId, verb, amount, table.Pot);
            analytics.Track("poker", "action", new Dictionary<string, object?>
            {
                ["invite_code"] = table.InviteCode,
                ["user_id"] = userId,
                ["action"] = verb,
                ["amount"] = amount,
                ["pot"] = table.Pot,
            });

            return await ResolveAfterActionAsync(table, list, ct);
        }
        finally { gate.Release(); }
    }

    public async Task<ActionResult?> RunAutoActionAsync(string inviteCode, CancellationToken ct)
    {
        var gate = GetGate(inviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var table = await tables.FindAsync(inviteCode, ct);
            if (table == null || table.Status != PokerTableStatus.HandActive) return null;

            var list = await seats.ListByTableAsync(inviteCode, ct);
            var current = list.FirstOrDefault(s => s.Position == table.CurrentSeat);
            if (current == null || current.Status != PokerSeatStatus.Seated) return null;

            var decision = PokerDomain.DecideAutoAction(table, current);
            PokerDomain.Apply(table, current, decision);
            current.HasActedThisRound = true;
            table.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var autoKind = decision.Kind == PokerActionKind.Check ? AutoAction.Check : AutoAction.Fold;
            LogPokerAutoAction(inviteCode, current.UserId, autoKind);
            analytics.Track("poker", "auto", new Dictionary<string, object?>
            {
                ["invite_code"] = inviteCode,
                ["user_id"] = current.UserId,
                ["action"] = autoKind.ToString(),
            });

            var result = await ResolveAfterActionAsync(table, list, ct);
            return result with { AutoActorName = current.DisplayName, AutoKind = autoKind };
        }
        finally { gate.Release(); }
    }

    public async Task<LeaveResult> LeaveTableAsync(long userId, long currentChatId, CancellationToken ct)
    {
        var precheckTable = await tables.FindOpenByChatAsync(currentChatId, ct);
        if (precheckTable == null) return LeaveFail(PokerError.NoTable);

        var gate = GetGate(precheckTable.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var table = await tables.FindOpenByChatAsync(currentChatId, ct);
            if (table == null) return LeaveFail(PokerError.NoTable);
            var seat = await seats.FindByUserInTableAsync(userId, table.InviteCode, ct);
            if (seat == null) return LeaveFail(PokerError.NoTable);

            if (seat.Stack > 0)
                await TryRefundSeatStackAsync(seat, "poker.leave", ct);

            if (table != null && table.Status == PokerTableStatus.HandActive && seat.Status == PokerSeatStatus.Seated)
            {
                seat.Status = PokerSeatStatus.Folded;
                seat.Stack = 0;
                await seats.UpdateAsync(seat, ct);

                var allSeats = await seats.ListByTableAsync(table.InviteCode, ct);
                var after = await ResolveAfterActionAsync(table, allSeats, ct);

                await seats.DeleteAsync(seat.InviteCode, seat.Position, ct);

                LogPokerLeaveMidhand(table.InviteCode, userId);
                analytics.Track("poker", "leave", new Dictionary<string, object?>
                {
                    ["invite_code"] = table.InviteCode,
                    ["user_id"] = userId,
                    ["refunded"] = 0,
                    ["mid_hand"] = true,
                });
                var remaining = allSeats.Where(s => s.UserId != userId).ToList();
                return new LeaveResult(PokerError.None, new TableSnapshot(table, remaining), false);
            }

            await seats.DeleteAsync(seat.InviteCode, seat.Position, ct);
            bool closed = false;
            if (table != null)
            {
                var remainingCount = await seats.CountByTableAsync(table.InviteCode, userId, ct);
                if (remainingCount == 0)
                {
                    table.Status = PokerTableStatus.Closed;
                    await tables.UpdateAsync(table, ct);
                    closed = true;
                }
            }

            TableSnapshot? snapshot = null;
            if (table != null && !closed)
            {
                var remaining = await seats.ListByTableAsync(table.InviteCode, ct);
                snapshot = new TableSnapshot(table, remaining);
            }

            LogPokerLeft(table?.InviteCode ?? "-", userId, closed);
            analytics.Track("poker", "leave", new Dictionary<string, object?>
            {
                ["invite_code"] = table?.InviteCode,
                ["user_id"] = userId,
                ["refunded"] = seat.Stack,
                ["table_closed"] = closed,
            });
            return new LeaveResult(PokerError.None, snapshot, closed);
        }
        finally { gate.Release(); }
    }

    public async Task SetTableStateMessageIdAsync(string inviteCode, int messageId, CancellationToken ct)
    {
        var gate = GetGate(inviteCode);
        await gate.WaitAsync(ct);
        try
        {
            await tables.UpsertStateMessageAsync(inviteCode, messageId, ct);
        }
        finally { gate.Release(); }
    }

    public Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct) =>
        tables.ListStuckCodesAsync(cutoffMs, ct);

    private async Task RefreshSeatChatAsync(PokerSeat seat, long currentChatId, CancellationToken ct)
    {
        if (seat.ChatId == currentChatId) return;

        seat.ChatId = currentChatId;
        seat.StateMessageId = null;
        await seats.UpdateAsync(seat, ct);
        LogPokerSeatChatRefreshed(seat.InviteCode, seat.UserId, currentChatId);
    }

    private async Task<bool> TryClearStaleSeatAsync(PokerSeat seat, CancellationToken ct)
    {
        var table = await tables.FindAsync(seat.InviteCode, ct);
        if (table is { Status: not PokerTableStatus.Closed }) return false;

        if (seat.Stack > 0)
            await TryRefundSeatStackAsync(seat, "poker.stale_refund", ct);
        await seats.DeleteAsync(seat.InviteCode, seat.Position, ct);

        LogPokerStaleSeatCleared(seat.InviteCode, seat.UserId);
        return true;
    }

    private async Task TryRefundSeatStackAsync(PokerSeat seat, string reason, CancellationToken ct)
    {
        try
        {
            await economics.CreditAsync(seat.UserId, seat.ChatId, seat.Stack, reason, ct);
        }
        catch (Exception ex)
        {
            LogPokerRefundFailed(seat.InviteCode, seat.UserId, seat.Stack, ex);
        }
    }

    private async Task RefundBuyInAfterCreateFailureAsync(
        long userId, long chatId, string code, int buyIn, Exception exception, CancellationToken ct)
    {
        try
        {
            await economics.CreditAsync(userId, chatId, buyIn, "poker.create.compensate", ct);
            LogPokerCreateCompensated(code, userId, buyIn);
        }
        catch (Exception creditEx)
        {
            LogPokerCreateCompensationFailed(code, userId, buyIn, creditEx);
        }

        LogPokerCreateFailedAfterDebit(code, userId, exception);
    }

    private async Task RefundBuyInAfterJoinFailureAsync(
        long userId, long chatId, string code, int buyIn, Exception exception, CancellationToken ct)
    {
        try
        {
            await economics.CreditAsync(userId, chatId, buyIn, "poker.join.compensate", ct);
            LogPokerJoinCompensated(code, userId, buyIn);
        }
        catch (Exception creditEx)
        {
            LogPokerJoinCompensationFailed(code, userId, buyIn, creditEx);
        }

        LogPokerJoinFailedAfterDebit(code, userId, exception);
    }

    // ───────────────────────── orchestration ─────────────────────────

    private async Task<ActionResult> ResolveAfterActionAsync(PokerTable table, List<PokerSeat> list, CancellationToken ct)
    {
        var transition = PokerDomain.ResolveAfterAction(table, list);
        await tables.UpdateAsync(table, ct);
        foreach (var s in list) await seats.UpdateAsync(s, ct);

        switch (transition.Kind)
        {
            case TransitionKind.HandEndedLastStanding:
            case TransitionKind.HandEndedRunout:
            case TransitionKind.HandEndedShowdown:
            {
                var showdown = transition.Showdown!.ToList();
                foreach (var entry in showdown.Where(e => e.Won > 0))
                    await economics.CreditAsync(entry.Seat.UserId, entry.Seat.ChatId, entry.Won, "poker.win", ct);

                string reason = transition.Kind switch
                {
                    TransitionKind.HandEndedLastStanding => "last_standing",
                    TransitionKind.HandEndedRunout => "runout",
                    _ => "showdown",
                };
                LogPokerHandEnded(table.InviteCode, reason, showdown.Sum(e => e.Won));
                analytics.Track("poker", "hand_end", new Dictionary<string, object?>
                {
                    ["invite_code"] = table.InviteCode,
                    ["reason"] = reason,
                });

                var winners = showdown
                    .Where(r => r.Won > 0)
                    .Select(r => (r.Seat.UserId, r.Won))
                    .ToList();
                await events.PublishAsync(
                    new PokerHandEnded(table.InviteCode, reason, winners, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                    ct);

                return new ActionResult(PokerError.None, new TableSnapshot(table, list), HandTransition.HandEnded, showdown, null, null);
            }

            case TransitionKind.PhaseAdvanced:
                LogPokerPhase(table.InviteCode, transition.FromPhase, transition.ToPhase);
                return new ActionResult(PokerError.None, new TableSnapshot(table, list), HandTransition.PhaseAdvanced, null, null, null);

            default:
                return new ActionResult(PokerError.None, new TableSnapshot(table, list), HandTransition.TurnAdvanced, null, null, null);
        }
    }

    private static PokerError MapValidation(ValidationResult v) => v switch
    {
        ValidationResult.CannotCheck => PokerError.CannotCheck,
        ValidationResult.RaiseTooSmall => PokerError.RaiseTooSmall,
        ValidationResult.RaiseTooLarge => PokerError.RaiseTooLarge,
        _ => PokerError.InvalidAction,
    };

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var chars = new char[5];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
            string code = new(chars);
            if (!await tables.CodeExistsAsync(code, ct))
                return code;
        }
        throw new InvalidOperationException("Failed to generate unique invite code");
    }

    [LoggerMessage(LogLevel.Information, "poker.create.rejected user={UserId} reason=not_enough_coins balance={Coins}")]
    partial void LogPokerCreateNotEnoughCoins(long userId, int coins);

    [LoggerMessage(LogLevel.Information, "poker.create.rejected user={UserId} reason=already_seated")]
    partial void LogPokerCreateAlreadySeated(long userId);

    [LoggerMessage(LogLevel.Information, "poker.create.ok code={Code} host={UserId} buy_in={BuyIn}")]
    partial void LogPokerCreated(string code, long userId, int buyIn);

    [LoggerMessage(LogLevel.Warning, "poker.create.failed_after_debit code={Code} host={UserId}")]
    partial void LogPokerCreateFailedAfterDebit(string code, long userId, Exception exception);

    [LoggerMessage(LogLevel.Information, "poker.create.compensated code={Code} host={UserId} amount={Amount}")]
    partial void LogPokerCreateCompensated(string code, long userId, int amount);

    [LoggerMessage(LogLevel.Error, "poker.create.compensation_failed code={Code} host={UserId} amount={Amount}")]
    partial void LogPokerCreateCompensationFailed(string code, long userId, int amount, Exception exception);

    [LoggerMessage(LogLevel.Information, "poker.join.ok code={Code} user={UserId} seat={Pos} seated={N}")]
    partial void LogPokerJoined(string code, long userId, int pos, int n);

    [LoggerMessage(LogLevel.Warning, "poker.join.failed_after_debit code={Code} user={UserId}")]
    partial void LogPokerJoinFailedAfterDebit(string code, long userId, Exception exception);

    [LoggerMessage(LogLevel.Information, "poker.join.compensated code={Code} user={UserId} amount={Amount}")]
    partial void LogPokerJoinCompensated(string code, long userId, int amount);

    [LoggerMessage(LogLevel.Error, "poker.join.compensation_failed code={Code} user={UserId} amount={Amount}")]
    partial void LogPokerJoinCompensationFailed(string code, long userId, int amount, Exception exception);

    [LoggerMessage(LogLevel.Information, "poker.hand.start code={Code} button={Button} utg={Utg} pot={Pot}")]
    partial void LogPokerHandStarted(string code, int button, int utg, int pot);

    [LoggerMessage(LogLevel.Information, "poker.action code={Code} user={UserId} action={Action} amount={Amount} pot={Pot}")]
    partial void LogPokerAction(string code, long userId, string action, int amount, int pot);

    [LoggerMessage(LogLevel.Information, "poker.auto code={Code} user={UserId} action={Action}")]
    partial void LogPokerAutoAction(string code, long userId, AutoAction action);

    [LoggerMessage(LogLevel.Information, "poker.leave.midhand code={Code} user={UserId}")]
    partial void LogPokerLeaveMidhand(string code, long userId);

    [LoggerMessage(LogLevel.Information, "poker.leave.ok code={Code} user={UserId} closed={Closed}")]
    partial void LogPokerLeft(string code, long userId, bool closed);

    [LoggerMessage(LogLevel.Information, "poker.seat.chat_refreshed code={Code} user={UserId} chat={ChatId}")]
    partial void LogPokerSeatChatRefreshed(string code, long userId, long chatId);

    [LoggerMessage(LogLevel.Warning, "poker.seat.stale_cleared code={Code} user={UserId}")]
    partial void LogPokerStaleSeatCleared(string code, long userId);

    [LoggerMessage(LogLevel.Error, "poker.refund_failed code={Code} user={UserId} amount={Amount}")]
    partial void LogPokerRefundFailed(string code, long userId, int amount, Exception exception);

    [LoggerMessage(LogLevel.Information, "poker.hand.end code={Code} reason={Reason} pot={Pot}")]
    partial void LogPokerHandEnded(string code, string reason, int pot);

    [LoggerMessage(LogLevel.Information, "poker.phase code={Code} {From}->{To}")]
    partial void LogPokerPhase(string code, PokerPhase from, PokerPhase to);
}
