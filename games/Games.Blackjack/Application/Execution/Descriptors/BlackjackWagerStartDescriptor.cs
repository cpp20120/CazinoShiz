using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerStartDescriptor
    : GameExecutionDescriptor<BlackjackWagerStart, BlackjackWagerState, BlackjackWagerResult>
{
    public override string GameId => BlackjackWagerConstants.StateGameId;
    public override IReadOnlyList<string> EntropyNames => BlackjackDecisionRules.ShuffleEntropyNames;
    public override string CommandId(BlackjackWagerStart command) => command.CommandId;
    public override string AggregateId(BlackjackWagerStart command) => command.BetId;
    public override long ChatId(BlackjackWagerStart command) => command.ChatId;
    public override string DisplayName(BlackjackWagerStart command) => command.DisplayName;
    public override WalletIdentity Wallet(BlackjackWagerStart command) => new(0, command.ChatId);
    public override bool UsesPrimaryWallet => false;
    public override BlackjackWagerState CreateInitialState(BlackjackWagerStart command) =>
        BlackjackWagerConstants.InitialState(command.BetId, command.PlayerId, command.ChatId, command.DisplayName);
}
