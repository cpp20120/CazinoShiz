using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>Read-only catalog of game manifests registered by application modules.</summary>
public interface IGameCatalog
{
    IReadOnlyList<GameDefinition> Games { get; }

    bool TryGet(string gameId, out GameDefinition? game);

    GameDefinition GetRequired(string gameId);
}
