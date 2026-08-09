using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Host.Composition.Migrations;

internal static class WagerOperationMigrationDefinition
{
    public static Migration Create() => new("043_wager_operations", """
        CREATE TABLE IF NOT EXISTS wager_operations (
            operation_id TEXT PRIMARY KEY, bet_id TEXT NOT NULL UNIQUE, game_id TEXT NOT NULL,
            player_id TEXT NOT NULL, game_input JSONB NOT NULL, terms JSONB NOT NULL,
            status INTEGER NOT NULL, outcome_code TEXT, error_code TEXT,
            created_at TIMESTAMPTZ NOT NULL, updated_at TIMESTAMPTZ NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_wager_operations_player_updated
            ON wager_operations (player_id, updated_at DESC);
        """);
}
