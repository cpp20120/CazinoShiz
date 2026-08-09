using System.Globalization;
using System.Net;
using BotFramework.Host.Composition.Builder;
using BotFramework.Rest;
using BotFramework.Sdk.MiniGames;
using CasinoShiz.FullRestWriteLoadTest;
using Games.Basketball.Infrastructure.Modules;
using Games.Blackjack.Infrastructure.Modules;
using Games.Blackjack.Rest;
using Games.Bowling.Infrastructure.Modules;
using Games.Challenges.Infrastructure.Modules;
using Games.Challenges.Rest;
using Games.Darts.Infrastructure.Modules;
using Games.Dice.Infrastructure.Modules;
using Games.Dice.Rest;
using Games.DiceCube.Infrastructure.Modules;
using Games.Football.Infrastructure.Modules;
using Games.Horse.Infrastructure.Modules;
using Games.Horse.Rest;
using Games.Leaderboard.Infrastructure.Modules;
using Games.Leaderboard.Rest;
using Games.Meta.Application.Tournaments;
using Games.Meta.Infrastructure.Modules;
using Games.Meta.Rest;
using Games.NativeDice.Rest;
using Games.Pick.Infrastructure.Modules;
using Games.Pick.Rest;
using Games.PixelBattle.Infrastructure.Modules;
using Games.PixelBattle.Rest;
using Games.Poker.Infrastructure.Modules;
using Games.Poker.Rest;
using Games.Redeem.Infrastructure.Modules;
using Games.Redeem.Rest;
using Games.SecretHitler.Infrastructure.Modules;
using Games.SecretHitler.Rest;
using Games.Transfer.Infrastructure.Modules;
using Games.Transfer.Rest;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

var port = ReadPort();
var database = new PostgreSqlBuilder("postgres:17-alpine")
    .WithDatabase("casinoshiz_full_rest_write_load")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();
WebApplication? app = null;

try
{
    await database.StartAsync();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        ApplicationName = typeof(Program).Assembly.GetName().Name,
        EnvironmentName = Environments.Production,
    });
    builder.WebHost.ConfigureKestrel(options =>
        options.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http1));
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ConnectionStrings:Postgres"] = database.GetConnectionString(),
        ["Bot:Enabled"] = "false",
        ["Bot:StartingCoins"] = "1000000",
        ["Redis:Enabled"] = "false",
        ["ClickHouse:Enabled"] = "false",
        ["Rendering:Minio:Enabled"] = "false",
        ["TelegramOutbox:Transport"] = "Local",
        ["RateLimit:Enabled"] = "false",
        ["Rest:ApiVersion"] = "v1",
        ["Rest:OpenApiEnabled"] = "false",
        ["Rest:RequireTenantClaim"] = "true",
        ["Rest:RequireScopeClaim"] = "true",
        ["Rest:RequireIdempotencyKeyForCommands"] = "true",
        ["DurableWorkflow:Mode"] = "Solo",
        ["DurableWorkflow:AutoCreate"] = "true",
        ["Games:dice:RedeemDropChance"] = "0",
        ["Games:dicecube:MinSecondsBetweenBets"] = "0",
        ["Games:dicecube:RedeemDropChance"] = "0",
        ["Games:darts:RedeemDropChance"] = "0",
        ["Games:football:RedeemDropChance"] = "0",
        ["Games:basketball:RedeemDropChance"] = "0",
        ["Games:bowling:RedeemDropChance"] = "0",
        ["Games:poker:BuyIn"] = "10",
        ["Games:sh:BuyIn"] = "10",
    });
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.AddBackendFramework()
        .AddModule<DiceModule>()
        .AddModule<DiceCubeModule>()
        .AddModule<DartsRemoteModule>()
        .AddModule<FootballModule>()
        .AddModule<BasketballModule>()
        .AddModule<BowlingModule>()
        .AddModule<BlackjackModule>()
        .AddModule<ChallengeModule>()
        .AddModule<HorseModule>()
        .AddModule<LeaderboardModule>()
        .AddModule<MetaModule>()
        .AddModule<PickModule>()
        .AddModule<PixelBattleModule>()
        .AddModule<PokerModule>()
        .AddModule<RedeemModule>()
        .AddModule<SecretHitlerModule>()
        .AddModule<TransferModule>();
    // Register after the framework: module migrations must complete before the
    // timeout dispatcher begins polling its durable-workflow tables.
    builder.AddDurableWorkflows(typeof(TournamentWorkflowHandler).Assembly);
    builder.AddRestFramework();
    builder.Services.AddBlackjackRest();
    builder.Services.AddChallengesRest();
    builder.Services.AddDiceRest();
    builder.Services.AddNativeDiceRest();
    builder.Services.AddSingleton<IMiniGameSessionGhostHeal, NullMiniGameSessionGhostHeal>();
    builder.Services.AddSingleton<BotFramework.Contracts.Identity.IPlayerDirectory, BotFramework.Contracts.Identity.NullPlayerDirectory>();
    builder.Services.AddHorseRest();
    builder.Services.AddLeaderboardRest();
    builder.Services.AddMetaRest();
    builder.Services.AddPickRest();
    builder.Services.AddPixelBattleRest();
    builder.Services.AddPokerRest();
    builder.Services.AddRedeemRest();
    builder.Services.AddSecretHitlerRest();
    builder.Services.AddTransferRest();
    builder.Services.AddAuthentication(LoadTestAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, LoadTestAuthenticationHandler>(LoadTestAuthenticationHandler.SchemeName, static _ => { });

    app = builder.Build();
    app.UseRestFramework();
    app.MapRestFramework();
    await app.StartAsync();
    await SeedTenantWalletsAsync(database.GetConnectionString());

    Console.WriteLine($"FULL_REST_WRITE_LOAD_READY http://127.0.0.1:{port}");
    Console.WriteLine($"FULL_REST_WRITE_LOAD_TOKEN {LoadTestAuthenticationHandler.Token}");
    Console.WriteLine($"FULL_REST_WRITE_LOAD_APP_PROCESS_ID {Environment.ProcessId}");
    Console.WriteLine($"FULL_REST_WRITE_LOAD_DATABASE_CONTAINER_ID {database.Id}");
    await app.WaitForShutdownAsync();
}
finally
{
    if (app is not null)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    await database.DisposeAsync();
}

