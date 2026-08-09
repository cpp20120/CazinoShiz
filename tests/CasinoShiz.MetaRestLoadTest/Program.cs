using System.Globalization;
using System.Net;
using BotFramework.Host.Composition.Builder;
using BotFramework.Host.Persistence.Connections;
using BotFramework.Rest;
using BotFramework.Scheduling.Abstractions;
using CasinoShiz.MetaRestLoadTest;
using Dapper;
using Games.Meta.Infrastructure.Modules;
using Games.Meta.Rest;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

var port = ReadPort();
var redisConnectionString = Environment.GetEnvironmentVariable("META_LOAD_TEST_REDIS_CONNECTION");
var database = new PostgreSqlBuilder("postgres:17-alpine")
    .WithDatabase("casinoshiz_meta_load_test")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();
WebApplication? app = null;

try
{
    await database.StartAsync();
    var adminConnectionString = database.GetConnectionString();
    var applicationPassword = Guid.NewGuid().ToString("N");
    await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
    {
        await adminConnection.OpenAsync();
#pragma warning disable S2077 // PostgreSQL does not permit parameters in CREATE ROLE PASSWORD; the value is a process-local GUID.
        await adminConnection.ExecuteAsync($"""
            CREATE ROLE meta_load LOGIN PASSWORD '{applicationPassword}';
            GRANT ALL PRIVILEGES ON DATABASE casinoshiz_meta_load_test TO meta_load;
            GRANT USAGE, CREATE ON SCHEMA public TO meta_load;
            """);
#pragma warning restore S2077
    }
    var applicationConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
    {
        Username = "meta_load",
        Password = applicationPassword,
    }.ConnectionString;

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        ApplicationName = typeof(Program).Assembly.GetName().Name,
        EnvironmentName = Environments.Production,
    });

    builder.WebHost.ConfigureKestrel(options =>
        options.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http1));
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ConnectionStrings:Postgres"] = applicationConnectionString,
        ["Bot:Enabled"] = "false",
        ["Redis:Enabled"] = (!string.IsNullOrWhiteSpace(redisConnectionString)).ToString(),
        ["Redis:ConnectionString"] = redisConnectionString,
        ["ClickHouse:Enabled"] = "false",
        ["Rendering:Minio:Enabled"] = "false",
        ["TelegramOutbox:Transport"] = "Local",
        ["RateLimit:Enabled"] = "false",
        ["Rest:ApiVersion"] = "v1",
        ["Rest:OpenApiEnabled"] = "false",
        ["Rest:RequireTenantClaim"] = "true",
        ["Rest:RequireScopeClaim"] = "true",
        ["DurableWorkflow:Mode"] = "Solo",
        ["DurableWorkflow:AutoCreate"] = "true",
    });
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.AddBackendFramework().AddModule<MetaModule>();
    // This host deliberately provisions only the Meta schema. Scheduled Meta
    // analytics aggregates every game table, so it would add unrelated failed
    // queries and allocations to the measured request path.
    builder.Services.RemoveAll<IRecurringScheduledCommand>();
    builder.AddRestFramework();
    builder.Services.AddMetaRest();
    builder.Services
        .AddAuthentication(LoadTestAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, LoadTestAuthenticationHandler>(
            LoadTestAuthenticationHandler.SchemeName,
            static _ => { });

    app = builder.Build();
    app.UseRestFramework();
    app.MapRestFramework();
    app.MapGet("/api/v1/tenants/{tenantId}/scopes/{scopeId}/debug/db-context", async (INpgsqlConnectionFactory connections, CancellationToken ct) =>
    {
        await using var connection = await connections.OpenAsync(ct);
        return Results.Ok(await connection.QuerySingleAsync(new CommandDefinition("""
            SELECT current_setting('casinoshiz.tenant_id', true) AS tenant_id,
                   current_setting('casinoshiz.scope_id', true) AS scope_id,
                   current_setting('casinoshiz.tenant_bound', true) AS tenant_bound,
                   casinoshiz_current_tenant_key() AS tenant_key,
                   casinoshiz_current_scope_key() AS scope_key,
                   (SELECT count(*) FROM meta_seasons WHERE status = 'active') AS visible_active_seasons
            """, cancellationToken: ct)));
    }).RequireAuthorization().WithName("MetaLoadDebugContext");
    await app.StartAsync();

    Console.WriteLine($"META_LOAD_TEST_READY http://127.0.0.1:{port}");
    Console.WriteLine("META_LOAD_TEST_TOKEN meta-load-test");
    Console.WriteLine($"META_LOAD_TEST_APP_PROCESS_ID {Environment.ProcessId}");
    Console.WriteLine($"META_LOAD_TEST_DATABASE_CONTAINER_ID {database.Id}");
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
    var value = Environment.GetEnvironmentVariable("META_LOAD_TEST_PORT");
    return int.TryParse(value, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65_535
        ? port
        : 18_110;
}
