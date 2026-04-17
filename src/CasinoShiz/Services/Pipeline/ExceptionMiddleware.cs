using CasinoShiz.Services.Analytics;

namespace CasinoShiz.Services.Pipeline;

public sealed partial class ExceptionMiddleware(
    ClickHouseReporter reporter,
    ILogger<ExceptionMiddleware> logger) : IUpdateMiddleware
{
    public async Task InvokeAsync(UpdateContext ctx, UpdateDelegate next)
    {
        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (ctx.Ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogUpdateError(ctx.Update.Id, ctx.UserId, ex);
            reporter.SendEvent(new EventData
            {
                EventType = "error_handler",
                Payload = new { error = ex.Message, stack = ex.StackTrace, update_id = ctx.Update.Id, user_id = ctx.UserId }
            });
        }
    }

    [LoggerMessage(EventId = 1900, Level = LogLevel.Error,
        Message = "update.error update_id={UpdateId} user={UserId}")]
    partial void LogUpdateError(int updateId, long userId, Exception exception);
}
