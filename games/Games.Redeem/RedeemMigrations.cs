using BotFramework.Sdk;

namespace Games.Redeem;

public sealed class RedeemMigrations : IModuleMigrations
{
    public string ModuleId => "redeem";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE redeem_codes (
                code          UUID        PRIMARY KEY,
                active        BOOLEAN     NOT NULL DEFAULT true,
                issued_by     BIGINT      NOT NULL,
                issued_at     BIGINT      NOT NULL,
                redeemed_by   BIGINT      NULL,
                redeemed_at   BIGINT      NULL
            );
            CREATE INDEX ix_redeem_codes_active ON redeem_codes (active);
            """),
        new Migration("002_free_spin_game_id", """
            ALTER TABLE redeem_codes
                ADD COLUMN free_spin_game_id TEXT NOT NULL DEFAULT 'dice';
            """),
    ];
}
