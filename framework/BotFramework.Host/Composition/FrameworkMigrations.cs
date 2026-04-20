// ─────────────────────────────────────────────────────────────────────────────
// FrameworkMigrations — schema the Host itself owns.
//
// Only two tables today: module_events (the event stream) and module_snapshots
// (the snapshot cache). Tracked in __module_migrations under module_id
// "_framework" so a future change lands via the same forward-only migration
// path modules use. The tracking table itself is created directly by
// ModuleMigrationRunner, not through this migration — chicken-and-egg.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host.Composition;

internal sealed class FrameworkMigrations : IModuleMigrations
{
    public string ModuleId => "_framework";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_event_store", """
            CREATE TABLE IF NOT EXISTS module_events (
                id           BIGSERIAL    PRIMARY KEY,
                stream_id    TEXT         NOT NULL,
                version      BIGINT       NOT NULL,
                event_type   TEXT         NOT NULL,
                payload      JSONB        NOT NULL,
                occurred_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                UNIQUE (stream_id, version)
            );
            CREATE INDEX IF NOT EXISTS ix_module_events_stream ON module_events (stream_id, version);
            CREATE INDEX IF NOT EXISTS ix_module_events_type   ON module_events (event_type, occurred_at);
            """),

        new Migration("002_snapshots", """
            CREATE TABLE IF NOT EXISTS module_snapshots (
                stream_id     TEXT         PRIMARY KEY,
                aggregate     TEXT         NOT NULL,
                version       BIGINT       NOT NULL,
                state         JSONB        NOT NULL,
                taken_at      TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_module_snapshots_aggregate ON module_snapshots (aggregate, taken_at);
            """),

        new Migration("003_users", """
            CREATE TABLE IF NOT EXISTS users (
                telegram_user_id  BIGINT       PRIMARY KEY,
                display_name      TEXT         NOT NULL,
                coins             INTEGER      NOT NULL DEFAULT 0,
                version           BIGINT       NOT NULL DEFAULT 0,
                created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at        TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            """),

        new Migration("004_event_log", """
            CREATE TABLE IF NOT EXISTS event_log (
                id           BIGSERIAL    PRIMARY KEY,
                event_type   TEXT         NOT NULL,
                payload      JSONB        NOT NULL,
                occurred_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_event_log_type ON event_log (event_type, occurred_at);
            CREATE INDEX IF NOT EXISTS ix_event_log_at ON event_log (occurred_at);
            """),
    ];
}
