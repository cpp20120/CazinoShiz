using BotFramework.Host;
using Microsoft.Extensions.Options;

namespace Games.Transfer;

public enum TransferError
{
    None,
    NetBelowMinimum,
    NetAboveMaximum,
    SameUser,
    InsufficientFunds,
    SenderMissing,
    RecipientMissing,
}

public readonly record struct TransferAttemptResult(
    TransferError Error,
    int NetToRecipient,
    int FeeCoins,
    int TotalDebited,
    int SenderBalance,
    int RecipientBalance);

public interface ITransferService
{
    Task<TransferAttemptResult> TryTransferAsync(
        long fromUserId,
        long toUserId,
        long chatId,
        string senderDisplayName,
        string recipientDisplayName,
        int netToRecipient,
        CancellationToken ct);
}

public sealed class TransferService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IOptions<TransferOptions> options) : ITransferService
{
    private readonly TransferOptions _opts = options.Value;

    public async Task<TransferAttemptResult> TryTransferAsync(
        long fromUserId,
        long toUserId,
        long chatId,
        string senderDisplayName,
        string recipientDisplayName,
        int netToRecipient,
        CancellationToken ct)
    {
        if (fromUserId == toUserId)
            return Fail(TransferError.SameUser, netToRecipient);

        if (netToRecipient < _opts.MinNetCoins)
            return Fail(TransferError.NetBelowMinimum, netToRecipient);

        if (_opts.MaxNetCoins > 0 && netToRecipient > _opts.MaxNetCoins)
            return Fail(TransferError.NetAboveMaximum, netToRecipient);

        var fee = TransferOptions.ComputeFeeCoins(netToRecipient, _opts.FeePercent, _opts.MinFeeCoins);
        var total = netToRecipient + fee;

        await economics.EnsureUserAsync(fromUserId, chatId, senderDisplayName, ct);
        await economics.EnsureUserAsync(toUserId, chatId, recipientDisplayName, ct);

        var result = await economics.TryPeerTransferAsync(
            fromUserId,
            toUserId,
            chatId,
            total,
            netToRecipient,
            "transfer.send",
            "transfer.receive",
            ct);

        if (!result.Ok)
        {
            var err = result.Failure switch
            {
                PeerTransferFailure.SameUser => TransferError.SameUser,
                PeerTransferFailure.SenderMissing => TransferError.SenderMissing,
                PeerTransferFailure.RecipientMissing => TransferError.RecipientMissing,
                PeerTransferFailure.InsufficientFunds => TransferError.InsufficientFunds,
                _ => TransferError.SenderMissing,
            };
            var bal = await economics.GetBalanceAsync(fromUserId, chatId, ct);
            return new TransferAttemptResult(err, netToRecipient, fee, total, bal, 0);
        }

        analytics.Track("transfer", "completed", new Dictionary<string, object?>
        {
            ["from_user_id"] = fromUserId,
            ["to_user_id"] = toUserId,
            ["chat_id"] = chatId,
            ["net"] = netToRecipient,
            ["fee"] = fee,
            ["total"] = total,
        });

        return new TransferAttemptResult(
            TransferError.None,
            netToRecipient,
            fee,
            total,
            result.SenderNewBalance,
            result.RecipientNewBalance);
    }

    private static TransferAttemptResult Fail(TransferError error, int net) =>
        new(error, net, 0, 0, 0, 0);
}
