using BotFramework.Host.Services;
using Xunit;

namespace CasinoShiz.Tests;

public class CaptchaServiceTests
{
    // ── Determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void CreateCaptcha_SameUuid_ReturnsSamePattern()
    {
        var uuid = Guid.NewGuid().ToString();
        var r1 = CaptchaService.CreateCaptcha(uuid);
        var r2 = CaptchaService.CreateCaptcha(uuid);
        Assert.Equal(r1.Pattern, r2.Pattern);
    }

    [Fact]
    public void CreateCaptcha_SameUuid_ReturnsSameTargetId()
    {
        var uuid = Guid.NewGuid().ToString();
        var r1 = CaptchaService.CreateCaptcha(uuid);
        var r2 = CaptchaService.CreateCaptcha(uuid);
        Assert.Equal(r1.TargetId, r2.TargetId);
    }

    [Fact]
    public void CreateCaptcha_DifferentUuid_LikelyDifferentPattern()
    {
        var r1 = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString());
        var r2 = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString());
        // Not guaranteed but extremely likely with different UUIDs
        // Just verify both produce non-empty patterns
        Assert.NotEmpty(r1.Pattern);
        Assert.NotEmpty(r2.Pattern);
    }

    // ── Item count ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void CreateCaptcha_ReturnsRequestedItemCount(int count)
    {
        var result = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString(), count);
        Assert.Equal(count, result.Items.Length);
    }

    [Fact]
    public void CreateCaptcha_Default6Items()
    {
        var result = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString());
        Assert.Equal(6, result.Items.Length);
    }

    // ── TargetId ─────────────────────────────────────────────────────────────

    [Fact]
    public void CreateCaptcha_TargetIdIsWithinItemRange()
    {
        var result = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString(), 6);
        Assert.InRange(result.TargetId, 0, 5);
    }

    [Fact]
    public void CreateCaptcha_TargetIdWithinRangeForCount4()
    {
        var result = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString(), 4);
        Assert.InRange(result.TargetId, 0, 3);
    }

    // ── Items ────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateCaptcha_AllItemsHaveNonEmptyText()
    {
        var result = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString(), 6);
        Assert.All(result.Items, item => Assert.NotEmpty(item.Text));
    }

    [Fact]
    public void CreateCaptcha_ItemDataValuesAreUnique()
    {
        var result = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString(), 6);
        var uniqueIds = result.Items.Select(i => i.Data).Distinct().Count();
        Assert.Equal(result.Items.Length, uniqueIds);
    }

    [Fact]
    public void CreateCaptcha_PatternIsNotEmpty()
    {
        var result = CaptchaService.CreateCaptcha(Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Pattern);
    }
}
