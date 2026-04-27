using BotFramework.Sdk;

namespace CasinoShiz.Host.Debug;

public sealed class DebugModule : IModule
{
    public string Id => "debug";
    public string DisplayName => "Debug";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services) =>
        services
            .AddSingleton<BotProcessClock>()
            .AddHandler<DebugHandler>();

    public IReadOnlyList<LocaleBundle> GetLocales() => [];
}
