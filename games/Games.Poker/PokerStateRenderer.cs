// ─────────────────────────────────────────────────────────────────────────────
// PokerStateRenderer — pure presentation: converts a TableSnapshot + viewer
// into Telegram HTML text for that viewer's private chat.
//
// Takes an ILocalizer so Russian phase names and status badges can be pulled
// from the module's locale bundle rather than hard-coded. Everything else
// (layout, card glyphs, formatting) stays static — it doesn't vary per culture
// in any meaningful way and bloating the locale with `"· "` separators isn't
// worth it.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text;
using BotFramework.Host;
using Games.Poker.Domain;

namespace Games.Poker;

public static class PokerStateRenderer
{
    public static string RenderCard(string card)
    {
        if (string.IsNullOrEmpty(card) || card.Length < 2) return "??";
        var rank = card[0] switch
        {
            'T' => "10",
            _ => card[0].ToString(),
        };
        var suit = card[1] switch
        {
            'S' => "♠",
            'H' => "♥",
            'D' => "♦",
            'C' => "♣",
            _ => "?",
        };
        return $"{rank}{suit}";
    }

    public static string RenderCards(string cards, int padToCount = 0)
    {
        var parts = Deck.Parse(cards);
        var rendered = parts.Select(RenderCard).ToList();
        while (rendered.Count < padToCount)
            rendered.Add("🂠");
        return rendered.Count == 0 ? "—" : string.Join(" ", rendered);
    }

    public static string RenderTable(PokerTable table, IList<PokerSeat> seats, long viewerUserId, ILocalizer localizer)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🃏 <b>{string.Format(localizer.Get("poker", "state.header"), table.InviteCode)}</b> · {PhaseName(table.Phase, localizer)}");

        var community = table.Phase switch
        {
            PokerPhase.PreFlop or PokerPhase.None => RenderCards(""),
            PokerPhase.Flop => RenderCards(table.CommunityCards, 3),
            PokerPhase.Turn => RenderCards(table.CommunityCards, 4),
            _ => RenderCards(table.CommunityCards, 5),
        };
        sb.AppendLine(string.Format(localizer.Get("poker", "state.community"), community));
        sb.AppendLine(string.Format(localizer.Get("poker", "state.pot_bet"), table.Pot, table.CurrentBet));
        if (table.Status is PokerTableStatus.Seating or PokerTableStatus.HandComplete)
            sb.AppendLine($"<i>{localizer.Get("poker", "state.waiting_for_start")}</i>");
        sb.AppendLine();

        var sorted = seats.OrderBy(s => s.Position).ToList();
        foreach (var s in sorted)
        {
            var marker = "";
            if (s.Position == table.ButtonSeat) marker += "🔘";
            if (s.Position == table.CurrentSeat && table.Status == PokerTableStatus.HandActive) marker += "➡️";
            var status = s.Status switch
            {
                PokerSeatStatus.Folded => $" <i>({localizer.Get("poker", "seat.folded")})</i>",
                PokerSeatStatus.AllIn => $" <i>({localizer.Get("poker", "seat.allin")})</i>",
                PokerSeatStatus.SittingOut => $" <i>({localizer.Get("poker", "seat.sitting_out")})</i>",
                _ => "",
            };
            var bet = s.CurrentBet > 0 ? $" · {string.Format(localizer.Get("poker", "seat.bet"), s.CurrentBet)}" : "";
            var you = s.UserId == viewerUserId ? $" ({localizer.Get("poker", "seat.you")})" : "";
            sb.AppendLine($"{marker} {s.DisplayName}{you} — {s.Stack}{bet}{status}");
        }

        var me = sorted.FirstOrDefault(s => s.UserId == viewerUserId);
        if (me == null || string.IsNullOrEmpty(me.HoleCards)) return sb.ToString().TrimEnd();
        sb.AppendLine();
        sb.AppendLine(string.Format(localizer.Get("poker", "state.your_cards"), RenderCards(me.HoleCards)));
        if (table.Status != PokerTableStatus.HandActive || me.Position != table.CurrentSeat)
            return sb.ToString().TrimEnd();
        var toCall = Math.Max(0, table.CurrentBet - me.CurrentBet);
        if (toCall > 0)
            sb.AppendLine(string.Format(localizer.Get("poker", "state.to_call"), toCall));
        else
            sb.AppendLine(localizer.Get("poker", "state.can_check"));

        return sb.ToString().TrimEnd();
    }

    public static string RenderShowdown(
        PokerTable table,
        IEnumerable<(PokerSeat Seat, HandRank? Rank, int Won, string HoleCards)> results,
        ILocalizer localizer)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🃏 <b>{localizer.Get("poker", "showdown.header")}</b>");
        sb.AppendLine(string.Format(localizer.Get("poker", "state.community"), RenderCards(table.CommunityCards, 5)));
        sb.AppendLine();

        foreach (var (seat, rank, won, holeCards) in results)
        {
            var cards = string.IsNullOrEmpty(holeCards) ? "—" : RenderCards(holeCards);
            var line = $"{seat.DisplayName} — <b>{cards}</b>";
            if (rank.HasValue) line += $" · {HandEvaluator.CategoryNameRu(rank.Value.Category)}";
            if (won > 0) line += $" · <b>+{won}</b>";
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    private static string PhaseName(PokerPhase phase, ILocalizer localizer) => phase switch
    {
        PokerPhase.PreFlop => localizer.Get("poker", "phase.preflop"),
        PokerPhase.Flop => localizer.Get("poker", "phase.flop"),
        PokerPhase.Turn => localizer.Get("poker", "phase.turn"),
        PokerPhase.River => localizer.Get("poker", "phase.river"),
        PokerPhase.Showdown => localizer.Get("poker", "phase.showdown"),
        _ => localizer.Get("poker", "phase.seating"),
    };
}
