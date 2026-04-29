using BotFramework.Sdk;

namespace Games.PixelBattle;

public sealed class PixelBattleMigrations : IModuleMigrations
{
    public string ModuleId => "pixelbattle";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE SEQUENCE IF NOT EXISTS pixelbattle_version_seq;

            CREATE TABLE IF NOT EXISTS pixelbattle_tiles (
                index       INTEGER     PRIMARY KEY,
                color       TEXT        NOT NULL,
                version     BIGINT      NOT NULL,
                updated_by  BIGINT      NOT NULL,
                updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS ix_pixelbattle_tiles_updated_at
                ON pixelbattle_tiles (updated_at DESC);
            """),
    ];
}
