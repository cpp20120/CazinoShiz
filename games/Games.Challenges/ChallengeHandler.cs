using System.Net;
using BotFramework.Host;
using BotFramework.Sdk;
using Games.Blackjack.Domain;
using Games.Horse.Generators;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.Challenges;

[Command("/challenge")]
[CallbackPrefix("ch:")]
public sealed class ChallengeHandler(
    IChallengeService service,
    ILocalizer localizer) : IUpdateHandler
{
    private static readonly TimeSpan HorseGifFrameDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan HorseResultDelayBuffer = TimeSpan.FromSeconds(1);

    public async Task HandleAsync(UpdateContext ctx)
    {
        if (ctx.Update.CallbackQuery is { } cbq)
        {
            await HandleCallbackAsync(ctx, cbq);
            return;
        }

        if (ctx.Update.Message is { Text: not null } msg)
            await HandleCommandAsync(ctx, msg);
    }

    private async Task HandleCommandAsync(UpdateContext ctx, Message msg)
    {
        var challenger = msg.From;
        if (challenger?.Id is null or 0)
            return;

        var chatId = msg.Chat.Id;
        var challengerName = DisplayName(challenger);
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var args = StripCommand(msg.Text!).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (!TryParseCommand(msg, args, out var target, out var username, out var amount, out var game))
        {
            await ctx.Bot.SendMessage(chatId, Loc("usage"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        if (target is null)
        {
            target = await service.FindKnownUserByUsernameAsync(chatId, username!, ctx.Ct);
            if (target is null)
            {
                await ctx.Bot.SendMessage(chatId, string.Format(Loc("target.not_found"), Html(username!)),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            }
        }

        var result = await service.CreateAsync(
            challenger.Id,
            challengerName,
            target,
            chatId,
            amount,
            game,
            ctx.Ct);

        if (result.Error != ChallengeCreateError.None)
        {
            await ctx.Bot.SendMessage(chatId, CreateErrorText(result),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var challenge = result.Challenge!;
        await ctx.Bot.SendMessage(
            chatId,
            string.Format(
                Loc("created"),
                Mention(challenge.ChallengerId, challenge.ChallengerName),
                Mention(challenge.TargetId, challenge.TargetName),
                challenge.Amount,
                ChallengeGameCatalog.DisplayName(challenge.Game),
                ChallengeGameCatalog.Emoji(challenge.Game)),
            parseMode: ParseMode.Html,
            replyMarkup: BuildMarkup(challenge.Id),
            replyParameters: reply,
            cancellationToken: ctx.Ct);
    }

    private async Task HandleCallbackAsync(UpdateContext ctx, CallbackQuery cbq)
    {
        var (action, challengeId) = ParseCallback(cbq.Data);
        if (challengeId == Guid.Empty || action is not ("a" or "d"))
        {
            await AnswerCallbackAsync(ctx, cbq);
            return;
        }

        if (action == "d")
        {
            var decline = await service.DeclineAsync(challengeId, cbq.From.Id, ctx.Ct);
            await AnswerCallbackAsync(ctx, cbq, CallbackErrorText(decline), decline != ChallengeAcceptError.None);
            if (decline == ChallengeAcceptError.None && cbq.Message is not null)
                await ctx.Bot.EditMessageText(cbq.Message.Chat.Id, cbq.Message.MessageId, Loc("declined"),
                    cancellationToken: ctx.Ct);
            return;
        }

        var begun = await service.BeginAcceptAsync(challengeId, cbq.From.Id, ctx.Ct);
        if (begun.Error != ChallengeAcceptError.None)
        {
            await AnswerCallbackAsync(ctx, cbq, CallbackErrorText(begun.Error), true);
            return;
        }

        await AnswerCallbackAsync(ctx, cbq, Loc("accepted.toast"));
        var challenge = begun.Challenge!;
        if (cbq.Message is not null)
        {
            await ctx.Bot.EditMessageText(
                cbq.Message.Chat.Id,
                cbq.Message.MessageId,
                string.Format(
                    Loc("accepted"),
                    Mention(challenge.ChallengerId, challenge.ChallengerName),
                    Mention(challenge.TargetId, challenge.TargetName),
                    ChallengeGameCatalog.Emoji(challenge.Game)),
                parseMode: ParseMode.Html,
                cancellationToken: ctx.Ct);
        }

        if (challenge.Game == ChallengeGame.Horse)
            await RaceAndSettleAsync(ctx, challenge, cbq.Message?.MessageId);
        else if (challenge.Game == ChallengeGame.Blackjack)
            await BlackjackAndSettleAsync(ctx, challenge);
        else
            await RollAndSettleAsync(ctx, challenge, cbq.Message?.MessageId);
    }

    private async Task RollAndSettleAsync(UpdateContext ctx, Challenge challenge, int? replyToMessageId)
    {
        var emoji = ChallengeGameCatalog.Emoji(challenge.Game);
        var reply = replyToMessageId is null ? null : new ReplyParameters { MessageId = replyToMessageId.Value };

        try
        {
            var challengerDice = await ctx.Bot.SendDice(
                challenge.ChatId,
                emoji: emoji,
                replyParameters: reply,
                cancellationToken: ctx.Ct);
            var targetDice = await ctx.Bot.SendDice(
                challenge.ChatId,
                emoji: emoji,
                replyParameters: reply,
                cancellationToken: ctx.Ct);

            if (challengerDice.Dice is not { Value: > 0 } c || targetDice.Dice is not { Value: > 0 } t)
            {
                await service.FailAcceptedAsync(challenge, ctx.Ct);
                await ctx.Bot.SendMessage(challenge.ChatId, Loc("roll.failed"), cancellationToken: ctx.Ct);
                return;
            }

            await Task.Delay(4000, ctx.Ct);
            var result = await service.CompleteAcceptedAsync(challenge, c.Value, t.Value, ctx.Ct);
            await ctx.Bot.SendMessage(challenge.ChatId, ResultText(result),
                parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        }
        catch
        {
            await service.FailAcceptedAsync(challenge, CancellationToken.None);
            await ctx.Bot.SendMessage(challenge.ChatId, Loc("roll.failed"), cancellationToken: CancellationToken.None);
        }
    }

    private async Task RaceAndSettleAsync(UpdateContext ctx, Challenge challenge, int? replyToMessageId)
    {
        var reply = replyToMessageId is null ? null : new ReplyParameters { MessageId = replyToMessageId.Value };

        try
        {
            var winner = SpeedGenerator.GenPlaces(2);
            var frameCount = 0;
            var gifBytes = await Task.Run(() =>
            {
                var speeds = SpeedGenerator.CreateSpeeds(2, winner);
                var (frames, height, width) = HorseRaceRenderer.DrawHorses(speeds);
                frameCount = frames.Length;
                return GifEncoder.RenderFramesToGif(frames, width, height);
            }, ctx.Ct);

            await using var stream = new MemoryStream(gifBytes);
            await ctx.Bot.SendAnimation(
                challenge.ChatId,
                InputFile.FromStream(stream, "challenge-horse.gif"),
                caption: string.Format(
                    Loc("horse.caption"),
                    Mention(challenge.ChallengerId, challenge.ChallengerName),
                    Mention(challenge.TargetId, challenge.TargetName)),
                parseMode: ParseMode.Html,
                replyParameters: reply,
                cancellationToken: ctx.Ct);

            var challengerScore = winner == 0 ? 2 : 1;
            var targetScore = winner == 1 ? 2 : 1;
            var result = await service.CompleteAcceptedAsync(challenge, challengerScore, targetScore, ctx.Ct);
            await Task.Delay((HorseGifFrameDelay * frameCount) + HorseResultDelayBuffer, ctx.Ct);
            await ctx.Bot.SendMessage(challenge.ChatId, HorseResultText(result),
                parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        }
        catch
        {
            await service.FailAcceptedAsync(challenge, CancellationToken.None);
            await ctx.Bot.SendMessage(challenge.ChatId, Loc("roll.failed"), cancellationToken: CancellationToken.None);
        }
    }

    private async Task BlackjackAndSettleAsync(UpdateContext ctx, Challenge challenge)
    {
        try
        {
            var duel = PlayBlackjackDuel();
            var challengerScore = BlackjackScore(duel.ChallengerTotal, duel.ChallengerNatural);
            var targetScore = BlackjackScore(duel.TargetTotal, duel.TargetNatural);
            var result = await service.CompleteAcceptedAsync(challenge, challengerScore, targetScore, ctx.Ct);
            await ctx.Bot.SendMessage(challenge.ChatId, BlackjackResultText(result, duel),
                parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        }
        catch
        {
            await service.FailAcceptedAsync(challenge, CancellationToken.None);
            await ctx.Bot.SendMessage(challenge.ChatId, Loc("roll.failed"), cancellationToken: CancellationToken.None);
        }
    }

    private static bool TryParseCommand(
        Message msg,
        string[] args,
        out ChallengeUser? target,
        out string? username,
        out int amount,
        out ChallengeGame game)
    {
        target = null;
        username = null;
        amount = 0;
        game = default;

        var offset = 0;
        if (args.Length > 0 && args[0].StartsWith('@'))
        {
            username = args[0][1..].Trim();
            offset = 1;
        }
        else if (msg.ReplyToMessage?.From is { } replied && replied.Id != 0)
        {
            target = new ChallengeUser(replied.Id, DisplayName(replied));
        }

        return (target is not null || !string.IsNullOrWhiteSpace(username))
            && args.Length >= offset + 2
            && int.TryParse(args[offset], out amount)
            && ChallengeGameCatalog.TryParse(args[offset + 1], out game);
    }

    private static string StripCommand(string text)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? parts[1] : "";
    }

    private static (string Action, Guid ChallengeId) ParseCallback(string? data)
    {
        var parts = data?.Split(':');
        if (parts is not { Length: 3 } || parts[0] != "ch")
            return ("", Guid.Empty);

        return Guid.TryParse(parts[2], out var id) ? (parts[1], id) : ("", Guid.Empty);
    }

    private static InlineKeyboardMarkup BuildMarkup(Guid challengeId) => new([
        [
            InlineKeyboardButton.WithCallbackData("✅ Accept", $"ch:a:{challengeId:N}"),
            InlineKeyboardButton.WithCallbackData("❌ Decline", $"ch:d:{challengeId:N}"),
        ],
    ]);

    private string CreateErrorText(ChallengeCreateResult result) => result.Error switch
    {
        ChallengeCreateError.InvalidAmount => Loc("err.amount"),
        ChallengeCreateError.SelfChallenge => Loc("err.self"),
        ChallengeCreateError.NotEnoughCoins => string.Format(Loc("err.not_enough"), result.Balance),
        ChallengeCreateError.AlreadyPending => Loc("err.pending"),
        _ => Loc("usage"),
    };

    private string CallbackErrorText(ChallengeAcceptError error) => error switch
    {
        ChallengeAcceptError.None => "",
        ChallengeAcceptError.NotTarget => Loc("err.not_target"),
        ChallengeAcceptError.Expired => Loc("err.expired"),
        ChallengeAcceptError.ChallengerNotEnoughCoins => Loc("err.challenger_poor"),
        ChallengeAcceptError.TargetNotEnoughCoins => Loc("err.target_poor"),
        ChallengeAcceptError.AlreadyResolved => Loc("err.resolved"),
        _ => Loc("err.not_found"),
    };

    private string ResultText(ChallengeAcceptResult result)
    {
        var challenge = result.Challenge!;
        if (result.IsTie)
            return string.Format(
                Loc("result.tie"),
                challenge.ChallengerRollLabel(result.ChallengerRoll),
                challenge.TargetRollLabel(result.TargetRoll),
                challenge.Amount);

        return string.Format(
            Loc("result.win"),
            Mention(challenge.ChallengerId, challenge.ChallengerName),
            result.ChallengerRoll,
            Mention(challenge.TargetId, challenge.TargetName),
            result.TargetRoll,
            Mention(result.WinnerId, result.WinnerName),
            result.Payout,
            result.Fee);
    }

    private string HorseResultText(ChallengeAcceptResult result)
    {
        var challenge = result.Challenge!;
        var challengerHorse = result.ChallengerRoll > result.TargetRoll ? "🏆 #1" : "#1";
        var targetHorse = result.TargetRoll > result.ChallengerRoll ? "🏆 #2" : "#2";

        return string.Format(
            Loc("horse.result"),
            Mention(challenge.ChallengerId, challenge.ChallengerName),
            challengerHorse,
            Mention(challenge.TargetId, challenge.TargetName),
            targetHorse,
            Mention(result.WinnerId, result.WinnerName),
            result.Payout,
            result.Fee);
    }

    private string BlackjackResultText(ChallengeAcceptResult result, BlackjackDuel duel)
    {
        var challenge = result.Challenge!;
        var challengerLine = FormatBlackjackLine(duel.ChallengerCards, duel.ChallengerTotal, duel.ChallengerNatural);
        var targetLine = FormatBlackjackLine(duel.TargetCards, duel.TargetTotal, duel.TargetNatural);

        if (result.IsTie)
            return string.Format(
                Loc("blackjack.tie"),
                Mention(challenge.ChallengerId, challenge.ChallengerName),
                challengerLine,
                Mention(challenge.TargetId, challenge.TargetName),
                targetLine,
                challenge.Amount);

        return string.Format(
            Loc("blackjack.win"),
            Mention(challenge.ChallengerId, challenge.ChallengerName),
            challengerLine,
            Mention(challenge.TargetId, challenge.TargetName),
            targetLine,
            Mention(result.WinnerId, result.WinnerName),
            result.Payout,
            result.Fee);
    }

    private static BlackjackDuel PlayBlackjackDuel()
    {
        var deck = Deck.BuildShuffled();
        var challenger = AutoPlayBlackjackHand(ref deck);
        var target = AutoPlayBlackjackHand(ref deck);
        return new BlackjackDuel(
            challenger.Cards,
            target.Cards,
            challenger.Total,
            target.Total,
            challenger.Natural,
            target.Natural);
    }

    private static (string[] Cards, int Total, bool Natural) AutoPlayBlackjackHand(ref string deck)
    {
        var cards = Deck.Draw(ref deck, 2).ToList();
        while (BlackjackHandValue.Compute(cards) < 17)
            cards.Add(Deck.Draw(ref deck, 1)[0]);

        return ([.. cards], BlackjackHandValue.Compute(cards), BlackjackHandValue.IsNaturalBlackjack(cards));
    }

    private static int BlackjackScore(int total, bool natural) =>
        total > 21 ? 0 : natural ? 22 : total;

    private static string FormatBlackjackLine(string[] cards, int total, bool natural)
    {
        var suffix = total > 21 ? " bust" : natural ? " blackjack" : "";
        return $"{Html(string.Join(' ', cards.Select(FormatCard)))} = <b>{total}</b>{suffix}";
    }

    private static string FormatCard(string card)
    {
        if (card.Length < 2)
            return card;

        var rank = card[..^1] == "T" ? "10" : card[..^1];
        var suit = card[^1] switch
        {
            'S' => "♠",
            'H' => "♥",
            'D' => "♦",
            'C' => "♣",
            _ => card[^1].ToString(),
        };
        return rank + suit;
    }

    private static string DisplayName(User user) =>
        user.Username ?? user.FirstName ?? $"User ID: {user.Id}";

    private static string Mention(long userId, string displayName) =>
        $"<a href=\"tg://user?id={userId}\">{Html(displayName)}</a>";

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private string Loc(string key) => localizer.Get("challenges", key);

    private static Task AnswerCallbackAsync(
        UpdateContext ctx,
        CallbackQuery cbq,
        string? text = null,
        bool alert = false) =>
        ctx.Bot.AnswerCallbackQuery(cbq.Id, text, showAlert: alert, cancellationToken: ctx.Ct);

    private sealed record BlackjackDuel(
        string[] ChallengerCards,
        string[] TargetCards,
        int ChallengerTotal,
        int TargetTotal,
        bool ChallengerNatural,
        bool TargetNatural);
}

file static class ChallengeResultExtensions
{
    public static string ChallengerRollLabel(this Challenge _, int roll) => roll.ToString();

    public static string TargetRollLabel(this Challenge _, int roll) => roll.ToString();
}
