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
// Concurrency: a static SemaphoreSlim serializes every mutating call across
// tables. Same compromise as the monolith — fine for a single-host process,
// would need per-table locks or optimistic concurrency to scale horizontally.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Games.Poker.Domain;
using Microsoft.Extensions.Options;
using static Games.Poker.PokerResultHelpers;

namespace Games.Poker;

public interface IPokerService
{
    Task<(TableSnapshot? Snapshot, PokerSeat? MySeat)> FindMyTableAsync(long userId, CancellationToken ct);
    Task<CreateResult> CreateTableAsync(long userId, string displayName, long chatId, CancellationToken ct);
    Task<JoinResult> JoinTableAsync(long userId, string displayName, long chatId, string code, CancellationToken ct);
    Task<StartResult> StartHandAsync(long userId, CancellationToken ct);
    Task<ActionResult> ApplyPlayerActionAsync(long userId, string verb, int amount, CancellationToken ct);
    Task<ActionResult?> RunAutoActionAsync(string inviteCode, CancellationToken ct);
    Task<LeaveResult> LeaveTableAsync(long userId, CancellationToken ct);
    Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct);
}

public sealed partial class PokerService(
    IPokerTableStore tables,
    IPokerSeatStore seats,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDomainEventBus events,
    IOptions<PokerOptions> options,
    ILogger<PokerService> logger) : IPokerService
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly PokerOptions _opts = options.Value;

    public async Task<(TableSnapshot? Snapshot, PokerSeat? MySeat)> FindMyTableAsync(long userId, CancellationToken ct)
    {
        var seat = await seats.FindByUserAsync(userId, ct);
        if (seat == null) return (null, null);
        var table = await tables.FindAsync(seat.InviteCode, ct);
        if (table == null) return (null, null);
        var list = await seats.ListByTableAsync(table.InviteCode, ct);
        return (new TableSnapshot(table, list), list.First(s => s.UserId == userId));
    }

    public async Task<CreateResult> CreateTableAsync(long userId, string displayName, long chatId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await economics.EnsureUserAsync(userId, displayName, ct);
            var balance = await economics.GetBalanceAsync(userId, ct);
            if (balance < _opts.BuyIn)
            {
                LogPokerCreateNotEnoughCoins(userId, balance);
                return Fail(PokerError.NotEnoughCoins);
            }

            if (await seats.AnyForUserAsync(userId, ct))
            {
                LogPokerCreateAlreadySeated(userId);
                return Fail(PokerError.AlreadySeated);
            }

            string code = await GenerateUniqueCodeAsync(ct);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var table = new PokerTable
            {
                InviteCode = code,
                HostUserId = userId,
                Status = PokerTableStatus.Seating,
                Phase = PokerPhase.None,
                SmallBlind = _opts.SmallBlind,
                BigBlind = _opts.BigBlind,
                CreatedAt = now,
                LastActionAt = now,
            };
            var seat = new PokerSeat
            {
                InviteCode = code,
                Position = 0,
                UserId = userId,
                DisplayName = displayName,
                Stack = _opts.BuyIn,
                ChatId = chatId,
                JoinedAt = now,
            };

            if (!await economics.TryDebitAsync(userId, _opts.BuyIn, "poker.create", ct))
                return Fail(PokerError.NotEnoughCoins);

            await tables.InsertAsync(table, ct);
            await seats.InsertAsync(seat, ct);

            LogPokerCreated(code, userId, _opts.BuyIn);
            analytics.Track("poker", "create", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["invite_code"] = code,
                ["buy_in"] = _opts.BuyIn,
            });
            await events.PublishAsync(new PokerTableCreated(code, userId, _opts.BuyIn, now), ct);

            return new CreateResult(PokerError.None, code, _opts.BuyIn);
        }
        finally { Gate.Release(); }
    }

    public async Task<JoinResult> JoinTableAsync(long userId, string displayName, long chatId, string code, CancellationToken ct)
    {
        code = code.ToUpperInvariant();
        await Gate.WaitAsync(ct);
        try
        {
            await economics.EnsureUserAsync(userId, displayName, ct);
            var balance = await economics.GetBalanceAsync(userId, ct);
            if (balance < _opts.BuyIn) return JoinFail(PokerError.NotEnoughCoins);
            if (await seats.AnyForUserAsync(userId, ct)) return JoinFail(PokerError.AlreadySeated);

            var table = await tables.FindAsync(code, ct);
            if (table == null || table.Status == PokerTableStatus.Closed) return JoinFail(PokerError.TableNotFound);
            if (table.Status != PokerTableStatus.Seating && table.Status != PokerTableStatus.HandComplete)
                return JoinFail(PokerError.HandInProgress);

            var list = await seats.ListByTableAsync(code, ct);
            if (list.Count >= _opts.MaxPlayers) return JoinFail(PokerError.TableFull);

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
                Stack = _opts.BuyIn,
                ChatId = chatId,
                JoinedAt = now,
            };
            if (!await economics.TryDebitAsync(userId, _opts.BuyIn, "poker.join", ct))
                return JoinFail(PokerError.NotEnoughCoins);
            await seats.InsertAsync(seat, ct);

            list.Add(seat);
            LogPokerJoined(code, userId, position, list.Count);
            analytics.Track("poker", "join", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["invite_code"] = code,
                ["seat"] = position,
                ["seated"] = list.Count,
                ["buy_in"] = _opts.BuyIn,
            });
            await events.PublishAsync(new PokerPlayerJoined(code, userId, position, _opts.BuyIn, now), ct);

            return new JoinResult(PokerError.None, new TableSnapshot(table, list), list.Count, _opts.MaxPlayers);
        }
        finally { Gate.Release(); }
    }

    public async Task<StartResult> StartHandAsync(long userId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var mySeat = await seats.FindByUserAsync(userId, ct);
            if (mySeat == null) return StartFail(PokerError.NoTable);
            var table = await tables.FindAsync(mySeat.InviteCode, ct);
            if (table == null) return StartFail(PokerError.NoTable);
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
        finally { Gate.Release(); }
    }

    public async Task<ActionResult> ApplyPlayerActionAsync(long userId, string verb, int amount, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var seat = await seats.FindByUserAsync(userId, ct);
            if (seat == null) return ActionFail(PokerError.NoTable);
            var table = await tables.FindAsync(seat.InviteCode, ct);
            if (table == null || table.Status != PokerTableStatus.HandActive) return ActionFail(PokerError.NotYourTurn);
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
        finally { Gate.Release(); }
    }

    public async Task<ActionResult?> RunAutoActionAsync(string inviteCode, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
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
        finally { Gate.Release(); }
    }

    public async Task<LeaveResult> LeaveTableAsync(long userId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var seat = await seats.FindByUserAsync(userId, ct);
            if (seat == null) return LeaveFail(PokerError.NoTable);

            var table = await tables.FindAsync(seat.InviteCode, ct);
            if (seat.Stack > 0)
                await economics.CreditAsync(userId, seat.Stack, "poker.leave", ct);

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
                return new LeaveResult(PokerError.None, after.Snapshot ?? new TableSnapshot(table, remaining), false);
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
        finally { Gate.Release(); }
    }

    public async Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await seats.UpsertStateMessageAsync(userId, messageId, ct);
        }
        finally { Gate.Release(); }
    }

    public Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct) =>
        tables.ListStuckCodesAsync(cutoffMs, ct);

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
                    await economics.CreditAsync(entry.Seat.UserId, entry.Won, "poker.win", ct);

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

    [LoggerMessage(LogLevel.Information, "poker.join.ok code={Code} user={UserId} seat={Pos} seated={N}")]
    partial void LogPokerJoined(string code, long userId, int pos, int n);

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

    [LoggerMessage(LogLevel.Information, "poker.hand.end code={Code} reason={Reason} pot={Pot}")]
    partial void LogPokerHandEnded(string code, string reason, int pot);

    [LoggerMessage(LogLevel.Information, "poker.phase code={Code} {From}->{To}")]
    partial void LogPokerPhase(string code, PokerPhase from, PokerPhase to);
}
