using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Poker.Domain;

namespace CasinoShiz.Services.Poker.Presentation;

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

    public static string RenderTable(PokerTable table, IList<PokerSeat> seats, long viewerUserId)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🃏 <b>Стол {table.InviteCode}</b> · {Locales.PokerPhaseName(table.Phase)}");

        var community = table.Phase switch
        {
            PokerPhase.PreFlop or PokerPhase.None => RenderCards(""),
            PokerPhase.Flop => RenderCards(table.CommunityCards, 3),
            PokerPhase.Turn => RenderCards(table.CommunityCards, 4),
            _ => RenderCards(table.CommunityCards, 5),
        };
        sb.AppendLine($"Общие: {community}");
        sb.AppendLine($"Банк: <b>{table.Pot}</b> · Ставка: <b>{table.CurrentBet}</b>");
        sb.AppendLine();

        var sorted = seats.OrderBy(s => s.Position).ToList();
        foreach (var s in sorted)
        {
            var marker = "";
            if (s.Position == table.ButtonSeat) marker += "🔘";
            if (s.Position == table.CurrentSeat && table.Status == PokerTableStatus.HandActive) marker += "➡️";
            var status = s.Status switch
            {
                PokerSeatStatus.Folded => " <i>(фолд)</i>",
                PokerSeatStatus.AllIn => " <i>(олл-ин)</i>",
                PokerSeatStatus.SittingOut => " <i>(не играет)</i>",
                _ => "",
            };
            var bet = s.CurrentBet > 0 ? $" · ставка {s.CurrentBet}" : "";
            var you = s.UserId == viewerUserId ? " (ты)" : "";
            sb.AppendLine($"{marker} {s.DisplayName}{you} — {s.Stack}{bet}{status}");
        }

        var me = sorted.FirstOrDefault(s => s.UserId == viewerUserId);
        if (me == null || string.IsNullOrEmpty(me.HoleCards)) return sb.ToString().TrimEnd();
        sb.AppendLine();
        sb.AppendLine($"Твои карты: <b>{RenderCards(me.HoleCards)}</b>");
        if (table.Status != PokerTableStatus.HandActive || me.Position != table.CurrentSeat)
            return sb.ToString().TrimEnd();
        var toCall = Math.Max(0, table.CurrentBet - me.CurrentBet);
        if (toCall > 0)
            sb.AppendLine($"Чтобы уравнять: <b>{toCall}</b>");
        else
            sb.AppendLine("Можно чекать.");

        return sb.ToString().TrimEnd();
    }

    public static string RenderShowdown(PokerTable table, IList<PokerSeat> seats, IEnumerable<(PokerSeat seat, HandRank rank, int won)> results)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🃏 <b>Шоудаун</b>");
        sb.AppendLine($"Общие: {RenderCards(table.CommunityCards, 5)}");
        sb.AppendLine();

        foreach (var (seat, rank, won) in results)
        {
            var cards = string.IsNullOrEmpty(seat.HoleCards) ? "—" : RenderCards(seat.HoleCards);
            var line = $"{seat.DisplayName} — {cards} · {HandEvaluator.CategoryNameRu(rank.Category)}";
            if (won > 0) line += $" · <b>+{won}</b>";
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }
}
