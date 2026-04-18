using CasinoShiz.Helpers;
using Telegram.Bot.Types.ReplyMarkups;

namespace CasinoShiz.Services.Blackjack;

public static class BlackjackRenderer
{
    public static string Render(BlackjackSnapshot snap)
    {
        var dealerCards = snap.DealerHoleRevealed
            ? string.Join(" ", snap.DealerCards.Select(Format))
            : $"{Format(snap.DealerCards[0])} 🂠";

        var dealerTotalLabel = snap.DealerHoleRevealed ? $" ({snap.DealerTotal})" : "";
        var playerCards = string.Join(" ", snap.PlayerCards.Select(Format));

        var lines = new List<string>
        {
            $"🃏 <b>Блэкджек</b> — ставка: {snap.Bet}",
            "",
            $"Дилер: {dealerCards}{dealerTotalLabel}",
            $"Ты: {playerCards} ({snap.PlayerTotal})",
        };

        if (snap.Outcome.HasValue)
        {
            lines.Add("");
            lines.Add(Locales.BlackjackOutcome(snap.Outcome.Value, snap.Bet, snap.Payout));
            lines.Add($"Баланс: {snap.PlayerCoins}");
        }

        return string.Join("\n", lines);
    }

    public static InlineKeyboardMarkup? BuildKeyboard(BlackjackSnapshot snap)
    {
        if (snap.Outcome.HasValue) return null;
        var row1 = new[]
        {
            InlineKeyboardButton.WithCallbackData("🃏 Ещё", "bj:hit"),
            InlineKeyboardButton.WithCallbackData("✋ Стоп", "bj:stand"),
        };
        if (!snap.CanDouble) return new InlineKeyboardMarkup([row1]);
        var row2 = new[] { InlineKeyboardButton.WithCallbackData("💰 Удвоить", "bj:double") };
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
