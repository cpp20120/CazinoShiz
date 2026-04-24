using BotFramework.Sdk;

namespace Games.Transfer;

public sealed class TransferMigrations : IModuleMigrations
{
    public string ModuleId => "transfer";

    public IReadOnlyList<Migration> Migrations { get; } = [];
}
