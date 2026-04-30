// ─────────────────────────────────────────────────────────────────────────────
// PickDailyLotterySweeperJob — IBackgroundJob that drains daily lotteries
// whose deadline (next-local-midnight UTC instant) has passed and posts the
// outcome in the originating chat.
//
// Same pattern as PickLotterySweeperJob but on the daily table. Runs on a
// minute-ish cadence (configurable). One missed minute is harmless: rows
// stay in 'open' and the next tick picks them up.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Games.Pick;

public sealed partial class PickDailyLotterySweeperJob(
    IPickDailyLotteryStore store,
    IPickDailyLotteryService service,
    ITelegramBotClient bot,
    ILocalizer localizer,
    IOptions<PickOptions> options,
    ILogger<PickDailyLotterySweeperJob> logger) : IBackgroundJob
{
    public string Name => "pick.daily.lottery.sweeper";

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(15, options.Value.Daily.SweeperIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await store.ListExpiredOpenAsync(limit: 32, stoppingToken);
                foreach (var row in expired)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    try
                    {
                        var settle = await service.SettleAsync(row, stoppingToken);
                        await PostAsync(settle, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) { LogSettleFailed(row.Id, row.ChatId, ex); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { LogTickFailed(ex); }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task PostAsync(DailySettleResult result, CancellationToken ct)
    {
        var chatId = result.Row.ChatId;
        var day = result.Row.DayLocal.ToString("yyyy-MM-dd");
        string text;

        if (!result.Drawn || result.WinnerId is null)
        {
            // Empty pool — say nothing if there were truly zero participants
            // (no point pinging the chat). Only post if some race made us
            // arrive here with a non-trivial state.
            if (result.TicketsTotal == 0) return;
            text = string.Format(Loc("daily.sweep.cancelled"), day, result.TicketsTotal);
        }
        else
        {
            var winnerLabel = string.IsNullOrEmpty(result.WinnerName)
                ? $"User ID: {result.WinnerId}"
                : System.Net.WebUtility.HtmlEncode(result.WinnerName);
            text = string.Format(
                Loc("daily.sweep.settled"),
                day,
                winnerLabel,
                result.WinnerTicketCount ?? 0,
                result.TicketsTotal,
                result.DistinctUsers,
                result.PotTotal,
                result.Fee,
                result.Payout);
        }

        try
        {
            await bot.SendMessage(chatId, text,
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            LogPostFailed(result.Row.Id, chatId, ex);
        }
    }

    private string Loc(string key) => localizer.Get("pick", key);

    [LoggerMessage(EventId = 5971, Level = LogLevel.Error,
        Message = "pick.daily.sweeper.tick_failed")]
    partial void LogTickFailed(Exception ex);

    [LoggerMessage(EventId = 5972, Level = LogLevel.Warning,
        Message = "pick.daily.sweeper.settle_failed lottery={LotteryId} chat={ChatId}")]
    partial void LogSettleFailed(Guid lotteryId, long chatId, Exception ex);

    [LoggerMessage(EventId = 5973, Level = LogLevel.Warning,
        Message = "pick.daily.sweeper.post_failed lottery={LotteryId} chat={ChatId}")]
    partial void LogPostFailed(Guid lotteryId, long chatId, Exception ex);
}
