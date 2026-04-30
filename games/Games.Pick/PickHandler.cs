// ─────────────────────────────────────────────────────────────────────────────
// PickHandler — parses /pick syntax, drives the suspense reveal, settles via
// IPickService, and handles the double-or-nothing chain callback.
//
// Syntax accepted:
//   /pick A, B                    → default bet, back A (first variant)
//   /pick A, B, C                 → default bet, back A
//   /pick 50 A, B, C              → bet 50, back A
//   /pick bet 50 A, B, C          → same, explicit
//   /pick B | A, B, C             → default bet, back B (named)
//   /pick 2 | A, B, C             → default bet, back position 2 (1-indexed)
//   /pick 50 B | A, B, C          → bet 50, back B
//   /pick A,C | A, B, C, D, E     → default bet, parlay on A and C
//   /pick 50 A,C | A, B, C, D     → bet 50, parlay on A and C
//
// "|" splits the choice list from the variant list. Without "|", we implicitly
// back the FIRST variant (backwards-compatible with the trivial form).
//
// Callback "pkc:<guid>" → claim the chain state, re-roll, post the chain
// result by editing the original offer message in place.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net;
using System.Text;
using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.Pick;

[Command("/pick")]
[CallbackPrefix("pkc:")]
public sealed partial class PickHandler(
    IPickService service,
    PickChainStore chains,
    ILocalizer localizer,
    IOptions<PickOptions> options,
    ILogger<PickHandler> logger) : IUpdateHandler
{
    private const string ChainCallbackPrefix = "pkc:";
    private readonly PickOptions _opts = options.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        if (ctx.Update.CallbackQuery is { } cbq)
        {
            await HandleChainCallbackAsync(ctx, cbq);
            return;
        }

        var msg = ctx.Update.Message;
        if (msg?.Text is not { Length: > 0 } text) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username is { Length: > 0 } un
            ? $"@{un}"
            : msg.From?.FirstName ?? $"User ID: {userId}";

        var argText = StripCommandPrefix(text);
        if (string.IsNullOrWhiteSpace(argText)
            || string.Equals(argText.Trim(), "help", StringComparison.OrdinalIgnoreCase))
        {
            await SendUsageAsync(ctx, chatId, reply);
            return;
        }

        if (!TryParse(argText, out var amount, out var variants, out var backedIndices, out var parseError))
        {
            await SendUsageAsync(ctx, chatId, reply, parseError);
            return;
        }

        try
        {
            await RunRoundAsync(
                ctx,
                userId,
                chatId,
                displayName,
                amount,
                variants,
                backedIndices,
                reply,
                editMessageId: null);
        }
        catch (Exception ex)
        {
            LogPickFailed(userId, ex);
            await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
        }
    }

    private async Task HandleChainCallbackAsync(UpdateContext ctx, CallbackQuery cbq)
    {
        if (cbq.Data is not { Length: > 0 } data || !data.StartsWith(ChainCallbackPrefix)) return;
        var payload = data[ChainCallbackPrefix.Length..];
        if (!Guid.TryParse(payload, out var chainId)) return;

        var clickerId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? 0;
        if (chatId == 0)
        {
            await TryAnswerCallbackAsync(ctx.Bot, cbq.Id, Loc("chain.expired"), ctx.Ct);
            return;
        }

        var state = chains.TryClaim(chainId);
        if (state is null)
        {
            await TryAnswerCallbackAsync(ctx.Bot, cbq.Id, Loc("chain.expired"), ctx.Ct);
            return;
        }

        if (state.UserId != clickerId)
        {
            // Hand the slot back so the rightful owner can still use it.
            chains.Add(state);
            await TryAnswerCallbackAsync(ctx.Bot, cbq.Id, Loc("chain.not_your_turn"), ctx.Ct);
            return;
        }

        await TryAnswerCallbackAsync(ctx.Bot, cbq.Id, Loc("chain.acknowledged"), ctx.Ct);

        try
        {
            // Use the existing message as the canvas for the suspense reveal:
            // strip the inline buttons, redraw header, then animate.
            var offerMessageId = cbq.Message?.MessageId;
            await RunChainHopAsync(ctx, state, offerMessageId);
        }
        catch (Exception ex)
        {
            LogPickFailed(state.UserId, ex);
            await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        }
    }

    // ── round runners ─────────────────────────────────────────────────────────

    private async Task RunRoundAsync(
        UpdateContext ctx,
        long userId,
        long chatId,
        string displayName,
        int amount,
        IReadOnlyList<string> variants,
        IReadOnlyList<int> backedIndices,
        ReplyParameters reply,
        int? editMessageId)
    {
        // Quick validate-locally for early errors that don't deserve a fancy
        // reveal (bad amount, bad choice, etc.).
        var preflight = await service.PickAsync(
            userId, displayName, chatId, amount, variants, backedIndices, ctx.Ct);
        if (preflight.Error != PickError.None)
        {
            await SendErrorAsync(ctx, chatId, reply, preflight, depth: 0);
            return;
        }

        await PresentResultAsync(
            ctx, chatId, reply, preflight,
            isChainHop: false,
            editMessageId: editMessageId,
            displayName: displayName);
    }

    private async Task RunChainHopAsync(UpdateContext ctx, PickChainState state, int? offerMessageId)
    {
        var result = await service.ContinueChainAsync(state, ctx.Ct);
        var reply = (ReplyParameters?)null; // chain results edit the offer message in place

        if (result.Error != PickError.None)
        {
            await SendErrorAsync(ctx, state.ChatId, reply, result, depth: state.Depth);
            return;
        }

        await PresentResultAsync(
            ctx, state.ChatId, reply, result,
            isChainHop: true,
            editMessageId: offerMessageId,
            displayName: state.DisplayName);
    }

    private async Task PresentResultAsync(
        UpdateContext ctx,
        long chatId,
        ReplyParameters? reply,
        PickResult result,
        bool isChainHop,
        int? editMessageId,
        string displayName)
    {
        // Step 1: suspense reveal (optional, best-effort — failure falls through
        // to a plain final-result post).
        if (_opts.RevealAnimation)
        {
            try
            {
                editMessageId = await RunRevealAsync(ctx, chatId, reply, result, isChainHop, editMessageId);
            }
            catch (Exception ex)
            {
                LogRevealFailed(result.PickedIndex, ex);
                editMessageId = null;
            }
        }

        // Step 2: final result post (with chain button when applicable).
        var finalText = BuildFinalText(result, displayName, isChainHop);
        var markup = BuildChainMarkupOrNull(result);

        if (editMessageId is { } mid)
        {
            try
            {
                await ctx.Bot.EditMessageText(chatId, mid, finalText,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
                return;
            }
            catch (ApiRequestException) { /* fall through to send */ }
            catch (HttpRequestException) { /* fall through to send */ }
        }

        await ctx.Bot.SendMessage(chatId, finalText,
            parseMode: ParseMode.Html, replyMarkup: markup, replyParameters: reply,
            cancellationToken: ctx.Ct);
    }

    // ── suspense reveal ───────────────────────────────────────────────────────

    /// <summary>
    /// Posts (or reuses) a single message and progressively eliminates losing
    /// variants until only the picked one remains. Returns the message id we
    /// ended up using so the caller can edit it once more for the final result.
    /// </summary>
    private async Task<int?> RunRevealAsync(
        UpdateContext ctx,
        long chatId,
        ReplyParameters? reply,
        PickResult result,
        bool isChainHop,
        int? editMessageId)
    {
        var n = result.Variants.Count;
        if (n <= 1) return editMessageId;

        var alive = new bool[n];
        for (var i = 0; i < n; i++) alive[i] = true;

        var initialText = RenderRevealFrame(result, alive, eliminatedThisFrame: -1, isChainHop, finished: false);

        int messageId;
        if (editMessageId is { } existing)
        {
            try
            {
                await ctx.Bot.EditMessageText(chatId, existing, initialText,
                    parseMode: ParseMode.Html, replyMarkup: null, cancellationToken: ctx.Ct);
                messageId = existing;
            }
            catch
            {
                var sent = await ctx.Bot.SendMessage(chatId, initialText,
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                messageId = sent.MessageId;
            }
        }
        else
        {
            var sent = await ctx.Bot.SendMessage(chatId, initialText,
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            messageId = sent.MessageId;
        }

        // Build elimination order: every losing variant except the picked one,
        // then optionally those backed-but-not-picked, ending with the picked
        // index alone visible.
        var elimQueue = new List<int>(capacity: n);
        for (var i = 0; i < n; i++)
        {
            if (i == result.PickedIndex) continue;
            elimQueue.Add(i);
        }
        // Random order so it doesn't always go top-to-bottom.
        for (var i = elimQueue.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (elimQueue[i], elimQueue[j]) = (elimQueue[j], elimQueue[i]);
        }

        var stepDelay = Math.Max(120, _opts.RevealStepDelayMs);
        var maxTotal = Math.Max(stepDelay, _opts.RevealMaxTotalMs);
        // If steps × stepDelay would blow the cap, shrink stepDelay.
        if (elimQueue.Count * stepDelay > maxTotal && elimQueue.Count > 0)
            stepDelay = Math.Max(80, maxTotal / elimQueue.Count);

        foreach (var elim in elimQueue)
        {
            try
            {
                await Task.Delay(stepDelay, ctx.Ct);
            }
            catch (TaskCanceledException) { return messageId; }

            alive[elim] = false;
            var frame = RenderRevealFrame(result, alive, eliminatedThisFrame: elim, isChainHop, finished: false);
            try
            {
                await ctx.Bot.EditMessageText(chatId, messageId, frame,
                    parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
            }
            catch (ApiRequestException) { /* rate-limited / message gone — abort animation */ break; }
            catch (HttpRequestException) { break; }
        }

        return messageId;
    }

    private string RenderRevealFrame(
        PickResult result, bool[] alive, int eliminatedThisFrame, bool isChainHop, bool finished)
    {
        var sb = new StringBuilder();
        var headerKey = isChainHop ? "reveal.header_chain" : "reveal.header";
        sb.Append(string.Format(Loc(headerKey), result.Bet, result.Variants.Count, result.BackedIndices.Count));
        sb.Append("\n\n");

        for (var i = 0; i < result.Variants.Count; i++)
        {
            var variant = WebUtility.HtmlEncode(result.Variants[i]);
            var marker = alive[i] ? (i == eliminatedThisFrame ? "💥" : "❓") : "❌";
            var backedTag = result.BackedIndices.Contains(i) ? " <i>(твой)</i>" : "";
            string row;
            if (alive[i])
            {
                if (finished)
                    row = $"🎯 <b>{variant}</b>{backedTag}";
                else if (i == eliminatedThisFrame)
                    row = $"{marker} <s>{variant}</s>{backedTag}";
                else
                    row = $"{marker} {variant}{backedTag}";
            }
            else
            {
                row = $"{marker} <s>{variant}</s>{backedTag}";
            }
            sb.Append(row);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ── final result text ─────────────────────────────────────────────────────

    private string BuildFinalText(PickResult result, string displayName, bool isChainHop)
    {
        var picked = WebUtility.HtmlEncode(result.Variants[result.PickedIndex]);
        var n = result.Variants.Count;
        var k = result.BackedIndices.Count;
        var backedNames = string.Join(", ",
            result.BackedIndices.OrderBy(i => i)
                .Select(i => "<b>" + WebUtility.HtmlEncode(result.Variants[i]) + "</b>"));

        if (result.Won)
        {
            var lines = new List<string>(8);
            var headerKey = isChainHop ? "ok.win.chain_header" : "ok.win.header";
            lines.Add(string.Format(Loc(headerKey), picked, result.ChainDepth + (isChainHop ? 0 : 0)));
            lines.Add(string.Format(Loc("ok.win.payout"), result.Bet, n, k, result.Payout, result.Net));

            if (result.StreakBonus > 0)
                lines.Add(string.Format(Loc("ok.win.streak"), result.StreakAfter, result.StreakBonus));
            else if (result.StreakAfter > 1 && !isChainHop)
                lines.Add(string.Format(Loc("ok.win.streak_no_bonus"), result.StreakAfter));

            lines.Add(string.Format(Loc("ok.win.balance"), result.Balance));

            if (result.ChainGuid is not null)
            {
                var hopsLeft = Math.Max(0, _opts.ChainMaxDepth - (result.ChainDepth + 1) + 1);
                lines.Add(string.Format(Loc("ok.win.chain_offer"), result.Payout, hopsLeft));
            }

            return string.Join('\n', lines);
        }

        // loss
        var lossKey = isChainHop ? "ok.lose.chain" : "ok.lose";
        var streakBroken = result.StreakBefore > 1 && !isChainHop;
        var loseText = string.Format(Loc(lossKey), picked, result.Bet, result.Balance, backedNames);
        if (streakBroken)
            loseText += "\n" + string.Format(Loc("ok.lose.streak_broken"), result.StreakBefore);
        return loseText;
    }

    private InlineKeyboardMarkup? BuildChainMarkupOrNull(PickResult result)
    {
        if (result.ChainGuid is not { } id) return null;
        var label = string.Format(Loc("chain.button"), result.Payout);
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(label, ChainCallbackPrefix + id) },
        });
    }

    // ── error rendering ───────────────────────────────────────────────────────

    private async Task SendErrorAsync(UpdateContext ctx, long chatId, ReplyParameters? reply, PickResult result, int depth)
    {
        var text = result.Error switch
        {
            PickError.NotEnoughVariants => string.Format(Loc("err.too_few"), _opts.MinVariants),
            PickError.TooManyVariants   => string.Format(Loc("err.too_many"), _opts.MaxVariants),
            PickError.InvalidAmount     => _opts.MaxBet > 0
                                              ? string.Format(Loc("err.invalid_bet_capped"), _opts.MaxBet)
                                              : Loc("err.invalid_bet"),
            PickError.InvalidChoice     => Loc("err.invalid_choice"),
            PickError.NotEnoughCoins    => depth > 0
                                              ? string.Format(Loc("err.no_coins_chain"), result.Balance)
                                              : string.Format(Loc("err.no_coins"), result.Balance),
            _                           => Loc("err.generic"),
        };
        await ctx.Bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    private async Task SendUsageAsync(UpdateContext ctx, long chatId, ReplyParameters reply, string? extraNote = null)
    {
        var usage = string.Format(
            Loc("usage"),
            _opts.DefaultBet,
            _opts.MinVariants,
            _opts.MaxVariants,
            _opts.MaxBet,
            (int)Math.Round(_opts.HouseEdge * 100));
        if (!string.IsNullOrEmpty(extraNote))
            usage = "<i>" + WebUtility.HtmlEncode(extraNote) + "</i>\n\n" + usage;
        await ctx.Bot.SendMessage(chatId, usage,
            parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    private static async Task TryAnswerCallbackAsync(ITelegramBotClient bot, string id, string text, CancellationToken ct)
    {
        try { await bot.AnswerCallbackQuery(id, text, cancellationToken: ct); }
        catch { /* nothing actionable */ }
    }

    // ── parser ────────────────────────────────────────────────────────────────

    /// <summary>Removes "/pick" or "/pick@Bot" prefix and returns the rest.</summary>
    private static string StripCommandPrefix(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '/')
            return string.Empty;

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..].Trim();
    }

    /// <summary>
    /// Parses
    ///   [bet] [bet keyword] [choice|choice,...] | variant1, variant2, ...
    /// or
    ///   [bet] variant1, variant2, ... (implicitly backs the first variant)
    /// </summary>
    private bool TryParse(
        string args,
        out int amount,
        out IReadOnlyList<string> variants,
        out IReadOnlyList<int> backedIndices,
        out string? error)
    {
        amount = _opts.DefaultBet;
        variants = [];
        backedIndices = [];
        error = null;

        if (string.IsNullOrWhiteSpace(args)) return false;

        // Split optional "choice | variants" form vs "variants" form.
        string choiceSpec;
        string variantSpec;
        var pipeIdx = args.IndexOf('|');
        if (pipeIdx >= 0)
        {
            choiceSpec = args[..pipeIdx].Trim();
            variantSpec = args[(pipeIdx + 1)..].Trim();
        }
        else
        {
            choiceSpec = string.Empty;
            variantSpec = args.Trim();
        }

        // Strip a leading bet from whichever side has it.
        var betSource = pipeIdx >= 0 ? choiceSpec : variantSpec;
        if (TryStripLeadingBet(betSource, out var newBetSource, out var parsedBet))
        {
            amount = parsedBet;
            if (pipeIdx >= 0) choiceSpec = newBetSource;
            else variantSpec = newBetSource;
        }

        if (string.IsNullOrWhiteSpace(variantSpec))
        {
            // pipe with empty right side, or empty everything — usage hint.
            return false;
        }

        var rawVariants = variantSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawVariants.Length == 0) return false;

        var maxLen = _opts.MaxVariantLength > 0 ? _opts.MaxVariantLength : 64;
        var variantList = new List<string>(rawVariants.Length);
        foreach (var v in rawVariants)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            variantList.Add(v.Length > maxLen ? v[..maxLen] : v);
        }
        if (variantList.Count == 0) return false;
        variants = variantList;

        if (string.IsNullOrEmpty(choiceSpec))
        {
            // Implicit: back first variant.
            backedIndices = new[] { 0 };
            return true;
        }

        // Resolve choice list. Each token may be a 1-based position or a
        // case-insensitive variant name (matched against the variant list).
        var rawChoices = choiceSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawChoices.Length == 0)
        {
            error = "Не удалось разобрать выбор.";
            return false;
        }

        var seen = new HashSet<int>();
        foreach (var tok in rawChoices)
        {
            if (TryResolveChoice(tok, variantList, out var idx))
                seen.Add(idx);
            else
            {
                error = $"Не нашёл вариант «{tok}» в списке.";
                return false;
            }
        }
        if (seen.Count == 0) return false;
        backedIndices = seen.OrderBy(i => i).ToArray();
        return true;
    }

    private static bool TryStripLeadingBet(string text, out string remainder, out int amount)
    {
        remainder = text;
        amount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var working = text.TrimStart();
        if (working.StartsWith("bet ", StringComparison.OrdinalIgnoreCase))
            working = working[4..].TrimStart();

        var firstSpace = working.IndexOf(' ');
        if (firstSpace <= 0) return false;
        var head = working[..firstSpace];
        if (!int.TryParse(head, out amount)) return false;

        remainder = working[(firstSpace + 1)..].TrimStart();
        return true;
    }

    private static bool TryResolveChoice(string token, List<string> variants, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (int.TryParse(token, out var pos) && pos >= 1 && pos <= variants.Count)
        {
            index = pos - 1;
            return true;
        }

        for (var i = 0; i < variants.Count; i++)
        {
            if (string.Equals(variants[i], token, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }
        return false;
    }

    private string Loc(string key) => localizer.Get("pick", key);

    [LoggerMessage(EventId = 5901, Level = LogLevel.Error,
        Message = "pick.handle.failed user={UserId}")]
    partial void LogPickFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 5902, Level = LogLevel.Warning,
        Message = "pick.reveal.failed picked_index={PickedIndex}")]
    partial void LogRevealFailed(int pickedIndex, Exception exception);
}
