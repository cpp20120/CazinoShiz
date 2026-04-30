using BotFramework.Sdk;

namespace Games.Challenges;

public sealed class ChallengeMigrations : IModuleMigrations
{
    public string ModuleId => "challenges";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE challenge_duels (
                id               UUID         PRIMARY KEY,
                chat_id          BIGINT       NOT NULL,
                challenger_id    BIGINT       NOT NULL,
                challenger_name  TEXT         NOT NULL,
                target_id        BIGINT       NOT NULL,
                target_name      TEXT         NOT NULL,
                amount           INTEGER      NOT NULL,
                game             TEXT         NOT NULL,
                status           TEXT         NOT NULL,
                created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
                expires_at       TIMESTAMPTZ  NOT NULL,
                responded_at     TIMESTAMPTZ,
                completed_at     TIMESTAMPTZ
            );
            CREATE INDEX ix_challenge_duels_chat_status_created
                ON challenge_duels (chat_id, status, created_at DESC);
            CREATE INDEX ix_challenge_duels_target_status
                ON challenge_duels (target_id, status, expires_at);
            """),
    ];
}
