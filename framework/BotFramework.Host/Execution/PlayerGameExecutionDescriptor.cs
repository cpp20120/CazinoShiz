using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>
/// Descriptor base for commands that carry the standard player identity. New
/// games can inherit it instead of repeating command, wallet and actor plumbing.
/// </summary>
public abstract class PlayerGameExecutionDescriptor<TCommand, TState, TResult>
    : GameExecutionDescriptor<TCommand, TState, TResult>
    where TCommand : IPlayerGameCommand
{
    protected PlayerGameExecutionDescriptor(GameDefinition game)
    {
        Game = game ?? throw new ArgumentNullException(nameof(game));
    }

    protected GameDefinition Game { get; }

    public sealed override string GameId => Game.GameId;

    public sealed override string CommandId(TCommand command) => command.CommandId;

    public override string AggregateId(TCommand command) => PlayerAggregateId(command);

    public sealed override long ChatId(TCommand command) => command.ChatId;

    public sealed override string DisplayName(TCommand command) => command.DisplayName;

    public sealed override WalletIdentity Wallet(TCommand command) => new(command.UserId, command.ChatId);

    public override IReadOnlyList<string> EntropyNames => Game.EntropyNames;

    public sealed override GameCapabilitySet RequiredCapabilities => Game.RequiredCapabilities;

    /// <summary>
    /// Default aggregate scope is one player in one chat, namespaced by game.
    /// Stateful games can override this for another aggregate identity.
    /// </summary>
    protected virtual string PlayerAggregateId(TCommand command) =>
        $"{GameId}:{command.ChatId}:{command.UserId}";
}
