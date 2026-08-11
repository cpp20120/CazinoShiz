using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Host.Composition.Migrations;

internal static class LedgerOperationsMigrationDefinition
{
    public static Migration Create() => new("046_ledger_operations", """
        CREATE TABLE IF NOT EXISTS ledger_operations (
            tenant_key       BIGINT NOT NULL,
            scope_key        BIGINT NOT NULL,
            operation_id     TEXT NOT NULL,
            operation_kind   TEXT NOT NULL,
            account_id       TEXT,
            target_account_id TEXT,
            reference_id     TEXT,
            amount           BIGINT NOT NULL,
            applied_amount   BIGINT NOT NULL DEFAULT 0,
            remaining_amount BIGINT,
            currency         TEXT NOT NULL,
            reason           TEXT NOT NULL,
            actor_id         TEXT,
            status           TEXT NOT NULL,
            error_code       TEXT,
            created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
            updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
            PRIMARY KEY (tenant_key, scope_key, operation_id),
            CHECK (amount >= 0),
            CHECK (applied_amount >= 0),
            CHECK (remaining_amount IS NULL OR remaining_amount >= 0),
            CHECK (status IN ('processing', 'held', 'partially_captured', 'captured',
                              'partially_released', 'released', 'partially_refunded',
                              'refunded', 'completed', 'rejected', 'failed') )
        );
        CREATE INDEX IF NOT EXISTS ix_ledger_operations_reference
            ON ledger_operations (tenant_key, scope_key, reference_id);
        CREATE INDEX IF NOT EXISTS ix_ledger_operations_status
            ON ledger_operations (tenant_key, scope_key, status, updated_at);

        CREATE TABLE IF NOT EXISTS ledger_holds (
            tenant_key       BIGINT NOT NULL,
            scope_key        BIGINT NOT NULL,
            hold_id          TEXT NOT NULL,
            operation_id     TEXT NOT NULL,
            account_id       TEXT NOT NULL,
            amount           BIGINT NOT NULL,
            captured_amount  BIGINT NOT NULL DEFAULT 0,
            released_amount  BIGINT NOT NULL DEFAULT 0,
            refunded_amount  BIGINT NOT NULL DEFAULT 0,
            currency         TEXT NOT NULL,
            expires_at       TIMESTAMPTZ NOT NULL,
            status           TEXT NOT NULL,
            error_code       TEXT,
            created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
            updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
            PRIMARY KEY (tenant_key, scope_key, hold_id),
            UNIQUE (tenant_key, scope_key, operation_id),
            CHECK (amount > 0),
            CHECK (captured_amount >= 0 AND released_amount >= 0 AND refunded_amount >= 0),
            CHECK (captured_amount + released_amount <= amount),
            CHECK (refunded_amount <= captured_amount),
            CHECK (status IN ('processing', 'held', 'partially_captured', 'captured',
                              'partially_released', 'released', 'partially_refunded',
                              'refunded', 'rejected', 'failed'))
        );
        CREATE INDEX IF NOT EXISTS ix_ledger_holds_account_status
            ON ledger_holds (tenant_key, scope_key, account_id, status);
        """);
}
