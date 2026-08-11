using BotFramework.Contracts.Operations;
using BotFramework.Host.Admin.Audit;
using BotFramework.Host.Events.Dispatch;
using BotFramework.Host.Contracts.Economics;
using BotFramework.Host.Runtime.Jobs;
using BotFramework.Host.TelegramOutbox;
using BotFramework.Contracts.Games;

namespace BotFramework.Host.Admin.Operations;

public sealed class OperationsAdminService(IEventDispatchFailureStore failures, IEventDispatchRetryService retry,
    ITelegramOutboxStore outbox, IBackgroundJobStatusService jobs, IAdminAuditReader audits, IAdminAuditLog audit,
    IEconomicsService economics, IGameAvailabilityService availability, IReadOnlyEventReplayService replay,
    IEconomySimulationService simulation, IRandomOutcomeGenerator fairness,
    IWagerWorkflowTimelineReader wagerTimelines)
    : IOperationsAdminService
{
    public async Task<IReadOnlyList<OperationFailure>> ListFailuresAsync(int limit, string? eventType, CancellationToken ct) =>
        [.. (await failures.ListUnresolvedAsync(Math.Clamp(limit, 1, 100), eventType, ct)).Select(x => new OperationFailure(
            x.Id,x.StreamId,x.StreamVersion,x.EventType,x.Stage,x.HandlerName,x.Error,x.ErrorType,x.RetryCount,x.CreatedAt,x.LastSeenAt))];
    public async Task<IReadOnlyList<OperationOutbox>> ListOutboxAsync(int limit, string? status, CancellationToken ct) =>
        [.. (await outbox.ListUnsentAsync(Math.Clamp(limit, 1, 100), status, ct)).Select(x => new OperationOutbox(
            x.Id,x.ChatId,x.Status,x.Attempts,x.NextAttemptAt,x.LockedUntil,x.LastError,x.MessagePreview,x.CreatedAt,x.UpdatedAt))];
    public Task<IReadOnlyList<OperationJob>> ListJobsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OperationJob>>(
        [.. jobs.Snapshot().Select(x => new OperationJob(x.Name,x.Kind,x.State,x.LastStartedAt,x.LastHeartbeatAt,
            x.LastCompletedAt,x.LastFailedAt,x.NextRunAt,x.CrashCount,x.RestartBackoffMs,x.LastError,x.Note))]);
    public async Task<IReadOnlyList<OperationAudit>> ListAuditAsync(int limit, string? actor, string? action,
        string? details, DateTimeOffset? from, DateTimeOffset? until, CancellationToken ct) =>
        [.. (await audits.ListAsync(Math.Clamp(limit,1,1000),actor,action,details,from,until,ct)).Select(x =>
            new OperationAudit(x.Id,x.ActorId,x.ActorName,x.Action,x.DetailsJson,x.OccurredAt))];
    public Task<WagerWorkflowTimeline?> GetWagerTimelineAsync(string operationId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(operationId)
            ? Task.FromResult<WagerWorkflowTimeline?>(null)
            : wagerTimelines.GetAsync(operationId.Trim(), ct);
    public async Task<OperationMutationResult> RetryEventAsync(long id,long actorId,string actorName,CancellationToken ct)
    {
        EventDispatchRetryResult result;
        try { result = await retry.RetryAsync(id,ct); }
        catch(Exception ex) { result = new(false,false,$"dispatch failed: {ex.Message}"); }
        await audit.LogAsync(actorId,actorName,"recovery.event_retry",new { recordId=id,result=result.Success?"succeeded":"rejected",result.Message },ct);
        return new(result.Success,result.Message ?? "event retry failed");
    }
    public async Task<OperationMutationResult> RescheduleOutboxAsync(long id,long actorId,string actorName,CancellationToken ct)
    {
        var result=await outbox.RescheduleNowAsync(id,ct);
        await audit.LogAsync(actorId,actorName,"recovery.outbox_reschedule",new { recordId=id,result=result.Outcome.ToString(),result.Message },ct);
        return new(result.Success,result.Message);
    }

    public async Task<OperationMutationResult> AdjustWalletAsync(long userId, long balanceScopeId, int delta,
        string operationId, long actorId, string actorName, CancellationToken ct)
    {
        if (delta == 0 || string.IsNullOrWhiteSpace(operationId))
            return new(false, "A non-zero delta and operation ID are required.");

        var result = delta > 0
            ? await economics.CreditOnceAsync(userId, balanceScopeId, delta, "admin.adjust", operationId, ct)
            : await economics.TryDebitOnceAsync(userId, balanceScopeId, -delta, "admin.adjust", operationId, ct);
        var success = result is { Applied: true, Rejected: false };
        await audit.LogAsync(actorId, actorName, "wallet.adjust", new
        {
            userId, balanceScopeId, delta, operationId,
            result = success ? "succeeded" : "rejected",
            result.NewBalance,
        }, ct);
        return new(success, success
            ? $"Wallet adjusted. Balance: {result.NewBalance}."
            : "Adjustment was rejected by Wallet.");
    }

    public Task<IReadOnlyList<GameAvailability>> ListGameAvailabilityAsync(long? chatId, CancellationToken ct) =>
        availability.ListOverridesAsync(chatId, ct);

    public async Task<OperationMutationResult> SetGameAvailabilityAsync(long chatId, string gameId, bool enabled,
        string reason, long actorId, string actorName, CancellationToken ct)
    {
        try
        {
            await availability.SetOverrideAsync(new(chatId, gameId, enabled, reason, actorId, actorName), ct);
            return new(true, $"Game '{gameId}' is now {(enabled ? "enabled" : "disabled")} for chat {chatId}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, exception.Message);
        }
    }

    public async Task<EventReplayReport> ReplayEventStreamAsync(string streamId, long actorId, string actorName,
        CancellationToken ct)
    {
        var report = await replay.ReplayAsync(streamId, ct);
        await audit.LogAsync(actorId, actorName, "tools.event_replay", new
        {
            streamId,
            events = report.Steps.Count,
            report.FirstIncompatibleVersion,
        }, ct);
        return report;
    }

    public async Task<EconomySimulationReport> SimulateEconomyAsync(EconomySimulationRequest request,
        long actorId, string actorName, CancellationToken ct)
    {
        var report = simulation.Simulate(request);
        await audit.LogAsync(actorId, actorName, "tools.economy_simulation", new
        {
            request.Players,
            request.Rounds,
            request.Seed,
            request.Rules,
        }, ct);
        return report;
    }

    public Task<IReadOnlyList<FairnessCommitment>> ListIncompleteFairnessAsync(CancellationToken ct) =>
        fairness.ListIncompleteAsync(ct);
}
