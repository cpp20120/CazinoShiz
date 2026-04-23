using BotFramework.Sdk;

namespace Games.Darts;

public sealed class DartsMigrations : IModuleMigrations
{
    public string ModuleId => "darts";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE darts_bets (
                user_id     BIGINT      NOT NULL,
                chat_id     BIGINT      NOT NULL,
                amount      INTEGER     NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, chat_id)
            );
            """),
        new Migration("002_rounds_queue", """
            CREATE TABLE darts_rounds (
                id                  BIGSERIAL PRIMARY KEY,
                user_id             BIGINT      NOT NULL,
                chat_id             BIGINT      NOT NULL,
                amount              INTEGER     NOT NULL,
                created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
                status              SMALLINT    NOT NULL DEFAULT 0,
                bot_message_id      INTEGER     NULL,
                reply_to_message_id INTEGER     NOT NULL DEFAULT 0
            );
            CREATE INDEX ix_darts_rounds_chat_status_id ON darts_rounds (chat_id, status, id);
            INSERT INTO darts_rounds (user_id, chat_id, amount, created_at, status, reply_to_message_id)
            SELECT user_id, chat_id, amount, created_at, 0, 0 FROM darts_bets;
            DROP TABLE darts_bets;
            """),
    ];
}
