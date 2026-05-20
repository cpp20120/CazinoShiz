using BotFramework.Host;

namespace Games.Meta;

public interface ITournamentService
{
    Task<TournamentCreateResult> CreateAsync(long chatId, long userId, string gameKey, int entryFee, int maxPlayers, CancellationToken ct);
    Task<TournamentJoinResult> JoinAsync(long tournamentId, long userId, long chatId, string displayName, CancellationToken ct);
    Task<TournamentInfo?> GetAsync(long tournamentId, CancellationToken ct);
    Task<IReadOnlyList<TournamentInfo>> GetOpenAsync(long chatId, int limit, CancellationToken ct);
    Task<IReadOnlyList<TournamentPlayerInfo>> GetPlayersAsync(long tournamentId, CancellationToken ct);
    Task<bool> StartAsync(long tournamentId, long userId, CancellationToken ct);
    Task<TournamentPlayerInfo?> FinishAsync(long tournamentId, long actorUserId, long winnerUserId, CancellationToken ct);
    Task<IReadOnlyList<TournamentPlayerInfo>?> CancelAsync(long tournamentId, long actorUserId, CancellationToken ct);
}

public sealed class TournamentService(
    IMetaService meta,
    ITournamentStore tournaments,
    IEconomicsService economics) : ITournamentService
{
    public async Task<TournamentCreateResult> CreateAsync(long chatId, long userId, string gameKey, int entryFee, int maxPlayers, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await tournaments.CreateAsync(season, chatId, userId, gameKey, entryFee, maxPlayers, ct);
    }

    public async Task<TournamentJoinResult> JoinAsync(long tournamentId, long userId, long chatId, string displayName, CancellationToken ct)
    {
        var tournament = await tournaments.GetAsync(tournamentId, ct);
        if (tournament is null) return new TournamentJoinResult(false, "Турнир не найден.");
        if (tournament.ChatId != chatId) return new TournamentJoinResult(false, "Этот турнир создан в другом чате.");

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        if (tournament.EntryFee > 0)
        {
            var debit = await economics.TryDebitOnceAsync(
                userId,
                chatId,
                tournament.EntryFee,
                "tournament.entry_fee",
                $"tournament:entry:{tournament.Id}:{chatId}:{userId}",
                ct);
            if (debit.Rejected)
                return new TournamentJoinResult(false, "Недостаточно монет для entry fee.", tournament);
        }

        var joined = await tournaments.JoinAsync(tournamentId, userId, displayName, ct);
        if (!joined.Joined && tournament.EntryFee > 0)
        {
            await economics.CreditOnceAsync(
                userId,
                chatId,
                tournament.EntryFee,
                "tournament.entry_fee.refund",
                $"tournament:entry-refund:{tournament.Id}:{chatId}:{userId}",
                ct);
        }

        return joined;
    }

    public Task<TournamentInfo?> GetAsync(long tournamentId, CancellationToken ct) =>
        tournaments.GetAsync(tournamentId, ct);

    public async Task<IReadOnlyList<TournamentInfo>> GetOpenAsync(long chatId, int limit, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await tournaments.GetOpenAsync(season, chatId, limit, ct);
    }

    public Task<IReadOnlyList<TournamentPlayerInfo>> GetPlayersAsync(long tournamentId, CancellationToken ct) =>
        tournaments.GetPlayersAsync(tournamentId, ct);

    public Task<bool> StartAsync(long tournamentId, long userId, CancellationToken ct) =>
        tournaments.StartAsync(tournamentId, userId, ct);

    public async Task<TournamentPlayerInfo?> FinishAsync(long tournamentId, long actorUserId, long winnerUserId, CancellationToken ct)
    {
        var before = await tournaments.GetAsync(tournamentId, ct);
        if (before is null) return null;

        var winner = await tournaments.FinishAsync(tournamentId, actorUserId, winnerUserId, ct);
        if (winner is null) return null;

        if (before.PrizePool > 0)
        {
            var amount = (int)Math.Min(int.MaxValue, before.PrizePool);
            await economics.CreditOnceAsync(
                winner.UserId,
                before.ChatId,
                amount,
                "tournament.prize",
                $"tournament:prize:{before.Id}:{winner.UserId}",
                ct);
        }

        return winner;
    }

    public async Task<IReadOnlyList<TournamentPlayerInfo>?> CancelAsync(long tournamentId, long actorUserId, CancellationToken ct)
    {
        var before = await tournaments.GetAsync(tournamentId, ct);
        if (before is null) return null;

        var players = await tournaments.CancelAsync(tournamentId, actorUserId, ct);
        if (players is null) return null;

        if (before.EntryFee > 0)
        {
            foreach (var player in players)
            {
                await economics.CreditOnceAsync(
                    player.UserId,
                    before.ChatId,
                    before.EntryFee,
                    "tournament.cancel.refund",
                    $"tournament:cancel-refund:{before.Id}:{player.UserId}",
                    ct);
            }
        }

        return players;
    }
}
