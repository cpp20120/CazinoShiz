using BotFramework.Sdk.Modules.Migrations;

namespace BotFramework.Host.Composition.Migrations;

internal sealed class IntegrationInboxMigrations : IModuleMigrations
{
    public string ModuleId => "_framework.integration";

    public IReadOnlyList<Migration> Migrations { get; } =
        [
            IntegrationInboxMigrationDefinition.Create(),
            IntegrationOutboxMigrationDefinition.Create(),
            IntegrationQuarantineMigrationDefinition.Create(),
            WagerOperationMigrationDefinition.Create(),
            WagerReservationMigrationDefinition.Create(),
            MultiPartyWagerMigrationDefinition.Create(),
        ];
}
