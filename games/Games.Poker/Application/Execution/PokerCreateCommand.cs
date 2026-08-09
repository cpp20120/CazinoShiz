namespace Games.Poker.Application.Execution;

public sealed record PokerCreateCommand(
    long ActorUserId,
    string DisplayName,
    long ChatId,
    string CommandId,
    int BuyIn,
    int SmallBlind,
    int BigBlind,
    IReadOnlyList<PokerWalletRef> ExpectedWallets,
    string? WagerBetId = null) : IPokerExecutionCommand
{
    public string InviteCode => "";
    public bool EnsureActorWallet => WagerBetId is null;
}
