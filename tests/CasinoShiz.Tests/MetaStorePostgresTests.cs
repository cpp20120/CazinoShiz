using BotFramework.Host.Persistence.Connections;
using Games.Meta.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace CasinoShiz.Tests;

[Collection(AtomicPostgresCollection.Name)]
public sealed class MetaStorePostgresTests(AtomicPostgresFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetOrCreateActiveSeasonAsync_ConcurrentFirstRequests_CreateOneSeason()
    {
        var stores = Enumerable.Range(0, 12)
            .Select(_ => new MetaStore(new TestConnectionFactory(database.ConnectionString), new FakeRuntimeTuning()))
            .ToArray();

        var seasons = await Task.WhenAll(stores.Select(store => store.GetOrCreateActiveSeasonAsync(CancellationToken.None)));

        Assert.Single(seasons.Select(season => season.Id).Distinct());
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT count(*) FROM meta_seasons WHERE status = 'active'"));
    }

    [Fact]
    public async Task ActiveSeasonIndex_IsScopedToTenantAndScope()
    {
        var definition = await database.ScalarAsync<string>("""
            SELECT pg_get_indexdef(indexrelid)
            FROM pg_index
            WHERE indexrelid = 'ux_meta_seasons_active'::regclass
            """);

        Assert.Contains("(tenant_key, scope_key)", definition, StringComparison.Ordinal);
        Assert.Contains("WHERE (status = 'active'::text)", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProfileAsync_NewPlayer_DoesNotPersistAPlayerRow()
    {
        var store = new MetaStore(new TestConnectionFactory(database.ConnectionString), new FakeRuntimeTuning());

        var profile = await store.GetProfileAsync(100, 42, "Alice", CancellationToken.None);

        Assert.Equal(42, profile.Player.UserId);
        Assert.Equal("Alice", profile.Player.DisplayName);
        Assert.Equal(0, profile.Player.GamesPlayed);
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT count(*) FROM meta_season_players"));
    }

    private sealed class TestConnectionFactory(string connectionString) : INpgsqlConnectionFactory
    {
        public NpgsqlConnection Create() => new(connectionString);

        public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var connection = Create();
            await connection.OpenAsync(ct);
            return connection;
        }
    }
}
