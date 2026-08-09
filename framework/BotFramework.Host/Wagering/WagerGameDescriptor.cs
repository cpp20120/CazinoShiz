using BotFramework.Contracts.Wagering;
using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Wagering;

public sealed class WagerGameDescriptor
    : GameExecutionDescriptor<WagerGameCommand, WagerGameState, WagerGameResult>
{
    public override string GameId => "wager-game";

    public override string CommandId(WagerGameCommand command) => command.CommandId;

    public override string AggregateId(WagerGameCommand command) => command.BetId;

    public override long ChatId(WagerGameCommand command) => command.ChatId;

    public override string DisplayName(WagerGameCommand command) => command.DisplayName;

    public override WalletIdentity Wallet(WagerGameCommand command) => new(0, command.ChatId);

    public override bool UsesPrimaryWallet => false;

    public override IReadOnlyList<string> EntropyNames => ["outcome"];

    public override WagerGameState CreateInitialState(WagerGameCommand command) =>
        new(0, command.GameId, command.BetId, command.PlayerId, null, null);
}
