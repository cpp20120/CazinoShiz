using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using CasinoShiz.Services.SecretHitler.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CasinoShiz.Services.SecretHitler.Application.ShResultHelpers;

namespace CasinoShiz.Services.SecretHitler.Application;

public sealed partial class SecretHitlerService(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter,
    EconomicsService economics,
    ILogger<SecretHitlerService> logger)
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly BotOptions _opts = options.Value;

    public async Task<(ShGameSnapshot? Snapshot, SecretHitlerPlayer? Me)> FindMyGameAsync(long userId, CancellationToken ct)
    {
        var player = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (player == null) return (null, null);
        var game = await db.SecretHitlerGames.FindAsync([player.InviteCode], ct);
        if (game == null) return (null, null);
        var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == game.InviteCode).ToListAsync(ct);
        return (new ShGameSnapshot(game, players), players.First(p => p.UserId == userId));
    }

    public async Task<ShCreateResult> CreateGameAsync(long userId, string displayName, long chatId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var buyIn = _opts.SecretHitlerBuyIn;
            var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
            if (user.Coins < buyIn) return CreateFail(ShError.NotEnoughCoins);
            if (await db.SecretHitlerPlayers.AnyAsync(p => p.UserId == userId, ct)) return CreateFail(ShError.AlreadyInGame);

            var code = await GenerateUniqueCodeAsync(ct);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            db.SecretHitlerGames.Add(new SecretHitlerGame
            {
                InviteCode = code,
                HostUserId = userId,
                ChatId = chatId,
                Status = ShStatus.Lobby,
                Phase = ShPhase.None,
                BuyIn = buyIn,
                Pot = buyIn,
                CreatedAt = now,
                LastActionAt = now,
            });
            db.SecretHitlerPlayers.Add(new SecretHitlerPlayer
            {
                InviteCode = code,
                Position = 0,
                UserId = userId,
                DisplayName = user.DisplayName,
                ChatId = chatId,
                IsAlive = true,
                JoinedAt = now,
            });
            await economics.DebitAsync(user, buyIn, "sh.create", ct);
            await db.SaveChangesAsync(ct);

            LogShCreated(code, userId, buyIn);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_create",
                Payload = new { user_id = userId, invite_code = code, buy_in = buyIn }
            });
            return new ShCreateResult(ShError.None, code, buyIn);
        }
        finally { Gate.Release(); }
    }

    public async Task<ShJoinResult> JoinGameAsync(long userId, string displayName, long chatId, string code, CancellationToken ct)
    {
        code = code.ToUpperInvariant();
        await Gate.WaitAsync(ct);
        try
        {
            var buyIn = _opts.SecretHitlerBuyIn;
            var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
            if (user.Coins < buyIn) return JoinFail(ShError.NotEnoughCoins);
            if (await db.SecretHitlerPlayers.AnyAsync(p => p.UserId == userId, ct)) return JoinFail(ShError.AlreadyInGame);

            var game = await db.SecretHitlerGames.FindAsync([code], ct);
            if (game == null || game.Status == ShStatus.Closed || game.Status == ShStatus.Completed) return JoinFail(ShError.GameNotFound);
            if (game.Status != ShStatus.Lobby) return JoinFail(ShError.GameInProgress);

            var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == code).ToListAsync(ct);
            if (players.Count >= ShRoleDealer.MaxPlayers) return JoinFail(ShError.GameFull);

            int position = 0;
            var used = players.Select(p => p.Position).ToHashSet();
            while (used.Contains(position)) position++;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var newPlayer = new SecretHitlerPlayer
            {
                InviteCode = code,
                Position = position,
                UserId = userId,
                DisplayName = user.DisplayName,
                ChatId = chatId,
                IsAlive = true,
                JoinedAt = now,
            };
            await economics.DebitAsync(user, buyIn, "sh.join", ct);
            db.SecretHitlerPlayers.Add(newPlayer);
            game.Pot += buyIn;
            game.LastActionAt = now;
            await db.SaveChangesAsync(ct);

            players.Add(newPlayer);
            LogShJoined(code, userId, position, players.Count);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_join",
                Payload = new { user_id = userId, invite_code = code, position, seated = players.Count, buy_in = buyIn }
            });
            return new ShJoinResult(ShError.None, new ShGameSnapshot(game, players), players.Count, ShRoleDealer.MaxPlayers);
        }
        finally { Gate.Release(); }
    }

    public async Task<ShStartResult> StartGameAsync(long userId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var me = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (me == null) return StartFail(ShError.NotInGame);
            var game = await db.SecretHitlerGames.FindAsync([me.InviteCode], ct);
            if (game == null) return StartFail(ShError.NotInGame);
            if (game.HostUserId != userId) return StartFail(ShError.NotHost);
            if (game.Status != ShStatus.Lobby) return StartFail(ShError.GameInProgress);

            var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == game.InviteCode).ToListAsync(ct);
            if (players.Count < ShRoleDealer.MinPlayers) return StartFail(ShError.NotEnoughPlayers);

            ShTransitions.StartGame(game, players);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct);

            LogShStarted(game.InviteCode, players.Count);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_start",
                Payload = new { invite_code = game.InviteCode, players = players.Count }
            });
            return new ShStartResult(ShError.None, new ShGameSnapshot(game, players));
        }
        finally { Gate.Release(); }
    }

    public async Task<ShNominateResult> NominateAsync(long userId, int chancellorPosition, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var me = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (me == null) return NominateFail(ShError.NotInGame);
            var game = await db.SecretHitlerGames.FindAsync([me.InviteCode], ct);
            if (game == null) return NominateFail(ShError.NotInGame);
            var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == game.InviteCode).ToListAsync(ct);

            var v = ShTransitions.ValidateNomination(game, me, chancellorPosition, players);
            if (v != ShValidation.Ok) return NominateFail(MapValidation(v));

            ShTransitions.ApplyNomination(game, chancellorPosition, players);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct);

            LogShNominated(game.InviteCode, userId, chancellorPosition);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_nominate",
                Payload = new { invite_code = game.InviteCode, president_id = userId, chancellor_pos = chancellorPosition }
            });
            return new ShNominateResult(ShError.None, new ShGameSnapshot(game, players));
        }
        finally { Gate.Release(); }
    }

    public async Task<ShVoteResult> VoteAsync(long userId, ShVote vote, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var me = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (me == null) return VoteFail(ShError.NotInGame);
            var game = await db.SecretHitlerGames.FindAsync([me.InviteCode], ct);
            if (game == null) return VoteFail(ShError.NotInGame);
            var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == game.InviteCode).ToListAsync(ct);
            me = players.First(p => p.UserId == userId);

            var v = ShTransitions.ValidateVote(game, me);
            if (v != ShValidation.Ok) return VoteFail(MapValidation(v));

            var after = ShTransitions.ApplyVote(game, me, vote, players);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (game.Status == ShStatus.Completed) await SettlePotAsync(game, players, ct);
            await db.SaveChangesAsync(ct);

            LogShVoted(game.InviteCode, userId, vote, after?.Kind);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_vote",
                Payload = new { invite_code = game.InviteCode, user_id = userId, vote = vote.ToString(), resolved = after?.Kind.ToString() }
            });
            return new ShVoteResult(ShError.None, new ShGameSnapshot(game, players), after);
        }
        finally { Gate.Release(); }
    }

    public async Task<ShDiscardResult> PresidentDiscardAsync(long userId, int discardIndex, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var me = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (me == null) return DiscardFail(ShError.NotInGame);
            var game = await db.SecretHitlerGames.FindAsync([me.InviteCode], ct);
            if (game == null) return DiscardFail(ShError.NotInGame);
            var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == game.InviteCode).ToListAsync(ct);
            me = players.First(p => p.UserId == userId);

            var v = ShTransitions.ValidatePresidentDiscard(game, me, discardIndex);
            if (v != ShValidation.Ok) return DiscardFail(MapValidation(v));

            ShTransitions.ApplyPresidentDiscard(game, discardIndex);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct);

            LogShPresidentDiscard(game.InviteCode, userId, discardIndex);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_president_discard",
                Payload = new { invite_code = game.InviteCode, president_id = userId, discard_index = discardIndex }
            });
            return new ShDiscardResult(ShError.None, new ShGameSnapshot(game, players));
        }
        finally { Gate.Release(); }
    }

    public async Task<ShEnactResult> ChancellorEnactAsync(long userId, int enactIndex, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var me = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (me == null) return EnactFail(ShError.NotInGame);
            var game = await db.SecretHitlerGames.FindAsync([me.InviteCode], ct);
            if (game == null) return EnactFail(ShError.NotInGame);
            var players = await db.SecretHitlerPlayers.Where(p => p.InviteCode == game.InviteCode).ToListAsync(ct);
            me = players.First(p => p.UserId == userId);

            var v = ShTransitions.ValidateChancellorEnact(game, me, enactIndex);
            if (v != ShValidation.Ok) return EnactFail(MapValidation(v));

            var after = ShTransitions.ApplyChancellorEnact(game, enactIndex, players);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (game.Status == ShStatus.Completed) await SettlePotAsync(game, players, ct);
            await db.SaveChangesAsync(ct);

            LogShChancellorEnact(game.InviteCode, userId, enactIndex, after.Enacted);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_chancellor_enact",
                Payload = new { invite_code = game.InviteCode, chancellor_id = userId, enact_index = enactIndex, policy = after.Enacted.ToString(), kind = after.Kind.ToString() }
            });
            return new ShEnactResult(ShError.None, new ShGameSnapshot(game, players), after);
        }
        finally { Gate.Release(); }
    }

    public async Task<ShLeaveResult> LeaveAsync(long userId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var me = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (me == null) return LeaveFail(ShError.NotInGame);
            var game = await db.SecretHitlerGames.FindAsync([me.InviteCode], ct);
            if (game == null) return LeaveFail(ShError.NotInGame);

            if (game.Status == ShStatus.Active)
            {
                return LeaveFail(ShError.GameInProgress);
            }

            var user = await db.Users.FindAsync([userId], ct);
            if (user != null) await economics.CreditAsync(user, game.BuyIn, "sh.leave", ct);
            game.Pot = Math.Max(0, game.Pot - game.BuyIn);

            db.SecretHitlerPlayers.Remove(me);
            await db.SaveChangesAsync(ct);

            var remaining = await db.SecretHitlerPlayers.Where(p => p.InviteCode == game.InviteCode).ToListAsync(ct);
            bool closed = false;
            if (remaining.Count == 0)
            {
                game.Status = ShStatus.Closed;
                closed = true;
                await db.SaveChangesAsync(ct);
            }

            LogShLeft(game.InviteCode, userId, closed);
            reporter.SendEvent(new EventData
            {
                EventType = "sh_leave",
                Payload = new { invite_code = game.InviteCode, user_id = userId, refunded = game.BuyIn, closed }
            });
            return new ShLeaveResult(ShError.None, closed ? null : new ShGameSnapshot(game, remaining), closed);
        }
        finally { Gate.Release(); }
    }

    public async Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var me = await db.SecretHitlerPlayers.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (me == null) return;
            me.StateMessageId = messageId;
            await db.SaveChangesAsync(ct);
        }
        finally { Gate.Release(); }
    }

    private async Task SettlePotAsync(SecretHitlerGame game, List<SecretHitlerPlayer> players, CancellationToken ct)
    {
        var winners = game.Winner switch
        {
            ShWinner.Liberals => players.Where(p => p.Role == ShRole.Liberal).ToList(),
            ShWinner.Fascists => players.Where(p => p.Role == ShRole.Fascist || p.Role == ShRole.Hitler).ToList(),
            _ => new List<SecretHitlerPlayer>(),
        };

        if (winners.Count == 0 || game.Pot == 0) return;

        var share = game.Pot / winners.Count;
        var remainder = game.Pot - share * winners.Count;
        foreach (var w in winners)
        {
            var user = await db.Users.FindAsync([w.UserId], ct);
            if (user == null) continue;
            var payout = share + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            await economics.CreditAsync(user, payout, "sh.winnings", ct);
        }
        game.Pot = 0;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var chars = new char[5];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
            string code = new(chars);
            if (!await db.SecretHitlerGames.AnyAsync(g => g.InviteCode == code, ct))
                return code;
        }
        throw new InvalidOperationException("Failed to generate unique invite code");
    }

    [LoggerMessage(LogLevel.Information, "sh.create code={Code} host={UserId} buy_in={BuyIn}")]
    partial void LogShCreated(string code, long userId, int buyIn);

    [LoggerMessage(LogLevel.Information, "sh.join code={Code} user={UserId} pos={Pos} players={N}")]
    partial void LogShJoined(string code, long userId, int pos, int n);

    [LoggerMessage(LogLevel.Information, "sh.start code={Code} players={N}")]
    partial void LogShStarted(string code, int n);

    [LoggerMessage(LogLevel.Information, "sh.nominate code={Code} president={UserId} chancellor_pos={Pos}")]
    partial void LogShNominated(string code, long userId, int pos);

    [LoggerMessage(LogLevel.Information, "sh.vote code={Code} user={UserId} vote={Vote} kind={Kind}")]
    partial void LogShVoted(string code, long userId, ShVote vote, ShAfterVoteKind? kind);

    [LoggerMessage(LogLevel.Information, "sh.president_discard code={Code} user={UserId} idx={Idx}")]
    partial void LogShPresidentDiscard(string code, long userId, int idx);

    [LoggerMessage(LogLevel.Information, "sh.chancellor_enact code={Code} user={UserId} idx={Idx} policy={Policy}")]
    partial void LogShChancellorEnact(string code, long userId, int idx, ShPolicy policy);

    [LoggerMessage(LogLevel.Information, "sh.leave code={Code} user={UserId} closed={Closed}")]
    partial void LogShLeft(string code, long userId, bool closed);
}
