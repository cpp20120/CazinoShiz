namespace CasinoShiz.Services.Pipeline;

public delegate Task UpdateDelegate(UpdateContext ctx);

public interface IUpdateMiddleware
{
    Task InvokeAsync(UpdateContext ctx, UpdateDelegate next);
}
