using BotFramework.Sdk;

namespace Games.SecretHitler;

public sealed class SecretHitlerMigrations : IModuleMigrations
{
    public string ModuleId => "sh";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE secret_hitler_games (
                invite_code                       VARCHAR(8)   PRIMARY KEY,
                host_user_id                      BIGINT       NOT NULL,
                chat_id                           BIGINT       NOT NULL,
                status                            INTEGER      NOT NULL,
                phase                             INTEGER      NOT NULL,
                liberal_policies                  INTEGER      NOT NULL DEFAULT 0,
                fascist_policies                  INTEGER      NOT NULL DEFAULT 0,
                election_tracker                  INTEGER      NOT NULL DEFAULT 0,
                current_president_position        INTEGER      NOT NULL DEFAULT 0,
                nominated_chancellor_position     INTEGER      NOT NULL DEFAULT -1,
                last_elected_president_position   INTEGER      NOT NULL DEFAULT -1,
                last_elected_chancellor_position  INTEGER      NOT NULL DEFAULT -1,
                deck_state                        VARCHAR(32)  NOT NULL DEFAULT '',
                discard_state                     VARCHAR(32)  NOT NULL DEFAULT '',
                president_draw                    VARCHAR(8)   NOT NULL DEFAULT '',
                chancellor_received               VARCHAR(8)   NOT NULL DEFAULT '',
                winner                            INTEGER      NOT NULL DEFAULT 0,
                win_reason                        INTEGER      NOT NULL DEFAULT 0,
                buy_in                            INTEGER      NOT NULL DEFAULT 0,
                pot                               INTEGER      NOT NULL DEFAULT 0,
                created_at                        BIGINT       NOT NULL,
                last_action_at                    BIGINT       NOT NULL
            );
            CREATE INDEX ix_sh_games_status_action ON secret_hitler_games (status, last_action_at);

            CREATE TABLE secret_hitler_players (
                invite_code       VARCHAR(8)   NOT NULL,
                position          INTEGER      NOT NULL,
                user_id           BIGINT       NOT NULL,
                display_name      VARCHAR(64)  NOT NULL DEFAULT '',
                chat_id           BIGINT       NOT NULL,
                role              INTEGER      NOT NULL DEFAULT 0,
                is_alive          BOOLEAN      NOT NULL DEFAULT true,
                last_vote         INTEGER      NOT NULL DEFAULT 0,
                state_message_id  INTEGER      NULL,
                joined_at         BIGINT       NOT NULL,
                PRIMARY KEY (invite_code, position)
            );
            CREATE INDEX ix_sh_players_user ON secret_hitler_players (user_id);
            CREATE INDEX ix_sh_players_code ON secret_hitler_players (invite_code);
            """),
        new Migration("002_game_state_message", """
            ALTER TABLE secret_hitler_games
                ADD COLUMN IF NOT EXISTS state_message_id INTEGER NULL;
            """),
    ];
}
