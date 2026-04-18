using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using CasinoShiz.Services.Poker.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CasinoShiz.Services.Poker.Application.PokerResultHelpers;

namespace CasinoShiz.Services.Poker.Application;

public sealed partial class PokerService(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter,
    EconomicsService economics,
    ILogger<PokerService> logger)
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly BotOptions _opts = options.Value;
    // need to make qeury use dapper and for commands efcore is fine
    // query 
    public async Task<(TableSnapshot? Snapshot, PokerSeat? MySeat)> FindMyTableAsync(long userId, CancellationToken ct)
    {
        var seat = await db.PokerSeats.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (seat == null) return (null, null);
        var table = await db.PokerTables.FindAsync([seat.InviteCode], ct);
        if (table == null) return (null, null);
        var seats = await db.PokerSeats.Where(s => s.InviteCode == table.InviteCode).ToListAsync(ct);
        return (new TableSnapshot(table, seats), seats.First(s => s.UserId == userId));
    }
    
    // commands
    public async Task<CreateResult> CreateTableAsync(long userId, string displayName, long chatId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
            if (user.Coins < _opts.PokerBuyIn)
            {
                LogPokerCreateNotEnoughCoins(userId, user.Coins);
                return Fail(PokerError.NotEnoughCoins);
            }

            if (await db.PokerSeats.AnyAsync(s => s.UserId == userId, ct))
            {
                LogPokerCreateAlreadySeated(userId);
                return Fail(PokerError.AlreadySeated);
            }

            string code = await GenerateUniqueCodeAsync(ct);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            db.PokerTables.Add(new PokerTable
            {
                InviteCode = code,
                HostUserId = userId,
                Status = PokerTableStatus.Seating,
                Phase = PokerPhase.None,
                SmallBlind = _opts.PokerSmallBlind,
                BigBlind = _opts.PokerBigBlind,
                CreatedAt = now,
                LastActionAt = now,
            });
            db.PokerSeats.Add(new PokerSeat
            {
                InviteCode = code,
                Position = 0,
                UserId = userId,
                DisplayName = user.DisplayName,
                Stack = _opts.PokerBuyIn,
                ChatId = chatId,
                JoinedAt = now,
            });
            await economics.DebitAsync(user, _opts.PokerBuyIn, "poker.create", ct);
            await db.SaveChangesAsync(ct);

            LogPokerCreated(code, userId, _opts.PokerBuyIn);
            reporter.SendEvent(new EventData
            {
                EventType = "poker_create",
                Payload = new { user_id = userId, invite_code = code, buy_in = _opts.PokerBuyIn }
            });

            return new CreateResult(PokerError.None, code, _opts.PokerBuyIn);
        }
        finally { Gate.Release(); }
    }

    public async Task<JoinResult> JoinTableAsync(long userId, string displayName, long chatId, string code, CancellationToken ct)
    {
        code = code.ToUpperInvariant();
        await Gate.WaitAsync(ct);
        try
        {
            var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
            if (user.Coins < _opts.PokerBuyIn) return JoinFail(PokerError.NotEnoughCoins);
            if (await db.PokerSeats.AnyAsync(s => s.UserId == userId, ct)) return JoinFail(PokerError.AlreadySeated);

            var table = await db.PokerTables.FindAsync([code], ct);
            if (table == null || table.Status == PokerTableStatus.Closed) return JoinFail(PokerError.TableNotFound);
            if (table.Status != PokerTableStatus.Seating && table.Status != PokerTableStatus.HandComplete)
                return JoinFail(PokerError.HandInProgress);

            var seats = await db.PokerSeats.Where(s => s.InviteCode == code).ToListAsync(ct);
            if (seats.Count >= _opts.PokerMaxPlayers) return JoinFail(PokerError.TableFull);

            int position = 0;
            var used = seats.Select(s => s.Position).ToHashSet();
            while (used.Contains(position)) position++;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var seat = new PokerSeat
            {
                InviteCode = code,
                Position = position,
                UserId = userId,
                DisplayName = user.DisplayName,
                Stack = _opts.PokerBuyIn,
                ChatId = chatId,
                JoinedAt = now,
            };
            await economics.DebitAsync(user, _opts.PokerBuyIn, "poker.join", ct);
            db.PokerSeats.Add(seat);
            await db.SaveChangesAsync(ct);

            seats.Add(seat);
            LogPokerJoined(code, userId, position, seats.Count);
            reporter.SendEvent(new EventData
            {
                EventType = "poker_join",
                Payload = new { user_id = userId, invite_code = code, seat = position, seated = seats.Count, buy_in = _opts.PokerBuyIn }
            });
            return new JoinResult(PokerError.None, new TableSnapshot(table, seats), seats.Count, _opts.PokerMaxPlayers);
        }
        finally { Gate.Release(); }
    }

    public async Task<StartResult> StartHandAsync(long userId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var mySeat = await db.PokerSeats.FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (mySeat == null) return StartFail(PokerError.NoTable);
            var table = await db.PokerTables.FindAsync([mySeat.InviteCode], ct);
            if (table == null) return StartFail(PokerError.NoTable);
            if (table.HostUserId != userId) return StartFail(PokerError.NotHost);
            if (table.Status == PokerTableStatus.HandActive) return StartFail(PokerError.HandInProgress);

            var seats = await db.PokerSeats.Where(s => s.InviteCode == table.InviteCode).ToListAsync(ct);
            if (seats.Count(s => s.Stack > 0) < 2) return StartFail(PokerError.NeedTwo);

            PokerDomain.StartHand(table, seats);
            await db.SaveChangesAsync(ct);

            LogPokerHandStarted(table.InviteCode, table.ButtonSeat, table.CurrentSeat, table.Pot);
            reporter.SendEvent(new EventData
            {
                EventType = "poker_hand_start",
                Payload = new { invite_code = table.InviteCode, seats = seats.Count(s => s.Status == PokerSeatStatus.Seated || s.Status == PokerSeatStatus.AllIn) }
            });
            return new StartResult(PokerError.None, new TableSnapshot(table, seats));
        }
        finally { Gate.Release(); }
    }

    public async Task<ActionResult> ApplyPlayerActionAsync(long userId, string verb, int amount, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var seat = await db.PokerSeats.FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (seat == null) return ActionFail(PokerError.NoTable);
            var table = await db.PokerTables.FindAsync([seat.InviteCode], ct);
            if (table == null || table.Status != PokerTableStatus.HandActive) return ActionFail(PokerError.NotYourTurn);
            var seats = await db.PokerSeats.Where(s => s.InviteCode == table.InviteCode).ToListAsync(ct);

            if (seat.Position != table.CurrentSeat || seat.Status != PokerSeatStatus.Seated)
                return ActionFail(PokerError.NotYourTurn);

            var action = PokerAction.FromVerb(verb, amount);
            if (action is null) return ActionFail(PokerError.InvalidAction);

            var validation = PokerDomain.Validate(table, seat, action.Value);
            if (validation != ValidationResult.Ok) return ActionFail(MapValidation(validation));

            PokerDomain.Apply(table, seat, action.Value);
            seat.HasActedThisRound = true;
            table.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            LogPokerAction(table.InviteCode, userId, verb, amount, table.Pot);
            reporter.SendEvent(new EventData
            {
                EventType = "poker_action",
                Payload = new { invite_code = table.InviteCode, user_id = userId, action = verb, amount, pot = table.Pot }
            });

            return await ResolveAfterActionAsync(table, seats, ct);
        }
        finally { Gate.Release(); }
    }

    public async Task<ActionResult?> RunAutoActionAsync(string inviteCode, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var table = await db.PokerTables.FindAsync([inviteCode], ct);
            if (table == null || table.Status != PokerTableStatus.HandActive) return null;

            var seats = await db.PokerSeats.Where(s => s.InviteCode == inviteCode).ToListAsync(ct);
            var current = seats.FirstOrDefault(s => s.Position == table.CurrentSeat);
            if (current == null || current.Status != PokerSeatStatus.Seated) return null;

            var decision = PokerDomain.DecideAutoAction(table, current);
            PokerDomain.Apply(table, current, decision);
            current.HasActedThisRound = true;
            table.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var autoKind = decision.Kind == PokerActionKind.Check ? AutoAction.Check : AutoAction.Fold;
            LogPokerAutoAction(inviteCode, current.UserId, autoKind);
            reporter.SendEvent(new EventData
            {
                EventType = "poker_auto",
                Payload = new { invite_code = inviteCode, user_id = current.UserId, action = autoKind.ToString() }
            });

            var result = await ResolveAfterActionAsync(table, seats, ct);
            return result with { AutoActorName = current.DisplayName, AutoKind = autoKind };
        }
        finally { Gate.Release(); }
    }

    public async Task<LeaveResult> LeaveTableAsync(long userId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var seat = await db.PokerSeats.FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (seat == null) return LeaveFail(PokerError.NoTable);

            var table = await db.PokerTables.FindAsync([seat.InviteCode], ct);
            var user = await db.Users.FindAsync([userId], ct);
            if (user != null) await economics.CreditAsync(user, seat.Stack, "poker.leave", ct);

            if (table != null && table.Status == PokerTableStatus.HandActive && seat.Status == PokerSeatStatus.Seated)
            {
                seat.Status = PokerSeatStatus.Folded;
                seat.Stack = 0;
                await db.SaveChangesAsync(ct);

                var allSeats = await db.PokerSeats.Where(s => s.InviteCode == table.InviteCode).ToListAsync(ct);
                var after = await ResolveAfterActionAsync(table, allSeats, ct);

                db.PokerSeats.Remove(seat);
                await db.SaveChangesAsync(ct);

                LogPokerLeaveMidhand(table.InviteCode, userId);
                reporter.SendEvent(new EventData
                {
                    EventType = "poker_leave",
                    Payload = new { invite_code = table.InviteCode, user_id = userId, refunded = 0, mid_hand = true }
                });
                var remaining = allSeats.Where(s => s.UserId != userId).ToList();
                return new LeaveResult(PokerError.None, after.Snapshot ?? new TableSnapshot(table, remaining), false);
            }

            db.PokerSeats.Remove(seat);
            bool closed = false;
            if (table != null)
            {
                var remainingCount = await db.PokerSeats.CountAsync(s => s.InviteCode == table.InviteCode && s.UserId != userId, ct);
                if (remainingCount == 0)
                {
                    table.Status = PokerTableStatus.Closed;
                    closed = true;
                }
            }
            await db.SaveChangesAsync(ct);

            TableSnapshot? snapshot = null;
            if (table != null && !closed)
            {
                var remaining = await db.PokerSeats.Where(s => s.InviteCode == table.InviteCode).ToListAsync(ct);
                snapshot = new TableSnapshot(table, remaining);
            }

            LogPokerLeft(table?.InviteCode ?? "-", userId, closed);
            reporter.SendEvent(new EventData
            {
                EventType = "poker_leave",
                Payload = new { invite_code = table?.InviteCode, user_id = userId, refunded = seat.Stack, table_closed = closed }
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
            var seat = await db.PokerSeats.FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (seat == null) return;
            seat.StateMessageId = messageId;
            await db.SaveChangesAsync(ct);
        }
        finally { Gate.Release(); }
    }

    // ───────────────────────── orchestration ─────────────────────────

    private async Task<ActionResult> ResolveAfterActionAsync(PokerTable table, List<PokerSeat> seats, CancellationToken ct)
    {
        var transition = PokerDomain.ResolveAfterAction(table, seats);
        await db.SaveChangesAsync(ct);

        switch (transition.Kind)
        {
            case TransitionKind.HandEndedLastStanding:
            case TransitionKind.HandEndedRunout:
            case TransitionKind.HandEndedShowdown:
            {
                var showdown = transition.Showdown!.ToList();
                string reason = transition.Kind switch
                {
                    TransitionKind.HandEndedLastStanding => "last_standing",
                    TransitionKind.HandEndedRunout => "runout",
                    _ => "showdown",
                };
                LogPokerHandEnded(table.InviteCode, reason, showdown.Sum(e => e.Won));
                reporter.SendEvent(new EventData
                {
                    EventType = "poker_hand_end",
                    Payload = new
                    {
                        invite_code = table.InviteCode,
                        reason,
                        winners = showdown.Where(r => r.Won > 0).Select(r => new { user_id = r.Seat.UserId, amount = r.Won })
                    }
                });
                return new ActionResult(PokerError.None, new TableSnapshot(table, seats), HandTransition.HandEnded, showdown, null, null);
            }

            case TransitionKind.PhaseAdvanced:
                LogPokerPhase(table.InviteCode, transition.FromPhase, transition.ToPhase);
                return new ActionResult(PokerError.None, new TableSnapshot(table, seats), HandTransition.PhaseAdvanced, null, null, null);

            default:
                return new ActionResult(PokerError.None, new TableSnapshot(table, seats), HandTransition.TurnAdvanced, null, null, null);
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
            if (!await db.PokerTables.AnyAsync(t => t.InviteCode == code, ct))
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
