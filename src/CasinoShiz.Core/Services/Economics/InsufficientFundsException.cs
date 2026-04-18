namespace CasinoShiz.Services.Economics;

public sealed class InsufficientFundsException(long userId, int requested, int available)
    : InvalidOperationException($"User {userId} has insufficient funds: requested {requested}, available {available}")
{
    public long UserId { get; } = userId;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}
