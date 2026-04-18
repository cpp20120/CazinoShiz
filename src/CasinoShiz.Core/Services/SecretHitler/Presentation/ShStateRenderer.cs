using CasinoShiz.Data.Entities;
using CasinoShiz.Services.SecretHitler.Domain;
using Telegram.Bot.Types.ReplyMarkups;

namespace CasinoShiz.Services.SecretHitler.Presentation;

public static class ShStateRenderer
{
    public static string RenderBoard(SecretHitlerGame game, List<SecretHitlerPlayer> players)
    {
        var libTrack = RenderTrack(game.LiberalPolicies, ShTransitions.LiberalWinThreshold, "🟦", "▫️");
        var facTrack = RenderTrack(game.FascistPolicies, ShTransitions.FascistWinThreshold, "🟥", "▫️");
        var electionTrack = RenderTrack(game.ElectionTracker, ShTransitions.ElectionTrackerCap, "⚠️", "▫️");

        var presidentName = NameByPosition(players, game.CurrentPresidentPosition) ?? "—";
        var chancellorName = game.NominatedChancellorPosition >= 0
            ? NameByPosition(players, game.NominatedChancellorPosition) ?? "—"
            : "—";
        var phaseLabel = PhaseLabel(game.Phase);

        var lines = new List<string>
        {
            $"<b>🗳 Secret Hitler</b> · стол <code>{game.InviteCode}</code> · банк <b>{game.Pot}</b>",
            $"Либералы: {libTrack} ({game.LiberalPolicies}/{ShTransitions.LiberalWinThreshold})",
            $"Фашисты:  {facTrack} ({game.FascistPolicies}/{ShTransitions.FascistWinThreshold})",
            $"Трекер выборов: {electionTrack}",
            "",
            $"Фаза: <b>{phaseLabel}</b>",
            $"Президент: <b>{presidentName}</b>"
        };
        if (game.Phase is ShPhase.Election or ShPhase.LegislativePresident or ShPhase.LegislativeChancellor)
            lines.Add($"Канцлер: <b>{chancellorName}</b>");

        if (game.Phase == ShPhase.Election)
        {
            lines.Add("");
            var alive = players.Where(p => p.IsAlive).OrderBy(p => p.Position).ToList();
            var voted = alive.Count(p => p.LastVote != ShVote.None);
            lines.Add($"Голосов: <b>{voted}/{alive.Count}</b>");
        }

        if (game.Phase == ShPhase.GameEnd)
        {
            lines.Add("");
            lines.Add(RenderEndSummary(game, players));
        }
        else
        {
            lines.Add("");
            lines.Add("Игроки:");
            foreach (var p in players.OrderBy(p => p.Position))
            {
                var marker = p.Position == game.CurrentPresidentPosition ? "👑"
                    : p.Position == game.NominatedChancellorPosition ? "🎩"
                    : p.IsAlive ? "•" : "💀";
                lines.Add($"  {marker} <b>{p.DisplayName}</b> <span class=\"muted\">(#{p.Position})</span>");
            }
        }

        return string.Join("\n", lines);
    }

    public static string RenderRoleCard(SecretHitlerPlayer me, List<SecretHitlerPlayer> players, int playerCount)
    {
        var roleName = me.Role switch
        {
            ShRole.Liberal => "🟦 Либерал",
            ShRole.Fascist => "🟥 Фашист",
            ShRole.Hitler => "🟥 <b>Гитлер</b>",
            _ => "?",
        };
        var lines = new List<string>
        {
            $"Твоя роль: {roleName}",
        };

        if (me.Role == ShRole.Fascist)
        {
            var teammates = players.Where(p => p.Position != me.Position && (p.Role == ShRole.Fascist || p.Role == ShRole.Hitler)).ToList();
            lines.Add("Твои союзники:");
            foreach (var t in teammates)
            {
                var label = t.Role == ShRole.Hitler ? "Гитлер" : "фашист";
                lines.Add($"  • <b>{t.DisplayName}</b> — {label}");
            }
        }
        else if (me.Role == ShRole.Hitler && playerCount <= 6)
        {
            var fascists = players.Where(p => p.Role == ShRole.Fascist).ToList();
            lines.Add("Твои фашисты:");
            foreach (var f in fascists) lines.Add($"  • <b>{f.DisplayName}</b>");
        }

        return string.Join("\n", lines);
    }

