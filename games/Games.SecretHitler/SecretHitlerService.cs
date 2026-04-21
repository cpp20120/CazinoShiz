using System.Collections.Concurrent;
using BotFramework.Host;
using BotFramework.Sdk;
using Games.SecretHitler.Domain;
using Microsoft.Extensions.Options;
using static Games.SecretHitler.ShResultHelpers;

namespace Games.SecretHitler;

public interface ISecretHitlerService
{
    Task<(ShGameSnapshot? Snapshot, SecretHitlerPlayer? Me)> FindMyGameAsync(long userId, CancellationToken ct);
    Task<ShCreateResult> CreateGameAsync(long userId, string displayName, long chatId, CancellationToken ct);
    Task<ShJoinResult> JoinGameAsync(long userId, string displayName, long chatId, string code, CancellationToken ct);
    Task<ShStartResult> StartGameAsync(long userId, CancellationToken ct);
    Task<ShNominateResult> NominateAsync(long userId, int chancellorPosition, CancellationToken ct);
    Task<ShVoteResult> VoteAsync(long userId, ShVote vote, CancellationToken ct);
    Task<ShDiscardResult> PresidentDiscardAsync(long userId, int discardIndex, CancellationToken ct);
    Task<ShEnactResult> ChancellorEnactAsync(long userId, int enactIndex, CancellationToken ct);
    Task<ShLeaveResult> LeaveAsync(long userId, CancellationToken ct);
    Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct);
}

