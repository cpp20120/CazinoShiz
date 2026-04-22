using BotFramework.Sdk;

namespace Games.Football;

public sealed class FootballMigrations : IModuleMigrations
{
    public string ModuleId => "football";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE football_bets (
                user_id     BIGINT      NOT NULL,
                chat_id     BIGINT      NOT NULL,
                amount      INTEGER     NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, chat_id)
            );
            """),
    ];
}
