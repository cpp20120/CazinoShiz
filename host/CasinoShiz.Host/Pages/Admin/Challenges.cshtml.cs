using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class ChallengesModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<ChallengeAdminRow> Rows { get; private set; } = [];
    public IReadOnlyList<ChallengeGameSummaryRow> ByGame { get; private set; } = [];
    public IReadOnlyList<ChallengeChatSummaryRow> ByChat { get; private set; } = [];
    public IReadOnlyList<ChallengeChatOptionRow> Chats { get; private set; } = [];
    public ChallengeTotals Totals { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Game { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? ChatId { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var filter = new
        {
            game = Game ?? "",
            status = Status ?? "",
            chatId = ChatId,
        };

        var chats = await conn.QueryAsync<ChallengeChatOptionRow>(new CommandDefinition("""
            SELECT DISTINCT
                d.chat_id AS ChatId,
                coalesce(k.chat_type || ' · ' || coalesce(k.title, k.username, k.chat_id::text), d.chat_id::text) AS Label
            FROM challenge_duels d
            LEFT JOIN known_chats k ON k.chat_id = d.chat_id
            ORDER BY Label
            """, cancellationToken: ct));
        Chats = chats.ToList();

        Totals = await conn.QuerySingleAsync<ChallengeTotals>(new CommandDefinition("""
            SELECT
                count(*)::int AS Total,
                count(*) FILTER (WHERE status = 'Pending')::int AS Pending,
                count(*) FILTER (WHERE status = 'Accepted')::int AS Accepted,
                count(*) FILTER (WHERE status = 'Completed')::int AS Completed,
                count(*) FILTER (WHERE status IN ('Declined', 'Failed'))::int AS Cancelled,
                coalesce(sum(amount * 2), 0)::bigint AS TotalPot
            FROM challenge_duels
            WHERE (@game = '' OR game = @game)
              AND (@status = '' OR status = @status)
              AND (@chatId IS NULL OR chat_id = @chatId)
            """, filter, cancellationToken: ct));

        var byGame = await conn.QueryAsync<ChallengeGameSummaryRow>(new CommandDefinition("""
            SELECT
                game AS Game,
                count(*)::int AS Total,
                count(*) FILTER (WHERE status = 'Pending')::int AS Pending,
                count(*) FILTER (WHERE status = 'Completed')::int AS Completed,
                coalesce(sum(amount * 2), 0)::bigint AS TotalPot,
                max(created_at) AS LastCreatedAt
            FROM challenge_duels
            WHERE (@status = '' OR status = @status)
              AND (@chatId IS NULL OR chat_id = @chatId)
            GROUP BY game
            ORDER BY count(*) DESC, game
            """, filter, cancellationToken: ct));
        ByGame = byGame.ToList();

        var byChat = await conn.QueryAsync<ChallengeChatSummaryRow>(new CommandDefinition("""
            SELECT
                d.chat_id AS ChatId,
                coalesce(k.chat_type || ' · ' || coalesce(k.title, k.username, k.chat_id::text), d.chat_id::text) AS ChatLabel,
                count(*)::int AS Total,
                count(*) FILTER (WHERE d.status = 'Pending')::int AS Pending,
                count(*) FILTER (WHERE d.status = 'Completed')::int AS Completed,
                coalesce(sum(d.amount * 2), 0)::bigint AS TotalPot,
                max(d.created_at) AS LastCreatedAt
            FROM challenge_duels d
            LEFT JOIN known_chats k ON k.chat_id = d.chat_id
            WHERE (@game = '' OR d.game = @game)
              AND (@status = '' OR d.status = @status)
            GROUP BY d.chat_id, k.chat_type, k.title, k.username, k.chat_id
            ORDER BY count(*) DESC, max(d.created_at) DESC
            LIMIT 100
            """, filter, cancellationToken: ct));
        ByChat = byChat.ToList();

        var rows = await conn.QueryAsync<ChallengeAdminRow>(new CommandDefinition("""
            SELECT
                d.id AS Id,
                d.chat_id AS ChatId,
                coalesce(k.chat_type || ' · ' || coalesce(k.title, k.username, k.chat_id::text), d.chat_id::text) AS ChatLabel,
                d.challenger_id AS ChallengerId,
                d.challenger_name AS ChallengerName,
                d.target_id AS TargetId,
                d.target_name AS TargetName,
                d.amount AS Amount,
                d.game AS Game,
                d.status AS Status,
                d.created_at AS CreatedAt,
                d.expires_at AS ExpiresAt,
                d.responded_at AS RespondedAt,
                d.completed_at AS CompletedAt
            FROM challenge_duels d
            LEFT JOIN known_chats k ON k.chat_id = d.chat_id
            WHERE (@game = '' OR d.game = @game)
              AND (@status = '' OR d.status = @status)
              AND (@chatId IS NULL OR d.chat_id = @chatId)
            ORDER BY d.created_at DESC
            LIMIT 500
            """, filter, cancellationToken: ct));
        Rows = rows.ToList();
    }

    public static readonly string[] Games =
    [
        "Dice",
        "DiceCube",
        "Darts",
        "Bowling",
        "Basketball",
        "Football",
        "Slots",
        "Horse",
        "Blackjack",
    ];

    public static readonly string[] Statuses =
    [
        "Pending",
        "Accepted",
        "Completed",
        "Declined",
        "Failed",
    ];
}

public sealed class ChallengeTotals
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Accepted { get; init; }
    public int Completed { get; init; }
    public int Cancelled { get; init; }
    public long TotalPot { get; init; }
}

public sealed class ChallengeGameSummaryRow
{
    public string Game { get; init; } = "";
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Completed { get; init; }
    public long TotalPot { get; init; }
    public DateTimeOffset? LastCreatedAt { get; init; }
}

public sealed class ChallengeChatSummaryRow
{
    public long ChatId { get; init; }
    public string ChatLabel { get; init; } = "";
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Completed { get; init; }
    public long TotalPot { get; init; }
    public DateTimeOffset? LastCreatedAt { get; init; }
}

public sealed class ChallengeAdminRow
{
    public Guid Id { get; init; }
    public long ChatId { get; init; }
    public string ChatLabel { get; init; } = "";
    public long ChallengerId { get; init; }
    public string ChallengerName { get; init; } = "";
    public long TargetId { get; init; }
    public string TargetName { get; init; } = "";
    public int Amount { get; init; }
    public string Game { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RespondedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record ChallengeChatOptionRow(long ChatId, string Label);
