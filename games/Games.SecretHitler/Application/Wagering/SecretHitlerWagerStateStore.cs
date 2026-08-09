using BotFramework.Host.Execution;
using Games.SecretHitler.Application.Execution;
using Games.SecretHitler.Infrastructure.Persistence;

namespace Games.SecretHitler.Application.Wagering;

public sealed class SecretHitlerWagerStateStore<TCommand>(
    SecretHitlerExecutionStateStore<TCommand> inner)
    : IGameStateStore<TCommand, SecretHitlerWagerState>
    where TCommand : ISecretHitlerExecutionCommand
{
    public async Task<SecretHitlerWagerState> LoadAsync(
        TCommand command, IGameExecutionContext context, CancellationToken ct)
    {
        var state = await inner.LoadAsync(command, context, ct);
        return new(state.Game, state.Players);
    }

    public Task SaveAsync(TCommand command, SecretHitlerWagerState state,
        IGameExecutionContext context, CancellationToken ct) =>
        inner.SaveAsync(command, new(state.Game, state.Players, null, false, false), context, ct);
}
