using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Host.Composition.Migrations;

internal static class FrameworkCasesMigrationDefinition
{
    public static Migration Create() => new("047_framework_cases", """
        CREATE TABLE IF NOT EXISTS framework_cases (
            tenant_key BIGINT NOT NULL,
            scope_key  BIGINT NOT NULL,
            case_id    TEXT NOT NULL,
            case_type  TEXT NOT NULL,
            subject_id TEXT NOT NULL,
            status     TEXT NOT NULL,
            state_json JSONB NOT NULL,
            version    BIGINT NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            PRIMARY KEY (tenant_key, scope_key, case_id),
            CHECK (version >= 0),
            CHECK (status IN ('open', 'evidence', 'review', 'resolved', 'appealed'))
        );
        CREATE INDEX IF NOT EXISTS ix_framework_cases_subject_status
            ON framework_cases (tenant_key, scope_key, subject_id, status, updated_at DESC);
        """);
}
