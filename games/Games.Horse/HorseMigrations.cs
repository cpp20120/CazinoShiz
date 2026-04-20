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
    ];
}
