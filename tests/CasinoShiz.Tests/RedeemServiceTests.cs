using BotFramework.Sdk;
using Games.Redeem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class RedeemServiceTests
{
    private const long DmScope = 5000;

    private static RedeemService MakeService(
        InMemoryRedeemStore? store = null,
        FakeEconomicsService? economics = null,
        RecordingTelegramDiceDailyRollLimiter? telegramDiceRolls = null,
        NullEventBus? bus = null,
        int captchaItems = 6) =>
        new(
            store ?? new InMemoryRedeemStore(),
            economics ?? new FakeEconomicsService(),
            telegramDiceRolls ?? new RecordingTelegramDiceDailyRollLimiter(),
            new NullAnalyticsService(),
            bus ?? new NullEventBus(),
            Options.Create(new RedeemOptions { CaptchaItems = captchaItems }),
            NullLogger<RedeemService>.Instance);

    // ── IssueAdminCodeAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task IssueAdminCodeAsync_ReturnsNewGuid()
    {
        var svc = MakeService();
        var code = await svc.IssueAdminCodeAsync(1, default);
        Assert.NotEqual(Guid.Empty, code);
    }

    [Fact]
    public async Task IssueAdminCodeAsync_TwoCalls_ReturnsDifferentGuids()
    {
        var svc = MakeService();
        var c1 = await svc.IssueAdminCodeAsync(1, default);
        var c2 = await svc.IssueAdminCodeAsync(1, default);
        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public async Task IssueAdminCodeAsync_PublishesEvent()
    {
        var bus = new NullEventBus();
        var svc = MakeService(bus: bus);
        await svc.IssueAdminCodeAsync(1, default);
        Assert.Single(bus.Published);
        Assert.IsType<RedeemCodeIssued>(bus.Published[0]);
    }

    [Fact]
    public async Task IssueAdminCodeAsync_CodeIsStoredAsActive()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store);
        var guid = await svc.IssueAdminCodeAsync(1, default);

        var code = await store.FindAsync(guid, default);
        Assert.NotNull(code);
        Assert.True(code!.Active);
    }

    // ── BeginRedeemAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task BeginRedeemAsync_InvalidText_ReturnsInvalidCode()
    {
        var svc = MakeService();
        var result = await svc.BeginRedeemAsync(1, DmScope, "u", "not-a-uuid", default);
        Assert.Equal(RedeemError.InvalidCode, result.Error);
    }

    [Fact]
    public async Task BeginRedeemAsync_EmptyText_ReturnsInvalidCode()
    {
        var svc = MakeService();
        var result = await svc.BeginRedeemAsync(1, DmScope, "u", "", default);
        Assert.Equal(RedeemError.InvalidCode, result.Error);
    }

    [Fact]
    public async Task BeginRedeemAsync_NonExistentCode_ReturnsAlreadyRedeemed()
    {
        var svc = MakeService();
        var result = await svc.BeginRedeemAsync(1, DmScope, "u", Guid.NewGuid().ToString(), default);
        Assert.Equal(RedeemError.AlreadyRedeemed, result.Error);
    }

    [Fact]
    public async Task BeginRedeemAsync_InactiveCode_ReturnsAlreadyRedeemed()
    {
        var store = new InMemoryRedeemStore();
        var code = new RedeemCode { Code = Guid.NewGuid(), Active = false, IssuedBy = 2, IssuedAt = 0 };
        await store.InsertAsync(code, default);
        var svc = MakeService(store);

        var result = await svc.BeginRedeemAsync(1, DmScope, "u", code.Code.ToString(), default);
        Assert.Equal(RedeemError.AlreadyRedeemed, result.Error);
    }

    [Fact]
    public async Task BeginRedeemAsync_SelfRedeem_ReturnsSelfRedeem()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store);
        var guid = await svc.IssueAdminCodeAsync(userId:5, default);

        var result = await svc.BeginRedeemAsync(userId: 5, DmScope, "u", guid.ToString(), default);
        Assert.Equal(RedeemError.SelfRedeem, result.Error);
    }

    [Fact]
    public async Task BeginRedeemAsync_ValidCode_ReturnsNoneWithCaptcha()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store);
        var guid = await svc.IssueAdminCodeAsync(userId:1, default);

        var result = await svc.BeginRedeemAsync(userId: 2, DmScope, "u", guid.ToString(), default);

        Assert.Equal(RedeemError.None, result.Error);
        Assert.NotNull(result.Captcha);
        Assert.Equal(guid, result.CodeGuid);
    }

    [Fact]
    public async Task BeginRedeemAsync_ValidCode_CaptchaHasCorrectItemCount()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store, captchaItems: 4);
        var guid = await svc.IssueAdminCodeAsync(userId:1, default);

        var result = await svc.BeginRedeemAsync(userId: 2, DmScope, "u", guid.ToString(), default);

        Assert.Equal(4, result.Captcha!.Items.Length);
    }

    // ── CompleteRedeemAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CompleteRedeemAsync_NonExistentCode_ReturnsAlreadyRedeemed()
    {
        var svc = MakeService();
        var result = await svc.CompleteRedeemAsync(1, DmScope, Guid.NewGuid(), default);
        Assert.Equal(RedeemError.AlreadyRedeemed, result.Error);
    }

    [Fact]
    public async Task CompleteRedeemAsync_InactiveCode_ReturnsAlreadyRedeemed()
    {
        var store = new InMemoryRedeemStore();
        var code = new RedeemCode { Code = Guid.NewGuid(), Active = false, IssuedBy = 1, IssuedAt = 0 };
        await store.InsertAsync(code, default);
        var svc = MakeService(store);

        var result = await svc.CompleteRedeemAsync(2, DmScope, code.Code, default);
        Assert.Equal(RedeemError.AlreadyRedeemed, result.Error);
    }

    [Fact]
    public async Task CompleteRedeemAsync_ValidCode_ReturnsNone()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store);
        var guid = await svc.IssueAdminCodeAsync(1, default);

        var result = await svc.CompleteRedeemAsync(2, DmScope, guid, default);
        Assert.Equal(RedeemError.None, result.Error);
    }

    [Fact]
    public async Task CompleteRedeemAsync_ValidCode_GrantsFreeSpin()
    {
        var store = new InMemoryRedeemStore();
        var telegramDiceRolls = new RecordingTelegramDiceDailyRollLimiter();
        var svc = MakeService(store, telegramDiceRolls: telegramDiceRolls);
        var guid = await svc.IssueAdminCodeAsync(1, default);

        await svc.CompleteRedeemAsync(2, DmScope, guid, default);

        Assert.Equal([MiniGameIds.Dice], telegramDiceRolls.GrantedGameIds);
    }

    [Fact]
    public async Task CompleteRedeemAsync_ValidCode_ReturnsFreeSpinGameId()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store);
        var guid = await svc.IssueAdminCodeAsync(1, default);

        var result = await svc.CompleteRedeemAsync(2, DmScope, guid, default);
        Assert.Equal(MiniGameIds.Dice, result.FreeSpinGameId);
    }

    [Fact]
    public async Task CompleteRedeemAsync_GameSpecificCode_GrantsThatGame()
    {
        var store = new InMemoryRedeemStore();
        var telegramDiceRolls = new RecordingTelegramDiceDailyRollLimiter();
        var svc = MakeService(store, telegramDiceRolls: telegramDiceRolls);
        var guid = await svc.IssueAdminCodeAsync(1, default, MiniGameIds.Bowling);

        var result = await svc.CompleteRedeemAsync(2, DmScope, guid, default);

        Assert.Equal(MiniGameIds.Bowling, result.FreeSpinGameId);
        Assert.Equal([MiniGameIds.Bowling], telegramDiceRolls.GrantedGameIds);
    }

    [Fact]
    public async Task CompleteRedeemAsync_ValidCode_DeactivatesCode()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store);
        var guid = await svc.IssueAdminCodeAsync(1, default);
        await svc.CompleteRedeemAsync(2, DmScope, guid, default);

        var code = await store.FindAsync(guid, default);
        Assert.False(code!.Active);
    }

    [Fact]
    public async Task CompleteRedeemAsync_SameCodeTwice_SecondReturnsAlreadyRedeemed()
    {
        var store = new InMemoryRedeemStore();
        var svc = MakeService(store);
        var guid = await svc.IssueAdminCodeAsync(1, default);
        await svc.CompleteRedeemAsync(2, DmScope, guid, default);

        var second = await svc.CompleteRedeemAsync(3, DmScope, guid, default);
        Assert.Equal(RedeemError.AlreadyRedeemed, second.Error);
    }

    [Fact]
    public async Task CompleteRedeemAsync_ValidCode_PublishesEvent()
    {
        var store = new InMemoryRedeemStore();
        var bus = new NullEventBus();
        var svc = MakeService(store, bus: bus);
        var guid = await svc.IssueAdminCodeAsync(1, default);
        bus.Published.Clear(); // clear the IssueAdminCodeAsync event

        await svc.CompleteRedeemAsync(2, DmScope, guid, default);

        Assert.Single(bus.Published);
        Assert.IsType<RedeemCodeRedeemed>(bus.Published[0]);
    }

    // ── ReportCaptcha ─────────────────────────────────────────────────────────

    [Fact]
    public void ReportCaptcha_DoesNotThrow()
    {
        var svc = MakeService();
        // Just validates it doesn't throw — analytics are null-sink
        var ex = Record.Exception(() => svc.ReportCaptcha(1, "code", "pattern", passed: true));
        Assert.Null(ex);
    }

    [Fact]
    public void ReportCaptcha_Failed_DoesNotThrow()
    {
        var svc = MakeService();
        var ex = Record.Exception(() => svc.ReportCaptcha(1, "code", "pattern", passed: false));
        Assert.Null(ex);
    }
}
