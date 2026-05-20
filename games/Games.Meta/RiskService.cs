using System.Text.Json;

namespace Games.Meta;

public interface IRiskService
{
    Task EvaluateGameCompletedAsync(GameCompletedMetaEvent ev, SeasonPlayer player, CancellationToken ct);
    Task<IReadOnlyList<RiskFlagView>> GetOpenAsync(long chatId, int limit, CancellationToken ct);
    Task<RiskResolveResult> UpdateStatusAsync(long flagId, string status, CancellationToken ct);
}

public sealed class RiskService(IMetaService meta, IRiskStore risks) : IRiskService
{
    public async Task EvaluateGameCompletedAsync(GameCompletedMetaEvent ev, SeasonPlayer player, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);

        if (ev.Multiplier >= 20m && ev.Payout > 0)
        {
            await risks.UpsertOpenAsync(
                season,
                ev.ChatId,
                ev.UserId,
                ev.DisplayName,
                "large_multiplier",
                ev.Multiplier >= 50m ? "high" : "medium",
                $"Large multiplier: x{ev.Multiplier:0.##}",
                Evidence(ev, player),
                ct);
        }

        if (ev.Payout >= 1_000)
        {
            await risks.UpsertOpenAsync(
                season,
                ev.ChatId,
                ev.UserId,
                ev.DisplayName,
                "large_payout",
                ev.Payout >= 10_000 ? "critical" : "high",
                $"Large payout: {ev.Payout}",
                Evidence(ev, player),
                ct);
        }

        if (player.GamesPlayed >= 20 && player.Wins * 100.0 / Math.Max(1, player.GamesPlayed) >= 85.0)
        {
            await risks.UpsertOpenAsync(
                season,
                ev.ChatId,
                ev.UserId,
                ev.DisplayName,
                "high_win_rate",
                player.GamesPlayed >= 50 ? "high" : "medium",
                $"High win rate: {player.Wins}/{player.GamesPlayed}",
                Evidence(ev, player),
                ct);
        }
    }

    public async Task<IReadOnlyList<RiskFlagView>> GetOpenAsync(long chatId, int limit, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await risks.GetOpenAsync(season, chatId, limit, ct);
    }

    public Task<RiskResolveResult> UpdateStatusAsync(long flagId, string status, CancellationToken ct) =>
        risks.UpdateStatusAsync(flagId, status, ct);

    private static string Evidence(GameCompletedMetaEvent ev, SeasonPlayer player) => JsonSerializer.Serialize(new
    {
        ev.GameKey,
        ev.Stake,
        ev.Payout,
        ev.IsWin,
        ev.Multiplier,
        ev.OccurredAt,
        player.Xp,
        player.Level,
        player.Rating,
        player.GamesPlayed,
        player.Wins,
        player.Losses,
        player.TotalStaked,
        player.TotalPayout,
    });
}
