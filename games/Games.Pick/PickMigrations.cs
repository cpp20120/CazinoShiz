using BotFramework.Sdk;

namespace Games.Pick;

public sealed class PickMigrations : IModuleMigrations
{
    public string ModuleId => "pick";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_lottery", """
            CREATE TABLE pick_lottery (
                id           UUID         PRIMARY KEY,
                chat_id      BIGINT       NOT NULL,
                opener_id    BIGINT       NOT NULL,
                opener_name  TEXT         NOT NULL,
                stake        INTEGER      NOT NULL,
                status       TEXT         NOT NULL,
                opened_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                deadline_at  TIMESTAMPTZ  NOT NULL,
                settled_at   TIMESTAMPTZ,
                winner_id    BIGINT,
                winner_name  TEXT,
                pot_total    INTEGER,
                payout       INTEGER,
                fee          INTEGER
            );

            -- One open pool per chat at most. Partial unique index avoids
            -- having to lock the table on every /picklottery — settled and
            -- cancelled rows are out of scope.
            CREATE UNIQUE INDEX ux_pick_lottery_open_per_chat
                ON pick_lottery (chat_id)
                WHERE status = 'open';

            -- Sweeper hot path: scan expired open pools cheaply.
            CREATE INDEX ix_pick_lottery_deadline_open
                ON pick_lottery (deadline_at)
                WHERE status = 'open';

            CREATE TABLE pick_lottery_entries (
                lottery_id    UUID         NOT NULL
                                            REFERENCES pick_lottery(id) ON DELETE CASCADE,
                user_id       BIGINT       NOT NULL,
                display_name  TEXT         NOT NULL,
                stake_paid    INTEGER      NOT NULL,
                entered_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                PRIMARY KEY (lottery_id, user_id)
            );
            """),

        new Migration("002_daily_lottery", """
            CREATE TABLE pick_daily_lottery (
                id            UUID         PRIMARY KEY,
                chat_id       BIGINT       NOT NULL,
                day_local     DATE         NOT NULL,
                ticket_price  INTEGER      NOT NULL,
                status        TEXT         NOT NULL,
                opened_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
                deadline_at   TIMESTAMPTZ  NOT NULL,
                settled_at    TIMESTAMPTZ,
                winner_id     BIGINT,
                winner_name   TEXT,
                ticket_count  INTEGER,
                pot_total     INTEGER,
                payout        INTEGER,
                fee           INTEGER
            );

            -- One pool per (chat, local day). The unique index is the source
            -- of truth for "is there already a pool for today in this chat?"
            -- and serves the get-or-create race-safely via ON CONFLICT.
            CREATE UNIQUE INDEX ux_pick_daily_lottery_chat_day
                ON pick_daily_lottery (chat_id, day_local);

            -- Sweeper hot path.
            CREATE INDEX ix_pick_daily_lottery_deadline_open
                ON pick_daily_lottery (deadline_at)
                WHERE status = 'open';

            -- Chat history view: latest settled draws first.
            CREATE INDEX ix_pick_daily_lottery_chat_settled
                ON pick_daily_lottery (chat_id, day_local DESC)
                WHERE status = 'settled';

            -- One row per ticket. Random draw is then a trivial
            -- ORDER BY random() LIMIT 1, with weight = number of rows owned
            -- by a user. Cheap because tables stay small (≤ a few hundred
            -- rows per chat per day).
            CREATE TABLE pick_daily_lottery_tickets (
                id            BIGSERIAL    PRIMARY KEY,
                lottery_id    UUID         NOT NULL
                                              REFERENCES pick_daily_lottery(id) ON DELETE CASCADE,
                user_id       BIGINT       NOT NULL,
                display_name  TEXT         NOT NULL,
                price_paid    INTEGER      NOT NULL,
                bought_at     TIMESTAMPTZ  NOT NULL DEFAULT now()
            );

            CREATE INDEX ix_pick_daily_tickets_lottery_user
                ON pick_daily_lottery_tickets (lottery_id, user_id);
            """),
    ];
}
