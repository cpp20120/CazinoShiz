using BotFramework.Sdk;

namespace CasinoShiz.Host.Debug;

public sealed class DebugMigrations : IModuleMigrations
{
    public string ModuleId => "debug";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_es_smoke_projection", """
            CREATE TABLE IF NOT EXISTS debug_es_smoke_projection (
                stream_id       TEXT        PRIMARY KEY,
                count           INTEGER     NOT NULL,
                stream_version  BIGINT      NOT NULL,
                updated_at_ms   BIGINT      NOT NULL
            );
            """)
    ];
}
