namespace CasinoShiz.Services.Pipeline;

public sealed class UpdatePipeline(
    ExceptionMiddleware exception,
    LoggingMiddleware logging,
    UpdateRouter router)
{
    public Task InvokeAsync(UpdateContext ctx)
    {
        UpdateDelegate terminal = router.DispatchAsync;
        UpdateDelegate withLogging = c => logging.InvokeAsync(c, terminal);
        UpdateDelegate withException = c => exception.InvokeAsync(c, withLogging);
        return withException(ctx);
    }
}
