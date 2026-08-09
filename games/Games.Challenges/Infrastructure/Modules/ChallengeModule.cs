
namespace Games.Challenges.Infrastructure.Modules;

using BotFramework.Host.Execution;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;
using Games.Challenges.Application.Execution;
using Games.Challenges.Application.Wagering;
using Games.Challenges.Infrastructure.Configuration;

public sealed class ChallengeModule : IModule
{
    public string Id => "challenges";
    public string DisplayName => "PvP Challenges";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<ChallengeOptions, ChallengeOptionsValidator>(ChallengeOptions.SectionName)
            .AddScoped<ChallengeDbContext>()
            .AddScoped<IChallengeStore, ChallengeStore>()
            .AddChallengeExecution<ChallengeCreateCommand, ChallengeCreateResult, ChallengeCreateAction, ChallengeCreateDescriptor>()
            .AddChallengeExecution<ChallengeAcceptCommand, ChallengeAcceptResult, ChallengeAcceptAction, ChallengeAcceptDescriptor>()
            .AddChallengeExecution<ChallengeDeclineCommand, ChallengeAcceptError, ChallengeDeclineAction, ChallengeDeclineDescriptor>()
            .AddChallengeExecution<ChallengeCompleteCommand, ChallengeAcceptResult, ChallengeCompleteAction, ChallengeCompleteDescriptor>()
            .AddChallengeExecution<ChallengeFailCommand, bool, ChallengeFailAction, ChallengeFailDescriptor>()
            .AddScoped<IMultiPartyWagerGameAdapter, ChallengeMultiPartyWagerAdapter>()
            .AddScoped<IMultiPartyWagerPayoutPolicy, ChallengeMultiPartyWagerPayoutPolicy>()
            .AddScoped<IChallengeService, ChallengeService>();
    }

    public IModuleMigrations GetMigrations() => new ChallengeMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/challenge", "challenges.cmd.challenge"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() => ChallengePresentationMetadata.Locales;
}
