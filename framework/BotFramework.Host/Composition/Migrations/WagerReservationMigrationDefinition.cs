using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Host.Composition.Migrations;

internal static class WagerReservationMigrationDefinition
{
    public static Migration Create() => new("044_wager_reservations", """
        CREATE TABLE IF NOT EXISTS wager_reservations (
            operation_id TEXT PRIMARY KEY,
            bet_id      TEXT NOT NULL UNIQUE,
            player_id   TEXT NOT NULL,
            amount      BIGINT NOT NULL,
            currency    TEXT NOT NULL,
            status      TEXT NOT NULL,
            created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
            settled_at  TIMESTAMPTZ,
            CHECK (amount > 0),
            CHECK (status IN ('processing', 'reserved', 'rejected', 'settled'))
        );
        CREATE INDEX IF NOT EXISTS ix_wager_reservations_player_status
            ON wager_reservations (player_id, status, created_at DESC);
        """);
}
