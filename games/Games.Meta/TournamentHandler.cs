using System.Net;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Meta;

[Command("/tournament")]
[Command("/tour")]
public sealed class TournamentHandler(ITournamentService tournaments) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;
        if (!msg.Text.StartsWith("/tournament", StringComparison.OrdinalIgnoreCase) &&
            !msg.Text.StartsWith("/tour", StringComparison.OrdinalIgnoreCase))
            return;

        var user = msg.From;
        if (user is null) return;

        var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await ReplyHelpAsync(ctx, msg);
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "create": await HandleCreateAsync(ctx, msg, user, parts); break;
            case "join": await HandleJoinAsync(ctx, msg, user, parts); break;
            case "status":
            case "info": await HandleStatusAsync(ctx, msg, parts); break;
            case "players": await HandlePlayersAsync(ctx, msg, parts); break;
            case "bracket": await HandleBracketAsync(ctx, msg, parts); break;
            case "report": await HandleReportAsync(ctx, msg, user, parts); break;
            case "list":
            case "open": await HandleListAsync(ctx, msg); break;
            case "start": await HandleStartAsync(ctx, msg, user, parts); break;
            case "finish": await HandleFinishAsync(ctx, msg, user, parts); break;
            case "cancel": await HandleCancelAsync(ctx, msg, user, parts); break;
            default: await ReplyHelpAsync(ctx, msg); break;
        }
    }

    private async Task HandleCreateAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 5 || !int.TryParse(parts[3], out var entryFee) || !int.TryParse(parts[4], out var maxPlayers))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament create &lt;game&gt; &lt;entryFee&gt; &lt;maxPlayers&gt;</code>");
            return;
        }
        var result = await tournaments.CreateAsync(msg.Chat.Id, user.Id, parts[2], entryFee, maxPlayers, ctx.Ct);
        await SendHtmlAsync(ctx, msg, result.Tournament is null
            ? $"❌ {Html(result.Message)}"
            : $"🏆 {Html(result.Message)} ID <code>{result.Tournament.Id}</code> · game <b>{Html(result.Tournament.GameKey)}</b> · fee <b>{result.Tournament.EntryFee}</b> · players <code>0/{result.Tournament.MaxPlayers}</code>");
    }

    private async Task HandleJoinAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 3 || !long.TryParse(parts[2], out var tournamentId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament join &lt;id&gt;</code>");
            return;
        }
        var result = await tournaments.JoinAsync(tournamentId, user.Id, msg.Chat.Id, DisplayName(user), ctx.Ct);
        await SendHtmlAsync(ctx, msg, result.Tournament is null
            ? $"❌ {Html(result.Message)}"
            : $"✅ {Html(result.Message)} Турнир <code>{result.Tournament.Id}</code>: <code>{result.Tournament.PlayerCount}/{result.Tournament.MaxPlayers}</code>, prize <b>{result.Tournament.PrizePool}</b>");
    }

    private async Task HandleStatusAsync(UpdateContext ctx, Message msg, string[] parts)
    {
        if (parts.Length < 3 || !long.TryParse(parts[2], out var tournamentId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament status &lt;id&gt;</code>");
            return;
        }
        var t = await tournaments.GetAsync(tournamentId, ctx.Ct);
        await SendHtmlAsync(ctx, msg, t is null ? "❌ Турнир не найден." : FormatTournament(t));
    }

    private async Task HandlePlayersAsync(UpdateContext ctx, Message msg, string[] parts)
    {
        if (parts.Length < 3 || !long.TryParse(parts[2], out var tournamentId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament players &lt;id&gt;</code>");
            return;
        }
        var players = await tournaments.GetPlayersAsync(tournamentId, ctx.Ct);
        if (players.Count == 0)
        {
            await SendHtmlAsync(ctx, msg, "👥 Участников пока нет.");
            return;
        }
        var lines = new List<string> { $"👥 <b>Участники турнира #{tournamentId}</b>" };
        foreach (var p in players.Take(40))
            lines.Add($"• <b>{Html(p.DisplayName)}</b> — <code>{Html(p.Status)}</code> · <code>{p.UserId}</code>");
        if (players.Count > 40) lines.Add($"…и ещё {players.Count - 40} участников.");
        await SendHtmlAsync(ctx, msg, string.Join("\n", lines));
    }

    private async Task HandleBracketAsync(UpdateContext ctx, Message msg, string[] parts)
    {
        if (parts.Length < 3 || !long.TryParse(parts[2], out var tournamentId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament bracket &lt;id&gt;</code>");
            return;
        }
        var matches = await tournaments.GetMatchesAsync(tournamentId, ctx.Ct);
        if (matches.Count == 0)
        {
            await SendHtmlAsync(ctx, msg, "🏆 Сетка ещё не создана. Creator должен выполнить <code>/tournament start &lt;id&gt;</code>.");
            return;
        }
        var lines = new List<string> { $"🏆 <b>Сетка турнира #{tournamentId}</b>" };
        foreach (var group in matches.GroupBy(x => x.Round).OrderBy(x => x.Key))
        {
            lines.Add($"\n<b>Round {group.Key}</b>");
            foreach (var m in group.OrderBy(x => x.MatchIndex))
            {
                var p1 = m.Player1UserId is null ? "—" : $"{Html(m.Player1DisplayName ?? m.Player1UserId.Value.ToString())} <code>{m.Player1UserId}</code>";
                var p2 = m.Player2UserId is null ? "—" : $"{Html(m.Player2DisplayName ?? m.Player2UserId.Value.ToString())} <code>{m.Player2UserId}</code>";
                var victor = m.VictorUserId is null ? "" : $" · victor <code>{m.VictorUserId}</code>";
                lines.Add($"#<code>{m.Id}</code> [{Html(m.Status)}] {p1} vs {p2}{victor}");
            }
        }
        await SendHtmlAsync(ctx, msg, string.Join("\n", lines));
    }

    private async Task HandleReportAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 4 || !long.TryParse(parts[2], out var matchId) || !long.TryParse(parts[3], out var victorUserId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament report &lt;matchId&gt; &lt;victorUserId&gt;</code>");
            return;
        }
        var result = await tournaments.ReportMatchAsync(matchId, user.Id, victorUserId, ctx.Ct);
        await SendHtmlAsync(ctx, msg, !result.Updated
            ? $"❌ {Html(result.Message)}"
            : result.Finished
                ? $"🏆 {Html(result.Message)} Победитель: <b>{Html(result.Victor?.DisplayName ?? victorUserId.ToString())}</b>."
                : $"✅ {Html(result.Message)} Матч <code>{matchId}</code> закрыт.");
    }

    private async Task HandleListAsync(UpdateContext ctx, Message msg)
    {
        var open = await tournaments.GetOpenAsync(msg.Chat.Id, 10, ctx.Ct);
        if (open.Count == 0)
        {
            await SendHtmlAsync(ctx, msg, "🏆 Открытых турниров нет. Создай: <code>/tournament create dice 100 8</code>");
            return;
        }
        var lines = new List<string> { "🏆 <b>Открытые турниры</b>" };
        foreach (var t in open)
            lines.Add($"#{t.Id}: <b>{Html(t.GameKey)}</b> · fee <b>{t.EntryFee}</b> · <code>{t.PlayerCount}/{t.MaxPlayers}</code> · prize <b>{t.PrizePool}</b>");
        await SendHtmlAsync(ctx, msg, string.Join("\n", lines));
    }

    private async Task HandleStartAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 3 || !long.TryParse(parts[2], out var tournamentId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament start &lt;id&gt;</code>");
            return;
        }
        var ok = await tournaments.StartAsync(tournamentId, user.Id, ctx.Ct);
        await SendHtmlAsync(ctx, msg, ok
            ? $"🚀 Турнир <code>{tournamentId}</code> стартовал. Сетка: <code>/tournament bracket {tournamentId}</code>. Репорт: <code>/tournament report &lt;matchId&gt; &lt;victorUserId&gt;</code>"
            : "❌ Не удалось стартовать турнир: нужен creator, статус open, минимум 2 участника и ещё не созданная сетка.");
    }

    private async Task HandleFinishAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 4 || !long.TryParse(parts[2], out var tournamentId) || !long.TryParse(parts[3], out var victorUserId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament finish &lt;id&gt; &lt;winnerUserId&gt;</code>");
            return;
        }
        var before = await tournaments.GetAsync(tournamentId, ctx.Ct);
        var winner = await tournaments.FinishAsync(tournamentId, user.Id, victorUserId, ctx.Ct);
        await SendHtmlAsync(ctx, msg, winner is null
            ? "❌ Не удалось завершить турнир: нужен creator, статус started и winner из joined players."
            : $"🏆 Турнир <code>{tournamentId}</code> завершён. Победитель: <b>{Html(winner.DisplayName)}</b>. Prize paid: <b>{before?.PrizePool ?? 0}</b>");
    }

    private async Task HandleCancelAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 3 || !long.TryParse(parts[2], out var tournamentId))
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/tournament cancel &lt;id&gt;</code>");
            return;
        }
        var players = await tournaments.CancelAsync(tournamentId, user.Id, ctx.Ct);
        await SendHtmlAsync(ctx, msg, players is null
            ? "❌ Не удалось отменить турнир: нужен creator и статус open/started."
            : $"🧾 Турнир <code>{tournamentId}</code> отменён. Refund выдан участникам: <b>{players.Count}</b>.");
    }

    private static string FormatTournament(TournamentInfo t) => string.Join("\n", [
        $"🏆 <b>Турнир #{t.Id}</b>",
        $"Игра: <b>{Html(t.GameKey)}</b>",
        $"Тип: <code>{Html(t.Type)}</code>",
        $"Статус: <code>{Html(t.Status)}</code>",
        $"Entry fee: <b>{t.EntryFee}</b>",
        $"Игроки: <code>{t.PlayerCount}/{t.MaxPlayers}</code>",
        $"Prize pool: <b>{t.PrizePool}</b>",
        $"Создан: <code>{t.CreatedAt:yyyy-MM-dd HH:mm 'UTC'}</code>",
    ]);

    private Task ReplyHelpAsync(UpdateContext ctx, Message msg) => SendHtmlAsync(ctx, msg, string.Join("\n", [
        "🏆 <b>Турниры</b>",
        "<code>/tournament create &lt;game&gt; &lt;entryFee&gt; &lt;maxPlayers&gt;</code>",
        "<code>/tournament join &lt;id&gt;</code>",
        "<code>/tournament status &lt;id&gt;</code>",
        "<code>/tournament players &lt;id&gt;</code>",
        "<code>/tournament list</code>",
        "<code>/tournament start &lt;id&gt;</code>",
        "<code>/tournament bracket &lt;id&gt;</code>",
        "<code>/tournament report &lt;matchId&gt; &lt;victorUserId&gt;</code>",
        "<code>/tournament finish &lt;id&gt; &lt;winnerUserId&gt;</code>",
        "<code>/tournament cancel &lt;id&gt;</code>",
    ]));

    private Task SendHtmlAsync(UpdateContext ctx, Message msg, string text) =>
        ctx.Bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ctx.Ct);

    private static string DisplayName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Username)) return "@" + user.Username;
        var name = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(name) ? user.Id.ToString() : name;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
