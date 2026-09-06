// ─────────────────────────────────────────────────────────────────────────────
// FrameworkMigrations — schema the Host itself owns.
//
// Framework-owned registry, coordination, execution, wallet, event and
// snapshot tables are tracked in __module_migrations under module_id
// "_framework" so every schema change lands through the same forward-only
// migration path modules use. The tracking table itself is created directly
// by ModuleMigrationRunner, not through this migration — chicken-and-egg.
// ─────────────────────────────────────────────────────────────────────────────


namespace BotFramework.Host.Composition.Migrations;

internal sealed class FrameworkMigrations : IModuleMigrations
{
    public string ModuleId => "_framework";

    /// <summary>
    /// Framework-owned migrations which must run after module schemas exist.
    /// They still use the framework migration stream so modules never own the
    /// tenant boundary they are being migrated onto.
    /// </summary>
    public static IReadOnlyList<Migration> PostModuleMigrations =>
        FrameworkTenantBoundaryMigrations.PostModuleMigrations;

    private static readonly HashSet<string> WalletOwnedIds =
    [
        "003_users",
        "006_per_chat_wallets_and_ledger",
        "008_users_last_daily_bonus",
        "009_users_telegram_dice_daily",
        "014_economics_operation_id",
        "017_responsible_gaming_and_ops_reports",
        "038_wallet_scope_aliases",
        "039_wallet_operation_results",
        "040_wallet_hot_path_maintenance",
    ];

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
            """)
        {
            // The local k3s database was initialized by an equivalent
            // pre-module image whose raw-string formatting produced this hash.
            // Keep the migration forward-only while allowing that deployment
            // to start and apply migrations 037+.
            LegacyContentHashes = new HashSet<string>(StringComparer.Ordinal)
            {
                "0D2AC35A42CDA4142BAD9B74195D55F2C726D08F7BA7CD32F0FBC383DB9895A7"
            }
        },

        new Migration("008_users_last_daily_bonus", """
            ALTER TABLE users
                ADD COLUMN IF NOT EXISTS last_daily_bonus_on DATE;
            """),

        new Migration("009_users_telegram_dice_daily", """
            ALTER TABLE users
                ADD COLUMN IF NOT EXISTS telegram_dice_rolls_on DATE,
                ADD COLUMN IF NOT EXISTS telegram_dice_roll_count INTEGER NOT NULL DEFAULT 0;
            """),

        new Migration("010_runtime_tuning", """
            CREATE TABLE IF NOT EXISTS runtime_tuning (
                id          SMALLINT PRIMARY KEY CHECK (id = 1),
                payload     JSONB NOT NULL DEFAULT '{}'::jsonb,
                updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            INSERT INTO runtime_tuning (id, payload) VALUES (1, '{}'::jsonb)
            ON CONFLICT (id) DO NOTHING;
            """),

        new Migration("011_delivery_and_coordination", """
            CREATE TABLE IF NOT EXISTS processed_updates (
                update_id      BIGINT       PRIMARY KEY,
                status         TEXT         NOT NULL,
                started_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
                completed_at   TIMESTAMPTZ,
                error          TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_processed_updates_started ON processed_updates (started_at DESC);

            CREATE TABLE IF NOT EXISTS game_command_idempotency (
                idempotency_key TEXT         PRIMARY KEY,
                status          TEXT         NOT NULL,
                result_json     JSONB,
                started_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
                completed_at    TIMESTAMPTZ,
                error           TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_game_command_idempotency_started ON game_command_idempotency (started_at DESC);

            CREATE TABLE IF NOT EXISTS mini_game_sessions (
                user_id     BIGINT       NOT NULL,
                chat_id     BIGINT       NOT NULL,
                game_id     TEXT         NOT NULL,
                expires_at  TIMESTAMPTZ  NOT NULL,
                updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, chat_id)
            );
            CREATE INDEX IF NOT EXISTS ix_mini_game_sessions_expires ON mini_game_sessions (expires_at);

            CREATE TABLE IF NOT EXISTS mini_game_roll_gates (
                game_id     TEXT         NOT NULL,
                user_id     BIGINT       NOT NULL,
                chat_id     BIGINT       NOT NULL,
                expires_at  TIMESTAMPTZ  NOT NULL,
                updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                PRIMARY KEY (game_id, user_id, chat_id)
            );
            CREATE INDEX IF NOT EXISTS ix_mini_game_roll_gates_expires ON mini_game_roll_gates (expires_at);
            """),

        new Migration("012_telegram_dice_daily_per_game", """
            CREATE TABLE IF NOT EXISTS telegram_dice_daily_rolls (
                telegram_user_id    BIGINT       NOT NULL,
                balance_scope_id    BIGINT       NOT NULL,
                game_id             TEXT         NOT NULL,
                rolls_on            DATE,
                roll_count          INTEGER      NOT NULL DEFAULT 0,
                updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                PRIMARY KEY (telegram_user_id, balance_scope_id, game_id)
            );
            CREATE INDEX IF NOT EXISTS ix_telegram_dice_daily_rolls_scope_game
                ON telegram_dice_daily_rolls (balance_scope_id, game_id, rolls_on);

            INSERT INTO telegram_dice_daily_rolls (
                telegram_user_id,
                balance_scope_id,
                game_id,
                rolls_on,
                roll_count,
                updated_at
            )
            SELECT
                u.telegram_user_id,
                u.balance_scope_id,
                g.game_id,
                u.telegram_dice_rolls_on,
                u.telegram_dice_roll_count,
                now()
            FROM users u
            CROSS JOIN (VALUES
                ('dice'),
                ('dicecube'),
                ('darts'),
                ('football'),
                ('basketball'),
                ('bowling')
            ) AS g(game_id)
            WHERE u.telegram_dice_rolls_on IS NOT NULL
              AND u.telegram_dice_roll_count > 0
            ON CONFLICT (telegram_user_id, balance_scope_id, game_id) DO NOTHING;
            """)
        {
            // The local k3s database was initialized by the previous
            // equivalent dice backfill. Its stored hash differs only because
            // the historical raw SQL formatting changed.
            LegacyContentHashes = new HashSet<string>(StringComparer.Ordinal)
            {
                "5A52EC8FAEE999274D63914F2299541047048A7227EDE8D60A62004FD3197FB7"
            }
        },

        new Migration("013_event_dispatch_failures", """
            CREATE TABLE IF NOT EXISTS event_dispatch_failures (
                id              BIGSERIAL    PRIMARY KEY,
                stream_id       TEXT         NOT NULL,
                stream_version  BIGINT       NOT NULL,
                event_type      TEXT         NOT NULL,
                stage           TEXT         NOT NULL,
                handler_name    TEXT         NOT NULL,
                error           TEXT         NOT NULL,
                error_type      TEXT,
                retry_count     INTEGER      NOT NULL DEFAULT 0,
                created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
                last_seen_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                resolved_at     TIMESTAMPTZ,
                UNIQUE (stream_id, stream_version, stage, handler_name)
            );
            CREATE INDEX IF NOT EXISTS ix_event_dispatch_failures_unresolved
                ON event_dispatch_failures (resolved_at, last_seen_at DESC);
            CREATE INDEX IF NOT EXISTS ix_event_dispatch_failures_event
                ON event_dispatch_failures (event_type, last_seen_at DESC);
            """),

        new Migration("014_economics_operation_id", """
            ALTER TABLE economics_ledger
                ADD COLUMN IF NOT EXISTS operation_id TEXT;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_economics_ledger_operation_id
                ON economics_ledger (operation_id)
                WHERE operation_id IS NOT NULL;
            """),

        new Migration("015_telegram_outbox", """
            CREATE TABLE IF NOT EXISTS telegram_outbox (
                id                  BIGSERIAL    PRIMARY KEY,
                dedupe_key          TEXT,
                chat_id             BIGINT       NOT NULL,
                text                TEXT         NOT NULL,
                parse_mode          TEXT,
                status              TEXT         NOT NULL DEFAULT 'pending',
                attempts            INTEGER      NOT NULL DEFAULT 0,
                next_attempt_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
                locked_until        TIMESTAMPTZ,
                last_error          TEXT,
                telegram_message_id INTEGER,
                created_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                sent_at             TIMESTAMPTZ
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_telegram_outbox_dedupe_key
                ON telegram_outbox (dedupe_key)
                WHERE dedupe_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_telegram_outbox_due
                ON telegram_outbox (status, next_attempt_at, id)
                WHERE status = 'pending';
            """),

        new Migration("016_event_analytics_checkpoint", """
            CREATE TABLE IF NOT EXISTS event_analytics_checkpoint (
                id              SMALLINT    PRIMARY KEY CHECK (id = 1),
                last_event_id   BIGINT      NOT NULL DEFAULT 0,
                updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            INSERT INTO event_analytics_checkpoint (id, last_event_id)
            VALUES (1, 0)
            ON CONFLICT (id) DO NOTHING;
            """),

        new Migration("017_responsible_gaming_and_ops_reports", """
            CREATE TABLE IF NOT EXISTS player_protection (
                telegram_user_id    BIGINT       PRIMARY KEY,
                daily_stake_limit   INTEGER,
                cooldown_until      TIMESTAMPTZ,
                self_excluded_until TIMESTAMPTZ,
                updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                CHECK (daily_stake_limit IS NULL OR daily_stake_limit >= 0)
            );
            CREATE INDEX IF NOT EXISTS ix_player_protection_active
                ON player_protection (cooldown_until, self_excluded_until);

            CREATE TABLE IF NOT EXISTS operations_report_checkpoint (
                report_key       TEXT         PRIMARY KEY,
                period_key       TEXT         NOT NULL,
                updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            """),

        new Migration("018_telegram_outbox_expired_lease_recovery", """
            CREATE INDEX IF NOT EXISTS ix_telegram_outbox_expired_lease
                ON telegram_outbox (locked_until, id)
                WHERE status = 'sending';
            """),

        // Quartz AdoJobStore schema, based on Quartz.NET 3.15's PostgreSQL DDL.
        // It is deliberately owned by the framework migration runner so the
        // scheduler may start after this service on every fresh deployment.
        new Migration("019_quartz_ado_job_store", """
            CREATE TABLE IF NOT EXISTS qrtz_job_details (
                sched_name TEXT NOT NULL, job_name TEXT NOT NULL, job_group TEXT NOT NULL,
                description TEXT NULL, job_class_name TEXT NOT NULL,
                is_durable BOOL NOT NULL, is_nonconcurrent BOOL NOT NULL,
                is_update_data BOOL NOT NULL, requests_recovery BOOL NOT NULL,
                job_data BYTEA NULL,
                PRIMARY KEY (sched_name, job_name, job_group)
            );
            CREATE TABLE IF NOT EXISTS qrtz_triggers (
                sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL,
                job_name TEXT NOT NULL, job_group TEXT NOT NULL, description TEXT NULL,
                next_fire_time BIGINT NULL, prev_fire_time BIGINT NULL, priority INTEGER NULL,
                trigger_state TEXT NOT NULL, trigger_type TEXT NOT NULL, start_time BIGINT NOT NULL,
                end_time BIGINT NULL, calendar_name TEXT NULL, misfire_instr SMALLINT NULL,
                job_data BYTEA NULL,
                PRIMARY KEY (sched_name, trigger_name, trigger_group),
                FOREIGN KEY (sched_name, job_name, job_group)
                    REFERENCES qrtz_job_details (sched_name, job_name, job_group)
            );
            CREATE TABLE IF NOT EXISTS qrtz_simple_triggers (
                sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL,
                repeat_count BIGINT NOT NULL, repeat_interval BIGINT NOT NULL, times_triggered BIGINT NOT NULL,
                PRIMARY KEY (sched_name, trigger_name, trigger_group),
                FOREIGN KEY (sched_name, trigger_name, trigger_group)
                    REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS qrtz_simprop_triggers (
                sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL,
                str_prop_1 TEXT NULL, str_prop_2 TEXT NULL, str_prop_3 TEXT NULL,
                int_prop_1 INTEGER NULL, int_prop_2 INTEGER NULL,
                long_prop_1 BIGINT NULL, long_prop_2 BIGINT NULL, dec_prop_1 NUMERIC NULL,
                dec_prop_2 NUMERIC NULL, bool_prop_1 BOOL NULL, bool_prop_2 BOOL NULL,
                time_zone_id TEXT NULL,
                PRIMARY KEY (sched_name, trigger_name, trigger_group),
                FOREIGN KEY (sched_name, trigger_name, trigger_group)
                    REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS qrtz_cron_triggers (
                sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL,
                cron_expression TEXT NOT NULL, time_zone_id TEXT NULL,
                PRIMARY KEY (sched_name, trigger_name, trigger_group),
                FOREIGN KEY (sched_name, trigger_name, trigger_group)
                    REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS qrtz_blob_triggers (
                sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL,
                blob_data BYTEA NULL,
                PRIMARY KEY (sched_name, trigger_name, trigger_group),
                FOREIGN KEY (sched_name, trigger_name, trigger_group)
                    REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS qrtz_calendars (
                sched_name TEXT NOT NULL, calendar_name TEXT NOT NULL, calendar BYTEA NOT NULL,
                PRIMARY KEY (sched_name, calendar_name)
            );
            CREATE TABLE IF NOT EXISTS qrtz_paused_trigger_grps (
                sched_name TEXT NOT NULL, trigger_group TEXT NOT NULL,
                PRIMARY KEY (sched_name, trigger_group)
            );
            CREATE TABLE IF NOT EXISTS qrtz_fired_triggers (
                sched_name TEXT NOT NULL, entry_id TEXT NOT NULL, trigger_name TEXT NOT NULL,
                trigger_group TEXT NOT NULL, instance_name TEXT NOT NULL, fired_time BIGINT NOT NULL,
                sched_time BIGINT NOT NULL, priority INTEGER NOT NULL, state TEXT NOT NULL,
                job_name TEXT NULL, job_group TEXT NULL, is_nonconcurrent BOOL NOT NULL,
                requests_recovery BOOL NULL, PRIMARY KEY (sched_name, entry_id)
            );
            CREATE TABLE IF NOT EXISTS qrtz_scheduler_state (
                sched_name TEXT NOT NULL, instance_name TEXT NOT NULL, last_checkin_time BIGINT NOT NULL,
                checkin_interval BIGINT NOT NULL, PRIMARY KEY (sched_name, instance_name)
            );
            CREATE TABLE IF NOT EXISTS qrtz_locks (
                sched_name TEXT NOT NULL, lock_name TEXT NOT NULL,
                PRIMARY KEY (sched_name, lock_name)
            );
            CREATE INDEX IF NOT EXISTS idx_qrtz_j_req_recovery ON qrtz_job_details (requests_recovery);
            CREATE INDEX IF NOT EXISTS idx_qrtz_t_next_fire_time ON qrtz_triggers (next_fire_time);
            CREATE INDEX IF NOT EXISTS idx_qrtz_t_state ON qrtz_triggers (trigger_state);
            CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st ON qrtz_triggers (next_fire_time, trigger_state);
            CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_name ON qrtz_fired_triggers (trigger_name);
            CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_group ON qrtz_fired_triggers (trigger_group);
            CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_nm_gp ON qrtz_fired_triggers (sched_name, trigger_name, trigger_group);
            CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_inst_name ON qrtz_fired_triggers (instance_name);
            CREATE INDEX IF NOT EXISTS idx_qrtz_ft_job_name ON qrtz_fired_triggers (job_name);
            CREATE INDEX IF NOT EXISTS idx_qrtz_ft_job_group ON qrtz_fired_triggers (job_group);
            CREATE INDEX IF NOT EXISTS idx_qrtz_ft_job_req_recovery ON qrtz_fired_triggers (requests_recovery);
            """),

        new Migration("020_game_availability", """
            CREATE TABLE IF NOT EXISTS game_availability_overrides (
                chat_id     BIGINT      NOT NULL,
                game_id     TEXT        NOT NULL,
                enabled     BOOLEAN     NOT NULL,
                reason      TEXT        NOT NULL,
                changed_by  BIGINT      NOT NULL,
                changed_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (chat_id, game_id)
            );
            CREATE INDEX IF NOT EXISTS ix_game_availability_game
                ON game_availability_overrides (game_id, chat_id);
            """),

        new Migration("021_fairness_audit", """
            CREATE TABLE IF NOT EXISTS fairness_audit (
                id                    BIGSERIAL   PRIMARY KEY,
                game_id               TEXT        NOT NULL,
                algorithm_version     TEXT        NOT NULL,
                commitment            TEXT        NOT NULL,
                canonical_input_hash  TEXT        NOT NULL,
                server_seed           TEXT        NOT NULL,
                revealed_seed         TEXT,
                result_value          INTEGER,
                result_hash           TEXT,
                entropy_source        TEXT        NOT NULL,
                status                TEXT        NOT NULL,
                created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
                completed_at          TIMESTAMPTZ,
                CHECK (entropy_source IN ('server', 'external')),
                CHECK (status IN ('committed', 'completed', 'abandoned'))
            );
            CREATE INDEX IF NOT EXISTS ix_fairness_incomplete
                ON fairness_audit (created_at) WHERE status = 'committed';
            """),

        new Migration("022_atomic_game_command_inbox", """
            ALTER TABLE game_command_idempotency
                ADD COLUMN IF NOT EXISTS game_id TEXT,
                ADD COLUMN IF NOT EXISTS aggregate_id TEXT,
                ADD COLUMN IF NOT EXISTS result_type TEXT,
                ADD COLUMN IF NOT EXISTS entropy_json JSONB;

            CREATE INDEX IF NOT EXISTS ix_game_command_idempotency_game_aggregate
                ON game_command_idempotency (game_id, aggregate_id, started_at DESC);
            """),

        new Migration("023_atomic_game_event_outbox", """
            CREATE TABLE IF NOT EXISTS game_event_outbox (
                id              BIGSERIAL   PRIMARY KEY,
                command_id      TEXT        NOT NULL,
                event_index     INTEGER     NOT NULL,
                event_type      TEXT        NOT NULL,
                type_name       TEXT        NOT NULL,
                payload         JSONB       NOT NULL,
                occurred_at     TIMESTAMPTZ NOT NULL,
                status          TEXT        NOT NULL DEFAULT 'pending',
                attempts        INTEGER     NOT NULL DEFAULT 0,
                next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                locked_until    TIMESTAMPTZ,
                last_error      TEXT,
                created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
                sent_at         TIMESTAMPTZ,
                UNIQUE (command_id, event_index)
            );
            CREATE INDEX IF NOT EXISTS ix_game_event_outbox_due
                ON game_event_outbox (status, next_attempt_at, id)
                WHERE status IN ('pending', 'sending');
            """),

        new Migration("024_versioned_game_aggregate_states", """
            CREATE TABLE IF NOT EXISTS game_aggregate_states (
                game_id       TEXT        NOT NULL,
                aggregate_id  TEXT        NOT NULL,
                state_type    TEXT        NOT NULL,
                version       BIGINT      NOT NULL,
                state         JSONB       NOT NULL,
                updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (game_id, aggregate_id),
                CHECK (version >= 0)
            );
            CREATE INDEX IF NOT EXISTS ix_game_aggregate_states_updated
                ON game_aggregate_states (game_id, updated_at DESC);
            """),

        new Migration("025_atomic_game_schedule_outbox", """
            CREATE TABLE IF NOT EXISTS game_schedule_outbox (
                id              BIGSERIAL   PRIMARY KEY,
                command_id      TEXT        NOT NULL,
                effect_index    INTEGER     NOT NULL,
                schedule_id     TEXT        NOT NULL,
                effect_kind     TEXT        NOT NULL,
                job_key         TEXT,
                due_at          TIMESTAMPTZ,
                data            JSONB       NOT NULL DEFAULT '{}'::jsonb,
                status          TEXT        NOT NULL DEFAULT 'pending',
                attempts        INTEGER     NOT NULL DEFAULT 0,
                next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                locked_until    TIMESTAMPTZ,
                last_error      TEXT,
                created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
                sent_at         TIMESTAMPTZ,
                UNIQUE (command_id, effect_index),
                CHECK (effect_kind IN ('schedule', 'cancel')),
                CHECK (
                    (effect_kind = 'schedule' AND job_key IS NOT NULL AND due_at IS NOT NULL)
                    OR effect_kind = 'cancel')
            );
            CREATE INDEX IF NOT EXISTS ix_game_schedule_outbox_due
                ON game_schedule_outbox (status, next_attempt_at, id)
                WHERE status IN ('pending', 'sending');
            CREATE INDEX IF NOT EXISTS ix_game_schedule_outbox_order
                ON game_schedule_outbox (schedule_id, id, status);
            """),

        new Migration("026_discord_outbox", """
            CREATE TABLE IF NOT EXISTS discord_outbox (
                id                  BIGSERIAL    PRIMARY KEY,
                dedupe_key          TEXT,
                user_id             BIGINT       NOT NULL,
                channel_id          BIGINT       NOT NULL,
                text                TEXT         NOT NULL,
                title               TEXT,
                culture             TEXT         NOT NULL DEFAULT 'ru',
                status              TEXT         NOT NULL DEFAULT 'pending',
                attempts            INTEGER      NOT NULL DEFAULT 0,
                next_attempt_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
                locked_until        TIMESTAMPTZ,
                last_error          TEXT,
                discord_message_id  BIGINT,
                created_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                sent_at             TIMESTAMPTZ
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_discord_outbox_dedupe_key
                ON discord_outbox (dedupe_key)
                WHERE dedupe_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_discord_outbox_due
                ON discord_outbox (status, next_attempt_at, id)
                WHERE status IN ('pending', 'sending');
            """),

        new Migration("027_schedule_outbox_ownership", """
            ALTER TABLE game_schedule_outbox
                ADD COLUMN IF NOT EXISTS game_id TEXT;

            -- Older rows already carry the game id in the scoped schedule id
            -- (game:aggregate:schedule). Backfill them before distributed
            -- workers start filtering the shared outbox by module ownership.
            UPDATE game_schedule_outbox
            SET game_id = split_part(schedule_id, ':', 1)
            WHERE game_id IS NULL
              AND position(':' IN schedule_id) > 0;

            CREATE INDEX IF NOT EXISTS ix_game_schedule_outbox_game_due
                ON game_schedule_outbox (game_id, status, next_attempt_at, id)
                WHERE status IN ('pending', 'sending');
            """),

        new Migration("028_tenant_registry", """
            CREATE TABLE IF NOT EXISTS tenants (
                tenant_key   BIGSERIAL PRIMARY KEY,
                tenant_id    TEXT        NOT NULL UNIQUE,
                display_name TEXT        NOT NULL,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS tenant_scopes (
                scope_key  BIGSERIAL PRIMARY KEY,
                tenant_key BIGINT      NOT NULL REFERENCES tenants(tenant_key),
                scope_id   TEXT        NOT NULL,
                is_main   BOOLEAN     NOT NULL DEFAULT false,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                UNIQUE (tenant_key, scope_id),
                UNIQUE (tenant_key, scope_key)
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_scopes_tenant
                ON tenant_scopes (tenant_key, scope_key);

            CREATE TABLE IF NOT EXISTS channel_bindings (
                binding_key BIGSERIAL PRIMARY KEY,
                channel     TEXT        NOT NULL,
                container_id TEXT       NOT NULL,
                topic_id    TEXT,
                tenant_key  BIGINT      NOT NULL REFERENCES tenants(tenant_key),
                scope_key   BIGINT      NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key),
                UNIQUE (channel, container_id, topic_id)
            );
            CREATE INDEX IF NOT EXISTS ix_channel_bindings_scope
                ON channel_bindings (tenant_key, scope_key, channel);

            INSERT INTO tenants (tenant_id, display_name)
            VALUES ('legacy:default', 'Legacy data')
            ON CONFLICT (tenant_id) DO NOTHING;

            INSERT INTO tenant_scopes (tenant_key, scope_id, is_main)
            SELECT tenant_key, 'main', true
            FROM tenants
            WHERE tenant_id = 'legacy:default'
            ON CONFLICT (tenant_key, scope_id) DO NOTHING;

            -- Existing deployments may run Backend without wallet tables.
            -- The conditional block keeps the registry migration valid in both
            -- profiles while preserving every legacy wallet's scope boundary.
            DO $$
            DECLARE legacy_tenant BIGINT;
            BEGIN
                SELECT tenant_key INTO legacy_tenant FROM tenants WHERE tenant_id = 'legacy:default';
                IF to_regclass('public.users') IS NOT NULL THEN
                    EXECUTE $sql$
                        INSERT INTO tenant_scopes (tenant_key, scope_id, is_main)
                        SELECT $1, 'legacy:' || balance_scope_id::text, false
                        FROM (SELECT DISTINCT balance_scope_id FROM users) legacy_scopes
                        ON CONFLICT (tenant_key, scope_id) DO NOTHING
                    $sql$ USING legacy_tenant;

                    ALTER TABLE users ADD COLUMN IF NOT EXISTS tenant_key BIGINT;
                    ALTER TABLE users ADD COLUMN IF NOT EXISTS scope_key BIGINT;
                    ALTER TABLE users ADD COLUMN IF NOT EXISTS player_id TEXT;

                    EXECUTE $sql$
                        UPDATE users u
                        SET tenant_key = t.tenant_key,
                            scope_key = s.scope_key,
                            player_id = u.telegram_user_id::text
                        FROM tenants t
                        JOIN tenant_scopes s ON s.tenant_key = t.tenant_key
                        WHERE t.tenant_id = 'legacy:default'
                          AND s.scope_id = 'legacy:' || u.balance_scope_id::text
                          AND (u.tenant_key IS NULL OR u.scope_key IS NULL OR u.player_id IS NULL)
                    $sql$;

                    ALTER TABLE users
                        ADD CONSTRAINT fk_users_tenant
                        FOREIGN KEY (tenant_key) REFERENCES tenants(tenant_key);
                    ALTER TABLE users
                        ADD CONSTRAINT fk_users_scope
                        FOREIGN KEY (tenant_key, scope_key)
                        REFERENCES tenant_scopes(tenant_key, scope_key);
                    CREATE INDEX IF NOT EXISTS ix_users_tenant_scope_player
                        ON users (tenant_key, scope_key, player_id);
                END IF;
            END $$;
            """),

        new Migration("029_tenant_owned_coordination", """
            -- Tenant-scoped coordination records are separate from legacy
            -- numeric message/source identifiers. They are the canonical
            -- storage boundary used by new SDK modules.
            CREATE TABLE IF NOT EXISTS tenant_idempotency_keys (
                tenant_key       BIGINT NOT NULL,
                scope_key        BIGINT NOT NULL,
                player_id        TEXT,
                idempotency_key  TEXT NOT NULL,
                request_id       TEXT NOT NULL,
                response_status  INTEGER,
                response_payload JSONB,
                created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
                completed_at     TIMESTAMPTZ,
                PRIMARY KEY (tenant_key, scope_key, idempotency_key),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key)
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_idempotency_player
                ON tenant_idempotency_keys (tenant_key, scope_key, player_id, created_at DESC);

            CREATE TABLE IF NOT EXISTS tenant_inbox_records (
                tenant_key      BIGINT NOT NULL,
                scope_key       BIGINT NOT NULL,
                message_id      TEXT NOT NULL,
                channel         TEXT NOT NULL,
                request_id      TEXT NOT NULL,
                payload         JSONB NOT NULL,
                received_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (tenant_key, scope_key, message_id),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key)
            );

            CREATE TABLE IF NOT EXISTS tenant_outbox_records (
                tenant_key      BIGINT NOT NULL,
                scope_key       BIGINT NOT NULL,
                event_id        BIGSERIAL,
                event_type      TEXT NOT NULL,
                request_id      TEXT NOT NULL,
                payload         JSONB NOT NULL,
                status          TEXT NOT NULL DEFAULT 'pending',
                created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
                sent_at         TIMESTAMPTZ,
                PRIMARY KEY (tenant_key, scope_key, event_id),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key)
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_outbox_due
                ON tenant_outbox_records (tenant_key, scope_key, status, created_at);

            CREATE TABLE IF NOT EXISTS tenant_schedules (
                tenant_key      BIGINT NOT NULL,
                scope_key       BIGINT NOT NULL,
                schedule_id     TEXT NOT NULL,
                module_id       TEXT NOT NULL,
                due_at          TIMESTAMPTZ NOT NULL,
                payload         JSONB NOT NULL,
                PRIMARY KEY (tenant_key, scope_key, schedule_id),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key)
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_schedules_due
                ON tenant_schedules (tenant_key, scope_key, due_at);

            CREATE TABLE IF NOT EXISTS tenant_telemetry_context (
                tenant_key      BIGINT NOT NULL,
                scope_key       BIGINT NOT NULL,
                request_id      TEXT NOT NULL,
                correlation_id  TEXT NOT NULL,
                channel         TEXT NOT NULL,
                player_id       TEXT,
                created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (tenant_key, scope_key, request_id),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key)
            );

            ALTER TABLE channel_bindings
                DROP CONSTRAINT IF EXISTS channel_bindings_channel_container_id_topic_id_key;
            ALTER TABLE channel_bindings
                ADD CONSTRAINT ux_channel_bindings_tenant_container
                UNIQUE (tenant_key, channel, container_id, topic_id);
            """),

        new Migration("030_rate_limit_policy_overrides", """
            CREATE TABLE IF NOT EXISTS rate_limit_policy_overrides (
                tenant_key         BIGINT NOT NULL REFERENCES tenants(tenant_key),
                channel            TEXT NOT NULL DEFAULT '',
                route_key          TEXT NOT NULL DEFAULT '',
                dimension          TEXT NOT NULL,
                capacity           INTEGER NOT NULL CHECK (capacity > 0),
                refill_per_second  DOUBLE PRECISION NOT NULL CHECK (refill_per_second >= 0),
                version             BIGINT NOT NULL DEFAULT 1 CHECK (version > 0),
                updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (tenant_key, channel, route_key, dimension),
                CHECK (channel IN ('', 'rest', 'telegram', 'discord')),
                CHECK (route_key = '' OR (length(route_key) <= 256 AND route_key !~ '[[:space:]/\\\\]')),
                CHECK (dimension IN ('tenant', 'tenantplayer', 'tenantip', 'tenantroute', 'tenantplayerroute'))
            );
            CREATE INDEX IF NOT EXISTS ix_rate_limit_policy_overrides_tenant
                ON rate_limit_policy_overrides (tenant_key, channel, route_key, dimension);
            """),

        new Migration("031_legacy_tenant_columns_backfill", """
            -- Legacy tables remain writable while demo modules migrate. The
            -- new columns are the canonical boundary for SDK 0.9 modules;
            -- old numeric columns are retained only for the staged game cutover.
            DO $$
            DECLARE
                table_name TEXT;
                table_names TEXT[] := ARRAY[
                    'module_events', 'module_snapshots', 'event_log',
                    'event_dispatch_failures', 'admin_audit', 'fairness_audit',
                    'game_command_idempotency', 'game_event_outbox',
                    'game_aggregate_states', 'game_schedule_outbox',
                    'telegram_outbox', 'discord_outbox', 'known_chats',
                    'mini_game_sessions', 'mini_game_roll_gates',
                    'telegram_dice_daily_rolls', 'economics_ledger',
                    'player_protection', 'game_availability_overrides'
                ];
            BEGIN
                FOREACH table_name IN ARRAY table_names LOOP
                    IF to_regclass('public.' || table_name) IS NOT NULL THEN
                        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS tenant_key BIGINT', table_name);
                        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS scope_key BIGINT', table_name);
                        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS player_id TEXT', table_name);
                        EXECUTE format($sql$
                            UPDATE %I c
                            SET tenant_key = t.tenant_key,
                                scope_key = s.scope_key
                            FROM tenants t
                            JOIN tenant_scopes s ON s.tenant_key = t.tenant_key AND s.scope_id = 'main'
                            WHERE t.tenant_id = 'legacy:default'
                              AND (c.tenant_key IS NULL OR c.scope_key IS NULL)
                        $sql$, table_name);
                    END IF;
                END LOOP;
            END $$;

            -- Preserve the old scope boundary wherever the legacy table had
            -- a chat/scope column. Unknown historical scopes safely remain in
            -- legacy:default/main until their owning module is migrated.
            DO $$
            BEGIN
                IF to_regclass('public.economics_ledger') IS NOT NULL THEN
                    UPDATE economics_ledger e
                    SET tenant_key = t.tenant_key,
                        scope_key = s.scope_key,
                        player_id = e.telegram_user_id::text
                    FROM users u
                    JOIN tenants t ON t.tenant_id = 'legacy:default'
                    JOIN tenant_scopes s ON s.tenant_key = t.tenant_key
                        AND s.scope_id = 'legacy:' || u.balance_scope_id::text
                    WHERE e.telegram_user_id = u.telegram_user_id
                      AND e.balance_scope_id = u.balance_scope_id;
                END IF;

                IF to_regclass('public.telegram_dice_daily_rolls') IS NOT NULL THEN
                    UPDATE telegram_dice_daily_rolls r
                    SET tenant_key = t.tenant_key,
                        scope_key = s.scope_key,
                        player_id = r.telegram_user_id::text
                    FROM tenants t
                    JOIN tenant_scopes s ON s.tenant_key = t.tenant_key
                    WHERE t.tenant_id = 'legacy:default'
                      AND s.scope_id = 'legacy:' || r.balance_scope_id::text;
                END IF;

                IF to_regclass('public.mini_game_sessions') IS NOT NULL THEN
                    UPDATE mini_game_sessions m
                    SET tenant_key = t.tenant_key,
                        scope_key = s.scope_key,
                        player_id = m.user_id::text
                    FROM tenants t
                    JOIN tenant_scopes s ON s.tenant_key = t.tenant_key
                    WHERE t.tenant_id = 'legacy:default'
                      AND s.scope_id = 'legacy:' || m.chat_id::text;
                END IF;

                IF to_regclass('public.mini_game_roll_gates') IS NOT NULL THEN
                    UPDATE mini_game_roll_gates m
                    SET tenant_key = t.tenant_key,
                        scope_key = s.scope_key,
                        player_id = m.user_id::text
                    FROM tenants t
                    JOIN tenant_scopes s ON s.tenant_key = t.tenant_key
                    WHERE t.tenant_id = 'legacy:default'
                      AND s.scope_id = 'legacy:' || m.chat_id::text;
                END IF;

                IF to_regclass('public.player_protection') IS NOT NULL THEN
                    UPDATE player_protection p
                    SET tenant_key = u.tenant_key,
                        scope_key = u.scope_key,
                        player_id = p.telegram_user_id::text
                    FROM users u
                    WHERE p.telegram_user_id = u.telegram_user_id;
                END IF;
            END $$;

            DO $$
            DECLARE table_name TEXT;
            DECLARE table_names TEXT[] := ARRAY[
                'module_events', 'module_snapshots', 'event_log',
                'event_dispatch_failures', 'admin_audit', 'fairness_audit',
                'game_command_idempotency', 'game_event_outbox',
                'game_aggregate_states', 'game_schedule_outbox',
                'telegram_outbox', 'discord_outbox', 'known_chats',
                'mini_game_sessions', 'mini_game_roll_gates',
                'telegram_dice_daily_rolls', 'economics_ledger',
                'player_protection', 'game_availability_overrides'
            ];
            BEGIN
                FOREACH table_name IN ARRAY table_names LOOP
                    IF to_regclass('public.' || table_name) IS NOT NULL THEN
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_constraint WHERE conname = 'fk_' || table_name || '_tenant') THEN
                            EXECUTE format(
                                'ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY (tenant_key) REFERENCES tenants(tenant_key)',
                                table_name, 'fk_' || table_name || '_tenant');
                        END IF;
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_constraint WHERE conname = 'fk_' || table_name || '_scope') THEN
                            EXECUTE format(
                                'ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY (tenant_key, scope_key) REFERENCES tenant_scopes(tenant_key, scope_key)',
                                table_name, 'fk_' || table_name || '_scope');
                        END IF;
                        EXECUTE format('CREATE INDEX IF NOT EXISTS ix_%s_tenant_scope ON %I (tenant_key, scope_key)', table_name, table_name);
                    END IF;
                END LOOP;
            END $$;
            """),

        new Migration("032_tenant_idempotency_execution_metadata", """
            ALTER TABLE tenant_idempotency_keys
                ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'pending',
                ADD COLUMN IF NOT EXISTS game_id TEXT,
                ADD COLUMN IF NOT EXISTS aggregate_id TEXT,
                ADD COLUMN IF NOT EXISTS result_type TEXT,
                ADD COLUMN IF NOT EXISTS entropy_json JSONB,
                ADD COLUMN IF NOT EXISTS error TEXT;
            CREATE INDEX IF NOT EXISTS ix_tenant_idempotency_status
                ON tenant_idempotency_keys (tenant_key, scope_key, status, created_at);
            """),

        new Migration("033_tenant_wallets", """
            CREATE TABLE IF NOT EXISTS tenant_wallets (
                tenant_key   BIGINT NOT NULL,
                scope_key    BIGINT NOT NULL,
                player_id    TEXT NOT NULL,
                display_name TEXT NOT NULL DEFAULT '',
                coins        BIGINT NOT NULL DEFAULT 0,
                version      BIGINT NOT NULL DEFAULT 0,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (tenant_key, scope_key, player_id),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes(tenant_key, scope_key),
                CHECK (length(player_id) BETWEEN 1 AND 256),
                CHECK (coins >= 0),
                CHECK (version >= 0)
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_wallets_scope_updated
                ON tenant_wallets (tenant_key, scope_key, updated_at DESC);

            CREATE TABLE IF NOT EXISTS tenant_wallet_ledger (
                id            BIGSERIAL PRIMARY KEY,
                tenant_key    BIGINT NOT NULL,
                scope_key     BIGINT NOT NULL,
                player_id     TEXT NOT NULL,
                delta         BIGINT NOT NULL,
                balance_after BIGINT NOT NULL,
                reason        TEXT NOT NULL,
                operation_id  TEXT,
                created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes(tenant_key, scope_key)
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_wallet_ledger_scope_player
                ON tenant_wallet_ledger (tenant_key, scope_key, player_id, id DESC);
            CREATE INDEX IF NOT EXISTS ix_tenant_wallet_ledger_operation
                ON tenant_wallet_ledger (tenant_key, scope_key, operation_id)
                WHERE operation_id IS NOT NULL;
            """),

        new Migration("034_tenant_execution_records", """
            -- Canonical execution records for SDK modules. Legacy game tables
            -- remain available for the staged demo-game migration.
            CREATE TABLE IF NOT EXISTS tenant_aggregate_states (
                tenant_key   BIGINT NOT NULL,
                scope_key    BIGINT NOT NULL,
                game_id      TEXT NOT NULL,
                aggregate_id TEXT NOT NULL,
                state_type   TEXT NOT NULL,
                version      BIGINT NOT NULL,
                state        JSONB NOT NULL,
                updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (tenant_key, scope_key, game_id, aggregate_id),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key),
                CHECK (length(game_id) BETWEEN 1 AND 100),
                CHECK (length(aggregate_id) BETWEEN 1 AND 256),
                CHECK (version >= 0)
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_aggregate_states_updated
                ON tenant_aggregate_states (tenant_key, scope_key, updated_at DESC);

            CREATE TABLE IF NOT EXISTS tenant_event_outbox (
                id             BIGSERIAL PRIMARY KEY,
                tenant_key     BIGINT NOT NULL,
                scope_key      BIGINT NOT NULL,
                command_id     TEXT NOT NULL,
                event_index    INTEGER NOT NULL,
                event_type     TEXT NOT NULL,
                type_name      TEXT NOT NULL,
                payload        JSONB NOT NULL,
                occurred_at    TIMESTAMPTZ NOT NULL,
                request_id     TEXT NOT NULL DEFAULT '',
                correlation_id  TEXT NOT NULL DEFAULT '',
                channel        TEXT NOT NULL DEFAULT '',
                player_id      TEXT,
                status         TEXT NOT NULL DEFAULT 'pending',
                attempts       INTEGER NOT NULL DEFAULT 0,
                next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                locked_until   TIMESTAMPTZ,
                sent_at        TIMESTAMPTZ,
                last_error     TEXT,
                created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
                UNIQUE (tenant_key, scope_key, command_id, event_index),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key),
                CHECK (event_index >= 0),
                CHECK (status IN ('pending', 'sending', 'sent'))
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_event_outbox_due
                ON tenant_event_outbox (tenant_key, scope_key, status, next_attempt_at, id);

            CREATE TABLE IF NOT EXISTS tenant_schedule_outbox (
                id             BIGSERIAL PRIMARY KEY,
                tenant_key     BIGINT NOT NULL,
                scope_key      BIGINT NOT NULL,
                command_id     TEXT NOT NULL,
                effect_index   INTEGER NOT NULL,
                game_id        TEXT NOT NULL,
                schedule_id    TEXT NOT NULL,
                effect_kind    TEXT NOT NULL,
                job_key        TEXT,
                due_at         TIMESTAMPTZ,
                data           JSONB NOT NULL DEFAULT '{}',
                request_id     TEXT NOT NULL DEFAULT '',
                correlation_id TEXT NOT NULL DEFAULT '',
                channel        TEXT NOT NULL DEFAULT '',
                player_id      TEXT,
                status         TEXT NOT NULL DEFAULT 'pending',
                attempts       INTEGER NOT NULL DEFAULT 0,
                next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                locked_until   TIMESTAMPTZ,
                sent_at        TIMESTAMPTZ,
                last_error     TEXT,
                created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
                UNIQUE (tenant_key, scope_key, command_id, effect_index),
                FOREIGN KEY (tenant_key, scope_key)
                    REFERENCES tenant_scopes (tenant_key, scope_key),
                CHECK (effect_index >= 0),
                CHECK (effect_kind IN ('schedule', 'cancel'))
            );
            CREATE INDEX IF NOT EXISTS ix_tenant_schedule_outbox_due
                ON tenant_schedule_outbox (tenant_key, scope_key, status, next_attempt_at, id);
            CREATE INDEX IF NOT EXISTS ix_tenant_schedule_outbox_order
                ON tenant_schedule_outbox (tenant_key, scope_key, schedule_id, id, status);
            """),

        new Migration("035_durable_workflow_steps", """
            CREATE TABLE IF NOT EXISTS durable_workflow_steps (
                id             BIGSERIAL PRIMARY KEY,
                workflow_id    TEXT        NOT NULL,
                command_id     TEXT        NOT NULL,
                command_type   TEXT        NOT NULL,
                aggregate_id   TEXT        NULL,
                operation      TEXT        NOT NULL,
                status         TEXT        NOT NULL,
                terminal       BOOLEAN     NOT NULL DEFAULT false,
                causation_id   TEXT        NULL,
                command_json   JSONB       NOT NULL DEFAULT '{}'::jsonb,
                payload        JSONB       NOT NULL DEFAULT '{}'::jsonb,
                result         JSONB       NULL,
                error          TEXT        NULL,
                occurred_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                CONSTRAINT ck_durable_workflow_steps_status
                    CHECK (status IN ('accepted', 'completed', 'rejected', 'failed'))
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_durable_workflow_steps_command
                ON durable_workflow_steps (command_id);

            CREATE INDEX IF NOT EXISTS ix_durable_workflow_steps_workflow
                ON durable_workflow_steps (workflow_id, occurred_at ASC, id ASC);
            """),

        new Migration("036_multi_game_mini_game_sessions", """
            DO $$
            BEGIN
                IF to_regclass('public.mini_game_sessions') IS NULL THEN
                    RETURN;
                END IF;

                ALTER TABLE mini_game_sessions
                    DROP CONSTRAINT IF EXISTS mini_game_sessions_pkey;
                ALTER TABLE mini_game_sessions
                    ADD CONSTRAINT mini_game_sessions_pkey
                    PRIMARY KEY (user_id, chat_id, game_id);
            END $$;

            CREATE INDEX IF NOT EXISTS ix_mini_game_sessions_user_chat
                ON mini_game_sessions (user_id, chat_id, updated_at DESC);
            """),

        new Migration("037_bot_scoped_update_inbox", """
            CREATE TABLE IF NOT EXISTS processed_update_inbox (
                bot_id         TEXT         NOT NULL,
                update_id      BIGINT       NOT NULL,
                status         TEXT         NOT NULL,
                correlation_id TEXT         NOT NULL,
                started_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
                completed_at   TIMESTAMPTZ,
                error          TEXT,
                PRIMARY KEY (bot_id, update_id)
            );
            CREATE INDEX IF NOT EXISTS ix_processed_update_inbox_started
                ON processed_update_inbox (started_at DESC);
            """),

        new Migration("038_wallet_scope_aliases", """
            CREATE SEQUENCE IF NOT EXISTS wallet_scope_aliases_effective_scope_seq
                AS BIGINT
                MINVALUE -9000000000000000000
                MAXVALUE -1
                START WITH -9000000000000000000
                INCREMENT BY 1;

            CREATE TABLE IF NOT EXISTS wallet_scope_aliases (
                tenant_id          TEXT   NOT NULL,
                source_scope_id    BIGINT NOT NULL,
                effective_scope_id BIGINT NOT NULL DEFAULT nextval('wallet_scope_aliases_effective_scope_seq'),
                PRIMARY KEY (tenant_id, source_scope_id),
                UNIQUE (effective_scope_id)
            );
            """),

        new Migration("039_wallet_operation_results", """
            CREATE TABLE IF NOT EXISTS wallet_operations (
                operation_id     TEXT        PRIMARY KEY,
                telegram_user_id BIGINT      NOT NULL,
                balance_scope_id BIGINT      NOT NULL,
                status           TEXT        NOT NULL CHECK (status IN ('pending', 'completed', 'rejected')),
                applied          BOOLEAN,
                balance_after    INTEGER,
                created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
                completed_at     TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ix_wallet_operations_user_scope
                ON wallet_operations (telegram_user_id, balance_scope_id, created_at DESC);

            -- WalletAtomicExecutionService historically stored a batch as
            -- operation-id:index entries in economics_ledger. Preserve retry
            -- safety across this migration by materializing its final result.
            INSERT INTO wallet_operations (
                operation_id,
                telegram_user_id,
                balance_scope_id,
                status,
                applied,
                balance_after,
                created_at,
                completed_at)
            SELECT DISTINCT ON (base_operation_id)
                base_operation_id,
                telegram_user_id,
                balance_scope_id,
                'completed',
                true,
                balance_after,
                created_at,
                created_at
            FROM (
                SELECT regexp_replace(operation_id, ':[0-9]+$', '') AS base_operation_id,
                       telegram_user_id,
                       balance_scope_id,
                       balance_after,
                       created_at,
                       id
                FROM economics_ledger
                WHERE operation_id ~ ':[0-9]+$'
            ) AS legacy_operations
            ORDER BY base_operation_id, id DESC
            ON CONFLICT (operation_id) DO NOTHING;
            """),

        new Migration("040_wallet_hot_path_maintenance", """
            -- Player-protection checks aggregate recent debits for one
            -- wallet. Keep the lookup ordered by time and cover the columns
            -- needed by the aggregate/filter so normal wallet mutations do
            -- not scan that user's complete ledger history.
            CREATE INDEX IF NOT EXISTS ix_economics_ledger_protection_lookup
                ON economics_ledger (telegram_user_id, balance_scope_id, created_at DESC)
                INCLUDE (delta, reason)
                WHERE delta < 0;

            -- These tables have materially different write patterns. Per-table
            -- settings keep dead tuples and planner statistics bounded without
            -- changing cluster-wide PostgreSQL policy.
            ALTER TABLE users SET (
                autovacuum_vacuum_scale_factor = 0.02,
                autovacuum_vacuum_threshold = 50,
                autovacuum_analyze_scale_factor = 0.02,
                autovacuum_analyze_threshold = 50
            );

            ALTER TABLE wallet_operations SET (
                autovacuum_vacuum_scale_factor = 0.02,
                autovacuum_vacuum_threshold = 100,
                autovacuum_analyze_scale_factor = 0.02,
                autovacuum_analyze_threshold = 100
            );

            ALTER TABLE economics_ledger SET (
                autovacuum_vacuum_scale_factor = 0.05,
                autovacuum_vacuum_threshold = 1000,
                autovacuum_analyze_scale_factor = 0.01,
                autovacuum_analyze_threshold = 500
            );
            """),

        new Migration("041_game_sessions", """
            CREATE TABLE IF NOT EXISTS game_sessions (
                session_id           TEXT        PRIMARY KEY,
                game_id              TEXT        NOT NULL,
                owner_id             TEXT        NOT NULL,
                scope_id             TEXT        NOT NULL,
                root_correlation_id  TEXT        NOT NULL,
                last_correlation_id  TEXT        NOT NULL,
                lifecycle            TEXT        NOT NULL,
                revision             BIGINT      NOT NULL,
                data                 JSONB       NOT NULL DEFAULT '{}'::jsonb,
                failure_code         TEXT,
                started_at           TIMESTAMPTZ NOT NULL,
                updated_at           TIMESTAMPTZ NOT NULL,
                expires_at           TIMESTAMPTZ,
                CONSTRAINT ck_game_sessions_lifecycle
                    CHECK (lifecycle IN ('Started', 'Suspended', 'Resumed', 'Completed', 'Failed')),
                CONSTRAINT ck_game_sessions_revision CHECK (revision >= 0)
            );

            CREATE INDEX IF NOT EXISTS ix_game_sessions_active_scope
                ON game_sessions (game_id, owner_id, scope_id, updated_at DESC)
                WHERE lifecycle IN ('Started', 'Suspended', 'Resumed');
            CREATE INDEX IF NOT EXISTS ix_game_sessions_expiry
                ON game_sessions (expires_at)
                WHERE expires_at IS NOT NULL
                  AND lifecycle IN ('Started', 'Suspended', 'Resumed');

            CREATE TABLE IF NOT EXISTS game_session_lifecycle (
                session_id      TEXT        NOT NULL REFERENCES game_sessions (session_id),
                correlation_id  TEXT        NOT NULL UNIQUE,
                lifecycle       TEXT        NOT NULL,
                revision        BIGINT      NOT NULL,
                failure_code    TEXT,
                snapshot        JSONB       NOT NULL,
                occurred_at     TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (session_id, revision),
                CONSTRAINT ck_game_session_lifecycle_state
                    CHECK (lifecycle IN ('Started', 'Suspended', 'Resumed', 'Completed', 'Failed'))
            );
            CREATE INDEX IF NOT EXISTS ix_game_session_lifecycle_session_time
                ON game_session_lifecycle (session_id, occurred_at DESC);
            """),

        new Migration("042_game_interactions_and_effect_outbox", """
            CREATE TABLE IF NOT EXISTS game_input_requests (
                request_id              TEXT        PRIMARY KEY,
                game_id                 TEXT        NOT NULL,
                aggregate_id            TEXT        NOT NULL,
                expected_player_id      TEXT        NOT NULL,
                scope_id                TEXT        NOT NULL,
                route                   TEXT        NOT NULL,
                payload                 JSONB       NOT NULL DEFAULT '{}'::jsonb,
                allowed_values          JSONB       NOT NULL DEFAULT '[]'::jsonb,
                session_id              TEXT,
                expires_at              TIMESTAMPTZ NOT NULL,
                status                  TEXT        NOT NULL DEFAULT 'pending',
                created_command_id      TEXT,
                consumed_correlation_id TEXT,
                consumed_value          TEXT,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                consumed_at             TIMESTAMPTZ,
                CONSTRAINT ck_game_input_requests_status
                    CHECK (status IN ('pending', 'consumed', 'expired', 'cancelled')),
                CONSTRAINT ck_game_input_requests_allowed_values
                    CHECK (jsonb_typeof(allowed_values) = 'array')
            );
            CREATE INDEX IF NOT EXISTS ix_game_input_requests_active_player
                ON game_input_requests (scope_id, expected_player_id, expires_at, request_id)
                WHERE status = 'pending';
            CREATE INDEX IF NOT EXISTS ix_game_input_requests_active_game
                ON game_input_requests (game_id, aggregate_id, expires_at, request_id)
                WHERE status = 'pending';

            CREATE TABLE IF NOT EXISTS game_effect_outbox (
                id                   BIGSERIAL   PRIMARY KEY,
                command_id           TEXT        NOT NULL,
                effect_index         INTEGER     NOT NULL,
                game_id              TEXT        NOT NULL,
                aggregate_id         TEXT        NOT NULL,
                type_name            TEXT        NOT NULL,
                payload              JSONB       NOT NULL,
                operation_id         TEXT,
                tenant_id            TEXT,
                scope_id             TEXT,
                player_id            TEXT,
                request_id           TEXT,
                correlation_id       TEXT,
                channel              TEXT,
                channel_container_id TEXT,
                channel_topic_id     TEXT,
                status               TEXT        NOT NULL DEFAULT 'pending',
                attempts             INTEGER     NOT NULL DEFAULT 0,
                next_attempt_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
                locked_until         TIMESTAMPTZ,
                last_error           TEXT,
                created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
                sent_at              TIMESTAMPTZ,
                UNIQUE (command_id, effect_index),
                CONSTRAINT ck_game_effect_outbox_status
                    CHECK (status IN ('pending', 'sending', 'sent')),
                CONSTRAINT ck_game_effect_outbox_effect_index CHECK (effect_index >= 0)
            );
            CREATE INDEX IF NOT EXISTS ix_game_effect_outbox_due
                ON game_effect_outbox (status, next_attempt_at, id)
                WHERE status IN ('pending', 'sending');
            CREATE INDEX IF NOT EXISTS ix_game_effect_outbox_command_order
                ON game_effect_outbox (command_id, effect_index, status);
            """),

        new Migration("043_game_execution_history", """
            CREATE TABLE IF NOT EXISTS game_execution_history (
                id               BIGSERIAL       PRIMARY KEY,
                command_id       TEXT            NOT NULL UNIQUE,
                game_id          TEXT            NOT NULL,
                aggregate_id     TEXT            NOT NULL,
                command_type     TEXT            NOT NULL,
                command_payload  JSONB           NOT NULL,
                state_type       TEXT            NOT NULL,
                previous_state   JSONB           NOT NULL,
                next_state       JSONB           NOT NULL,
                result_type      TEXT            NOT NULL,
                result_payload   JSONB           NOT NULL,
                decision_status  TEXT            NOT NULL,
                rejection_reason TEXT,
                entropy          JSONB           NOT NULL DEFAULT '{}'::jsonb,
                effects          JSONB           NOT NULL DEFAULT '[]'::jsonb,
                occurred_at      TIMESTAMPTZ     NOT NULL,
                tenant_key       BIGINT,
                scope_key        BIGINT,
                player_id        TEXT,
                CONSTRAINT ck_game_execution_history_status
                    CHECK (decision_status IN ('accepted', 'rejected')),
                CONSTRAINT ck_game_execution_history_entropy
                    CHECK (jsonb_typeof(entropy) = 'object'),
                CONSTRAINT ck_game_execution_history_effects
                    CHECK (jsonb_typeof(effects) = 'array')
            );
            CREATE INDEX IF NOT EXISTS ix_game_execution_history_aggregate
                ON game_execution_history (game_id, aggregate_id, id DESC);
            CREATE INDEX IF NOT EXISTS ix_game_execution_history_occurred
                ON game_execution_history (occurred_at DESC);
            """),

        IntegrationInboxMigrationDefinition.Create(),
        IntegrationOutboxMigrationDefinition.Create(),
        IntegrationQuarantineMigrationDefinition.Create(),
        DurableWorkflowTimeoutMigrationDefinition.Create(),

    ];

    /// <summary>
    /// Framework schema for a Backend instance in the microservices profile.
    /// Wallet-owned tables are omitted. Historical backfills which selected
    /// from users are replaced with empty-schema migrations.
    /// </summary>
    public IReadOnlyList<Migration> MicroservicesBackendMigrations =>
        Migrations
            .Where(migration => !WalletOwnedIds.Contains(migration.Id))
            .Select(migration => migration.Id switch
            {
                "007_known_chats" => new Migration("007_known_chats", """
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
                    """)
                {
                    LegacyContentHashes = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "0D2AC35A42CDA4142BAD9B74195D55F2C726D08F7BA7CD32F0FBC383DB9895A7"
                    }
                },
                "012_telegram_dice_daily_per_game" => new Migration("012_telegram_dice_daily_per_game", """
                    CREATE TABLE IF NOT EXISTS telegram_dice_daily_rolls (
                        telegram_user_id    BIGINT       NOT NULL,
                        balance_scope_id    BIGINT       NOT NULL,
                        game_id             TEXT         NOT NULL,
                        rolls_on            DATE,
                        roll_count          INTEGER      NOT NULL DEFAULT 0,
                        updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                        PRIMARY KEY (telegram_user_id, balance_scope_id, game_id)
                    );
                    CREATE INDEX IF NOT EXISTS ix_telegram_dice_daily_rolls_scope_game
                        ON telegram_dice_daily_rolls (balance_scope_id, game_id, rolls_on);
                    """),
                _ => migration,
            })
            .Append(new Migration("027_operations_report_checkpoint", """
                CREATE TABLE IF NOT EXISTS operations_report_checkpoint (
                    report_key       TEXT         PRIMARY KEY,
                    period_key       TEXT         NOT NULL,
                    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now()
                );
                """))
            .ToArray();

    /// <summary>
    /// Framework schema for the Wallet instance. The former combined 017
    /// migration is replaced by a wallet-only forward migration.
    /// </summary>
    public IReadOnlyList<Migration> WalletMigrations =>
        Migrations
            .Where(migration => (WalletOwnedIds.Contains(migration.Id)
                || string.Equals(migration.Id, "010_runtime_tuning", StringComparison.Ordinal))
                && !string.Equals(migration.Id, "009_users_telegram_dice_daily", StringComparison.Ordinal)
                && !string.Equals(migration.Id, "017_responsible_gaming_and_ops_reports", StringComparison.Ordinal))
            .Append(new Migration("027_wallet_player_protection", """
                CREATE TABLE IF NOT EXISTS player_protection (
                    telegram_user_id    BIGINT       PRIMARY KEY,
                    daily_stake_limit   INTEGER,
                    cooldown_until      TIMESTAMPTZ,
                    self_excluded_until TIMESTAMPTZ,
                    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
                    CHECK (daily_stake_limit IS NULL OR daily_stake_limit >= 0)
                );
                CREATE INDEX IF NOT EXISTS ix_player_protection_active
                    ON player_protection (cooldown_until, self_excluded_until);
                """))
            .Append(Migrations.Single(migration => string.Equals(migration.Id, "028_tenant_registry", StringComparison.Ordinal)))
            .Append(Migrations.Single(migration => string.Equals(migration.Id, "029_tenant_owned_coordination", StringComparison.Ordinal)))
            .ToArray();
}
