namespace BotFramework.Contracts.Wagering;

public sealed record MultiPartyWagerResult(
    string WorkflowId,
    string Status,
    IReadOnlyList<MultiPartyWagerOutcome> Outcomes,
    string? ErrorCode = null)
{
    public bool Accepted => Status is "completed" or "compensated";
    public bool Terminal => Accepted || Status is "failed";
}
