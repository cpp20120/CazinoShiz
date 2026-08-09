using BotFramework.Host.Execution;
using Games.Horse.Application.Execution;

namespace Games.Horse.Application.Wagering;

public sealed class HorseWagerPlaceBetDescriptor
    : GameExecutionDescriptor<HorsePlaceBetCommand, HorseWagerState, BetResult>
{
    public override bool UsesPrimaryWallet => false;
    public override string GameId => "horse";
    public override string CommandId(HorsePlaceBetCommand command) => command.CommandId;
    public override string AggregateId(HorsePlaceBetCommand command) =>
        $"wager:{command.WagerBetId}";
    public override long ChatId(HorsePlaceBetCommand command) => command.BalanceScopeId;
    public override string DisplayName(HorsePlaceBetCommand command) => command.DisplayName;
    public override WalletIdentity Wallet(HorsePlaceBetCommand command) => new(0, command.BalanceScopeId);
}
