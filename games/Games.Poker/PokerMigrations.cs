using BotFramework.Sdk;

namespace Games.Poker;

public sealed class PokerMigrations : IModuleMigrations
{
    public string ModuleId => "poker";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE poker_tables (
                invite_code      VARCHAR(8)  PRIMARY KEY,
                host_user_id     BIGINT      NOT NULL,
                status           INTEGER     NOT NULL,
                phase            INTEGER     NOT NULL,
                small_blind      INTEGER     NOT NULL,
                big_blind        INTEGER     NOT NULL,
                pot              INTEGER     NOT NULL,
                community_cards  VARCHAR(32) NOT NULL DEFAULT '',
                deck_state       VARCHAR(256) NOT NULL DEFAULT '',
                button_seat      INTEGER     NOT NULL,
                current_seat     INTEGER     NOT NULL,
                current_bet      INTEGER     NOT NULL,
                min_raise        INTEGER     NOT NULL,
                last_action_at   BIGINT      NOT NULL,
                created_at       BIGINT      NOT NULL
            );
            CREATE INDEX ix_poker_tables_status_action ON poker_tables (status, last_action_at);

            CREATE TABLE poker_seats (
                invite_code       VARCHAR(8)  NOT NULL,
                position          INTEGER     NOT NULL,
                user_id           BIGINT      NOT NULL,
                display_name      VARCHAR(64) NOT NULL DEFAULT '',
                stack             INTEGER     NOT NULL,
                hole_cards        VARCHAR(8)  NOT NULL DEFAULT '',
                status            INTEGER     NOT NULL,
                current_bet       INTEGER     NOT NULL,
                has_acted_round   BOOLEAN     NOT NULL DEFAULT false,
                chat_id           BIGINT      NOT NULL,
                state_message_id  INTEGER     NULL,
                joined_at         BIGINT      NOT NULL,
                PRIMARY KEY (invite_code, position)
            );
            CREATE INDEX ix_poker_seats_user ON poker_seats (user_id);
            CREATE INDEX ix_poker_seats_code ON poker_seats (invite_code);
            """),
    ];
}
