using BotFramework.Sdk;

namespace Games.Admin;

public sealed class AdminMigrations : IModuleMigrations
{
    public string ModuleId => "admin";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new("001_display_name_overrides", """
                                          CREATE TABLE display_name_overrides (
                                              original_name  TEXT  PRIMARY KEY,
                                              new_name       TEXT  NOT NULL
                                          );
                                          """),
    ];
}
