using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class GameCatalog : IGameCatalog
{
    private readonly IReadOnlyDictionary<string, GameDefinition> games;

    public GameCatalog(IEnumerable<GameDefinition> definitions)
    {
        games = Build(definitions);
        Games = games.Values
            .OrderBy(static game => game.GameId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<GameDefinition> Games { get; }

    public bool TryGet(string gameId, out GameDefinition? game)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (games.TryGetValue(gameId, out var found))
        {
            game = found;
            return true;
        }

        game = null;
        return false;
    }

    public GameDefinition GetRequired(string gameId) => TryGet(gameId, out var game)
        ? game!
        : throw new KeyNotFoundException($"Game '{gameId}' is not registered.");

    private static Dictionary<string, GameDefinition> Build(IEnumerable<GameDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var catalog = new Dictionary<string, GameDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition is null)
                throw new ArgumentNullException(nameof(definitions), "A game catalog cannot contain a null definition.");
            if (!catalog.TryAdd(definition.GameId, definition))
                throw new InvalidOperationException($"Game '{definition.GameId}' is registered more than once.");
        }

        return catalog;
    }
}
