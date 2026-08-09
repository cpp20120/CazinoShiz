using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Host.Composition.Migrations;

internal static class MultiPartyWagerMigrationDefinition
{
    public static Migration Create() => new("045_multiparty_wagers", """
        CREATE TABLE IF NOT EXISTS wager_groups (
            workflow_id TEXT PRIMARY KEY,
            game_id TEXT NOT NULL,
            game_input JSONB NOT NULL,
            tenant_id TEXT NOT NULL,
            scope_id TEXT NOT NULL,
            status TEXT NOT NULL,
            outcome_json JSONB,
            error_code TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            CHECK (status IN ('reserving', 'playing', 'settling', 'compensating',
                              'completed', 'compensated', 'failed'))
        );

        CREATE TABLE IF NOT EXISTS wager_group_participants (
            workflow_id TEXT NOT NULL REFERENCES wager_groups(workflow_id) ON DELETE CASCADE,
            operation_id TEXT NOT NULL UNIQUE,
            bet_id TEXT NOT NULL UNIQUE,
            player_id TEXT NOT NULL,
            amount BIGINT NOT NULL,
            currency TEXT NOT NULL,
            rules_version TEXT NOT NULL,
            settlement_rule TEXT NOT NULL,
            status TEXT NOT NULL,
            outcome_code TEXT,
            payout BIGINT,
            error_code TEXT,
            PRIMARY KEY (workflow_id, bet_id),
            CHECK (amount > 0),
            CHECK (status IN ('pending', 'reserved', 'rejected', 'refunding',
                              'refunded', 'settling', 'settled', 'failed'))
        );
        CREATE INDEX IF NOT EXISTS ix_wager_group_participants_workflow
            ON wager_group_participants (workflow_id, status);

        ALTER TABLE wager_reservations DROP CONSTRAINT IF EXISTS wager_reservations_status_check;
        ALTER TABLE wager_reservations
            ADD CONSTRAINT wager_reservations_status_check
            CHECK (status IN ('processing', 'reserved', 'rejected', 'settled', 'refunded'));
        """);
}
