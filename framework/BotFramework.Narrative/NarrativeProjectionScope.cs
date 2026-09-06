namespace BotFramework.Narrative;

/// <summary>
/// Optional tenant boundary of a persisted narrative projection. It keeps an
/// application-defined <see cref="NarrativeAddress"/> isolated when the same
/// conversation ids exist in more than one tenant or scope.
/// </summary>
public sealed record NarrativeProjectionScope
{
    public NarrativeProjectionScope(string tenantId, string scopeId)
    {
        TenantId = NarrativeValue.Required(tenantId, nameof(tenantId));
        ScopeId = NarrativeValue.Required(scopeId, nameof(scopeId));
    }

    public string TenantId { get; }

    public string ScopeId { get; }
}
