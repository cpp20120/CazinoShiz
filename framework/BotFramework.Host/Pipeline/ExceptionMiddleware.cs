using BotFramework.Sdk;

namespace BotFramework.Host.Pipeline;

public sealed partial class ExceptionMiddleware(
    IAnalyticsService analytics,
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
            analytics.Track("_framework", "error", new Dictionary<string, object?>
            {
                ["exception_type"] = ex.GetType().FullName,
                ["message"] = ex.Message,
                ["stack"] = ex.StackTrace,
                ["update_id"] = ctx.Update.Id,
                ["user_id"] = ctx.UserId,
            });
        }
    }

    [LoggerMessage(EventId = 1900, Level = LogLevel.Error,
        Message = "update.error update_id={UpdateId} user={UserId}")]
    partial void LogUpdateError(int updateId, long userId, Exception exception);
}
