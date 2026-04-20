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

using BotFramework.Host.Composition;
using Games.Admin;
using Games.Basketball;
using Games.Blackjack;
using Games.Bowling;
using Games.Darts;
using Games.Dice;
using Games.DiceCube;
using Games.Horse;
using Games.Leaderboard;
using Games.Poker;
using Games.Redeem;
using Games.SecretHitler;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CasinoShiz.Host.Pages.Admin.HorseGifCache>();

builder.AddBotFramework()
    .AddModule<DiceModule>()
    .AddModule<DiceCubeModule>()
    .AddModule<DartsModule>()
    .AddModule<BasketballModule>()
    .AddModule<BowlingModule>()
    .AddModule<BlackjackModule>()
    .AddModule<HorseModule>()
    .AddModule<PokerModule>()
    .AddModule<SecretHitlerModule>()
    .AddModule<RedeemModule>()
    .AddModule<LeaderboardModule>()
    .AddModule<AdminModule>();

var app = builder.Build();

app.UseBotFramework();

app.Run();
