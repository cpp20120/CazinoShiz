using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;

namespace BotFramework.Host.Execution;

internal sealed record GameEffectOutboxItem(
    long Id,
    string CommandId,
    string GameId,
    string AggregateId,
    string TypeName,
    string Payload,
    string? OperationId,
    string? TenantId,
    string? ScopeId,
    string? PlayerId,
    string? RequestId,
    string? CorrelationId,
    string? Channel,
    string? ChannelContainerId,
    string? ChannelTopicId,
    int Attempts)
{
    public TenantContext? TenantContext => CreateTenantContext();

    private TenantContext? CreateTenantContext()
    {
        if (TenantId is null || ScopeId is null || RequestId is null || CorrelationId is null)
            return null;

        var channel = Enum.TryParse<BotChannel>(Channel, true, out var parsed)
            ? parsed
            : BotChannel.System;
        return new TenantContext(
            TenantId: BotFramework.Contracts.Tenancy.TenantId.Create(TenantId),
            ScopeId: BotFramework.Contracts.Tenancy.ScopeId.Create(ScopeId),
            PlayerId: PlayerId is null ? null : BotFramework.Contracts.Tenancy.PlayerId.Create(PlayerId),
            Channel: channel,
            RequestId: BotFramework.Contracts.Tenancy.RequestId.Create(RequestId),
            CorrelationId: BotFramework.Contracts.Tenancy.RequestId.Create(CorrelationId))
        {
            ChannelContainerId = ChannelContainerId,
            ChannelTopicId = ChannelTopicId,
        };
    }
}
