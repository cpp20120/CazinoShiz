using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Services;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Handlers;
using CasinoShiz.Services.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));
builder.Services.Configure<ClickHouseOptions>(builder.Configuration.GetSection(ClickHouseOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=busino.db")
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
builder.Services.AddScoped<PokerHandler>();

builder.Services.AddScoped<LoggingMiddleware>();
builder.Services.AddScoped<ExceptionMiddleware>();
builder.Services.AddScoped<UpdateRouter>();
builder.Services.AddScoped<UpdatePipeline>();

builder.Services.AddHostedService<BotHostedService>();
builder.Services.AddHostedService<PokerTurnTimeoutService>();

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

        var provided = ctx.Request.Query["token"].ToString();
        if (string.IsNullOrEmpty(provided))
            provided = ctx.Request.Cookies["admin_token"] ?? "";

        if (provided != expected)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }

        ctx.Response.Cookies.Append("admin_token", provided, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromDays(30),
        });
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
