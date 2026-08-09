using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Execution;

namespace BotFramework.Host.Wagering;

public sealed class WagerGameCommandHandler(
    IOutcomeOnlyGameExecutor<WagerGameCommand, WagerGameState, WagerGameResult> executor)
    : IIntegrationCommandHandler<WagerGameCommand>
{
    public Task HandleAsync(WagerGameCommand command, CancellationToken ct) =>
        executor.ExecuteAsync(new GameExecutionEnvelope<WagerGameCommand>(command), ct);
}
