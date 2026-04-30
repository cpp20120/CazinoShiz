// ─────────────────────────────────────────────────────────────────────────────
// CasinoShiz.Host — Program composition root.
//
// This is the entire Host for the pilot distribution: standard ASP.NET Core
// WebApplication bootstrap, one call to AddBotFramework() to register every
// framework-owned service, one AddModule<T>() per game this distribution
// ships, then Build() + UseBotFramework() + Run().
//
// Bringing up another distribution (a party-games bot, a trading bot) is the
// same three lines plus a different module list. No per-module DI wiring
// leaks here — modules own their own ConfigureServices.
// ─────────────────────────────────────────────────────────────────────────────

using CasinoShiz.Host;
using CasinoShiz.Host.Debug;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Games.Admin;
using Games.Basketball;
using Games.Blackjack;
using Games.Bowling;
using Games.Challenges;
using Games.Darts;
using Games.Dice;
using Games.Football;
using Games.DiceCube;
using Games.Horse;
using Games.Leaderboard;
using Games.PixelBattle;
using Games.Poker;
using Games.Redeem;
using Games.SecretHitler;
using Games.Transfer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CasinoShiz.Host.Pages.Admin.HorseGifCache>();
builder.Services.AddScoped<IMiniGameSessionGhostHeal, MiniGameSessionGhostHeal>();

builder.AddBotFramework()
    .AddModule<DebugModule>()
    .AddModule<DiceModule>()
    .AddModule<DiceCubeModule>()
    .AddModule<DartsModule>()
    .AddModule<FootballModule>()
    .AddModule<BasketballModule>()
    .AddModule<BowlingModule>()
    .AddModule<ChallengeModule>()
    .AddModule<BlackjackModule>()
    .AddModule<HorseModule>()
    .AddModule<PokerModule>()
    .AddModule<SecretHitlerModule>()
    .AddModule<RedeemModule>()
    .AddModule<LeaderboardModule>()
    .AddModule<TransferModule>()
    .AddModule<PixelBattleModule>()
    .AddModule<AdminModule>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;


#pragma warning disable ASPDEPR005
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
#pragma warning restore ASPDEPR005
});


var app = builder.Build();

app.UseBotFramework();
app.UseForwardedHeaders();
app.UseStaticFiles();
app.MapPixelBattle();

app.Run();
