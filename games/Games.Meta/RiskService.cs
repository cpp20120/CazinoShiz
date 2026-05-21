using System.Text.Json;

namespace Games.Meta;

public interface IRiskService
{
    Task EvaluateGameCompletedAsync(GameCompletedMetaEvent ev, SeasonPlayer player, CancellationToken ct);
    Task<IReadOnlyList<RiskFlagView>> GetOpenAsync(long chatId, int limit, CancellationToken ct);
    Task<RiskResolveResult> UpdateStatusAsync(long flagId, string status, CancellationToken ct);
}

public sealed class RiskService(
    IMetaService meta,
    IRiskStore risks,
    IMetaHistoryStore history) : IRiskService
{
    public async Task EvaluateGameCompletedAsync(GameCompletedMetaEvent ev, SeasonPlayer player, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);

        if (ev.Multiplier >= 20m && ev.Payout > 0)
        {
            var severity = ev.Multiplier >= 50m ? "high" : "medium";
            var reason = $"Large multiplier: x{ev.Multiplier:0.##}";
            var evidence = Evidence(ev, player);
            await risks.UpsertOpenAsync(
                season,
                ev.ChatId,
                ev.UserId,
                ev.DisplayName,
                "large_multiplier",
                severity,
                reason,
                evidence,
                ct);
            await AppendFlaggedAsync(season, ev, "large_multiplier", severity, reason, evidence, ct);
        }

        if (ev.Payout >= 1_000)
        {
            var severity = ev.Payout >= 10_000 ? "critical" : "high";
            var reason = $"Large payout: {ev.Payout}";
            var evidence = Evidence(ev, player);
            await risks.UpsertOpenAsync(
                season,
                ev.ChatId,
                ev.UserId,
                ev.DisplayName,
                "large_payout",
                severity,
                reason,
                evidence,
                ct);
            await AppendFlaggedAsync(season, ev, "large_payout", severity, reason, evidence, ct);
        }

        if (player.GamesPlayed >= 20 && player.Wins * 100.0 / Math.Max(1, player.GamesPlayed) >= 85.0)
        {
            var severity = player.GamesPlayed >= 50 ? "high" : "medium";
            var reason = $"High win rate: {player.Wins}/{player.GamesPlayed}";
            var evidence = Evidence(ev, player);
            await risks.UpsertOpenAsync(
                season,
                ev.ChatId,
                ev.UserId,
                ev.DisplayName,
                "high_win_rate",
                severity,
                reason,
                evidence,
                ct);
            await AppendFlaggedAsync(season, ev, "high_win_rate", severity, reason, evidence, ct);
        }
    }

    public async Task<IReadOnlyList<RiskFlagView>> GetOpenAsync(long chatId, int limit, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await risks.GetOpenAsync(season, chatId, limit, ct);
    }

    public async Task<RiskResolveResult> UpdateStatusAsync(long flagId, string status, CancellationToken ct)
    {
        var result = await risks.UpdateStatusAsync(flagId, status, ct);
        if (result.Updated)
        {
            await history.AppendAsync(
                "risk.status_updated",
                "risk_flag",
                flagId.ToString(),
                null,
                null,
                null,
                new { flagId, status, result.Message },
                ct);
        }
        return result;
    }

    private Task AppendFlaggedAsync(MetaSeason season, GameCompletedMetaEvent ev, string kind, string severity, string reason, string evidence, CancellationToken ct) =>
        history.AppendAsync(
            "risk.flagged",
            "player",
            $"{season.Id}:{ev.ChatId}:{ev.UserId}",
            season.Id,
            ev.ChatId,
            ev.UserId,
            new
            {
                kind,
                severity,
                reason,
                evidenceJson = evidence,
                ev.GameKey,
                ev.Stake,
                ev.Payout,
                ev.IsWin,
                ev.Multiplier,
            },
            ct);

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
