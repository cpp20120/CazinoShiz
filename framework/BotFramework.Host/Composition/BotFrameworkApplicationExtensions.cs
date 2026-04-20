// ─────────────────────────────────────────────────────────────────────────────
// UseBotFramework — runtime wiring for a Host WebApplication.
//
// Maps:
//   • POST /{token}          — Telegram webhook. Only mapped when
//                              BotFrameworkOptions.IsProduction is true.
//                              Reads the Update JSON via ASP.NET Core's
//                              built-in System.Text.Json and dispatches
//                              through the same UpdatePipeline the polling
//                              driver uses.
//   • GET /health/live       — liveness-kind health checks only.
//   • GET /health/ready      — every health check (liveness + readiness).
//                              503 when any check reports unhealthy.
//   • /admin/*               — gated by BotFrameworkOptions.AdminWebToken.
//                              503 when the token is not configured (opt-in
//                              admin UI). 401 on missing/wrong token. A
//                              valid ?token=... query param sets a 30-day
//                              admin_token cookie and redirects so the URL
//                              bar loses the token. Page rendering itself
//                              is handled by AdminMount (wiring deferred).
//
// Admin token comparison is constant-time — ported verbatim from the live
// bot's /admin middleware (CryptographicOperations.FixedTimeEquals), same
// reasoning (sidechannel resistance).
// ─────────────────────────────────────────────────────────────────────────────

using System.Security.Cryptography;
using System.Text;
using BotFramework.Host.Pipeline;
using BotFramework.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFramework.Host.Composition;

public static class BotFrameworkApplicationExtensions
{
    public static WebApplication UseBotFramework(this WebApplication app)
    {
        var opts = app.Services.GetRequiredService<IOptions<BotFrameworkOptions>>().Value;

        MapAdminGate(app);
        app.MapRazorPages();
        MapHealth(app);

        if (opts.IsProduction && !string.IsNullOrWhiteSpace(opts.Token))
            MapWebhook(app, opts.Token);

        return app;
    }

    private static void MapWebhook(WebApplication app, string token)
    {
        app.MapPost($"/{token}", async (HttpContext ctx) =>
        {
            var update = await ctx.Request.ReadFromJsonAsync<Update>(ctx.RequestAborted);
            if (update is null) return Results.BadRequest();

            using var scope = ctx.RequestServices.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<UpdatePipeline>();
            var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
            var updateCtx = new UpdateContext(bot, update, scope.ServiceProvider, ctx.RequestAborted);
            await pipeline.InvokeAsync(updateCtx);
            return Results.Ok();
        });
    }

    private static void MapHealth(WebApplication app)
    {
        app.MapGet("/health/live", async (HealthEndpoint endpoint, CancellationToken ct) =>
        {
            var report = await endpoint.RunAsync(HealthCheckKind.Liveness, ct);
            return report.Healthy
                ? Results.Ok(report)
                : Results.Json(report, statusCode: 503);
        });

        app.MapGet("/health/ready", async (HealthEndpoint endpoint, CancellationToken ct) =>
        {
            var report = await endpoint.RunAsync(kindFilter: null, ct);
            return report.Healthy
                ? Results.Ok(report)
                : Results.Json(report, statusCode: 503);
        });
    }

    private static void MapAdminGate(WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/admin"))
            {
                await next();
                return;
            }

            var opts = ctx.RequestServices.GetRequiredService<IOptions<BotFrameworkOptions>>().Value;
            var expected = opts.AdminWebToken;
            if (string.IsNullOrEmpty(expected))
            {
                ctx.Response.StatusCode = 503;
                await ctx.Response.WriteAsync("Admin UI disabled: Bot:AdminWebToken not set");
                return;
            }

            var queryToken = ctx.Request.Query["token"].ToString();
            if (!string.IsNullOrEmpty(queryToken))
            {
                if (!TokensMatch(queryToken, expected))
                {
                    ctx.Response.StatusCode = 401;
                    await ctx.Response.WriteAsync("Unauthorized");
                    return;
                }
                ctx.Response.Cookies.Append("admin_token", expected, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = ctx.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(30),
                });
                ctx.Response.Redirect(ctx.Request.Path);
                return;
            }

            var cookieToken = ctx.Request.Cookies["admin_token"] ?? "";
            if (!TokensMatch(cookieToken, expected))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Unauthorized");
                return;
            }

            await next();
        });
    }

    private static bool TokensMatch(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