    public static InlineKeyboardMarkup? BuildBoardMarkup(SecretHitlerGame game, SecretHitlerPlayer viewer, List<SecretHitlerPlayer> players)
    {
        if (game.Phase == ShPhase.Nomination && viewer.Position == game.CurrentPresidentPosition)
        {
            var candidates = EligibleChancellors(game, viewer, players);
            var rows = candidates.Chunk(2).Select(chunk =>
                chunk.Select(c => InlineKeyboardButton.WithCallbackData($"🎩 {c.DisplayName}", $"sh:nominate:{c.Position}")).ToArray()
            ).ToArray();
            return rows.Length == 0 ? null : new InlineKeyboardMarkup(rows);
        }

        if (game.Phase == ShPhase.Election && viewer.IsAlive && viewer.LastVote == ShVote.None)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Ja!", "sh:vote:ja"),
                    InlineKeyboardButton.WithCallbackData("❌ Nein!", "sh:vote:nein"),
                }
            });
        }

        if (game.Phase == ShPhase.LegislativePresident && viewer.Position == game.CurrentPresidentPosition)
        {
            var draw = ShPolicyDeck.Parse(game.PresidentDraw);
            var buttons = draw.Select((p, i) =>
                InlineKeyboardButton.WithCallbackData(
                    $"🗑 {PolicyLabel(p)} #{i + 1}",
                    $"sh:discard:{i}")).ToArray();
            return new InlineKeyboardMarkup(new[] { buttons });
        }

        if (game.Phase == ShPhase.LegislativeChancellor && viewer.Position == game.NominatedChancellorPosition)
        {
            var received = ShPolicyDeck.Parse(game.ChancellorReceived);
            var buttons = received.Select((p, i) =>
                InlineKeyboardButton.WithCallbackData(
                    $"📜 {PolicyLabel(p)} #{i + 1}",
                    $"sh:enact:{i}")).ToArray();
            return new InlineKeyboardMarkup(new[] { buttons });
        }

        return null;
    }

    public static List<SecretHitlerPlayer> EligibleChancellors(
        SecretHitlerGame game, SecretHitlerPlayer president, List<SecretHitlerPlayer> players)
    {
        var alive = players.Where(p => p.IsAlive && p.Position != president.Position).ToList();
        int aliveCount = players.Count(p => p.IsAlive);
        return alive.Where(c =>
        {
            if (c.Position == game.LastElectedChancellorPosition) return false;
            if (aliveCount > 5 && c.Position == game.LastElectedPresidentPosition) return false;
            return true;
        }).OrderBy(c => c.Position).ToList();
    }

    public static string PhaseLabel(ShPhase phase) => phase switch
    {
        ShPhase.Nomination => "Выдвижение канцлера",
        ShPhase.Election => "Голосование",
        ShPhase.LegislativePresident => "Президент выбирает карту",
        ShPhase.LegislativeChancellor => "Канцлер принимает закон",
        ShPhase.GameEnd => "Игра окончена",
        _ => "—",
    };

    public static string PolicyLabel(ShPolicy policy) => policy == ShPolicy.Liberal ? "🟦 Либерал" : "🟥 Фашист";

    public static string RenderEndSummary(SecretHitlerGame game, List<SecretHitlerPlayer> players)
    {
        var winnerTeam = game.Winner switch
        {
            ShWinner.Liberals => "🟦 <b>Либералы побеждают!</b>",
            ShWinner.Fascists => "🟥 <b>Фашисты побеждают!</b>",
            _ => "Игра закончена",
        };
        var reason = game.WinReason switch
        {
            ShWinReason.LiberalPolicies => "Принято 5 либеральных законов.",
            ShWinReason.FascistPolicies => "Принято 6 фашистских законов.",
            ShWinReason.HitlerElected => "Гитлер избран канцлером (3+ фашистских).",
            ShWinReason.HitlerExecuted => "Гитлер казнён.",
            _ => "",
        };
        var reveal = string.Join("\n", players.OrderBy(p => p.Position).Select(p =>
        {
            var role = p.Role switch
            {
                ShRole.Liberal => "🟦 Либерал",
                ShRole.Fascist => "🟥 Фашист",
                ShRole.Hitler => "🟥 <b>Гитлер</b>",
                _ => "?",
            };
            return $"  • <b>{p.DisplayName}</b> — {role}";
        }));
        return $"{winnerTeam}\n<i>{reason}</i>\n\nРоли:\n{reveal}";
    }

    public static string RenderVoteReveal(List<SecretHitlerPlayer> players)
    {
        var lines = new List<string> { "Результаты голосования:" };
        foreach (var p in players.Where(p => p.IsAlive).OrderBy(p => p.Position))
        {
            var mark = p.LastVote switch
            {
                ShVote.Ja => "✅ Ja",
                ShVote.Nein => "❌ Nein",
                _ => "—",
            };
            lines.Add($"  {mark}  <b>{p.DisplayName}</b>");
        }
        return string.Join("\n", lines);
    }

    private static string RenderTrack(int filled, int total, string onChar, string offChar)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < total; i++) sb.Append(i < filled ? onChar : offChar);
        return sb.ToString();
    }

    private static string? NameByPosition(List<SecretHitlerPlayer> players, int position) =>
        players.FirstOrDefault(p => p.Position == position)?.DisplayName;
}
