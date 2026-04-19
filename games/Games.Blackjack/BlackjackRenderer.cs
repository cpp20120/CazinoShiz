using BotFramework.Host;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.Blackjack;

public static class BlackjackRenderer
{
    public static string Render(BlackjackSnapshot snap, ILocalizer localizer)
    {
        var dealerCards = snap.DealerHoleRevealed
            ? string.Join(" ", snap.DealerCards.Select(Format))
            : $"{Format(snap.DealerCards[0])} 🂠";

        var dealerTotalLabel = snap.DealerHoleRevealed ? $" ({snap.DealerTotal})" : "";
        var playerCards = string.Join(" ", snap.PlayerCards.Select(Format));

        var lines = new List<string>
        {
            string.Format(localizer.Get("blackjack", "render.header"), snap.Bet),
            "",
            string.Format(localizer.Get("blackjack", "render.dealer"), dealerCards, dealerTotalLabel),
            string.Format(localizer.Get("blackjack", "render.player"), playerCards, snap.PlayerTotal),
        };

        if (snap.Outcome.HasValue)
        {
            var net = snap.Payout - snap.Bet;
            var outcomeKey = snap.Outcome.Value switch
            {
                BlackjackOutcome.PlayerBlackjack => "outcome.player_blackjack",
                BlackjackOutcome.PlayerWin => "outcome.player_win",
                BlackjackOutcome.DealerBust => "outcome.dealer_bust",
                BlackjackOutcome.PlayerBust => "outcome.player_bust",
                BlackjackOutcome.DealerWin => "outcome.dealer_win",
                BlackjackOutcome.Push => "outcome.push",
                _ => "",
            };
            var outcomeText = outcomeKey.Length == 0
                ? ""
                : string.Format(localizer.Get("blackjack", outcomeKey), net, snap.Bet);
            lines.Add("");
            if (!string.IsNullOrEmpty(outcomeText)) lines.Add(outcomeText);
            lines.Add(string.Format(localizer.Get("blackjack", "render.balance"), snap.PlayerCoins));
        }

        return string.Join("\n", lines);
    }

    public static InlineKeyboardMarkup? BuildKeyboard(BlackjackSnapshot snap, ILocalizer localizer)
    {
        if (snap.Outcome.HasValue) return null;
        var row1 = new[]
        {
            InlineKeyboardButton.WithCallbackData(localizer.Get("blackjack", "btn.hit"), "bj:hit"),
            InlineKeyboardButton.WithCallbackData(localizer.Get("blackjack", "btn.stand"), "bj:stand"),
        };
        if (!snap.CanDouble) return new InlineKeyboardMarkup([row1]);
        var row2 = new[] { InlineKeyboardButton.WithCallbackData(localizer.Get("blackjack", "btn.double"), "bj:double") };
        return new InlineKeyboardMarkup([row1, row2]);
    }

    private static string Format(string card)
    {
        var rank = card[..^1] switch { "T" => "10", var r => r };
        var suit = card[^1] switch
        {
            'S' => "♠",
            'H' => "♥",
            'D' => "♦",
            'C' => "♣",
            _ => "?",
        };
        return rank + suit;
    }
}
