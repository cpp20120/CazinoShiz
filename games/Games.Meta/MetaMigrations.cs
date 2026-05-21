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

        new Migration("002_achievements", """
            CREATE TABLE meta_player_achievements (
                achievement_id TEXT        NOT NULL,
                season_id      BIGINT      NOT NULL REFERENCES meta_seasons(id) ON DELETE CASCADE,
                chat_id        BIGINT      NOT NULL,
                user_id        BIGINT      NOT NULL,
                unlocked_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (achievement_id, season_id, chat_id, user_id)
            );

            CREATE INDEX ix_meta_player_achievements_user
                ON meta_player_achievements (season_id, chat_id, user_id, unlocked_at DESC);
            """),

        new Migration("003_quests", """
            CREATE TABLE meta_player_quests (
                quest_id     TEXT        NOT NULL,
                season_id    BIGINT      NOT NULL REFERENCES meta_seasons(id) ON DELETE CASCADE,
                chat_id      BIGINT      NOT NULL,
                user_id      BIGINT      NOT NULL,
                period_key   TEXT        NOT NULL,
                progress     INTEGER     NOT NULL DEFAULT 0,
                target       INTEGER     NOT NULL,
                completed    BOOLEAN     NOT NULL DEFAULT false,
                claimed      BOOLEAN     NOT NULL DEFAULT false,
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                claimed_at   TIMESTAMPTZ NULL,
                PRIMARY KEY (quest_id, season_id, chat_id, user_id, period_key),
                CONSTRAINT ck_meta_player_quests_progress CHECK (progress >= 0),
                CONSTRAINT ck_meta_player_quests_target CHECK (target > 0)
            );

            CREATE INDEX ix_meta_player_quests_user_period
                ON meta_player_quests (season_id, chat_id, user_id, period_key, completed, claimed);
            """),

        new Migration("004_clans", """
            CREATE TABLE meta_clans (
                id            BIGSERIAL    PRIMARY KEY,
                chat_id       BIGINT       NOT NULL,
                name          TEXT         NOT NULL,
                tag           TEXT         NOT NULL,
                owner_user_id BIGINT       NOT NULL,
                created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                CONSTRAINT ck_meta_clans_tag_len CHECK (char_length(tag) BETWEEN 2 AND 12),
                CONSTRAINT ck_meta_clans_name_len CHECK (char_length(name) BETWEEN 2 AND 64)
            );

            CREATE UNIQUE INDEX ux_meta_clans_chat_tag
                ON meta_clans (chat_id, lower(tag));

            CREATE TABLE meta_clan_members (
                clan_id      BIGINT      NOT NULL REFERENCES meta_clans(id) ON DELETE CASCADE,
                chat_id      BIGINT      NOT NULL,
                user_id      BIGINT      NOT NULL,
                display_name TEXT        NOT NULL,
                role         TEXT        NOT NULL DEFAULT 'member',
                joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (clan_id, user_id),
                CONSTRAINT ck_meta_clan_members_role CHECK (role IN ('owner', 'officer', 'member'))
            );

            CREATE UNIQUE INDEX ux_meta_clan_members_chat_user
                ON meta_clan_members (chat_id, user_id);

            CREATE INDEX ix_meta_clan_members_clan
                ON meta_clan_members (clan_id, joined_at ASC);

            CREATE TABLE meta_season_clans (
                season_id BIGINT  NOT NULL REFERENCES meta_seasons(id) ON DELETE CASCADE,
                chat_id   BIGINT  NOT NULL,
                clan_id   BIGINT  NOT NULL REFERENCES meta_clans(id) ON DELETE CASCADE,
                xp        BIGINT  NOT NULL DEFAULT 0,
                rating    INTEGER NOT NULL DEFAULT 1000,
                wins      INTEGER NOT NULL DEFAULT 0,
                losses    INTEGER NOT NULL DEFAULT 0,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (season_id, chat_id, clan_id),
                CONSTRAINT ck_meta_season_clans_xp CHECK (xp >= 0),
                CONSTRAINT ck_meta_season_clans_rating CHECK (rating >= 0)
            );

            CREATE INDEX ix_meta_season_clans_top
                ON meta_season_clans (season_id, chat_id, xp DESC, rating DESC, clan_id ASC);
            """),

        new Migration("005_tournaments", """
            CREATE TABLE meta_tournaments (
                id          BIGSERIAL    PRIMARY KEY,
                season_id   BIGINT       NOT NULL REFERENCES meta_seasons(id) ON DELETE CASCADE,
                chat_id     BIGINT       NOT NULL,
                game_key    TEXT         NOT NULL,
                type        TEXT         NOT NULL DEFAULT 'single_elimination',
                status      TEXT         NOT NULL DEFAULT 'open',
                entry_fee   INTEGER      NOT NULL DEFAULT 0,
                max_players INTEGER      NOT NULL,
                created_by  BIGINT       NOT NULL,
                created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
                CONSTRAINT ck_meta_tournaments_status CHECK (status IN ('open', 'started', 'cancelled', 'finished')),
                CONSTRAINT ck_meta_tournaments_entry_fee CHECK (entry_fee >= 0),
                CONSTRAINT ck_meta_tournaments_max_players CHECK (max_players BETWEEN 2 AND 64)
            );

            CREATE INDEX ix_meta_tournaments_chat_status
                ON meta_tournaments (season_id, chat_id, status, created_at DESC);

            CREATE TABLE meta_tournament_players (
                tournament_id BIGINT      NOT NULL REFERENCES meta_tournaments(id) ON DELETE CASCADE,
                user_id       BIGINT      NOT NULL,
                display_name  TEXT        NOT NULL,
                status        TEXT        NOT NULL DEFAULT 'joined',
                joined_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (tournament_id, user_id),
                CONSTRAINT ck_meta_tournament_players_status CHECK (status IN ('joined', 'dropped', 'eliminated', 'winner'))
            );

            CREATE INDEX ix_meta_tournament_players_tournament
                ON meta_tournament_players (tournament_id, joined_at ASC);
            """),

        new Migration("006_risk_flags", """
            CREATE TABLE meta_risk_flags (
                id            BIGSERIAL    PRIMARY KEY,
                season_id     BIGINT       NOT NULL REFERENCES meta_seasons(id) ON DELETE CASCADE,
                chat_id       BIGINT       NOT NULL,
                user_id       BIGINT       NOT NULL,
                display_name  TEXT         NOT NULL,
                kind          TEXT         NOT NULL,
                severity      TEXT         NOT NULL,
                status        TEXT         NOT NULL DEFAULT 'open',
                reason        TEXT         NOT NULL,
                evidence      JSONB        NOT NULL DEFAULT '{}'::jsonb,
                created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
                resolved_at   TIMESTAMPTZ  NULL,
                CONSTRAINT ck_meta_risk_flags_severity CHECK (severity IN ('low', 'medium', 'high', 'critical')),
                CONSTRAINT ck_meta_risk_flags_status CHECK (status IN ('open', 'ignored', 'resolved'))
            );

            CREATE INDEX ix_meta_risk_flags_open
                ON meta_risk_flags (season_id, chat_id, status, severity, created_at DESC);

            CREATE INDEX ix_meta_risk_flags_user
                ON meta_risk_flags (season_id, chat_id, user_id, created_at DESC);

            CREATE UNIQUE INDEX ux_meta_risk_flags_recent_duplicate
                ON meta_risk_flags (season_id, chat_id, user_id, kind, status)
                WHERE status = 'open';
            """),

        new Migration("007_tournament_matches", """
            CREATE TABLE meta_tournament_matches (
                id                    BIGSERIAL    PRIMARY KEY,
                tournament_id          BIGINT       NOT NULL REFERENCES meta_tournaments(id) ON DELETE CASCADE,
                round                 INTEGER      NOT NULL,
                match_index           INTEGER      NOT NULL,
                status                TEXT         NOT NULL DEFAULT 'pending',
                player1_user_id        BIGINT       NULL,
                player1_display_name   TEXT         NULL,
                player2_user_id        BIGINT       NULL,
                player2_display_name   TEXT         NULL,
                victor_user_id         BIGINT       NULL,
                created_at             TIMESTAMPTZ  NOT NULL DEFAULT now(),
                updated_at             TIMESTAMPTZ  NOT NULL DEFAULT now(),
                CONSTRAINT ck_meta_tournament_matches_round CHECK (round >= 1),
                CONSTRAINT ck_meta_tournament_matches_index CHECK (match_index >= 1),
                CONSTRAINT ck_meta_tournament_matches_status CHECK (status IN ('pending', 'ready', 'finished', 'byed'))
            );

            CREATE UNIQUE INDEX ux_meta_tournament_matches_slot
                ON meta_tournament_matches (tournament_id, round, match_index);

            CREATE INDEX ix_meta_tournament_matches_tournament
                ON meta_tournament_matches (tournament_id, round, match_index);
            """),
    ];
}
