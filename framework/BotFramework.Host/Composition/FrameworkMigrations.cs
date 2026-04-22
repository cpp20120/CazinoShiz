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

        new Migration("005_admin_audit", """
            CREATE TABLE IF NOT EXISTS admin_audit (
                id           BIGSERIAL    PRIMARY KEY,
                actor_id     BIGINT       NOT NULL,
                actor_name   TEXT         NOT NULL,
                action       TEXT         NOT NULL,
                details      JSONB        NOT NULL DEFAULT '{}',
                occurred_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_admin_audit_actor ON admin_audit (actor_id, occurred_at);
            CREATE INDEX IF NOT EXISTS ix_admin_audit_at    ON admin_audit (occurred_at);
            """),

        new Migration("006_per_chat_wallets_and_ledger", """
            ALTER TABLE users RENAME TO users_legacy;
            CREATE TABLE users (
                telegram_user_id  BIGINT       NOT NULL,
                balance_scope_id  BIGINT       NOT NULL,
                display_name      TEXT         NOT NULL,
                coins             INTEGER      NOT NULL DEFAULT 0,
                version           BIGINT       NOT NULL DEFAULT 0,
                created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
                PRIMARY KEY (telegram_user_id, balance_scope_id)
            );
            CREATE INDEX IF NOT EXISTS ix_users_scope_coins ON users (balance_scope_id, coins DESC);
            INSERT INTO users (telegram_user_id, balance_scope_id, display_name, coins, version, created_at, updated_at)
            SELECT telegram_user_id, telegram_user_id, display_name, coins, version, created_at, updated_at
            FROM users_legacy;
            DROP TABLE users_legacy;
            -- Replace any pre-existing wrong-shape economics_ledger (IF NOT EXISTS would skip a bad table).
            DROP TABLE IF EXISTS economics_ledger;
            CREATE TABLE economics_ledger (
                id                  BIGSERIAL      PRIMARY KEY,
                telegram_user_id    BIGINT         NOT NULL,
                balance_scope_id    BIGINT         NOT NULL,
                delta               INTEGER        NOT NULL,
                balance_after       INTEGER        NOT NULL,
                reason              TEXT           NOT NULL,
                created_at          TIMESTAMPTZ    NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_economics_ledger_user_scope ON economics_ledger (telegram_user_id, balance_scope_id, id DESC);
            CREATE INDEX IF NOT EXISTS ix_economics_ledger_created ON economics_ledger (created_at DESC);
            """),

        new Migration("007_known_chats", """
            CREATE TABLE IF NOT EXISTS known_chats (
                chat_id       BIGINT         PRIMARY KEY,
                chat_type     TEXT           NOT NULL,
                title         TEXT,
                username      TEXT,
                first_seen_at TIMESTAMPTZ    NOT NULL DEFAULT now(),
                last_seen_at  TIMESTAMPTZ    NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_known_chats_last ON known_chats (last_seen_at DESC);
            CREATE INDEX IF NOT EXISTS ix_known_chats_type ON known_chats (chat_type);
            INSERT INTO known_chats (chat_id, chat_type, title, username, first_seen_at, last_seen_at)
            SELECT u.balance_scope_id,
                CASE
                    WHEN u.balance_scope_id < 0 THEN 'supergroup'
                    ELSE 'private'
                END,
                NULL,
                NULL,
                min(u.created_at),
                max(u.updated_at)
            FROM users u
            GROUP BY u.balance_scope_id
            ON CONFLICT (chat_id) DO NOTHING;
            """),
    ];
}
