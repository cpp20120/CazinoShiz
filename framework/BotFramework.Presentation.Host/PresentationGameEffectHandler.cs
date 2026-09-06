using BotFramework.Host.Execution;

namespace BotFramework.Presentation.Host;

internal sealed class PresentationGameEffectHandler(IPresentationEffectSink sink)
    : GameEffectHandler<PresentationEffect>
{
    protected override Task ApplyBatchAsync(
        IReadOnlyList<PresentationEffect> effects,
        IGameExecutionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        return sink.ApplyAsync(effects, CreateDeliveryContext(context), ct);
    }

    private static PresentationDeliveryContext CreateDeliveryContext(IGameExecutionContext context)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (context.TenantContext is { } tenant)
        {
            metadata.Add("tenant.id", tenant.TenantId.Value);
            metadata.Add("scope.id", tenant.ScopeId.Value);
            metadata.Add("player.id", tenant.PlayerId?.Value);
            metadata.Add("channel", tenant.Channel.ToString());
            metadata.Add("request.id", tenant.RequestId.Value);
            metadata.Add("correlation.id", tenant.CorrelationId.Value);
            metadata.Add("channel.container-id", tenant.ChannelContainerId);
            metadata.Add("channel.topic-id", tenant.ChannelTopicId);
        }

        return new PresentationDeliveryContext(context.OperationId, metadata, context.EffectDeliveryId);
    }
}
