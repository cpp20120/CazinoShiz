namespace BotFramework.Contracts.Wagering;

public interface IMultiPartyWagerCoordinator
{
    Task<MultiPartyWagerResult> StartAsync(
        MultiPartyWagerRequest request,
        CancellationToken ct);

    Task<MultiPartyWagerResult?> GetAsync(
        string workflowId,
        CancellationToken ct);
}
