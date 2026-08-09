namespace BotFramework.Contracts.Wagering;

/// <summary>One independently reserved player in a multi-party wager.</summary>
public sealed record MultiPartyWagerParticipant(
    string OperationId,
    string BetId,
    string PlayerId,
    WagerTermsSnapshot Terms)
{
    public long Stake => Terms.Stake;

    public string Currency => Terms.Currency;
}
