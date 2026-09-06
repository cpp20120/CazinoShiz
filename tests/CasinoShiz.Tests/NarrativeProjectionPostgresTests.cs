using BotFramework.Host.Persistence.Connections;
using BotFramework.Narrative;
using BotFramework.Narrative.Host;
using Npgsql;
using Xunit;

namespace CasinoShiz.Tests;

[Collection(AtomicPostgresCollection.Name)]
public sealed class NarrativeProjectionPostgresTests(AtomicPostgresFixture database)
{
    [Fact]
    public async Task Apply_PersistsResumableStateAndDeduplicatesDurableDelivery()
    {
        await database.ResetAsync();
        var store = CreateStore();
        var address = new NarrativeAddress("story:42", "player:7");
        var scope = new NarrativeProjectionScope("tenant:alpha", "scope:main");

        await store.ApplyAsync(
            new NarrativeFlagEffect(address, "met-guide"),
            Context("delivery:flag"),
            CancellationToken.None);
        await store.ApplyAsync(
            new NarrativeCheckpointEffect(address, "forest:arrival", "resume:42", new Dictionary<string, string?>
            {
                ["chapter"] = "forest",
            }),
            Context("delivery:checkpoint"),
            CancellationToken.None);
        var choice = new ChoiceEffect(
            address,
            "forest:turn:1",
            [new NarrativeChoiceOption("follow", new NarrativeText("choice.follow"), "follow")],
            DateTimeOffset.UtcNow.AddMinutes(5));
        await store.ApplyAsync(choice, Context("delivery:choice"), CancellationToken.None);
        await store.ApplyAsync(choice, Context("delivery:choice"), CancellationToken.None);

        var projection = await store.GetAsync(new NarrativeProjectionKey(address, scope), CancellationToken.None);

        Assert.NotNull(projection);
        Assert.Equal(3, projection.Revision);
        Assert.True(projection.Flags["met-guide"]);
        Assert.Equal("forest:arrival", projection.Checkpoint?.CheckpointId);
        Assert.Equal("forest", projection.Checkpoint?.Data["chapter"]);
        Assert.Equal("forest:turn:1", projection.ActiveChoice?.InteractionId);
        Assert.Equal("follow", Assert.Single(projection.ActiveChoice!.Options).Value);
    }

    [Fact]
    public async Task Get_ExpiresChoiceAndKeepsTenantScopesIsolated()
    {
        await database.ResetAsync();
        var store = CreateStore();
        var address = new NarrativeAddress("story:42", "player:7");
        await store.ApplyAsync(
            new ChoiceEffect(
                address,
                "expired-choice",
                [new NarrativeChoiceOption("close", new NarrativeText("choice.close"))],
                DateTimeOffset.UtcNow.AddMinutes(-1)),
            Context("delivery:expired"),
            CancellationToken.None);

        var current = await store.GetAsync(
            new NarrativeProjectionKey(address, new NarrativeProjectionScope("tenant:alpha", "scope:main")),
            CancellationToken.None);
        var anotherScope = await store.GetAsync(
            new NarrativeProjectionKey(address, new NarrativeProjectionScope("tenant:beta", "scope:main")),
            CancellationToken.None);

        Assert.NotNull(current);
        Assert.Null(current.ActiveChoice);
        Assert.Null(anotherScope);
    }

    private PostgresNarrativeProjectionStore CreateStore() =>
        new(new TestConnectionFactory(database.ConnectionString), TimeProvider.System);

    private static NarrativeDeliveryContext Context(string deliveryId) =>
        new(
            operationId: "command:42",
            metadata: new Dictionary<string, string?>
            {
                ["tenant.id"] = "tenant:alpha",
                ["scope.id"] = "scope:main",
            },
            deliveryId: deliveryId);

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
