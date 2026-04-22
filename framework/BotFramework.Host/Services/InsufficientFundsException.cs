namespace BotFramework.Host.Services;

public sealed class InsufficientFundsException(long userId, long balanceScopeId, int requested, int available)
    : InvalidOperationException(
        $"User {userId} scope {balanceScopeId} has insufficient funds: requested {requested}, available {available}")
{
    public long UserId { get; } = userId;
    public long BalanceScopeId { get; } = balanceScopeId;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}
