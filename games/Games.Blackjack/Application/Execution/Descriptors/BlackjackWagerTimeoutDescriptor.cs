using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerTimeoutDescriptor
    : GameExecutionDescriptor<BlackjackWagerTimeout, BlackjackWagerState, BlackjackWagerResult>
{
    public override string GameId => BlackjackWagerConstants.StateGameId;
    public override string CommandId(BlackjackWagerTimeout command) => command.CommandId;
    public override string AggregateId(BlackjackWagerTimeout command) => command.BetId;
    public override long ChatId(BlackjackWagerTimeout command) => command.ChatId;
    public override string DisplayName(BlackjackWagerTimeout command) => command.DisplayName;
    public override WalletIdentity Wallet(BlackjackWagerTimeout command) => new(0, command.ChatId);
    public override bool UsesPrimaryWallet => false;
    public override BlackjackWagerState CreateInitialState(BlackjackWagerTimeout command) =>
        BlackjackWagerConstants.InitialState(command.BetId, command.PlayerId, command.ChatId, command.DisplayName);
}
