using BotFramework.Sdk;
using Microsoft.Extensions.Logging;

namespace Games.Darts;

public sealed partial class DartsRollDispatcherJob(
    IDartsRollQueue queue,
    DartsBotDiceSender sender,
    ILogger<DartsRollDispatcherJob> logger) : IBackgroundJob
{
    public string Name => "darts.roll_dispatcher";

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
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

    [LoggerMessage(EventId = 2231, Level = LogLevel.Error, Message = "darts.roll_dispatcher crashed")]
    private partial void LogDispatcherCrash(Exception ex);
}
