using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Narrative.Host;

/// <summary>Schema owned by the reusable narrative persistence extension.</summary>
public sealed class NarrativeProjectionMigrations : IModuleMigrations
{
    public string ModuleId => "narrative";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_projection", """
            CREATE TABLE IF NOT EXISTS narrative_projections (
                tenant_id TEXT NOT NULL DEFAULT '',
                scope_id TEXT NOT NULL DEFAULT '',
                conversation_id TEXT NOT NULL,
                recipient_id TEXT NOT NULL DEFAULT '',
                flags JSONB NOT NULL DEFAULT '{}'::jsonb,
                checkpoint JSONB NULL,
                active_choice JSONB NULL,
                active_choice_expires_at TIMESTAMPTZ NULL,
                revision BIGINT NOT NULL DEFAULT 0,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (tenant_id, scope_id, conversation_id, recipient_id),
                CONSTRAINT ck_narrative_projections_flags_object
                    CHECK (jsonb_typeof(flags) = 'object'),
                CONSTRAINT ck_narrative_projections_revision
                    CHECK (revision >= 0)
            );

            CREATE INDEX IF NOT EXISTS ix_narrative_projections_active_choice_expiry
                ON narrative_projections (active_choice_expires_at)
                WHERE active_choice IS NOT NULL;

            CREATE TABLE IF NOT EXISTS narrative_projection_deliveries (
                delivery_id TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """),
    ];
}
