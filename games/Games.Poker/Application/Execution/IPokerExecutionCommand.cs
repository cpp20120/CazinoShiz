namespace Games.Poker.Application.Execution;

public interface IPokerExecutionCommand
{
    string InviteCode { get; }
    long ChatId { get; }
    long ActorUserId { get; }
    string DisplayName { get; }
    string CommandId { get; }
    IReadOnlyList<PokerWalletRef> ExpectedWallets { get; }
    bool EnsureActorWallet { get; }

    /// <summary>
    /// Wagering identity for the optional outcome-only path. Legacy Telegram
    /// and REST commands leave this null and retain their atomic wallet flow.
    /// </summary>
    string? WagerBetId => null;
}
