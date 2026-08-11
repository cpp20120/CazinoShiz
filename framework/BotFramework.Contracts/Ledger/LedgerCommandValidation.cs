namespace BotFramework.Contracts.Ledger;

public static class LedgerCommandValidation
{
    public static string ValidateAmount(long amount, string parameterName = "amount")
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(parameterName, amount, "Amount must be positive.");

        return parameterName;
    }

    public static void ValidateCommon(string operationId, string currency, string reason)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("Operation id is required.", nameof(operationId));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));
    }

    public static void ValidateAccount(string accountId, string parameterName = "accountId")
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("Account id is required.", parameterName);
    }
}
