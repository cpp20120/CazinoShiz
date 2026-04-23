using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class BetsModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<BetRow> Rows { get; private set; } = [];
    public IReadOnlyDictionary<string, int> Counts { get; private set; } =
        new Dictionary<string, int>();
    public long TotalAmount { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Game { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);

        const string sql = """
            SELECT game, user_id AS UserId, amount, chat_id AS ChatId,
                   note, created_at AS CreatedAt
            FROM (
                SELECT 'darts' AS game, user_id, amount, chat_id,
                       ('id=' || id::text || ' s=' || status::text)::text AS note, created_at FROM darts_rounds
                UNION ALL
                SELECT 'dicecube'   AS game, user_id, amount, chat_id, NULL::text AS note, created_at FROM dicecube_bets
                UNION ALL
                SELECT 'basketball' AS game, user_id, amount, chat_id, NULL::text AS note, created_at FROM basketball_bets
                UNION ALL
                SELECT 'bowling'    AS game, user_id, amount, chat_id, NULL::text AS note, created_at FROM bowling_bets
                UNION ALL
                SELECT 'blackjack'  AS game, user_id, bet AS amount, chat_id, NULL::text AS note, created_at FROM blackjack_hands
                UNION ALL
                SELECT 'horse'      AS game, user_id, amount, 0::bigint AS chat_id,
                       'race=' || race_date || ' horse=#' || (horse_id + 1)::text AS note, created_at
                FROM horse_bets
            ) all_bets
            WHERE (@game = '' OR game = @game)
            ORDER BY created_at DESC
            LIMIT 500
            """;
        var rows = await conn.QueryAsync<BetRow>(new CommandDefinition(
            sql, new { game = Game ?? "" }, cancellationToken: ct));
        Rows = rows.ToList();

        var counts = await conn.QueryAsync<(string Game, int Cnt, long Sum)>(new CommandDefinition("""
            SELECT 'darts'      AS game, COUNT(*)::int AS cnt, COALESCE(SUM(amount),0)::bigint AS sum FROM darts_rounds
            UNION ALL
            SELECT 'dicecube',   COUNT(*)::int,               COALESCE(SUM(amount),0)::bigint     FROM dicecube_bets
            UNION ALL
            SELECT 'basketball', COUNT(*)::int,               COALESCE(SUM(amount),0)::bigint     FROM basketball_bets
            UNION ALL
            SELECT 'bowling',    COUNT(*)::int,               COALESCE(SUM(amount),0)::bigint     FROM bowling_bets
            UNION ALL
            SELECT 'blackjack',  COUNT(*)::int,               COALESCE(SUM(bet),0)::bigint        FROM blackjack_hands
            UNION ALL
            SELECT 'horse',      COUNT(*)::int,               COALESCE(SUM(amount),0)::bigint     FROM horse_bets
            """, cancellationToken: ct));
        var list = counts.ToList();
        Counts = list.ToDictionary(x => x.Game, x => x.Cnt);
        TotalAmount = list.Sum(x => x.Sum);
    }
}

public sealed record BetRow(string Game, long UserId, int Amount, long ChatId, string? Note, DateTimeOffset CreatedAt);
