using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Horse;

public interface IHorseRaceNotifier
{
    Task SendResultGifsAsync(RaceOutcome outcome, string raceDate, CancellationToken ct);
    void ScheduleWinnerAnnouncements(RaceOutcome outcome);
}

public sealed partial class HorseRaceNotifier(
    ITelegramBotClient bot,
    IHorseService service,
    ILocalizer localizer,
    IOptions<HorseOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<HorseRaceNotifier> logger) : IHorseRaceNotifier
{
    private readonly HorseOptions _opts = options.Value;

    public async Task SendResultGifsAsync(RaceOutcome outcome, string raceDate, CancellationToken ct)
    {
        var targetChatIds = outcome.BetScopeIds;
        string? fileId = null;
        foreach (var targetChatId in targetChatIds)
        {
            try
            {
                Message gifMessage;
                if (fileId is null)
                {
                    await using var gifStream = new MemoryStream(outcome.GifBytes);
                    gifMessage = await bot.SendAnimation(targetChatId, InputFile.FromStream(gifStream, "horses.gif"),
                        cancellationToken: ct);
                }
                else
                {
                    gifMessage = await bot.SendAnimation(targetChatId, InputFile.FromFileId(fileId),
                        cancellationToken: ct);
                }

                fileId ??= gifMessage.Animation?.FileId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogHorseRunBroadcastFailed(targetChatId, ex);
            }
        }

        if (fileId is null) return;

        foreach (var scopeId in targetChatIds.Prepend(0L).Distinct())
            await service.SaveFileIdAsync(raceDate, scopeId, fileId, ct);
    }

    public void ScheduleWinnerAnnouncements(RaceOutcome outcome)
    {
        var targetChatIds = outcome.BetScopeIds;
        var transactions = outcome.Transactions;
        var delayMs = _opts.HorseCount == 2 ? _opts.AnnounceDelay1v1Ms : _opts.AnnounceDelayMs;
        var announceCt = lifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, announceCt);
                foreach (var targetChatId in targetChatIds)
                {
                    var text = FormatWinnerText(targetChatId, transactions);
                    await bot.SendMessage(targetChatId, text, parseMode: ParseMode.Html, cancellationToken: announceCt);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogHorseRunAnnounceFailed(ex); }
        }, announceCt);
    }

    private string FormatWinnerText(long targetChatId, IReadOnlyList<RaceTransaction> transactions)
    {
        var chatTransactions = transactions
            .Where(tx => tx.BalanceScopeId == targetChatId)
            .ToList();

        return chatTransactions.Count > 0
            ? string.Join("\n", new[] { Loc("run.winners_header") + "\n" }
                .Concat(chatTransactions.Select((tx, i) =>
                    string.Format(Loc("run.winner_line"), i + 1, tx.UserId, tx.Amount))))
            : Loc("run.no_winners");
    }

    private string Loc(string key) => localizer.Get("horse", key);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Debug, Message = "horse.run.announce failed")]
    private partial void LogHorseRunAnnounceFailed(Exception exception);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Warning, Message = "horse.run.broadcast failed chat={ChatId}")]
    private partial void LogHorseRunBroadcastFailed(long chatId, Exception exception);
}
