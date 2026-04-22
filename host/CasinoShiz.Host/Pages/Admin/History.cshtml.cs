using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class HistoryModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<HistoryRow> Rows { get; private set; } = [];
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public long CoinsIn { get; private set; }
    public long CoinsOut { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Game { get; set; }

    [BindProperty(SupportsGet = true, Name = "user")]
    public string? UserFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? ChatId { get; set; }

    public IReadOnlyList<ChatPickRow> Chats { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);

        var chats = await conn.QueryAsync<ChatPickRow>(new CommandDefinition(
            """
            SELECT k.chat_id AS ChatId,
                   (k.chat_type || ' · ' || coalesce(k.title, k.username, k.chat_id::text)) AS Label
            FROM known_chats k
            ORDER BY k.last_seen_at DESC
            LIMIT 200
            """, cancellationToken: ct));
        Chats = chats.ToList();

        // Each emoji game publishes `<module>.throw_completed` / `.roll_completed`
        // with a consistent JSON shape: { UserId, ChatId, Face, Bet, Multiplier, Payout }.
        // Horse has { UserId, HorseId, Amount, RaceDate } for bet placement and
        // { RaceDate, WinnerHorseId, BetsCount, PayoutsCount, Pot } for race finish.
        // Dice (slots) has { UserId, DiceValue, Prize, Loss }.
        const string sql = """
            SELECT id AS Id,
                   split_part(event_type, '.', 1) AS Game,
                   event_type AS EventType,
                   occurred_at AS OccurredAt,
                   COALESCE(
                       (payload ->> 'UserId')::bigint,
                       (payload ->> 'HostUserId')::bigint,
                       0
                   ) AS UserId,
                   COALESCE(
                       (payload ->> 'Bet')::int,
                       (payload ->> 'Amount')::int,
                       (payload ->> 'BuyIn')::int,
                       (payload ->> 'Loss')::int,
                       0
                   ) AS Bet,
                   COALESCE(
                       (payload ->> 'Payout')::int,
                       (payload ->> 'Prize')::int,
                       0
                   ) AS Payout,
                   (payload ->> 'Multiplier')::int AS Multiplier,
                   COALESCE(
                       (payload ->> 'Face')::int,
                       (payload ->> 'DiceValue')::int,
                       (payload ->> 'HorseId')::int
                   ) AS Face,
                   COALESCE((payload ->> 'ChatId')::bigint, 0) AS ChatScopeId,
                   payload::text AS PayloadJson
            FROM event_log
            WHERE event_type IN (
                'darts.throw_completed',
                'basketball.throw_completed',
                'bowling.roll_completed',
                'dicecube.roll_completed',
                'dice.roll_completed',
                'horse.bet_placed',
                'horse.race_finished'
            )
              AND (@game = '' OR split_part(event_type, '.', 1) = @game)
              AND (@user = '' OR (payload ->> 'UserId') = @user)
              AND (
                  @chatId IS NULL
                  OR (payload ? 'ChatId' AND (payload ->> 'ChatId')::bigint = @chatId)
              )
            ORDER BY id DESC
            LIMIT 500
            """;
        var rows = await conn.QueryAsync<HistoryRow>(new CommandDefinition(
            sql, new { game = Game ?? "", user = UserFilter ?? "", chatId = ChatId }, cancellationToken: ct));
        Rows = rows.ToList();

        foreach (var r in Rows)
        {
            CoinsIn += r.Bet;
            CoinsOut += r.Payout;
            if (r.Payout > 0) Wins++;
            else if (r.Bet > 0) Losses++;
        }
    }
}

public sealed record HistoryRow(
    long Id,
    string Game,
    string EventType,
    DateTimeOffset OccurredAt,
    long UserId,
    int Bet,
    int Payout,
    int? Multiplier,
    int? Face,
    long ChatScopeId,
    string PayloadJson);

public sealed record ChatPickRow(long ChatId, string Label);
