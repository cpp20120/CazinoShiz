namespace Games.Poker.Application.Execution;

public sealed record PokerJoinCommand(
    string InviteCode,
    long ActorUserId,
    string DisplayName,
    long ChatId,
    string CommandId,
    int BuyIn,
    int MaxPlayers,
    IReadOnlyList<PokerWalletRef> ExpectedWallets,
    string? WagerBetId = null) : IPokerExecutionCommand
{
    public bool EnsureActorWallet => WagerBetId is null;
}
