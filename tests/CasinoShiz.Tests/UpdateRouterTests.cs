using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace CasinoShiz.Tests;

// ── Test handlers (file-scoped, live in this assembly) ────────────────────────

[Command("/testcmd")]
file sealed class TestCommandHandler : IUpdateHandler
{
    public static bool WasCalled { get; set; }
    public Task HandleAsync(UpdateContext ctx) { WasCalled = true; return Task.CompletedTask; }
}

[CallbackPrefix("testcb:")]
file sealed class TestCallbackHandler : IUpdateHandler
{
    public static bool WasCalled { get; set; }
    public Task HandleAsync(UpdateContext ctx) { WasCalled = true; return Task.CompletedTask; }
}

[CallbackFallback]
file sealed class FallbackHandler : IUpdateHandler
{
    public static bool WasCalled { get; set; }
    public Task HandleAsync(UpdateContext ctx) { WasCalled = true; return Task.CompletedTask; }
}

// Module whose assembly is this test project — router scans it for handlers
file sealed class TestModule : IModule
{
    public string Id => "test";
    public string DisplayName => "Test";
    public string Version => "1.0";
    public void ConfigureServices(IModuleServiceCollection services) { }
    public IReadOnlyList<LocaleBundle> GetLocales() => [];
}

public class UpdateRouterTests
{
    private static UpdateRouter MakeRouter() =>
        new([new TestModule()], NullLogger<UpdateRouter>.Instance);

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddScoped<TestCommandHandler>();
        sc.AddScoped<TestCallbackHandler>();
        sc.AddScoped<FallbackHandler>();
        return sc.BuildServiceProvider();
    }

    private static Update TextUpdate(string text) => new()
    {
        Id = 1,
        Message = new Message
        {
            Id = 1,
            Text = text,
            From = new User { Id = 1, IsBot = false, FirstName = "T" },
            Chat = new Chat { Id = 1, Type = ChatType.Private },
            Date = DateTime.UtcNow,
        }
    };

    private static Update CallbackUpdate(string data) => new()
    {
        Id = 1,
        CallbackQuery = new CallbackQuery
        {
            Id = "1",
            Data = data,
            From = new User { Id = 1, IsBot = false, FirstName = "T" },
        }
    };

    // ── Dispatch ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Router_MatchingCommand_CallsHandler()
    {
        TestCommandHandler.WasCalled = false;
        var router = MakeRouter();
        using var sp = (ServiceProvider)BuildServices();
        using var scope = sp.CreateScope();

        await router.DispatchAsync(null!, TextUpdate("/testcmd create"), scope.ServiceProvider, default);

        Assert.True(TestCommandHandler.WasCalled);
    }

    [Fact]
    public async Task Router_MatchingCallback_CallsCallbackHandler()
    {
        TestCallbackHandler.WasCalled = false;
        var router = MakeRouter();
        using var sp = (ServiceProvider)BuildServices();
        using var scope = sp.CreateScope();

        await router.DispatchAsync(null!, CallbackUpdate("testcb:action"), scope.ServiceProvider, default);

        Assert.True(TestCallbackHandler.WasCalled);
    }

    [Fact]
    public async Task Router_UnmatchedCallback_FallsBackToFallbackHandler()
    {
        FallbackHandler.WasCalled = false;
        var router = MakeRouter();
        using var sp = (ServiceProvider)BuildServices();
        using var scope = sp.CreateScope();

        await router.DispatchAsync(null!, CallbackUpdate("unknown:data"), scope.ServiceProvider, default);

        Assert.True(FallbackHandler.WasCalled);
    }

    [Fact]
    public async Task Router_CallbackPrefixTakesPriorityOverFallback()
    {
        TestCallbackHandler.WasCalled = false;
        FallbackHandler.WasCalled = false;
        var router = MakeRouter();
        using var sp = (ServiceProvider)BuildServices();
        using var scope = sp.CreateScope();

        await router.DispatchAsync(null!, CallbackUpdate("testcb:check"), scope.ServiceProvider, default);

        Assert.True(TestCallbackHandler.WasCalled);
        Assert.False(FallbackHandler.WasCalled);
    }

    [Fact]
    public async Task Router_NoMatch_DoesNotThrow()
    {
        var router = MakeRouter();
        using var sp = (ServiceProvider)BuildServices();
        using var scope = sp.CreateScope();

        var ex = await Record.ExceptionAsync(() =>
            router.DispatchAsync(null!, TextUpdate("/no_handler_registered"), scope.ServiceProvider, default));
        Assert.Null(ex);
    }

    // ── Construction ─────────────────────────────────────────────────────────

    [Fact]
    public void Router_NoModules_DoesNotThrow()
    {
        var ex = Record.Exception(() => new UpdateRouter([], NullLogger<UpdateRouter>.Instance));
        Assert.Null(ex);
    }

    [Fact]
    public void Router_LogRegisteredRoutes_DoesNotThrow()
    {
        var router = MakeRouter();
        var ex = Record.Exception(() => router.LogRegisteredRoutes());
        Assert.Null(ex);
    }
}
