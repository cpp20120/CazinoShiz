using BotFramework.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Games.Darts;

public sealed partial class DartsRollDispatcherJob(
    IDartsRollQueue queue,
    IServiceProvider services,
    DartsBotDiceSender sender,
    ILogger<DartsRollDispatcherJob> logger) : IBackgroundJob
{
    public string Name => "darts.roll_dispatcher";

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverQueuedRoundsAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var job = await queue.ReadAsync(stoppingToken);
                await sender.SendAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogDispatcherCrash(ex);
            throw;
        }
    }

    private async Task RecoverQueuedRoundsAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var rounds = scope.ServiceProvider.GetRequiredService<IDartsRoundStore>();
        var queued = await rounds.ListQueuedAsync(ct);
        foreach (var round in queued)
        {
            queue.Enqueue(new DartsRollJob(
                round.Id,
                round.ChatId,
                round.UserId,
                $"User ID: {round.UserId}",
                round.ReplyToMessageId));
        }

        if (queued.Count > 0)
            LogRecoveredQueuedRounds(queued.Count);
    }

    [LoggerMessage(EventId = 2231, Level = LogLevel.Error, Message = "darts.roll_dispatcher crashed")]
    private partial void LogDispatcherCrash(Exception ex);

    [LoggerMessage(EventId = 2233, Level = LogLevel.Information, Message = "darts.roll_dispatcher recovered_queued={Count}")]
    private partial void LogRecoveredQueuedRounds(int count);
}
