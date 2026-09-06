using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Host.Composition.Migrations;

/// <summary>
/// Framework-owned migration for the last legacy storage boundary.
///
/// The game modules keep their old numeric columns and SQL. The framework
/// stamps the ambient tenant into shadow columns at the database boundary and
/// PostgreSQL RLS makes those columns authoritative for bound requests.
/// </summary>
internal static class FrameworkTenantBoundaryMigrations
{
    public static IReadOnlyList<Migration> PostModuleMigrations { get; } =
    [
        new Migration("039_legacy_game_tenant_boundary", """
            -- This migration runs after module schemas exist. It is kept in
            -- the framework migration stream even though it touches module
            -- tables: modules must not own or understand tenant columns.
            CREATE OR REPLACE FUNCTION casinoshiz_current_tenant_key()
            RETURNS BIGINT
            LANGUAGE sql
            STABLE
            SECURITY DEFINER
            SET search_path = public, pg_temp
            AS $function$
                SELECT t.tenant_key
                FROM tenants t
                WHERE t.tenant_id = NULLIF(current_setting('casinoshiz.tenant_id', true), '')
                LIMIT 1
            $function$;

            CREATE OR REPLACE FUNCTION casinoshiz_current_scope_key()
            RETURNS BIGINT
            LANGUAGE sql
            STABLE
            SECURITY DEFINER
            SET search_path = public, pg_temp
            AS $function$
                SELECT s.scope_key
                FROM tenant_scopes s
                WHERE s.tenant_key = casinoshiz_current_tenant_key()
                  AND s.scope_id = NULLIF(current_setting('casinoshiz.scope_id', true), '')
                LIMIT 1
            $function$;

            CREATE OR REPLACE FUNCTION casinoshiz_stamp_tenant_row()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                current_tenant BIGINT;
                current_scope BIGINT;
                current_player TEXT;
            BEGIN
                IF current_setting('casinoshiz.tenant_bound', true) = 'true' THEN
                    current_tenant := casinoshiz_current_tenant_key();
                    current_scope := casinoshiz_current_scope_key();
                    IF current_tenant IS NULL OR current_scope IS NULL THEN
                        RAISE EXCEPTION
                            'Tenant context is not provisioned: tenant_id=%, scope_id=%',
                            current_setting('casinoshiz.tenant_id', true),
                            current_setting('casinoshiz.scope_id', true)
                            USING ERRCODE = '23514';
                    END IF;

                    NEW.tenant_key := current_tenant;
                    NEW.scope_key := current_scope;
                    current_player := NULLIF(current_setting('casinoshiz.player_id', true), '');
                    IF NEW.player_id IS NULL THEN
                        NEW.player_id := current_player;
                    END IF;
                END IF;
                RETURN NEW;
            END
            $function$;

            DO $migration$
            DECLARE
                current_table TEXT;
                has_chat_id BOOLEAN;
                has_balance_scope_id BOOLEAN;
                has_user_id BOOLEAN;
                has_telegram_user_id BOOLEAN;
                scope_expression TEXT;
                legacy_tenant BIGINT;
                legacy_main_scope BIGINT;
                legacy_tables TEXT[] := ARRAY[
                    'basketball_bets',
                    'blackjack_hands',
                    'bowling_bets',
                    'challenge_duels',
                    'darts_rounds',
                    'dice_rolls',
                    'dicecube_bets',
                    'football_bets',
                    'horse_bets',
                    'horse_results',
                    'meta_clan_members',
                    'meta_clans',
                    'meta_event_log',
                    'meta_player_achievements',
                    'meta_player_game_streaks',
                    'meta_player_quests',
                    'meta_risk_flags',
                    'meta_season_clans',
                    'meta_season_players',
                    'meta_seasons',
                    'meta_tournament_matches',
                    'meta_tournament_players',
                    'meta_tournaments',
                    'pick_chains',
                    'pick_daily_lottery',
                    'pick_daily_lottery_tickets',
                    'pick_lottery',
                    'pick_lottery_entries',
                    'pick_streaks',
                    'pixelbattle_tiles',
                    'poker_seats',
                    'poker_tables',
                    'redeem_codes',
                    'secret_hitler_games',
                    'secret_hitler_players',
                    'admin_audit',
                    'economics_ledger',
                    'event_dispatch_failures',
                    'event_log',
                    'fairness_audit',
                    'game_aggregate_states',
                    'game_availability_overrides',
                    'game_command_idempotency',
                    'game_execution_history',
                    'game_event_outbox',
                    'game_schedule_outbox',
                    'known_chats',
                    'mini_game_roll_gates',
                    'mini_game_sessions',
                    'module_events',
                    'module_snapshots',
                    'player_protection',
                    'telegram_dice_daily_rolls',
                    'telegram_outbox',
                    'discord_outbox'
                ];
            BEGIN
                SELECT tenant_key
                INTO legacy_tenant
                FROM tenants
                WHERE tenant_id = 'legacy:default';

                SELECT scope_key
                INTO legacy_main_scope
                FROM tenant_scopes
                WHERE tenant_key = legacy_tenant AND scope_id = 'main';

                IF legacy_tenant IS NULL OR legacy_main_scope IS NULL THEN
                    RAISE EXCEPTION 'Framework tenant registry is incomplete';
                END IF;

                FOREACH current_table IN ARRAY legacy_tables LOOP
                    IF to_regclass('public.' || current_table) IS NULL THEN
                        CONTINUE;
                    END IF;

                    EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS tenant_key BIGINT', current_table);
                    EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS scope_key BIGINT', current_table);
                    EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS player_id TEXT', current_table);

                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND information_schema.columns.table_name = current_table
                          AND column_name = 'chat_id'
                    )
                    INTO has_chat_id;

                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND information_schema.columns.table_name = current_table
                          AND column_name = 'balance_scope_id'
                    )
                    INTO has_balance_scope_id;

                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND information_schema.columns.table_name = current_table
                          AND column_name = 'user_id'
                    )
                    INTO has_user_id;

                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND information_schema.columns.table_name = current_table
                          AND column_name = 'telegram_user_id'
                    )
                    INTO has_telegram_user_id;

                    IF has_chat_id THEN
                        scope_expression :=
                            'CASE WHEN c.chat_id IS NULL THEN ''main'' ELSE ''legacy:'' || c.chat_id::text END';
                        EXECUTE format($sql$
                            INSERT INTO tenant_scopes (tenant_key, scope_id, is_main)
                            SELECT %s, 'legacy:' || c.chat_id::text, false
                            FROM %I c
                            WHERE c.chat_id IS NOT NULL
                            ON CONFLICT (tenant_key, scope_id) DO NOTHING
                        $sql$, legacy_tenant, current_table);
                    ELSIF has_balance_scope_id THEN
                        scope_expression :=
                            'CASE WHEN c.balance_scope_id IS NULL OR c.balance_scope_id = 0 THEN ''main'' ELSE ''legacy:'' || c.balance_scope_id::text END';
                        EXECUTE format($sql$
                            INSERT INTO tenant_scopes (tenant_key, scope_id, is_main)
                            SELECT %s, 'legacy:' || c.balance_scope_id::text, false
                            FROM %I c
                            WHERE c.balance_scope_id IS NOT NULL AND c.balance_scope_id <> 0
                            ON CONFLICT (tenant_key, scope_id) DO NOTHING
                        $sql$, legacy_tenant, current_table);
                    ELSE
                        scope_expression := '''main''';
                    END IF;

                    EXECUTE format(
                        'UPDATE %I c
                         SET tenant_key = COALESCE(c.tenant_key, %s),
                             scope_key = COALESCE(c.scope_key, s.scope_key)
                         FROM tenant_scopes s
                         WHERE s.tenant_key = %s
                           AND s.scope_id = %s
                           AND (c.tenant_key IS NULL OR c.scope_key IS NULL)',
                        current_table,
                        legacy_tenant,
                        legacy_tenant,
                        scope_expression);

                    IF has_user_id THEN
                        EXECUTE format(
                            'UPDATE %I SET player_id = COALESCE(player_id, user_id::text) WHERE player_id IS NULL',
                            current_table);
                    ELSIF has_telegram_user_id THEN
                        EXECUTE format(
                            'UPDATE %I SET player_id = COALESCE(player_id, telegram_user_id::text) WHERE player_id IS NULL',
                            current_table);
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'fk_tenant_boundary_' || current_table || '_tenant'
                    ) THEN
                        EXECUTE format(
                            'ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY (tenant_key) REFERENCES tenants(tenant_key)',
                            current_table,
                            'fk_tenant_boundary_' || current_table || '_tenant');
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'fk_tenant_boundary_' || current_table || '_scope'
                    ) THEN
                        EXECUTE format(
                            'ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY (tenant_key, scope_key) REFERENCES tenant_scopes(tenant_key, scope_key)',
                            current_table,
                            'fk_tenant_boundary_' || current_table || '_scope');
                    END IF;

                    EXECUTE format(
                        'CREATE INDEX IF NOT EXISTS %I ON %I (tenant_key, scope_key)',
                        'ix_tenant_boundary_' || current_table || '_scope',
                        current_table);

                    EXECUTE format('DROP TRIGGER IF EXISTS casinoshiz_tenant_stamp ON %I', current_table);
                    EXECUTE format(
                        'CREATE TRIGGER casinoshiz_tenant_stamp BEFORE INSERT OR UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION casinoshiz_stamp_tenant_row()',
                        current_table);

                    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', current_table);
                    EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', current_table);
                    EXECUTE format('DROP POLICY IF EXISTS casinoshiz_tenant_boundary ON %I', current_table);
                    EXECUTE format(
                        'CREATE POLICY casinoshiz_tenant_boundary ON %I
                         USING (current_setting(''casinoshiz.tenant_bound'', true) IS DISTINCT FROM ''true''
                                OR (tenant_key = casinoshiz_current_tenant_key()
                                    AND scope_key = casinoshiz_current_scope_key()))
                         WITH CHECK (current_setting(''casinoshiz.tenant_bound'', true) IS DISTINCT FROM ''true''
                                     OR (tenant_key = casinoshiz_current_tenant_key()
                                         AND scope_key = casinoshiz_current_scope_key()))',
                        current_table);
                END LOOP;

                -- Child records without their own chat/scope inherit the
                -- tenant boundary from their parent aggregate.
                IF to_regclass('public.pick_lottery_entries') IS NOT NULL
                   AND to_regclass('public.pick_lottery') IS NOT NULL THEN
                    UPDATE pick_lottery_entries e
                    SET tenant_key = p.tenant_key, scope_key = p.scope_key
                    FROM pick_lottery p
                    WHERE p.id = e.lottery_id
                      AND (e.tenant_key IS NULL OR e.scope_key IS NULL);
                END IF;

                IF to_regclass('public.pick_daily_lottery_tickets') IS NOT NULL
                   AND to_regclass('public.pick_daily_lottery') IS NOT NULL THEN
                    UPDATE pick_daily_lottery_tickets e
                    SET tenant_key = p.tenant_key, scope_key = p.scope_key
                    FROM pick_daily_lottery p
                    WHERE p.id = e.lottery_id
                      AND (e.tenant_key IS NULL OR e.scope_key IS NULL);
                END IF;

                IF to_regclass('public.meta_tournament_players') IS NOT NULL
                   AND to_regclass('public.meta_tournaments') IS NOT NULL THEN
                    UPDATE meta_tournament_players p
                    SET tenant_key = t.tenant_key, scope_key = t.scope_key
                    FROM meta_tournaments t
                    WHERE t.id = p.tournament_id
                      AND (p.tenant_key IS NULL OR p.scope_key IS NULL);
                END IF;

                IF to_regclass('public.meta_tournament_matches') IS NOT NULL
                   AND to_regclass('public.meta_tournaments') IS NOT NULL THEN
                    UPDATE meta_tournament_matches m
                    SET tenant_key = t.tenant_key, scope_key = t.scope_key
                    FROM meta_tournaments t
                    WHERE t.id = m.tournament_id
                      AND (m.tenant_key IS NULL OR m.scope_key IS NULL);
                END IF;
            END
            $migration$;
            """),

        new Migration("040_meta_active_season_tenant_scope", """
            -- meta_seasons became tenant-bound in migration 039. Its original
            -- partial index still allowed only one active season globally,
            -- which prevented a second tenant from creating its own season.
            DROP INDEX IF EXISTS ux_meta_seasons_active;

            CREATE UNIQUE INDEX ux_meta_seasons_active
                ON meta_seasons (tenant_key, scope_key)
                WHERE status = 'active';
            """),

        new Migration("044_game_execution_history_tenant_boundary", """
            -- 043 can be applied to installations which already completed the
            -- legacy tenant-boundary migration. Give the new history journal
            -- the same tenant stamping and RLS policy in that upgrade path.
            ALTER TABLE game_execution_history ADD COLUMN IF NOT EXISTS tenant_key BIGINT;
            ALTER TABLE game_execution_history ADD COLUMN IF NOT EXISTS scope_key BIGINT;
            ALTER TABLE game_execution_history ADD COLUMN IF NOT EXISTS player_id TEXT;

            DO $migration$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_tenant_boundary_game_execution_history_tenant'
                ) THEN
                    ALTER TABLE game_execution_history
                    ADD CONSTRAINT fk_tenant_boundary_game_execution_history_tenant
                    FOREIGN KEY (tenant_key) REFERENCES tenants(tenant_key);
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_tenant_boundary_game_execution_history_scope'
                ) THEN
                    ALTER TABLE game_execution_history
                    ADD CONSTRAINT fk_tenant_boundary_game_execution_history_scope
                    FOREIGN KEY (tenant_key, scope_key) REFERENCES tenant_scopes(tenant_key, scope_key);
                END IF;
            END
            $migration$;

            CREATE INDEX IF NOT EXISTS ix_tenant_boundary_game_execution_history_scope
                ON game_execution_history (tenant_key, scope_key);
            DROP TRIGGER IF EXISTS casinoshiz_tenant_stamp ON game_execution_history;
            CREATE TRIGGER casinoshiz_tenant_stamp
                BEFORE INSERT OR UPDATE ON game_execution_history
                FOR EACH ROW EXECUTE FUNCTION casinoshiz_stamp_tenant_row();
            ALTER TABLE game_execution_history ENABLE ROW LEVEL SECURITY;
            ALTER TABLE game_execution_history FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS casinoshiz_tenant_boundary ON game_execution_history;
            CREATE POLICY casinoshiz_tenant_boundary ON game_execution_history
                USING (current_setting('casinoshiz.tenant_bound', true) IS DISTINCT FROM 'true'
                       OR (tenant_key = casinoshiz_current_tenant_key()
                           AND scope_key = casinoshiz_current_scope_key()))
                WITH CHECK (current_setting('casinoshiz.tenant_bound', true) IS DISTINCT FROM 'true'
                            OR (tenant_key = casinoshiz_current_tenant_key()
                                AND scope_key = casinoshiz_current_scope_key()));
            """)
    ];
}
