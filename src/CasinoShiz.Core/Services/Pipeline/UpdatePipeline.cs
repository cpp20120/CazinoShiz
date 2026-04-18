namespace CasinoShiz.Services.Pipeline;

public sealed class UpdatePipeline(
    ExceptionMiddleware exception,
    LoggingMiddleware logging,
    RateLimitMiddleware rateLimit,
    UpdateRouter router)
{
    public Task InvokeAsync(UpdateContext ctx)
    {
        UpdateDelegate terminal = router.DispatchAsync;
        UpdateDelegate withRateLimit = c => rateLimit.InvokeAsync(c, terminal);
        UpdateDelegate withLogging = c => logging.InvokeAsync(c, withRateLimit);
        UpdateDelegate withException = c => exception.InvokeAsync(c, withLogging);
        return withException(ctx);
    }
}
