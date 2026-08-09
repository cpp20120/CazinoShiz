using BotFramework.Host.Execution;
using Games.SecretHitler.Application.Execution;

namespace Games.SecretHitler.Application.Wagering;

public abstract class SecretHitlerWagerDescriptor<TCommand, TResult>
    : GameExecutionDescriptor<TCommand, SecretHitlerWagerState, TResult>
    where TCommand : ISecretHitlerExecutionCommand
{
    public override string GameId => "sh";
    public override bool UsesPrimaryWallet => false;
    public override string CommandId(TCommand command) => command.CommandId;
    public override string AggregateId(TCommand command) => string.IsNullOrEmpty(command.InviteCode)
        ? $"user:{command.ActorUserId}" : $"game:{command.InviteCode}";
    public override long ChatId(TCommand command) => command.PublicChatId;
    public override string DisplayName(TCommand command) => command.DisplayName;
    public override WalletIdentity Wallet(TCommand command) => new(command.ActorUserId, command.ActorChatId);
    public override IReadOnlyList<string> AdditionalLockKeys(TCommand command) =>
        command.ExpectedWallets.Select(wallet => new WalletIdentity(wallet.UserId, wallet.ChatId).LockKey)
            .Append($"sh:user:{command.ActorUserId}")
            .Append(string.IsNullOrEmpty(command.InviteCode) ? $"sh:chat:{command.PublicChatId}" : $"sh:game:{command.InviteCode}")
            .Distinct(StringComparer.Ordinal).ToArray();
}
