using BotFramework.Contracts.Wagering;
using BotFramework.Sdk.Execution;
using System.Text.Json;

namespace BotFramework.Host.Wagering;

public sealed class WagerGameAction(IEnumerable<IWagerGameResolver> resolvers)
    : IGameAction<WagerGameCommand, WagerGameState, WagerGameResult>
{
    public GameDecision<WagerGameState, WagerGameResult> Decide(
        GameActionInput<WagerGameState, WagerGameCommand> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var command = input.Command;
        var state = input.State;

        if (!string.Equals(state.GameId, command.GameId, StringComparison.Ordinal)
            || !string.Equals(state.BetId, command.BetId, StringComparison.Ordinal)
            || !string.Equals(state.PlayerId, command.PlayerId, StringComparison.Ordinal))
            return Reject(state, "aggregate_mismatch");
        if (command.ExpectedRevision != state.Revision)
            return Reject(state, "revision_conflict");
        if (state.OutcomeCode is not null)
            return Reject(state, "already_completed");

        var resolver = resolvers.SingleOrDefault(x =>
            string.Equals(x.GameId, command.GameId, StringComparison.Ordinal));
        if (resolver is null)
            throw new InvalidOperationException($"No wager game resolver is registered for '{command.GameId}'.");

        WagerGameResolution resolution;
        try
        {
            resolution = resolver.Resolve(command, state, input.Quotas, input.Entropy, input.UtcNow);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            resolution = new WagerGameResolution(
                "invalid_input",
                JsonSerializer.Serialize(new { error = exception.Message }));
        }
        var next = state with
        {
            Revision = checked(state.Revision + 1),
            OutcomeCode = resolution.OutcomeCode,
            Evidence = resolution.Evidence,
        };
        var domainEvent = new WagerGameOutcomeDeclared(
            command.GameId,
            command.BetId,
            command.PlayerId,
            resolution.OutcomeCode,
            resolution.Evidence,
            next.Revision,
            input.UtcNow.ToUnixTimeMilliseconds());

        return new GameDecision<WagerGameState, WagerGameResult>(
            DecisionStatus.Accepted,
            next,
            new WagerGameResult(true, resolution.OutcomeCode, resolution.Evidence, next.Revision),
            [], [], [], [domainEvent], []);
    }

    private static GameDecision<WagerGameState, WagerGameResult> Reject(
        WagerGameState state,
        string code) =>
        new(
            DecisionStatus.Rejected,
            state,
            new WagerGameResult(false, ErrorCode: code, Revision: state.Revision),
            [], [], [], [], [], code);
}
