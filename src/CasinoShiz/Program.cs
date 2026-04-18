using System.Security.Cryptography;
using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Services;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Handlers;
using CasinoShiz.Services.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));
builder.Services.Configure<ClickHouseOptions>(builder.Configuration.GetSection(ClickHouseOptions.SectionName));

var pgConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(pgConnectionString).Build());

builder.Services.AddDbContext<AppDbContext>((sp, opts) =>
    opts.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

var botToken = builder.Configuration.GetSection(BotOptions.SectionName).GetValue<string>("Token")
    ?? throw new InvalidOperationException("Bot:Token is required");
builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));

builder.Services.AddSingleton<ClickHouseReporter>();
builder.Services.AddScoped<UpdateHandler>();
builder.Services.AddScoped<CaptchaService>();

builder.Services.AddScoped<DiceHandler>();
builder.Services.AddScoped<HorseHandler>();
builder.Services.AddScoped<RedeemHandler>();
builder.Services.AddScoped<AdminHandler>();
builder.Services.AddScoped<LeaderboardHandler>();
builder.Services.AddScoped<ChatHandler>();
builder.Services.AddScoped<ChannelHandler>();
builder.Services.AddScoped<CasinoShiz.Services.Poker.Application.PokerService>();
builder.Services.AddScoped<CasinoShiz.Services.Horse.HorseService>();
builder.Services.AddScoped<CasinoShiz.Services.Dice.DiceService>();
builder.Services.AddScoped<CasinoShiz.Services.Redeem.RedeemService>();
builder.Services.AddScoped<CasinoShiz.Services.Leaderboard.LeaderboardService>();
builder.Services.AddScoped<CasinoShiz.Services.Admin.AdminService>();
builder.Services.AddScoped<CasinoShiz.Services.Blackjack.BlackjackService>();
builder.Services.AddScoped<CasinoShiz.Services.Economics.EconomicsService>();
builder.Services.AddScoped<PokerHandler>();
builder.Services.AddScoped<BlackjackHandler>();

builder.Services.AddScoped<LoggingMiddleware>();
builder.Services.AddScoped<ExceptionMiddleware>();
builder.Services.AddScoped<RateLimitMiddleware>();
builder.Services.AddScoped<UpdateRouter>();
builder.Services.AddScoped<UpdatePipeline>();

builder.Services.AddHostedService<BotHostedService>();
builder.Services.AddHostedService<PokerTurnTimeoutService>();
builder.Services.AddHostedService<BlackjackHandTimeoutService>();

builder.Services.AddRazorPages().AddRazorRuntimeCompilation();

var app = builder.Build();

app.UseStaticFiles();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/admin"))
    {
        var opts = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<BotOptions>>().Value;
        var expected = opts.AdminWebToken;
        if (string.IsNullOrEmpty(expected))
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsync("Admin UI disabled: Bot:AdminWebToken not set");
            return;
        }

        static bool TokensMatch(string a, string b)
        {
            var ab = System.Text.Encoding.UTF8.GetBytes(a);
            var bb = System.Text.Encoding.UTF8.GetBytes(b);
            return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
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
    }
    await next();
});

app.MapRazorPages();

var isProduction = builder.Configuration.GetSection(BotOptions.SectionName).GetValue<bool>("IsProduction");
if (isProduction)
{
    app.MapPost($"/{botToken}", async (ITelegramBotClient botClient, UpdateHandler handler, HttpContext ctx) =>
    {
        var update = await ctx.Request.ReadFromJsonAsync<Telegram.Bot.Types.Update>(ctx.RequestAborted);
        if (update != null)
            await handler.HandleUpdateAsync(botClient, update, ctx.RequestAborted);
        return Results.Ok();
    });
}

app.MapGet("/health", () => Results.Ok("ok"));

app.Run();
