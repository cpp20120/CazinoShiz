using BotFramework.Host.Execution;

namespace BotFramework.Narrative.Host;

internal sealed class NarrativeGameEffectHandler(
    INarrativeProjectionWriter projections,
    INarrativeEffectSink? sink = null)
    : GameEffectHandler<NarrativeEffect>
{
    protected override async Task ApplyBatchAsync(
        IReadOnlyList<NarrativeEffect> effects,
        IGameExecutionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var delivery = CreateDeliveryContext(context);
        foreach (var effect in effects)
            await projections.ApplyAsync(effect, delivery, ct);

        if (sink is not null)
        {
            await sink.ApplyAsync(effects, delivery, ct);
            return;
        }

        if (effects.Any(static effect => effect is SceneEffect or DialogEffect))
        {
            throw new InvalidOperationException(
                "Narrative scene or dialog output requires an INarrativeEffectSink frontend adapter.");
        }
    }

    private static NarrativeDeliveryContext CreateDeliveryContext(IGameExecutionContext context)
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

        return new NarrativeDeliveryContext(context.OperationId, metadata, context.EffectDeliveryId);
    }
}
