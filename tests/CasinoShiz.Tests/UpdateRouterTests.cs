using CasinoShiz.Services.Handlers;
using CasinoShiz.Services.Pipeline;
using Xunit;

namespace BusinoBot.Tests;

public class UpdateRouterTests
{
    [Fact]
    public void AllHandlers_ImplementIUpdateHandler()
    {
        var handlerTypes = new[]
        {
            typeof(DiceHandler), typeof(HorseHandler), typeof(PokerHandler),
            typeof(RedeemHandler), typeof(AdminHandler), typeof(LeaderboardHandler),
            typeof(ChatHandler), typeof(ChannelHandler),
        };
        foreach (var t in handlerTypes)
            Assert.True(typeof(IUpdateHandler).IsAssignableFrom(t), $"{t.Name} missing IUpdateHandler");
    }

    [Theory]
    [InlineData(typeof(ChannelHandler), typeof(ChannelPostAttribute))]
    [InlineData(typeof(DiceHandler), typeof(MessageDiceAttribute))]
    [InlineData(typeof(PokerHandler), typeof(CommandAttribute))]
    [InlineData(typeof(PokerHandler), typeof(CallbackPrefixAttribute))]
    [InlineData(typeof(HorseHandler), typeof(CommandAttribute))]
    [InlineData(typeof(RedeemHandler), typeof(CallbackFallbackAttribute))]
    public void HandlerType_HasExpectedRouteAttribute(System.Type handler, System.Type attr)
    {
        var found = System.Attribute.GetCustomAttributes(handler, attr);
        Assert.NotEmpty(found);
    }

    [Fact]
    public void HorseCommandAttribute_HorserunHigherPriorityThanHorse()
    {
        var attrs = System.Attribute.GetCustomAttributes(typeof(HorseHandler), typeof(CommandAttribute))
            .OfType<CommandAttribute>().ToList();
        Assert.Contains(attrs, a => a.Prefix == "/horse");
        Assert.Contains(attrs, a => a.Prefix == "/horserun");
        var horse = attrs.First(a => a.Prefix == "/horse");
        var run = attrs.First(a => a.Prefix == "/horserun");
        Assert.True(run.Priority > horse.Priority);
    }
}
