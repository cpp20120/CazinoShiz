// ─────────────────────────────────────────────────────────────────────────────
// UpdatePipeline — composes every registered IUpdateMiddleware around the
// UpdateRouter dispatch, onion-style. Order: in registration order, outermost
// first. The Host registers Exception → Logging → RateLimit by default; modules
// can contribute their own update middleware but usually don't need to.
//
// Built once on resolution (singleton); the delegate is cached because
// rebuilding the chain on every update is wasteful.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host.Pipeline;

public sealed class UpdatePipeline
{
    private readonly UpdateDelegate _chain;

    public UpdatePipeline(IEnumerable<IUpdateMiddleware> middleware, UpdateRouter router)
    {
        var ordered = middleware.ToArray();

        UpdateDelegate terminal = async ctx =>
        {
            await router.DispatchAsync(ctx.Bot, ctx.Update, ctx.Services, ctx.Ct);
        };

        var chain = terminal;
        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            var mw = ordered[i];
            var next = chain;
            chain = ctx => mw.InvokeAsync(ctx, next);
        }
        _chain = chain;
    }

    public Task InvokeAsync(UpdateContext ctx) => _chain(ctx);
}
