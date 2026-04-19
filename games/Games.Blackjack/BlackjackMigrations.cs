using BotFramework.Sdk;

namespace Games.Blackjack;

public sealed class BlackjackMigrations : IModuleMigrations
{
    public string ModuleId => "blackjack";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE blackjack_hands (
                user_id           BIGINT      PRIMARY KEY,
                chat_id           BIGINT      NOT NULL,
                bet               INTEGER     NOT NULL,
                player_cards      TEXT        NOT NULL,
                dealer_cards      TEXT        NOT NULL,
                deck_state        TEXT        NOT NULL,
                state_message_id  INTEGER     NULL,
                created_at        TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_blackjack_hands_created ON blackjack_hands (created_at);
            """),
    ];
}
