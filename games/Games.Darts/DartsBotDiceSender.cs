using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
namespace Games.Darts;

public sealed partial class DartsBotDiceSender(
    ITelegramBotClient bot,
    IDartsRoundStore rounds,
    IEconomicsService economics,
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

            DartsDiceRoundBinding.Bind(job.ChatId, sent.MessageId, job.RoundId);
            BotMiniGameDiceOwner.Bind(job.ChatId, sent.MessageId, job.UserId, job.DisplayName);
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

    [LoggerMessage(EventId = 2230, Level = LogLevel.Warning, Message = "darts.bot_dice.send_failed round={RoundId}")]
    private partial void LogSendFailed(Exception ex, long roundId);
}
