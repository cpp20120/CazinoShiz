namespace Games.SecretHitler.Application.Execution;

public sealed record ShJoinCommand(string InviteCode, long ActorUserId, string DisplayName,
    long PublicChatId, long ActorChatId, string CommandId, int BuyIn,
    IReadOnlyList<SecretHitlerWalletRef> ExpectedWallets, string? WagerBetId = null) : ISecretHitlerExecutionCommand
{
    public bool EnsureActorWallet => WagerBetId is null;
}
