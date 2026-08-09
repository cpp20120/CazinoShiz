using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerStandDescriptor
    : GameExecutionDescriptor<BlackjackWagerStand, BlackjackWagerState, BlackjackWagerResult>
{
    public override string GameId => BlackjackWagerConstants.StateGameId;
    public override string CommandId(BlackjackWagerStand command) => command.CommandId;
    public override string AggregateId(BlackjackWagerStand command) => command.BetId;
    public override long ChatId(BlackjackWagerStand command) => command.ChatId;
    public override string DisplayName(BlackjackWagerStand command) => command.DisplayName;
    public override WalletIdentity Wallet(BlackjackWagerStand command) => new(0, command.ChatId);
    public override bool UsesPrimaryWallet => false;
    public override BlackjackWagerState CreateInitialState(BlackjackWagerStand command) =>
        BlackjackWagerConstants.InitialState(command.BetId, command.PlayerId, command.ChatId, command.DisplayName);
}
