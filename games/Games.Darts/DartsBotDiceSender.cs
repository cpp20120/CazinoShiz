using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
namespace Games.Darts;

public sealed partial class DartsBotDiceSender(
    ITelegramBotClient bot,
    IServiceProvider services,
    ILogger<DartsBotDiceSender> logger)
{
    private const string DiceEmoji = "🎯";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, SemaphoreSlim> ChatLocks = new();

    public async Task SendAsync(DartsRollJob job, CancellationToken ct)
    {
        var gate = ChatLocks.GetOrAdd(job.ChatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            using var scope = services.CreateScope();
            var rounds = scope.ServiceProvider.GetRequiredService<IDartsRoundStore>();
            var row = await rounds.FindByIdAsync(job.RoundId, ct);
            if (row is not { Status: DartsRoundStatus.Queued })
                return;

            var sent = await bot.SendDice(
                job.ChatId,
                emoji: DiceEmoji,
                replyParameters: new ReplyParameters { MessageId = job.ReplyToMessageId },
                cancellationToken: ct);

            if (!await rounds.TryMarkAwaitingOutcomeAsync(job.RoundId, sent.MessageId, ct))
                return;

            if (sent.Dice is { Value: > 0 })
                await CompleteImmediatelyAsync(job, sent.MessageId, sent.Dice.Value, ct);
            else
            {
                DartsDiceRoundBinding.Bind(job.ChatId, sent.MessageId, job.RoundId);
                BotMiniGameDiceOwner.Bind(job.ChatId, sent.MessageId, job.UserId, job.DisplayName);
            }
        }
        catch (Exception ex)
        {
            LogSendFailed(ex, job.RoundId);
            await RefundIfStillQueuedAsync(job.RoundId, job.UserId, job.ChatId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RefundIfStillQueuedAsync(long roundId, long userId, long chatId, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var rounds = scope.ServiceProvider.GetRequiredService<IDartsRoundStore>();
        var economics = scope.ServiceProvider.GetRequiredService<IEconomicsService>();
        var row = await rounds.FindByIdAsync(roundId, ct);
        if (row is not { Status: DartsRoundStatus.Queued })
            return;

        await economics.CreditAsync(userId, chatId, row.Amount, "darts.bot_dice.refund", ct);
        await rounds.DeleteAsync(roundId, ct);

        var remaining = await rounds.CountActiveByUserChatAsync(userId, chatId, ct);
        if (remaining == 0)
        {
            BotMiniGameRollGate.Clear("darts", userId, chatId);
            BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
        }
    }

    private async Task CompleteImmediatelyAsync(DartsRollJob job, int botMessageId, int face, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDartsService>();
        var localizer = scope.ServiceProvider.GetRequiredService<ILocalizer>();
        var result = await service.ThrowAsync(
            job.RoundId, job.UserId, job.DisplayName, job.ChatId, botMessageId, face, ct);
        if (result.Outcome == DartsThrowOutcome.NoBet)
            return;

        var net = result.Payout - result.Bet;
        var text = result.Payout > 0
            ? string.Format(localizer.Get("darts", "throw.win"),
                result.Face, result.Multiplier, result.Bet, result.Payout, net, result.Balance)
            : string.Format(localizer.Get("darts", "throw.lose"),
                result.Face, result.Bet, result.Balance);

        try
        {
            await bot.SendMessage(
                job.ChatId,
                text,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyParameters: new ReplyParameters { MessageId = botMessageId },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            LogResultReplyFailed(job.UserId, ex);
        }
    }

    [LoggerMessage(EventId = 2230, Level = LogLevel.Warning, Message = "darts.bot_dice.send_failed round={RoundId}")]
    private partial void LogSendFailed(Exception ex, long roundId);

    [LoggerMessage(EventId = 2232, Level = LogLevel.Error, Message = "darts.result.reply_failed user={UserId}")]
    private partial void LogResultReplyFailed(long userId, Exception ex);
}
