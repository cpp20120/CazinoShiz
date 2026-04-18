using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using CasinoShiz.Services.Horse;
using CasinoShiz.Services.Poker.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Services.Admin;

public sealed class AdminService(
    AppDbContext db,
    ClickHouseReporter reporter,
    PokerService poker,
    EconomicsService economics,
    HorseService horse,
    ITelegramBotClient bot,
    IOptions<BotOptions> options)
{
    private readonly BotOptions _opts = options.Value;

    public async Task<int> UserSyncAsync(long callerId, CancellationToken ct)
    {
        var users = await db.Users.ToListAsync(ct);

        reporter.SendEvents(users.Select(u => new EventData
        {
            EventType = "user_map",
            Payload = new { display_name = u.DisplayName, user_id = u.TelegramUserId }
        }));

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "usersync", calleeId = callerId, count = users.Count }
        });

        return users.Count;
    }

    public async Task<PayResult> PayAsync(long callerId, long targetUserId, int amount, CancellationToken ct)
    {
        var user = await economics.GetOrCreateUserAsync(targetUserId, $"User ID: {targetUserId}", ct);
        var oldCoins = user.Coins;
        await economics.AdjustUncheckedAsync(user, amount, "admin.pay", ct);

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "pay", calleeId = callerId, amount, forUserId = targetUserId }
        });

        return new PayResult(user.DisplayName, oldCoins, user.Coins, amount);
    }

    public async Task<UserLookup> GetUserAsync(long targetUserId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([targetUserId], ct);
        return new UserLookup(user);
    }

    public async Task<UserListResult> ListUsersAsync(string? search, int skip, int take, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = long.TryParse(s, out var id)
                ? query.Where(u => u.TelegramUserId == id || EF.Functions.Like(u.DisplayName, $"%{s}%"))
                : query.Where(u => EF.Functions.Like(u.DisplayName, $"%{s}%"));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderByDescending(u => u.Coins)
            .Skip(skip)
            .Take(take)
            .Select(u => new UserListItem(u.TelegramUserId, u.DisplayName, u.Coins, u.LastDayUtc, u.AttemptCount, u.ExtraAttempts))
            .ToListAsync(ct);

        return new UserListResult(users, total, skip, take);
    }

    public async Task<UserDetail?> GetUserDetailAsync(long targetUserId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == targetUserId, ct);
        if (user == null) return null;

        var bets = await db.HorseBets.AsNoTracking()
            .Where(b => b.UserId == targetUserId)
            .OrderByDescending(b => b.RaceDate)
            .Take(10)
            .ToListAsync(ct);

        var codes = await db.FreespinCodes.AsNoTracking()
            .Where(c => c.IssuedBy == targetUserId)
            .OrderByDescending(c => c.IssuedAt)
            .Take(10)
            .ToListAsync(ct);

        var seat = await db.PokerSeats.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == targetUserId, ct);

        var blackjack = await db.BlackjackHands.AsNoTracking()
            .FirstOrDefaultAsync(h => h.UserId == targetUserId, ct);

        var cube = await db.DiceCubeBets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == targetUserId, ct);

        var darts = await db.DartsBets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == targetUserId, ct);

        var shPlayer = await db.SecretHitlerPlayers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == targetUserId, ct);
        SecretHitlerGame? shGame = null;
        if (shPlayer != null)
            shGame = await db.SecretHitlerGames.AsNoTracking()
                .FirstOrDefaultAsync(g => g.InviteCode == shPlayer.InviteCode, ct);

        return new UserDetail(user, bets, codes, seat, blackjack, cube, darts, shPlayer, shGame);
    }

    public async Task<CancelResult> CancelDiceCubeBetAsync(long callerId, long targetUserId, CancellationToken ct)
    {
        var bet = await db.DiceCubeBets.FirstOrDefaultAsync(b => b.UserId == targetUserId, ct);
        if (bet == null) return new CancelResult(AdminCancelOp.Noop, 0);

        var user = await db.Users.FindAsync([targetUserId], ct);
        var refunded = bet.Amount;
        if (user != null) await economics.CreditAsync(user, refunded, "admin.cancel_dicecube", ct);
        db.DiceCubeBets.Remove(bet);
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "cancel_dicecube", calleeId = callerId, forUserId = targetUserId, refunded },
        });
        return new CancelResult(AdminCancelOp.Done, refunded);
    }

    public async Task<CancelResult> CancelDartsBetAsync(long callerId, long targetUserId, CancellationToken ct)
    {
        var bet = await db.DartsBets.FirstOrDefaultAsync(b => b.UserId == targetUserId, ct);
        if (bet == null) return new CancelResult(AdminCancelOp.Noop, 0);

        var user = await db.Users.FindAsync([targetUserId], ct);
        var refunded = bet.Amount;
        if (user != null) await economics.CreditAsync(user, refunded, "admin.cancel_darts", ct);
        db.DartsBets.Remove(bet);
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "cancel_darts", calleeId = callerId, forUserId = targetUserId, refunded },
        });
        return new CancelResult(AdminCancelOp.Done, refunded);
    }

    public async Task<CancelResult> ResetSlotAttemptsAsync(long callerId, long targetUserId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([targetUserId], ct);
        if (user == null) return new CancelResult(AdminCancelOp.Noop, 0);

        var cleared = user.AttemptCount;
        user.AttemptCount = 0;
        user.ExtraAttempts = 0;
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "reset_attempts", calleeId = callerId, forUserId = targetUserId, cleared },
        });
        return new CancelResult(AdminCancelOp.Done, cleared);
    }

    public async Task<CancelResult> CancelBlackjackHandAsync(long callerId, long targetUserId, CancellationToken ct)
    {
        var hand = await db.BlackjackHands.FindAsync([targetUserId], ct);
        if (hand == null) return new CancelResult(AdminCancelOp.Noop, 0);

        var user = await db.Users.FindAsync([targetUserId], ct);
        var refunded = hand.Bet;
        if (user != null) await economics.CreditAsync(user, refunded, "admin.cancel_blackjack", ct);
        db.BlackjackHands.Remove(hand);
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "cancel_blackjack", calleeId = callerId, forUserId = targetUserId, refunded },
        });
        return new CancelResult(AdminCancelOp.Done, refunded);
    }

    public async Task<PokerKickResult> KickFromPokerAsync(long callerId, long targetUserId, CancellationToken ct)
    {
        var stack = await db.PokerSeats.AsNoTracking()
            .Where(s => s.UserId == targetUserId)
            .Select(s => (int?)s.Stack)
            .FirstOrDefaultAsync(ct);
        if (stack == null) return new PokerKickResult(AdminCancelOp.Noop, 0, null);

        var result = await poker.LeaveTableAsync(targetUserId, ct);
        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "kick_poker", calleeId = callerId, forUserId = targetUserId, refunded = stack.Value, error = result.Error.ToString() },
        });
        return new PokerKickResult(AdminCancelOp.Done, stack.Value, result.TableClosed ? null : result.Snapshot);
    }

    public async Task<HorseRaceAdminView> GetHorseRaceViewAsync(CancellationToken ct)
    {
        var raceDate = TimeHelper.GetRaceDate();
        var bets = await db.HorseBets.AsNoTracking().Where(b => b.RaceDate == raceDate).ToListAsync(ct);
        var stakes = new Dictionary<int, int>();
        for (var i = 0; i < HorseService.HorseCount; i++) stakes[i] = 0;
        foreach (var b in bets) stakes[b.HorseId] += b.Amount;
        var koefs = HorseService.GetKoefs(stakes);
        var result = await db.HorseResults.AsNoTracking().FirstOrDefaultAsync(r => r.RaceDate == raceDate, ct);
        return new HorseRaceAdminView(raceDate, bets.Count, HorseService.MinBetsToRun, stakes, koefs, result);
    }

    public async Task<HorseRunAdminResult> RunHorseRaceAsync(long callerId, CancellationToken ct)
    {
        var outcome = await horse.RunRaceFromAdminAsync(ct);
        if (outcome.Error == HorseError.NotEnoughBets)
            return new HorseRunAdminResult(HorseError.NotEnoughBets, null, [], false);

        var winners = outcome.Transactions;
        bool broadcast = false;
        if (!string.IsNullOrWhiteSpace(_opts.TrustedChannel))
        {
            var channel = _opts.TrustedChannel.StartsWith('@') ? _opts.TrustedChannel : "@" + _opts.TrustedChannel;
            try
            {
                await using var gifStream = new MemoryStream(outcome.GifBytes);
                await bot.SendAnimation(channel, InputFile.FromStream(gifStream, "horses.gif"), cancellationToken: ct);
                var text = winners.Count > 0
                    ? string.Join("\n", new[] { $"<b>Лошадь {outcome.Winner + 1} побеждает!</b>\n" }
                        .Concat(winners.Select((tx, i) =>
                            $"<a href=\"tg://user?id={tx.UserId}\">Победитель {i + 1}</a>: <b>+{tx.Amount}</b>")))
                    : $"<b>Лошадь {outcome.Winner + 1} побеждает!</b>\nСегодня никому не удалось победить :(";
                await bot.SendMessage(channel, text, parseMode: ParseMode.Html, cancellationToken: ct);
                broadcast = true;
            }
            catch { /* swallow: admin still gets inline view */ }
        }

        int notified = 0;
        foreach (var p in outcome.Participants)
        {
            var text = p.Payout > 0
                ? Locales.HorseRaceWinnerDm(outcome.Winner + 1, p.TotalBet, p.Payout)
                : Locales.HorseRaceLoserDm(outcome.Winner + 1, p.TotalBet);
            try
            {
                await using var gifStream = new MemoryStream(outcome.GifBytes);
                await bot.SendAnimation(p.UserId, InputFile.FromStream(gifStream, "horses.gif"),
                    caption: text, parseMode: ParseMode.Html, cancellationToken: ct);
                notified++;
            }
            catch { /* user may have never DMed the bot — skip silently */ }
        }

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "horse_run", calleeId = callerId, winner = outcome.Winner + 1, payouts = winners.Count, broadcast, notified, participants = outcome.Participants.Count },
        });

        return new HorseRunAdminResult(HorseError.None, outcome.Winner, winners, broadcast, outcome.GifBytes);
    }

    public async Task<OverviewStats> GetOverviewStatsAsync(CancellationToken ct)
    {
        var raceDate = TimeHelper.GetRaceDate();
        var currentDayMs = TimeHelper.GetCurrentDayMillis();

        var totalUsers = await db.Users.CountAsync(ct);
        var pokerTables = await db.PokerTables.CountAsync(ct);
        var pokerPlayers = await db.PokerSeats.CountAsync(ct);
        var activeBj = await db.BlackjackHands.CountAsync(ct);
        var totalBj = await db.Users.SumAsync(u => (long)u.BlackjackHandsPlayed, ct);

        var todayBets = await db.HorseBets.Where(b => b.RaceDate == raceDate)
            .Select(b => new { b.Amount }).ToListAsync(ct);
        var horseBetsToday = todayBets.Count;
        var horsePotToday = todayBets.Sum(x => x.Amount);
        var horseRacesRun = await db.HorseResults.CountAsync(ct);

        var diceAttemptsToday = await db.Users
            .Where(u => u.LastDayUtc == currentDayMs)
            .SumAsync(u => (int?)u.AttemptCount, ct) ?? 0;

        var activeCodes = await db.FreespinCodes.CountAsync(c => c.Active, ct);

        var cubeBets = await db.DiceCubeBets.Select(b => b.Amount).ToListAsync(ct);
        var cubePendingBets = cubeBets.Count;
        var cubePendingPot = cubeBets.Sum();

        var dartsBets = await db.DartsBets.Select(b => b.Amount).ToListAsync(ct);
        var dartsPendingBets = dartsBets.Count;
        var dartsPendingPot = dartsBets.Sum();

        var shLobby = await db.SecretHitlerGames.CountAsync(g => g.Status == ShStatus.Lobby, ct);
        var shActive = await db.SecretHitlerGames.CountAsync(g => g.Status == ShStatus.Active, ct);
        var shPlayers = await db.SecretHitlerPlayers.CountAsync(ct);
        var shPotLocked = await db.SecretHitlerGames
            .Where(g => g.Status == ShStatus.Lobby || g.Status == ShStatus.Active)
            .SumAsync(g => (int?)g.Pot, ct) ?? 0;

        return new OverviewStats(
            totalUsers, pokerTables, pokerPlayers, activeBj, totalBj,
            horseBetsToday, horsePotToday, horseRacesRun,
            diceAttemptsToday, activeCodes,
            cubePendingBets, cubePendingPot,
            dartsPendingBets, dartsPendingPot,
            shLobby, shActive, shPlayers, shPotLocked);
    }

    public async Task<ShRoomListResult> ListSecretHitlerRoomsAsync(CancellationToken ct)
    {
        var games = await db.SecretHitlerGames.AsNoTracking()
            .Where(g => g.Status == ShStatus.Lobby || g.Status == ShStatus.Active)
            .OrderByDescending(g => g.LastActionAt)
            .ToListAsync(ct);
        if (games.Count == 0) return new ShRoomListResult([]);

        var codes = games.Select(g => g.InviteCode).ToList();
        var playerCounts = await db.SecretHitlerPlayers.AsNoTracking()
            .Where(p => codes.Contains(p.InviteCode))
            .GroupBy(p => p.InviteCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byCode = playerCounts.ToDictionary(x => x.Code, x => x.Count);

        var rooms = games.Select(g => new ShRoomListItem(
            g.InviteCode, g.HostUserId, g.Status, g.Phase,
            byCode.GetValueOrDefault(g.InviteCode, 0),
            g.BuyIn, g.Pot,
            g.LiberalPolicies, g.FascistPolicies,
            g.CreatedAt, g.LastActionAt)).ToList();
        return new ShRoomListResult(rooms);
    }

    public async Task<ShRoomDetailView?> GetSecretHitlerRoomAsync(string inviteCode, CancellationToken ct)
    {
        var code = inviteCode.ToUpperInvariant();
        var game = await db.SecretHitlerGames.AsNoTracking().FirstOrDefaultAsync(g => g.InviteCode == code, ct);
        if (game == null) return null;
        var players = await db.SecretHitlerPlayers.AsNoTracking()
            .Where(p => p.InviteCode == code)
            .OrderBy(p => p.Position)
            .ToListAsync(ct);
        return new ShRoomDetailView(game, players);
    }

    public async Task<ShCancelResult> CancelSecretHitlerRoomAsync(long callerId, string inviteCode, CancellationToken ct)
    {
        var code = inviteCode.ToUpperInvariant();
        var game = await db.SecretHitlerGames.FirstOrDefaultAsync(g => g.InviteCode == code, ct);
        if (game == null) return new ShCancelResult(AdminCancelOp.Noop, 0, 0);
        if (game.Status == ShStatus.Closed || game.Status == ShStatus.Completed)
            return new ShCancelResult(AdminCancelOp.Noop, 0, 0);

        var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == code).ToListAsync(ct);
        var refund = game.BuyIn;
        var refunded = 0;
        foreach (var p in players)
        {
            var user = await db.Users.FindAsync([p.UserId], ct);
            if (user == null) continue;
            await economics.CreditAsync(user, refund, "admin.cancel_sh", ct);
            refunded++;
        }

        db.SecretHitlerPlayers.RemoveRange(players);
        game.Pot = 0;
        game.Status = ShStatus.Closed;
        game.Phase = ShPhase.None;
        game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "cancel_sh_room", calleeId = callerId, invite_code = code, players_refunded = refunded, refund_each = refund },
        });
        return new ShCancelResult(AdminCancelOp.Done, refund * refunded, refunded);
    }

    public void ReportNotAdmin(long userId)
    {
        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { type = "insufficient_permissions", command = "not_admin", calleeId = userId }
        });
    }

    public void ReportUserInfo(long callerId, string targetId)
    {
        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "userinfo", calleeId = callerId, requestedUserId = targetId }
        });
    }

    public async Task<RenameResult> RenameAsync(string oldName, string newName, CancellationToken ct)
    {
        var existing = await db.DisplayNameOverrides.FindAsync([oldName], ct);

        if (newName == "*")
        {
            if (existing == null) return new RenameResult(RenameOp.NoChange, oldName, newName);
            db.DisplayNameOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
            return new RenameResult(RenameOp.Cleared, oldName, newName);
        }

        if (existing != null)
            existing.NewName = newName;
        else
            db.DisplayNameOverrides.Add(new DisplayNameOverride { OriginalName = oldName, NewName = newName });

        await db.SaveChangesAsync(ct);
        return new RenameResult(RenameOp.Set, oldName, newName);
    }
}
