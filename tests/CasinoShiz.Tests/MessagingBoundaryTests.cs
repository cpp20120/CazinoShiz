using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Identity;
using BotFramework.Contracts.Operations;
using BotFramework.Contracts.Tenancy;
using BotFramework.Host.Messaging;
using BotFramework.Host.Contracts.Economics;
using BotFramework.Host.Contracts.ResponsibleGaming;
using BotFramework.Host.Composition.Modules;
using Games.Dice.Application.Requests;
using Games.Dice.Contracts.Play;
using Games.Dice.Telegram;
using Games.Dice.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Games.Dice.Transport.Grpc;
using Games.NativeDice.Transport.Grpc;
using Games.Transfer.Transport.Grpc;
using CasinoShiz.Identity;
using CasinoShiz.Identity.Transport.Grpc;
using CasinoShiz.Wallet.Transport.Grpc;
using CasinoShiz.Operations.Transport.Grpc;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class MessagingBoundaryTests
{
    [Fact]
    public void CoreSdk_DoesNotReferenceTelegramBot()
    {
        var references = typeof(BotFramework.Sdk.Modules.IModule).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references,
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(BotFramework.Sdk.UpdateHandling.UpdateContext).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
    }

    [Fact]
    public void SecretHitlerGameStore_UsesEfCoreContext()
    {
        var constructor = typeof(Games.SecretHitler.Infrastructure.Persistence.SecretHitlerGameStore)
            .GetConstructors().Single();
        Assert.Contains(constructor.GetParameters(), parameter =>
            parameter.ParameterType == typeof(Games.SecretHitler.Infrastructure.Persistence.SecretHitlerDbContext));
        Assert.DoesNotContain(constructor.GetParameters(), parameter =>
            parameter.ParameterType.Name.Contains("NpgsqlConnectionFactory", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityAndWalletContracts_AreTransportNeutral()
    {
        Assert.Same(typeof(IRequest<>).Assembly, typeof(IPlayerDirectory).Assembly);
        Assert.Same(typeof(IRequest<>).Assembly, typeof(IEconomicsService).Assembly);
        Assert.Same(typeof(IRequest<>).Assembly, typeof(IDailyBonusService).Assembly);
        Assert.Same(typeof(IRequest<>).Assembly, typeof(IWalletReadService).Assembly);
        Assert.Same(typeof(IRequest<>).Assembly, typeof(IWalletAnalyticsService).Assembly);
        Assert.Same(typeof(IRequest<>).Assembly, typeof(IPlayerProtectionService).Assembly);
        AssertClean(typeof(IPlayerDirectory).Assembly);

        Assert.DoesNotContain(
            typeof(PlayerDirectory).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Telegram", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void OperationsContract_IsTransportNeutral_AndClientIsResolvable()
    {
        Assert.Same(typeof(IRequest<>).Assembly, typeof(IOperationsAdminService).Assembly);
        AssertClean(typeof(IOperationsAdminService).Assembly);

        var services = new ServiceCollection();
        services.AddOperationsGrpcClient(new Uri("http://localhost:5081"), "test-key");
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IOperationsAdminService>());
    }

    [Fact]
    public void IdentityAndWalletGrpcClients_DoNotShareAnAmbientChannel()
    {
        var services = new ServiceCollection();
        services.AddIdentityGrpcClient(new Uri("http://localhost:5082"));
        services.AddWalletGrpcClients(new Uri("http://localhost:5083"));

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GrpcChannel));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<GrpcPlayerDirectory>(provider.GetRequiredService<IPlayerDirectory>());
        Assert.NotNull(provider.GetRequiredService<IEconomicsService>());
        Assert.NotNull(provider.GetRequiredService<IDailyBonusService>());
        Assert.NotNull(provider.GetRequiredService<IWalletReadService>());
        Assert.NotNull(provider.GetRequiredService<IWalletAnalyticsService>());
        Assert.NotNull(provider.GetRequiredService<IPlayerProtectionService>());
    }

    [Fact]
    public void ContractAssemblies_DoNotReferenceTelegramOrInfrastructure()
    {
        AssertClean(typeof(IRequest<>).Assembly);
        AssertClean(typeof(DicePlayRequest).Assembly);
        Assert.DoesNotContain(
            typeof(DiceService).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Telegram", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(
            typeof(DiceTelegramModule).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(DiceTelegramModule).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(
            typeof(BotFramework.Host.Composition.Builder.BotFrameworkBuilderExtensions)
                .Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(BotFramework.Telegram.Composition.LegacyBotFrameworkBuilderExtensions)
                .Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeDiceGames_KeepTelegramOutsideBackendAndContracts()
    {
        var boundaries = new[]
        {
            (Contract: typeof(Games.DiceCube.Application.Services.IDiceCubeService).Assembly,
                Backend: typeof(Games.DiceCube.Application.Services.DiceCubeService).Assembly,
                Adapter: typeof(Games.DiceCube.Application.Handlers.DiceCubeHandler).Assembly),
            (Contract: typeof(Games.Darts.Application.Services.IDartsService).Assembly,
                Backend: typeof(Games.Darts.Application.Services.DartsService).Assembly,
                Adapter: typeof(Games.Darts.Application.Handlers.DartsHandler).Assembly),
            (Contract: typeof(Games.Football.Application.Services.IFootballService).Assembly,
                Backend: typeof(Games.Football.Application.Services.FootballService).Assembly,
                Adapter: typeof(Games.Football.Application.Handlers.FootballHandler).Assembly),
            (Contract: typeof(Games.Basketball.Application.Services.IBasketballService).Assembly,
                Backend: typeof(Games.Basketball.Application.Services.BasketballService).Assembly,
                Adapter: typeof(Games.Basketball.Application.Handlers.BasketballHandler).Assembly),
            (Contract: typeof(Games.Bowling.Application.Services.IBowlingService).Assembly,
                Backend: typeof(Games.Bowling.Application.Services.BowlingService).Assembly,
                Adapter: typeof(Games.Bowling.Application.Handlers.BowlingHandler).Assembly),
        };

        foreach (var boundary in boundaries)
        {
            AssertClean(boundary.Contract);
            Assert.DoesNotContain(boundary.Backend.GetReferencedAssemblies(),
                reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
            Assert.Contains(boundary.Adapter.GetReferencedAssemblies(),
                reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
            Assert.DoesNotContain(boundary.Adapter.GetReferencedAssemblies(),
                reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    [Fact]
    public void NativeDiceGrpcWire_RoundTripsContractResults()
    {
        var cube = new Games.DiceCube.Domain.Results.CubeBetResult(
            Games.DiceCube.Domain.Results.CubeBetError.Cooldown, Balance: 42, CooldownSeconds: 7);
        var darts = new Games.Darts.Domain.Results.DartsBetResult(
            Games.Darts.Domain.Results.DartsBetError.None, Amount: 10, RoundId: 99, QueuedAhead: 2);
        var football = new Games.Football.Domain.Results.FootballThrowResult(
            Games.Football.Domain.Results.FootballThrowOutcome.Thrown, Face: 5, Bet: 10, Payout: 20);
        var basketball = new Games.Basketball.Domain.Results.BasketballThrowResult(
            Games.Basketball.Domain.Results.BasketballThrowOutcome.Thrown, Face: 4, Bet: 10, Payout: 20);
        var bowling = new Games.Bowling.Domain.Results.BowlingRollResult(
            Games.Bowling.Domain.Results.BowlingRollOutcome.Rolled, Face: 6, Bet: 10, Payout: 20);

        Assert.Equal(cube, NativeDiceWireCodec.Reply(cube).Read<Games.DiceCube.Domain.Results.CubeBetResult>());
        Assert.Equal(darts, NativeDiceWireCodec.Reply(darts).Read<Games.Darts.Domain.Results.DartsBetResult>());
        Assert.Equal(football, NativeDiceWireCodec.Reply(football).Read<Games.Football.Domain.Results.FootballThrowResult>());
        Assert.Equal(basketball, NativeDiceWireCodec.Reply(basketball).Read<Games.Basketball.Domain.Results.BasketballThrowResult>());
        Assert.Equal(bowling, NativeDiceWireCodec.Reply(bowling).Read<Games.Bowling.Domain.Results.BowlingRollResult>());
    }

    [Fact]
    public void NativeDiceGrpcWire_PreservesSourceMessageForRetryableDiceCubeCalls()
    {
        const int sourceMessageId = 731;
        var roll = NativeDiceWireCodec.Call(new RollCall(7, "player", 42, 6, sourceMessageId)).Read<RollCall>();
        var abort = NativeDiceWireCodec.Call(new AbortCall(7, 42, "player", sourceMessageId)).Read<AbortCall>();

        Assert.Equal(sourceMessageId, roll.SourceMessageId);
        Assert.Equal(sourceMessageId, abort.SourceMessageId);
        Assert.Equal("player", abort.DisplayName);
    }

    [Fact]
    public void TransferBoundary_KeepsClientAndTransportOutsideBackendContract()
    {
        AssertClean(typeof(Games.Transfer.Application.Services.ITransferService).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Transfer.Application.Services.TransferService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(Games.Transfer.Application.Handlers.TransferHandler).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Games.Transfer.Application.Handlers.TransferHandler).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);

        var result = new Games.Transfer.Application.Results.TransferAttemptResult(
            Games.Transfer.Application.Results.TransferError.None, 100, 3, 103, 897, 200);
        var wire = TransferGrpcEndpoint.Map(result);
        Assert.Equal((int)result.Error, wire.Error);
        Assert.Equal(result.TotalDebited, wire.TotalDebited);
        Assert.Equal(result.RecipientBalance, wire.RecipientBalance);
    }

    [Fact]
    public void RedeemBoundary_UsesIndependentCaptchaContract()
    {
        AssertClean(typeof(Games.Redeem.Contracts.IRedeemClient).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Redeem.Application.Services.RedeemService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(Games.Redeem.Application.Handlers.RedeemHandler).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Games.Redeem.Application.Handlers.RedeemHandler).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);

        var challenge = new Games.Redeem.Contracts.RedeemCaptchaChallenge(
            "pattern", [new Games.Redeem.Contracts.RedeemCaptchaItem("🎲", 1)]);
        var response = new Games.Redeem.Contracts.BeginRedeemResponse(
            Games.Redeem.Contracts.RedeemClientError.None, Guid.NewGuid(), challenge);
        Assert.Equal("pattern", response.Captcha?.Pattern);
        Assert.Equal(1, Assert.Single(response.Captcha!.Items).Data);
    }

    [Fact]
    public void LeaderboardBoundary_KeepsDailyBonusBehindReadClient()
    {
        AssertClean(typeof(Games.Leaderboard.Contracts.ILeaderboardClient).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Leaderboard.Application.Services.LeaderboardService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(Games.Leaderboard.Application.Handlers.LeaderboardHandler).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Games.Leaderboard.Application.Handlers.LeaderboardHandler).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);

        var response = new Games.Leaderboard.Contracts.DailyClaimResponse(
            Games.Leaderboard.Contracts.DailyClaimStatus.Claimed, 5, 105);
        Assert.Equal(105, response.NewBalance);
    }

    [Fact]
    public void PixelBattleBoundary_UsesHttpTransportWithoutBackendTelegramDependency()
    {
        AssertClean(typeof(Games.PixelBattle.Contracts.IPixelBattleService).Assembly);
        Assert.DoesNotContain(
            typeof(Games.PixelBattle.Application.PixelBattleService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(Games.PixelBattle.Application.Handlers.PixelBattleHandler).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Games.PixelBattle.Application.Handlers.PixelBattleHandler).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task PixelBattleService_DelegatesMutationToAtomicExecutor()
    {
        var store = new PixelStoreStub();
        var executor = new PixelExecutorStub();
        var service = new Games.PixelBattle.Application.PixelBattleService(store, executor);

        var invalid = await service.UpdateAsync(42, -1, "#ffffff", default);
        var unknown = await service.UpdateAsync(42, 0, "#ffffff", default);
        executor.Known = true;
        var updated = await service.UpdateAsync(42, 0, "#ffffff", default);

        Assert.Equal(Games.PixelBattle.Contracts.PixelUpdateStatus.InvalidIndex, invalid.Status);
        Assert.Equal(Games.PixelBattle.Contracts.PixelUpdateStatus.UnknownUser, unknown.Status);
        Assert.Equal(Games.PixelBattle.Contracts.PixelUpdateStatus.Updated, updated.Status);
        Assert.Equal(3, executor.ExecuteCount);
    }

    [Fact]
    public void PickBoundary_KeepsSweepersOutOfClientContract()
    {
        AssertClean(typeof(Games.Pick.Application.Services.IPickClient).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Pick.Application.Services.PickService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(Games.Pick.Application.Handlers.PickHandler).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Games.Pick.Application.Handlers.PickHandler).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);

        var methods = typeof(Games.Pick.Application.Services.IPickClient).GetMethods()
            .Select(method => method.Name)
            .ToArray();
        Assert.DoesNotContain(methods, name => name.Contains("Settle", StringComparison.Ordinal));
        Assert.Contains(nameof(Games.Pick.Application.Services.IPickClient.ClaimChainAsync), methods);
        Assert.Contains(nameof(Games.Pick.Application.Services.IPickClient.RestoreChainAsync), methods);
    }

    [Fact]
    public void BlackjackBoundary_KeepsStateAndRenderingSeparated()
    {
        AssertClean(typeof(Games.Blackjack.Contracts.IBlackjackClient).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Blackjack.Application.Services.BlackjackService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(Games.Blackjack.Application.Handlers.BlackjackHandler).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Games.Blackjack.Application.Handlers.BlackjackHandler).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);

        var state = new Games.Blackjack.Contracts.BlackjackState(null, 42);
        Assert.Equal(42, state.StateMessageId);
    }

    [Fact]
    public void HorseBoundary_UsesDeliveryPortForScheduledRaces()
    {
        AssertClean(typeof(Games.Horse.Application.Services.IHorseService).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Horse.Application.Services.HorseService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.Contains(
            typeof(Games.Horse.Application.Handlers.HorseHandler).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Games.Horse.Application.Handlers.HorseHandler).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);

        var eventType = typeof(Games.Horse.Contracts.HorseRaceCompletedIntegrationEvent);
        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(eventType));
    }

    [Fact]
    public void ChallengeBoundary_KeepsTelegramAndGameImplementationsOutsideBackend()
    {
        AssertClean(typeof(Games.Challenges.Application.Services.IChallengeService).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Challenges.Application.Services.ChallengeService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));

        var adapterReferences = typeof(Games.Challenges.Application.Handlers.ChallengeHandler)
            .Assembly.GetReferencedAssemblies();
        Assert.Contains(
            adapterReferences,
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            adapterReferences,
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(
            adapterReferences,
            reference => string.Equals(reference.Name, "Games.Blackjack", StringComparison.Ordinal));
        Assert.DoesNotContain(
            adapterReferences,
            reference => string.Equals(reference.Name, "Games.Horse", StringComparison.Ordinal));

        Assert.DoesNotContain(
            adapterReferences,
            reference => string.Equals(reference.Name, "Games.Challenges", StringComparison.Ordinal));

        var telegramModule = new Games.Challenges.Telegram.ChallengeTelegramModule();
        Assert.Contains(
            telegramModule.GetBotCommands(),
            command => string.Equals(command.Command, "/challenge", StringComparison.Ordinal));
        Assert.Contains(
            telegramModule.GetLocales(),
            locale => locale.Strings.ContainsKey("usage"));
    }

    [Fact]
    public void MetaBoundary_KeepsTelegramOutsideBackendAndContracts()
    {
        AssertClean(typeof(Games.Meta.Application.Meta.IMetaService).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Meta.Application.Meta.MetaService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));

        var adapterReferences = typeof(Games.Meta.Application.Meta.MetaHandler)
            .Assembly.GetReferencedAssemblies();
        Assert.Contains(
            adapterReferences,
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(
            adapterReferences,
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void MetaGrpcProxy_CanCreateContractImplementation()
    {
        using var channel = Grpc.Net.Client.GrpcChannel.ForAddress("http://localhost");
        var wireClient = new Games.Meta.Transport.Grpc.Wire.MetaApi.MetaApiClient(channel);

        var client = Games.Meta.Transport.Grpc.MetaGrpcProxy<Games.Meta.Application.Meta.IMetaService>
            .Create(wireClient);

        Assert.IsAssignableFrom<Games.Meta.Application.Meta.IMetaService>(client);
    }

    [Fact]
    public void AdminBoundary_KeepsTelegramOutsideBackendAndContracts()
    {
        AssertClean(typeof(Games.Admin.Application.Services.IAdminService).Assembly);
        Assert.DoesNotContain(
            typeof(Games.Admin.Application.Services.AdminService).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));

        var adapterReferences = typeof(Games.Admin.Application.Handlers.AdminHandler)
            .Assembly.GetReferencedAssemblies();
        Assert.Contains(adapterReferences,
            reference => string.Equals(reference.Name, "Telegram.Bot", StringComparison.Ordinal));
        Assert.DoesNotContain(adapterReferences,
            reference => reference.Name?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void AdminGrpcProxy_CanCreateContractImplementation()
    {
        using var channel = Grpc.Net.Client.GrpcChannel.ForAddress("http://localhost");
        var wireClient = new Games.Admin.Transport.Grpc.Wire.AdminApi.AdminApiClient(channel);

        var client = Games.Admin.Transport.Grpc.AdminGrpcProxy<Games.Admin.Application.Services.IAdminService>
            .Create(wireClient);

        Assert.IsAssignableFrom<Games.Admin.Application.Services.IAdminService>(client);
    }

    [Fact]
    public async Task LocalRequestClient_DispatchesThroughTransportNeutralPort()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<LocalRequestClient>());
        services.AddScoped<IRequestHandler<PingRequest, PingResponse>, PingHandler>();
        services.AddScoped<MediatR.IRequestHandler<PingRequest, PingResponse>, PingHandler>();
        services.AddScoped<IRequestClient, LocalRequestClient>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var metadata = RequestMetadata.Create("test", "42", "100");

        var response = await scope.ServiceProvider.GetRequiredService<IRequestClient>()
            .SendAsync<PingRequest, PingResponse>(new PingRequest("hello"), metadata, default);

        Assert.Equal("hello:test", response.Value);
    }

    [Fact]
    public async Task LocalRequestClient_ProjectsTenantIntoFrameworkAccessor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<LocalRequestClient>());
        services.AddScoped<IRequestHandler<TenantPingRequest, TenantPingResponse>, TenantPingHandler>();
        services.AddScoped<MediatR.IRequestHandler<TenantPingRequest, TenantPingResponse>, TenantPingHandler>();
        services.AddScoped<IRequestClient, LocalRequestClient>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var tenant = TenantContext.Create(
            TenantId.Create("local-tenant"),
            ScopeId.Create("main"),
            PlayerId.Create("player"),
            BotChannel.Rest);

        var response = await scope.ServiceProvider.GetRequiredService<IRequestClient>()
            .SendAsync<TenantPingRequest, TenantPingResponse>(
                new TenantPingRequest(),
                RequestMetadata.FromTenantContext(tenant, "test"),
                default);

        Assert.Equal("local-tenant", response.TenantId);
    }

    [Fact]
    public async Task LocalIntegrationEventPublisher_DispatchesThroughContractHandlers()
    {
        var first = new RecordingEventHandler();
        var second = new RecordingEventHandler();
        var services = new ServiceCollection()
            .AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(first)
            .AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(second)
            .AddScoped<IIntegrationEventPublisher, LocalIntegrationEventPublisher>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var integrationEvent = new TestIntegrationEvent(
            "test.completed.v1",
            DateTimeOffset.UtcNow,
            "result-1");

        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
            .PublishAsync(integrationEvent, default);

        Assert.Equal([integrationEvent], first.Events);
        Assert.Equal([integrationEvent], second.Events);
    }

    [Fact]
    public async Task IntegrationEventDispatcher_RestoresTenantAndRequestMetadata()
    {
        var accessor = new TenantContextAccessor();
        var handler = new TenantRecordingEventHandler(accessor);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContextAccessor>(accessor)
            .AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(handler)
            .AddSingleton<IntegrationEventDispatcher>();
        await using var provider = services.BuildServiceProvider();

        var envelope = new IntegrationEventEnvelope(
            "message-1",
            "test.completed.v1",
            IntegrationContractNames.Stable(typeof(TestIntegrationEvent)),
            1,
            "{\"EventType\":\"test.completed.v1\",\"OccurredAt\":\"2026-08-01T00:00:00Z\",\"ResultId\":\"result-1\"}",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            "correlation-1",
            "causation-1",
            "local-tenant",
            "main",
            "player-1",
            BotChannel.Rest);

        await provider.GetRequiredService<IntegrationEventDispatcher>()
            .DispatchAsync(envelope, default);

        Assert.Equal("result-1", handler.Event?.ResultId);
        Assert.Equal("local-tenant", handler.Tenant?.TenantId.Value);
        Assert.Equal("main", handler.Tenant?.ScopeId.Value);
        Assert.Equal("message-1", handler.Metadata?.RequestId);
        Assert.Equal("correlation-1", handler.Metadata?.CorrelationId);
    }

    [Fact]
    public async Task IntegrationCommandDispatcher_RestoresTenantAndRequestMetadata()
    {
        var accessor = new TenantContextAccessor();
        var handler = new TenantRecordingCommandHandler(accessor);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContextAccessor>(accessor)
            .AddSingleton<IIntegrationCommandHandler<TestIntegrationCommand>>(handler)
            .AddSingleton<IntegrationCommandDispatcher>();
        await using var provider = services.BuildServiceProvider();

        var envelope = new IntegrationCommandEnvelope(
            "command-1",
            "test.reserve.v1",
            IntegrationContractNames.Stable(typeof(TestIntegrationCommand)),
            1,
            "{\"CommandType\":\"test.reserve.v1\",\"OccurredAt\":\"2026-08-01T00:00:00Z\",\"ReservationId\":\"reservation-1\"}",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            "correlation-1",
            "causation-1",
            "local-tenant",
            "main",
            "player-1",
            BotChannel.Rest);

        await provider.GetRequiredService<IntegrationCommandDispatcher>()
            .DispatchAsync(envelope, default);

        Assert.Equal("reservation-1", handler.Command?.ReservationId);
        Assert.Equal("local-tenant", handler.Tenant?.TenantId.Value);
        Assert.Equal("main", handler.Tenant?.ScopeId.Value);
        Assert.Equal("command-1", handler.Metadata?.RequestId);
        Assert.Equal("correlation-1", handler.Metadata?.CorrelationId);
    }

    [Fact]
    public async Task DiceRequestHandler_MapsBackendResultWithoutClientTypes()
    {
        var handler = new DicePlayRequestHandler(new DiceServiceStub());
        var request = new DicePlayRequest(42, "alice", 64, -100, "123", false);

        var response = await handler.HandleAsync(
            request,
            RequestMetadata.Create("telegram", "42", "-100", "ru", BotChannel.Telegram),
            default);

        Assert.Equal(DicePlayStatus.Played, response.Status);
        Assert.Equal(77, response.Prize);
        Assert.Equal(5, response.Stake);
        Assert.Equal(1_072, response.Balance);
        Assert.Equal(1, response.Tax);
        Assert.Equal(2, response.DailyRollsUsed);
        Assert.Equal(10, response.DailyRollLimit);
    }

    [Fact]
    public async Task DiceRequestHandler_RejectsNonNumericIdempotencySource()
    {
        var handler = new DicePlayRequestHandler(new DiceServiceStub());
        var request = new DicePlayRequest(42, "alice", 64, -100, "telegram-message", false);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            request,
            RequestMetadata.Create("telegram", channel: BotChannel.Telegram),
            default));
    }

    [Fact]
    public void DiceGrpcMapper_RoundTripsLogicalContractsAndMetadata()
    {
        var request = new DicePlayRequest(42, "alice", 64, -100, "123", false);
        var metadata = new RequestMetadata(
            "request-1",
            "correlation-1",
            "telegram",
            "42",
            "-100",
            "ru",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["trace"] = "abc" });

        var wire = request.ToGrpc(metadata);
        var restoredRequest = wire.ToContract();
        var restoredMetadata = wire.Metadata.ToContract();

        Assert.Equal(request, restoredRequest);
        Assert.Equal(metadata.RequestId, restoredMetadata.RequestId);
        Assert.Equal(metadata.CorrelationId, restoredMetadata.CorrelationId);
        Assert.Equal(metadata.ClientId, restoredMetadata.ClientId);
        Assert.Equal(metadata.UserId, restoredMetadata.UserId);
        Assert.Equal(metadata.ScopeId, restoredMetadata.ScopeId);
        Assert.Equal(metadata.Culture, restoredMetadata.Culture);
        Assert.Equal("abc", restoredMetadata.Baggage["trace"]);

        var response = new DicePlayResponse(DicePlayStatus.Played, 77, 5, 1_072, 1, 2, 10);
        Assert.Equal(response, response.ToGrpc().ToContract());
    }

    [Fact]
    public void DiceTelegramModule_DoesNotRegisterBackendServicesOrPersistence()
    {
        var services = new ServiceCollection();
        ModuleLoader.LoadAll(
            [new DiceTelegramModule()],
            services,
            new ConfigurationBuilder().Build());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(DiceHandler));
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IDiceService>());
        Assert.Empty(provider.GetRequiredService<LoadedModules>().Migrations);
    }

    private static void AssertClean(System.Reflection.Assembly assembly)
    {
        var references = assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x?.Contains("Telegram", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, x => x?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, x => x?.Contains("Dapper", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, x => x?.Contains("DotNetCore.CAP", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, x => x?.Contains("Grpc", StringComparison.OrdinalIgnoreCase) == true);
    }

    private sealed record PingRequest(string Value) : IRequest<PingResponse>
    {
        public string MessageType => "test.ping.v1";
    }

    private sealed record PingResponse(string Value);

    private sealed record TenantPingRequest : IRequest<TenantPingResponse>
    {
        public string MessageType => "test.tenant-ping.v1";
    }

    private sealed record TenantPingResponse(string? TenantId);

    private sealed record TestIntegrationEvent(
        string EventType,
        DateTimeOffset OccurredAt,
        string ResultId) : IIntegrationEvent;

    private sealed record TestIntegrationCommand(
        string CommandType,
        DateTimeOffset OccurredAt,
        string ReservationId) : IIntegrationCommand;

    private sealed class RecordingEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public List<TestIntegrationEvent> Events { get; } = [];

        public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken ct)
        {
            Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class TenantRecordingEventHandler(ITenantContextAccessor accessor)
        : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public TestIntegrationEvent? Event { get; private set; }
        public TenantContext? Tenant { get; private set; }
        public RequestMetadata? Metadata { get; private set; }

        public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken ct)
        {
            Event = integrationEvent;
            Tenant = accessor.Current;
            Metadata = RequestMetadataContext.TryGetCurrent();
            return Task.CompletedTask;
        }
    }

    private sealed class TenantRecordingCommandHandler(ITenantContextAccessor accessor)
        : IIntegrationCommandHandler<TestIntegrationCommand>
    {
        public TestIntegrationCommand? Command { get; private set; }
        public TenantContext? Tenant { get; private set; }
        public RequestMetadata? Metadata { get; private set; }

        public Task HandleAsync(TestIntegrationCommand command, CancellationToken ct)
        {
            Command = command;
            Tenant = accessor.Current;
            Metadata = RequestMetadataContext.TryGetCurrent();
            return Task.CompletedTask;
        }
    }

    private sealed class PingHandler : IRequestHandler<PingRequest, PingResponse>
    {
        public Task<PingResponse> HandleAsync(PingRequest request, RequestMetadata metadata, CancellationToken ct) =>
            Task.FromResult(new PingResponse($"{request.Value}:{metadata.ClientId}"));
    }

    private sealed class TenantPingHandler(ITenantContextAccessor accessor)
        : IRequestHandler<TenantPingRequest, TenantPingResponse>
    {
        public Task<TenantPingResponse> HandleAsync(
            TenantPingRequest request,
            RequestMetadata metadata,
            CancellationToken ct) =>
            Task.FromResult(new TenantPingResponse(accessor.Current?.TenantId.Value));
    }

    private sealed class DiceServiceStub : IDiceService
    {
        public Task<DicePlayResult> PlayAsync(
            long userId,
            string displayName,
            int diceValue,
            long chatId,
            int sourceMessageId,
            bool isForwarded,
            CancellationToken ct) =>
            Task.FromResult(new DicePlayResult(
                DiceOutcome.Played,
                Prize: 77,
                Loss: 5,
                NewBalance: 1_072,
                Gas: 1,
                DailyDiceUsed: 2,
                DailyDiceLimit: 10));
    }

    private sealed class PixelStoreStub : Games.PixelBattle.Infrastructure.Persistence.IPixelBattleStore
    {
        public Task<Games.PixelBattle.Domain.Entities.PixelBattleGrid> GetGridAsync(CancellationToken ct) =>
            Task.FromResult(new Games.PixelBattle.Domain.Entities.PixelBattleGrid([], []));
    }

    private sealed class PixelExecutorStub : BotFramework.Host.Execution.IAtomicGameExecutor<
        Games.PixelBattle.Application.Execution.PixelBattleCommand,
        Games.PixelBattle.Application.Execution.PixelBattleExecutionState,
        Games.PixelBattle.Contracts.PixelUpdateResult>,
        BotFramework.Host.Execution.IGameStateExecutor<
            Games.PixelBattle.Application.Execution.PixelBattleCommand,
            Games.PixelBattle.Application.Execution.PixelBattleExecutionState,
            Games.PixelBattle.Contracts.PixelUpdateResult>
    {
        public Type StateType => typeof(Games.PixelBattle.Application.Execution.PixelBattleExecutionState);
        public bool Known { get; set; }
        public int ExecuteCount { get; private set; }

        public Task<Games.PixelBattle.Contracts.PixelUpdateResult> ExecuteAsync(
            BotFramework.Host.Execution.GameExecutionEnvelope<
                Games.PixelBattle.Application.Execution.PixelBattleCommand> envelope,
            CancellationToken ct)
        {
            ExecuteCount++;
            var command = envelope.Command;
            var status = !Games.PixelBattle.Domain.Models.PixelBattleConstants.IsValidIndex(command.Index)
                ? Games.PixelBattle.Contracts.PixelUpdateStatus.InvalidIndex
                : !Games.PixelBattle.Domain.Models.PixelBattleConstants.IsValidColor(command.Color)
                    ? Games.PixelBattle.Contracts.PixelUpdateStatus.InvalidColor
                    : !Known
                        ? Games.PixelBattle.Contracts.PixelUpdateStatus.UnknownUser
                        : Games.PixelBattle.Contracts.PixelUpdateStatus.Updated;
            var update = status == Games.PixelBattle.Contracts.PixelUpdateStatus.Updated
                ? new Games.PixelBattle.Domain.Entities.PixelBattleUpdate(command.Index, command.Color, "1")
                : null;
            return Task.FromResult(new Games.PixelBattle.Contracts.PixelUpdateResult(status, update));
        }
    }
}