public sealed partial class SecretHitlerService(
    ISecretHitlerGameStore games,
    ISecretHitlerPlayerStore players,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDomainEventBus events,
    IOptions<SecretHitlerOptions> options,
    ILogger<SecretHitlerService> logger) : ISecretHitlerService
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

    private readonly SecretHitlerOptions _opts = options.Value;

    public async Task<(ShGameSnapshot? Snapshot, SecretHitlerPlayer? Me)> FindMyGameAsync(long userId, CancellationToken ct)
    {
        var player = await players.FindByUserAsync(userId, ct);
        if (player == null) return (null, null);
        var game = await games.FindAsync(player.InviteCode, ct);
        if (game == null) return (null, null);
        var list = await players.ListByGameAsync(game.InviteCode, ct);
        return (new ShGameSnapshot(game, list), list.First(p => p.UserId == userId));
    }

    public async Task<ShCreateResult> CreateGameAsync(long userId, string displayName, long chatId, CancellationToken ct)
    {
        var gate = GetGate($"u:{userId}");
        await gate.WaitAsync(ct);
        try
        {
            var buyIn = _opts.BuyIn;
            await economics.EnsureUserAsync(userId, displayName, ct);
            var balance = await economics.GetBalanceAsync(userId, ct);
            if (balance < buyIn) return CreateFail(ShError.NotEnoughCoins);
            if (await players.AnyForUserAsync(userId, ct)) return CreateFail(ShError.AlreadyInGame);

            var code = await GenerateUniqueCodeAsync(ct);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var game = new SecretHitlerGame
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
            };
            var player = new SecretHitlerPlayer
            {
                InviteCode = code,
                Position = 0,
                UserId = userId,
                DisplayName = displayName,
                ChatId = chatId,
                IsAlive = true,
                JoinedAt = now,
            };

            if (!await economics.TryDebitAsync(userId, buyIn, "sh.create", ct))
                return CreateFail(ShError.NotEnoughCoins);

            await games.InsertAsync(game, ct);
            await players.InsertAsync(player, ct);

            LogShCreated(code, userId, buyIn);
            analytics.Track("sh", "create", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["invite_code"] = code,
                ["buy_in"] = buyIn,
            });
            await events.PublishAsync(new SecretHitlerGameCreated(code, userId, buyIn, now), ct);

            return new ShCreateResult(ShError.None, code, buyIn);
        }
        finally { gate.Release(); }
    }

    public async Task<ShJoinResult> JoinGameAsync(long userId, string displayName, long chatId, string code, CancellationToken ct)
    {
        code = code.ToUpperInvariant();
        var gate = GetGate(code);
        await gate.WaitAsync(ct);
        try
        {
            var buyIn = _opts.BuyIn;
            await economics.EnsureUserAsync(userId, displayName, ct);
            var balance = await economics.GetBalanceAsync(userId, ct);
            if (balance < buyIn) return JoinFail(ShError.NotEnoughCoins);
            if (await players.AnyForUserAsync(userId, ct)) return JoinFail(ShError.AlreadyInGame);

            var game = await games.FindAsync(code, ct);
            if (game == null || game.Status == ShStatus.Closed || game.Status == ShStatus.Completed)
                return JoinFail(ShError.GameNotFound);
            if (game.Status != ShStatus.Lobby) return JoinFail(ShError.GameInProgress);

            var list = await players.ListByGameAsync(code, ct);
            if (list.Count >= ShRoleDealer.MaxPlayers) return JoinFail(ShError.GameFull);

            int position = 0;
            var used = list.Select(p => p.Position).ToHashSet();
            while (used.Contains(position)) position++;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var newPlayer = new SecretHitlerPlayer
            {
                InviteCode = code,
                Position = position,
                UserId = userId,
                DisplayName = displayName,
                ChatId = chatId,
                IsAlive = true,
                JoinedAt = now,
            };
            if (!await economics.TryDebitAsync(userId, buyIn, "sh.join", ct))
                return JoinFail(ShError.NotEnoughCoins);

            await players.InsertAsync(newPlayer, ct);
            game.Pot += buyIn;
            game.LastActionAt = now;
            await games.UpdateAsync(game, ct);

            list.Add(newPlayer);
            LogShJoined(code, userId, position, list.Count);
            analytics.Track("sh", "join", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["invite_code"] = code,
                ["position"] = position,
                ["seated"] = list.Count,
                ["buy_in"] = buyIn,
            });
            await events.PublishAsync(new SecretHitlerPlayerJoined(code, userId, position, buyIn, now), ct);

            return new ShJoinResult(ShError.None, new ShGameSnapshot(game, list), list.Count, ShRoleDealer.MaxPlayers);
        }
        finally { gate.Release(); }
    }

    public async Task<ShStartResult> StartGameAsync(long userId, CancellationToken ct)
    {
        var precheck = await players.FindByUserAsync(userId, ct);
        if (precheck == null) return StartFail(ShError.NotInGame);

        var gate = GetGate(precheck.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var me = await players.FindByUserAsync(userId, ct);
            if (me == null) return StartFail(ShError.NotInGame);
            var game = await games.FindAsync(me.InviteCode, ct);
            if (game == null) return StartFail(ShError.NotInGame);
            if (game.HostUserId != userId) return StartFail(ShError.NotHost);
            if (game.Status != ShStatus.Lobby) return StartFail(ShError.GameInProgress);

            var list = await players.ListByGameAsync(game.InviteCode, ct);
            if (list.Count < ShRoleDealer.MinPlayers) return StartFail(ShError.NotEnoughPlayers);

            ShTransitions.StartGame(game, list);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await games.UpdateAsync(game, ct);
            foreach (var p in list) await players.UpdateAsync(p, ct);

            LogShStarted(game.InviteCode, list.Count);
            analytics.Track("sh", "start", new Dictionary<string, object?>
            {
                ["invite_code"] = game.InviteCode,
                ["players"] = list.Count,
            });
            await events.PublishAsync(
                new SecretHitlerGameStarted(game.InviteCode, list.Count, game.LastActionAt), ct);

            return new ShStartResult(ShError.None, new ShGameSnapshot(game, list));
        }
        finally { gate.Release(); }
    }

    public async Task<ShNominateResult> NominateAsync(long userId, int chancellorPosition, CancellationToken ct)
    {
        var precheck = await players.FindByUserAsync(userId, ct);
        if (precheck == null) return NominateFail(ShError.NotInGame);

        var gate = GetGate(precheck.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var me = await players.FindByUserAsync(userId, ct);
            if (me == null) return NominateFail(ShError.NotInGame);
            var game = await games.FindAsync(me.InviteCode, ct);
            if (game == null) return NominateFail(ShError.NotInGame);
            var list = await players.ListByGameAsync(game.InviteCode, ct);

            var v = ShTransitions.ValidateNomination(game, me, chancellorPosition, list);
            if (v != ShValidation.Ok) return NominateFail(MapValidation(v));

            ShTransitions.ApplyNomination(game, chancellorPosition, list);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await games.UpdateAsync(game, ct);
            foreach (var p in list) await players.UpdateAsync(p, ct);

            LogShNominated(game.InviteCode, userId, chancellorPosition);
            analytics.Track("sh", "nominate", new Dictionary<string, object?>
            {
                ["invite_code"] = game.InviteCode,
                ["president_id"] = userId,
                ["chancellor_pos"] = chancellorPosition,
            });
            return new ShNominateResult(ShError.None, new ShGameSnapshot(game, list));
        }
        finally { gate.Release(); }
    }

    public async Task<ShVoteResult> VoteAsync(long userId, ShVote vote, CancellationToken ct)
    {
        var precheck = await players.FindByUserAsync(userId, ct);
        if (precheck == null) return VoteFail(ShError.NotInGame);

        var gate = GetGate(precheck.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var me = await players.FindByUserAsync(userId, ct);
            if (me == null) return VoteFail(ShError.NotInGame);
            var game = await games.FindAsync(me.InviteCode, ct);
            if (game == null) return VoteFail(ShError.NotInGame);
            var list = await players.ListByGameAsync(game.InviteCode, ct);
            me = list.First(p => p.UserId == userId);

            var v = ShTransitions.ValidateVote(game, me);
            if (v != ShValidation.Ok) return VoteFail(MapValidation(v));

            var after = ShTransitions.ApplyVote(game, me, vote, list);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            IReadOnlyList<(long UserId, int Amount)> payouts = Array.Empty<(long, int)>();
            if (game.Status == ShStatus.Completed)
                payouts = await SettlePotAsync(game, list, ct);

            await games.UpdateAsync(game, ct);
            foreach (var p in list) await players.UpdateAsync(p, ct);

            LogShVoted(game.InviteCode, userId, vote, after?.Kind);
            analytics.Track("sh", "vote", new Dictionary<string, object?>
            {
                ["invite_code"] = game.InviteCode,
                ["user_id"] = userId,
                ["vote"] = vote.ToString(),
                ["resolved"] = after?.Kind.ToString(),
            });

            if (game.Status == ShStatus.Completed)
                await events.PublishAsync(
                    new SecretHitlerGameEnded(game.InviteCode, game.Winner, game.WinReason, payouts, game.LastActionAt),
                    ct);

            return new ShVoteResult(ShError.None, new ShGameSnapshot(game, list), after);
        }
        finally { gate.Release(); }
    }

    public async Task<ShDiscardResult> PresidentDiscardAsync(long userId, int discardIndex, CancellationToken ct)
    {
        var precheck = await players.FindByUserAsync(userId, ct);
        if (precheck == null) return DiscardFail(ShError.NotInGame);

        var gate = GetGate(precheck.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var me = await players.FindByUserAsync(userId, ct);
            if (me == null) return DiscardFail(ShError.NotInGame);
            var game = await games.FindAsync(me.InviteCode, ct);
            if (game == null) return DiscardFail(ShError.NotInGame);
            var list = await players.ListByGameAsync(game.InviteCode, ct);
            me = list.First(p => p.UserId == userId);

            var v = ShTransitions.ValidatePresidentDiscard(game, me, discardIndex);
            if (v != ShValidation.Ok) return DiscardFail(MapValidation(v));

            ShTransitions.ApplyPresidentDiscard(game, discardIndex);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await games.UpdateAsync(game, ct);

            LogShPresidentDiscard(game.InviteCode, userId, discardIndex);
            analytics.Track("sh", "president_discard", new Dictionary<string, object?>
            {
                ["invite_code"] = game.InviteCode,
                ["president_id"] = userId,
                ["discard_index"] = discardIndex,
            });
            return new ShDiscardResult(ShError.None, new ShGameSnapshot(game, list));
        }
        finally { gate.Release(); }
    }

    public async Task<ShEnactResult> ChancellorEnactAsync(long userId, int enactIndex, CancellationToken ct)
    {
        var precheck = await players.FindByUserAsync(userId, ct);
        if (precheck == null) return EnactFail(ShError.NotInGame);

        var gate = GetGate(precheck.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var me = await players.FindByUserAsync(userId, ct);
            if (me == null) return EnactFail(ShError.NotInGame);
            var game = await games.FindAsync(me.InviteCode, ct);
            if (game == null) return EnactFail(ShError.NotInGame);
            var list = await players.ListByGameAsync(game.InviteCode, ct);
            me = list.First(p => p.UserId == userId);

            var v = ShTransitions.ValidateChancellorEnact(game, me, enactIndex);
            if (v != ShValidation.Ok) return EnactFail(MapValidation(v));

            var after = ShTransitions.ApplyChancellorEnact(game, enactIndex, list);
            game.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            IReadOnlyList<(long UserId, int Amount)> payouts = Array.Empty<(long, int)>();
            if (game.Status == ShStatus.Completed)
                payouts = await SettlePotAsync(game, list, ct);

            await games.UpdateAsync(game, ct);
            foreach (var p in list) await players.UpdateAsync(p, ct);

            LogShChancellorEnact(game.InviteCode, userId, enactIndex, after.Enacted);
            analytics.Track("sh", "chancellor_enact", new Dictionary<string, object?>
            {
                ["invite_code"] = game.InviteCode,
                ["chancellor_id"] = userId,
                ["enact_index"] = enactIndex,
                ["policy"] = after.Enacted.ToString(),
                ["kind"] = after.Kind.ToString(),
            });

            if (game.Status == ShStatus.Completed)
                await events.PublishAsync(
                    new SecretHitlerGameEnded(game.InviteCode, game.Winner, game.WinReason, payouts, game.LastActionAt),
                    ct);

            return new ShEnactResult(ShError.None, new ShGameSnapshot(game, list), after);
        }
        finally { gate.Release(); }
    }

    public async Task<ShLeaveResult> LeaveAsync(long userId, CancellationToken ct)
    {
        var precheck = await players.FindByUserAsync(userId, ct);
        if (precheck == null) return LeaveFail(ShError.NotInGame);

        var gate = GetGate(precheck.InviteCode);
        await gate.WaitAsync(ct);
        try
        {
            var me = await players.FindByUserAsync(userId, ct);
            if (me == null) return LeaveFail(ShError.NotInGame);
            var game = await games.FindAsync(me.InviteCode, ct);
            if (game == null) return LeaveFail(ShError.NotInGame);

            if (game.Status == ShStatus.Active) return LeaveFail(ShError.GameInProgress);

            await economics.CreditAsync(userId, game.BuyIn, "sh.leave", ct);
            game.Pot = Math.Max(0, game.Pot - game.BuyIn);

            await players.DeleteAsync(me.InviteCode, me.Position, ct);

            var remainingCount = await players.CountByGameAsync(game.InviteCode, ct);
            bool closed = false;
            if (remainingCount == 0)
            {
                game.Status = ShStatus.Closed;
                closed = true;
            }
            await games.UpdateAsync(game, ct);

            var remaining = closed
                ? []
                : await players.ListByGameAsync(game.InviteCode, ct);

            LogShLeft(game.InviteCode, userId, closed);
            analytics.Track("sh", "leave", new Dictionary<string, object?>
            {
                ["invite_code"] = game.InviteCode,
                ["user_id"] = userId,
                ["refunded"] = game.BuyIn,
                ["closed"] = closed,
            });
            return new ShLeaveResult(ShError.None, closed ? null : new ShGameSnapshot(game, remaining), closed);
        }
        finally { gate.Release(); }
    }

    public async Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        var gate = GetGate($"u:{userId}");
        await gate.WaitAsync(ct);
        try
        {
            await players.UpsertStateMessageAsync(userId, messageId, ct);
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<(long UserId, int Amount)>> SettlePotAsync(
        SecretHitlerGame game, List<SecretHitlerPlayer> list, CancellationToken ct)
    {
        var winners = game.Winner switch
        {
            ShWinner.Liberals => list.Where(p => p.Role == ShRole.Liberal).ToList(),
            ShWinner.Fascists => list.Where(p => p.Role == ShRole.Fascist || p.Role == ShRole.Hitler).ToList(),
            _ => [],
        };

        if (winners.Count == 0 || game.Pot == 0) return Array.Empty<(long, int)>();

        var share = game.Pot / winners.Count;
        var remainder = game.Pot - share * winners.Count;
        var payouts = new List<(long, int)>(winners.Count);
        foreach (var w in winners)
        {
            var payout = share + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            await economics.CreditAsync(w.UserId, payout, "sh.winnings", ct);
            payouts.Add((w.UserId, payout));
        }
        game.Pot = 0;
        return payouts;
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
            if (!await games.CodeExistsAsync(code, ct))
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
