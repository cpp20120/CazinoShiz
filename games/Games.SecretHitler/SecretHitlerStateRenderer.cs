using System.Text;
using BotFramework.Host;
using Games.SecretHitler.Domain;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.SecretHitler;

public static class SecretHitlerStateRenderer
{
    public static string RenderBoard(SecretHitlerGame game, List<SecretHitlerPlayer> players, ILocalizer localizer)
    {
        var libTrack = RenderTrack(game.LiberalPolicies, ShTransitions.LiberalWinThreshold, "🟦", "▫️");
        var facTrack = RenderTrack(game.FascistPolicies, ShTransitions.FascistWinThreshold, "🟥", "▫️");
        var electionTrack = RenderTrack(game.ElectionTracker, ShTransitions.ElectionTrackerCap, "⚠️", "▫️");

        var presidentName = NameByPosition(players, game.CurrentPresidentPosition) ?? "—";
        var chancellorName = game.NominatedChancellorPosition >= 0
            ? NameByPosition(players, game.NominatedChancellorPosition) ?? "—"
            : "—";
        var phaseLabel = PhaseLabel(game.Phase, localizer);

        var lines = new List<string>
        {
            string.Format(Loc(localizer, "board.header"), game.InviteCode, game.Pot),
            string.Format(Loc(localizer, "board.liberals"), libTrack, game.LiberalPolicies, ShTransitions.LiberalWinThreshold),
            string.Format(Loc(localizer, "board.fascists"), facTrack, game.FascistPolicies, ShTransitions.FascistWinThreshold),
            string.Format(Loc(localizer, "board.election_tracker"), electionTrack),
            "",
            string.Format(Loc(localizer, "board.phase"), phaseLabel),
            string.Format(Loc(localizer, "board.president"), presidentName),
        };
        if (game.Phase is ShPhase.Election or ShPhase.LegislativePresident or ShPhase.LegislativeChancellor)
            lines.Add(string.Format(Loc(localizer, "board.chancellor"), chancellorName));

        if (game.Phase == ShPhase.Election)
        {
            lines.Add("");
            var alive = players.Where(p => p.IsAlive).OrderBy(p => p.Position).ToList();
            var voted = alive.Count(p => p.LastVote != ShVote.None);
            lines.Add(string.Format(Loc(localizer, "board.votes"), voted, alive.Count));
        }

        if (game.Phase == ShPhase.GameEnd)
        {
            lines.Add("");
            lines.Add(RenderEndSummary(game, players, localizer));
        }
        else
        {
            lines.Add("");
            lines.Add(Loc(localizer, "board.players"));
            lines.AddRange(from p in players.OrderBy(p => p.Position)
                let marker = p.Position == game.CurrentPresidentPosition ? "👑"
                    : p.Position == game.NominatedChancellorPosition ? "🎩"
                    : p.IsAlive ? "•" : "💀"
                select $"  {marker} <b>{p.DisplayName}</b> <span class=\"muted\">(#{p.Position})</span>");
        }

        return string.Join("\n", lines);
    }

    public static string RenderRoleCard(SecretHitlerPlayer me, List<SecretHitlerPlayer> players, int playerCount, ILocalizer localizer)
    {
        var roleName = RoleLabel(me.Role, localizer);
        var lines = new List<string>
        {
            string.Format(Loc(localizer, "role.your_role"), roleName),
        };

        switch (me.Role)
        {
            case ShRole.Fascist:
            {
                var teammates = players.Where(p => p.Position != me.Position && (p.Role == ShRole.Fascist || p.Role == ShRole.Hitler)).ToList();
                lines.Add(Loc(localizer, "role.your_allies"));
                lines.AddRange(from t in teammates let label = t.Role == ShRole.Hitler ? Loc(localizer, "role.hitler_short") : Loc(localizer, "role.fascist_short") select $"  • <b>{t.DisplayName}</b> — {label}");
                break;
            }
            case ShRole.Hitler when playerCount <= 6:
            {
                var fascists = players.Where(p => p.Role == ShRole.Fascist).ToList();
                lines.Add(Loc(localizer, "role.your_fascists"));
                lines.AddRange(fascists.Select(f => $"  • <b>{f.DisplayName}</b>"));
                break;
            }
        }

        return string.Join("\n", lines);
    }

