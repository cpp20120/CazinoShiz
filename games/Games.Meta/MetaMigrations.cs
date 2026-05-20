using BotFramework.Sdk;

namespace Games.Meta;

public sealed class MetaMigrations : IModuleMigrations
{
    public string ModuleId => "meta";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial_seasons", """
            CREATE TABLE meta_seasons (
                id          BIGSERIAL    PRIMARY KEY,
                name        TEXT         NOT NULL,
                starts_at   TIMESTAMPTZ  NOT NULL,
                ends_at     TIMESTAMPTZ  NOT NULL,
                status      TEXT         NOT NULL DEFAULT 'planned',
                config      JSONB        NOT NULL DEFAULT '{}'::jsonb,
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                CONSTRAINT ck_meta_seasons_status CHECK (status IN ('planned', 'active', 'finished')),
                CONSTRAINT ck_meta_seasons_time CHECK (ends_at > starts_at)
            );

            CREATE UNIQUE INDEX ux_meta_seasons_active
                ON meta_seasons ((status))
                WHERE status = 'active';

            CREATE INDEX ix_meta_seasons_status_time
                ON meta_seasons (status, starts_at DESC, ends_at DESC);

            CREATE TABLE meta_season_players (
                season_id     BIGINT       NOT NULL REFERENCES meta_seasons(id) ON DELETE CASCADE,
                chat_id       BIGINT       NOT NULL,
                user_id       BIGINT       NOT NULL,
                display_name  TEXT         NOT NULL,
                xp            BIGINT       NOT NULL DEFAULT 0,
                level         INTEGER      NOT NULL DEFAULT 1,
                rating        INTEGER      NOT NULL DEFAULT 1000,
                games_played  INTEGER      NOT NULL DEFAULT 0,
                wins          INTEGER      NOT NULL DEFAULT 0,
                losses        INTEGER      NOT NULL DEFAULT 0,
                total_staked  BIGINT       NOT NULL DEFAULT 0,
                total_payout  BIGINT       NOT NULL DEFAULT 0,
                created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                PRIMARY KEY (season_id, chat_id, user_id),
                CONSTRAINT ck_meta_season_players_xp CHECK (xp >= 0),
                CONSTRAINT ck_meta_season_players_level CHECK (level >= 1),
                CONSTRAINT ck_meta_season_players_rating CHECK (rating >= 0),
                CONSTRAINT ck_meta_season_players_games CHECK (games_played >= 0 AND wins >= 0 AND losses >= 0),
                CONSTRAINT ck_meta_season_players_volume CHECK (total_staked >= 0 AND total_payout >= 0)
            );

            CREATE INDEX ix_meta_season_players_top_xp
                ON meta_season_players (season_id, chat_id, xp DESC, rating DESC, user_id ASC);

            CREATE INDEX ix_meta_season_players_top_rating
                ON meta_season_players (season_id, chat_id, rating DESC, xp DESC, user_id ASC);
            """),
    ];
}
