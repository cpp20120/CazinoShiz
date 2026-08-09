using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerHitDescriptor
    : GameExecutionDescriptor<BlackjackWagerHit, BlackjackWagerState, BlackjackWagerResult>
{
    public override string GameId => BlackjackWagerConstants.StateGameId;
    public override string CommandId(BlackjackWagerHit command) => command.CommandId;
    public override string AggregateId(BlackjackWagerHit command) => command.BetId;
    public override long ChatId(BlackjackWagerHit command) => command.ChatId;
    public override string DisplayName(BlackjackWagerHit command) => command.DisplayName;
    public override WalletIdentity Wallet(BlackjackWagerHit command) => new(0, command.ChatId);
    public override bool UsesPrimaryWallet => false;
    public override BlackjackWagerState CreateInitialState(BlackjackWagerHit command) =>
        BlackjackWagerConstants.InitialState(command.BetId, command.PlayerId, command.ChatId, command.DisplayName);
}
