namespace Games.SecretHitler.Application.Execution;

public sealed record ShCreateCommand(long ActorUserId, string DisplayName, long PublicChatId,
    long ActorChatId, string CommandId, int BuyIn,
    IReadOnlyList<SecretHitlerWalletRef> ExpectedWallets, string? WagerBetId = null) : ISecretHitlerExecutionCommand
{
    public string InviteCode => "";
    public bool EnsureActorWallet => WagerBetId is null;
}
