using BotFramework.Host.Composition.Builder;
using BotFramework.Host.Configuration.RuntimeTuning;
using BotFramework.Host.Economics.Options;
using BotFramework.Host.Execution;
using BotFramework.Host.Execution.Telegram;
using BotFramework.Sdk.Execution;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class DailyGameQuotaExecutionDescriptorTests
{
    [Fact]
    public void Quotas_DelegatesGenericRequestToPolicy()
    {
        var policy = new CapturingQuotaPolicy();
        var descriptor = new TestDescriptor(policy);
        var now = new DateTimeOffset(2026, 8, 30, 20, 0, 0, TimeSpan.Zero);

        var quota = Assert.Single(descriptor.Quotas(new(10, "player", 20, "command"), now));

        Assert.Equal(new GameDailyQuotaRequest("test.daily-roll", "test-dice", 10, 20, now, true), policy.Request);
        Assert.Equal("test.daily-roll", quota.QuotaId);
        Assert.Equal(3, quota.Limit);
    }

    [Fact]
    public void Quotas_NullQuotaId_OptOutsWithoutCallingPolicy()
    {
        var descriptor = new TestDescriptor(new FailingQuotaPolicy(), quotaId: null);

        var quotas = descriptor.Quotas(new(10, "player", 20, "command"), DateTimeOffset.UnixEpoch);

        Assert.Empty(quotas);
    }

    [Fact]
    public void RuntimePolicy_AdaptsExistingNativeDiceConfigurationInternally()
    {
        var policy = new TelegramDiceGameDailyQuotaPolicy(
            new TestRuntimeTuning(new TelegramDiceDailyLimitOptions
            {
                MaxRollsPerUserPerDay = 10,
                MaxRollsPerUserPerDayByGame = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["test-dice"] = 3,
                },
                TimezoneOffsetHours = 7,
            }),
            Options.Create(new BotFrameworkOptions()));

        var quota = Assert.Single(policy.Create(new(
            "test.daily-roll",
            "test-dice",
            10,
            20,
            new(2026, 8, 30, 20, 0, 0, TimeSpan.Zero),
            true)));

        Assert.Equal(new DateOnly(2026, 8, 31), quota.OnDate);
        Assert.Equal(3, quota.Limit);
    }

    [Fact]
    public void RuntimePolicy_PrivateChatAdminReceivesUnlimitedQuotaSnapshot()
    {
        var policy = new TelegramDiceGameDailyQuotaPolicy(
            new TestRuntimeTuning(new TelegramDiceDailyLimitOptions { MaxRollsPerUserPerDay = 3 }),
            Options.Create(new BotFrameworkOptions { Admins = [10] }));

        var quota = Assert.Single(policy.Create(new(
            "test.daily-roll",
            "test-dice",
            10,
            10,
            DateTimeOffset.UnixEpoch,
            true)));

        Assert.Equal(0, quota.Limit);
    }

    [Fact]
    public void RuntimePolicy_CanOmitUnlimitedQuotaWhenActionHasNoQuotaEffect()
    {
        var policy = new TelegramDiceGameDailyQuotaPolicy(
            new TestRuntimeTuning(new TelegramDiceDailyLimitOptions { MaxRollsPerUserPerDay = 0 }),
            Options.Create(new BotFrameworkOptions()));

        var quotas = policy.Create(new(
            "test.daily-roll",
            "test-dice",
            10,
            20,
            DateTimeOffset.UnixEpoch,
            false));

        Assert.Empty(quotas);
    }

    private static readonly GameDefinition TestGame = new("test-dice", "Test dice");

    private sealed record TestCommand(long UserId, string DisplayName, long ChatId, string CommandId)
        : IPlayerGameCommand;

    private sealed class TestDescriptor(IGameDailyQuotaPolicy dailyQuotaPolicy, string? quotaId = "test.daily-roll")
        : DailyGameQuotaExecutionDescriptor<TestCommand, NoGameState, string>(TestGame, dailyQuotaPolicy)
    {
        protected override string? DailyQuotaId(TestCommand command) => quotaId;
    }

    private sealed class CapturingQuotaPolicy : IGameDailyQuotaPolicy
    {
        public GameDailyQuotaRequest? Request { get; private set; }

        public IReadOnlyList<QuotaIdentity> Create(GameDailyQuotaRequest request)
        {
            Request = request;
            return [new(request.QuotaId, request.GameId, request.UserId, request.BalanceScopeId, DateOnly.MinValue, 3)];
        }
    }

    private sealed class FailingQuotaPolicy : IGameDailyQuotaPolicy
    {
        public IReadOnlyList<QuotaIdentity> Create(GameDailyQuotaRequest request) =>
            throw new Xunit.Sdk.XunitException("The quota policy must not be called.");
    }

    private sealed class TestRuntimeTuning(TelegramDiceDailyLimitOptions options) : IRuntimeTuningAccessor
    {
        public DailyBonusOptions DailyBonus { get; } = new();
        public TelegramDiceDailyLimitOptions TelegramDiceDailyLimit { get; } = options;

        public T GetSection<T>(string sectionPath) where T : class, new() => new();

        public Task ReloadFromDatabaseAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
