using BotFramework.Sdk;

namespace Games.Horse;

public sealed class HorseMigrations : IModuleMigrations
{
    public string ModuleId => "horse";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE horse_bets (
                id          UUID        PRIMARY KEY,
                race_date   TEXT        NOT NULL,
                user_id     BIGINT      NOT NULL,
                horse_id    INTEGER     NOT NULL,
                amount      INTEGER     NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_horse_bets_race_date ON horse_bets (race_date);

            CREATE TABLE horse_results (
                race_date   TEXT        PRIMARY KEY,
                winner      INTEGER     NOT NULL,
                image_data  BYTEA       NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """),

        new Migration("002_result_file_id", """
            ALTER TABLE horse_results ADD COLUMN IF NOT EXISTS file_id TEXT NULL;
            ALTER TABLE horse_results DROP COLUMN IF EXISTS image_data;
            """),

        new Migration("003_bet_balance_scope", """
            ALTER TABLE horse_bets ADD COLUMN IF NOT EXISTS balance_scope_id BIGINT NOT NULL DEFAULT 0;
            UPDATE horse_bets SET balance_scope_id = user_id WHERE balance_scope_id = 0;
            """),

        new Migration("004_horse_results_per_scope", """
            ALTER TABLE horse_results ADD COLUMN IF NOT EXISTS balance_scope_id BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE horse_results DROP CONSTRAINT IF EXISTS horse_results_pkey;
            ALTER TABLE horse_results ADD PRIMARY KEY (race_date, balance_scope_id);
            """),
    ];
}