static int ReadPort()
{
    var value = Environment.GetEnvironmentVariable("FULL_REST_WRITE_LOAD_PORT");
    return int.TryParse(value, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65_535 ? port : 18_120;
}

static async Task SeedTenantWalletsAsync(string connectionString)
{
    var count = ReadLong("FULL_REST_WRITE_LOAD_SEED_USER_COUNT");
    if (count <= 0) return;

    var userBase = ReadLong("FULL_REST_WRITE_LOAD_SEED_USER_BASE");
    var coins = ReadLong("FULL_REST_WRITE_LOAD_SEED_COINS");
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO tenants (tenant_id, display_name)
        VALUES ('write-load', 'full-rest-load')
        ON CONFLICT (tenant_id) DO NOTHING;
        INSERT INTO tenant_scopes (tenant_key, scope_id, is_main)
        SELECT tenant_key, '42', true
        FROM tenants
        WHERE tenant_id = 'write-load'
        ON CONFLICT (tenant_key, scope_id) DO NOTHING;
        """;
    await command.ExecuteNonQueryAsync();

    await using var seedCommand = connection.CreateCommand();
    seedCommand.CommandText = """
        INSERT INTO tenant_wallets (tenant_key, scope_key, player_id, display_name, coins)
        SELECT t.tenant_key, s.scope_key, ids.player_id::text, 'full-rest-load', @coins
        FROM tenants t
        JOIN tenant_scopes s ON s.tenant_key = t.tenant_key AND s.scope_id = '42'
        CROSS JOIN generate_series(@userBase, @userBase + @count - 1) AS ids(player_id)
        WHERE t.tenant_id = 'write-load'
        ON CONFLICT (tenant_key, scope_key, player_id) DO UPDATE
            SET coins = GREATEST(tenant_wallets.coins, EXCLUDED.coins), updated_at = now()
        """;
    seedCommand.Parameters.AddWithValue("coins", coins);
    seedCommand.Parameters.AddWithValue("userBase", userBase);
    seedCommand.Parameters.AddWithValue("count", count);
    await seedCommand.ExecuteNonQueryAsync();
}

static long ReadLong(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : 0;
}