    public static InlineKeyboardMarkup? BuildBoardMarkup(SecretHitlerGame game, SecretHitlerPlayer viewer, List<SecretHitlerPlayer> players, ILocalizer localizer)
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
            return new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData(Loc(localizer, "btn.ja"), "sh:vote:ja"),
                    InlineKeyboardButton.WithCallbackData(Loc(localizer, "btn.nein"), "sh:vote:nein")
                ]
            ]);
        }

        if (game.Phase == ShPhase.LegislativePresident && viewer.Position == game.CurrentPresidentPosition)
        {
            var draw = ShPolicyDeck.Parse(game.PresidentDraw);
            var buttons = draw.Select((p, i) =>
                InlineKeyboardButton.WithCallbackData(
                    string.Format(Loc(localizer, "btn.discard"), PolicyLabel(p, localizer), i + 1),
                    $"sh:discard:{i}")).ToArray();
            return new InlineKeyboardMarkup([buttons]);
        }

        if (game.Phase == ShPhase.LegislativeChancellor && viewer.Position == game.NominatedChancellorPosition)
        {
            var received = ShPolicyDeck.Parse(game.ChancellorReceived);
            var buttons = received.Select((p, i) =>
                InlineKeyboardButton.WithCallbackData(
                    string.Format(Loc(localizer, "btn.enact"), PolicyLabel(p, localizer), i + 1),
                    $"sh:enact:{i}")).ToArray();
            return new InlineKeyboardMarkup([buttons]);
        }

        return null;
    }

    private static List<SecretHitlerPlayer> EligibleChancellors(
        SecretHitlerGame game, SecretHitlerPlayer president, List<SecretHitlerPlayer> players)
    {
        var alive = players.Where(p => p.IsAlive && p.Position != president.Position).ToList();
        var aliveCount = players.Count(p => p.IsAlive);
        return alive.Where(c =>
        {
            if (c.Position == game.LastElectedChancellorPosition) return false;
            return aliveCount <= 5 || c.Position != game.LastElectedPresidentPosition;
        }).OrderBy(c => c.Position).ToList();
    }

    private static string PhaseLabel(ShPhase phase, ILocalizer localizer) => phase switch
    {
        ShPhase.Nomination => Loc(localizer, "phase.nomination"),
        ShPhase.Election => Loc(localizer, "phase.election"),
        ShPhase.LegislativePresident => Loc(localizer, "phase.legislative_president"),
        ShPhase.LegislativeChancellor => Loc(localizer, "phase.legislative_chancellor"),
        ShPhase.GameEnd => Loc(localizer, "phase.game_end"),
        _ => "—",
    };

    public static string PolicyLabel(ShPolicy policy, ILocalizer localizer) =>
        policy == ShPolicy.Liberal ? Loc(localizer, "policy.liberal") : Loc(localizer, "policy.fascist");

    public static string RoleLabel(ShRole role, ILocalizer localizer) => role switch
    {
        ShRole.Liberal => Loc(localizer, "role.liberal"),
        ShRole.Fascist => Loc(localizer, "role.fascist"),
        ShRole.Hitler => Loc(localizer, "role.hitler"),
        _ => "?",
    };

    public static string RenderEndSummary(SecretHitlerGame game, List<SecretHitlerPlayer> players, ILocalizer localizer)
    {
        var winnerTeam = game.Winner switch
        {
            ShWinner.Liberals => Loc(localizer, "end.liberals_win"),
            ShWinner.Fascists => Loc(localizer, "end.fascists_win"),
            _ => Loc(localizer, "end.generic"),
        };
        var reason = game.WinReason switch
        {
            ShWinReason.LiberalPolicies => Loc(localizer, "end.reason.liberal_policies"),
            ShWinReason.FascistPolicies => Loc(localizer, "end.reason.fascist_policies"),
            ShWinReason.HitlerElected => Loc(localizer, "end.reason.hitler_elected"),
            ShWinReason.HitlerExecuted => Loc(localizer, "end.reason.hitler_executed"),
            _ => "",
        };
        var reveal = string.Join("\n", players.OrderBy(p => p.Position).Select(p =>
        {
            var role = RoleLabel(p.Role, localizer);
            return $"  • <b>{p.DisplayName}</b> — {role}";
        }));
        return $"{winnerTeam}\n<i>{reason}</i>\n\n{Loc(localizer, "end.roles_header")}\n{reveal}";
    }

    public static string RenderVoteReveal(List<SecretHitlerPlayer> players, ILocalizer localizer)
    {
        var lines = new List<string> { Loc(localizer, "vote.reveal_header") };
        foreach (var p in players.Where(p => p.IsAlive).OrderBy(p => p.Position))
        {
            var mark = p.LastVote switch
            {
                ShVote.Ja => Loc(localizer, "vote.ja"),
                ShVote.Nein => Loc(localizer, "vote.nein"),
                _ => "—",
            };
            lines.Add($"  {mark}  <b>{p.DisplayName}</b>");
        }
        return string.Join("\n", lines);
    }

    private static string RenderTrack(int filled, int total, string onChar, string offChar)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < total; i++) sb.Append(i < filled ? onChar : offChar);
        return sb.ToString();
    }

    private static string? NameByPosition(List<SecretHitlerPlayer> players, int position) =>
        players.FirstOrDefault(p => p.Position == position)?.DisplayName;

    private static string Loc(ILocalizer localizer, string key) => localizer.Get("sh", key);
}
