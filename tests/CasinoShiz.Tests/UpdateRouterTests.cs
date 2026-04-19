using BotFramework.Sdk;
using Games.Admin;
using Games.Basketball;
using Games.Blackjack;
using Games.Bowling;
using Games.Darts;
using Games.Dice;
using Games.DiceCube;
using Games.Horse;
using Games.Leaderboard;
using Games.Poker;
using Games.Redeem;
using Games.SecretHitler;
using Xunit;

namespace CasinoShiz.Tests;

public class UpdateRouterTests
{
    [Fact]
    public void AllHandlers_ImplementIUpdateHandler()
    {
        var handlerTypes = new[]
        {
            typeof(DiceHandler), typeof(DiceCubeHandler), typeof(DartsHandler),
            typeof(BasketballHandler), typeof(BowlingHandler),
            typeof(BlackjackHandler), typeof(HorseHandler), typeof(PokerHandler),
            typeof(SecretHitlerHandler), typeof(RedeemHandler),
            typeof(LeaderboardHandler), typeof(AdminHandler),
        };
        foreach (var t in handlerTypes)
            Assert.True(typeof(IUpdateHandler).IsAssignableFrom(t), $"{t.Name} missing IUpdateHandler");
    }

    [Theory]
    [InlineData(typeof(DiceHandler), typeof(MessageDiceAttribute))]
    [InlineData(typeof(BasketballHandler), typeof(MessageDiceAttribute))]
    [InlineData(typeof(BasketballHandler), typeof(CommandAttribute))]
    [InlineData(typeof(BowlingHandler), typeof(MessageDiceAttribute))]
    [InlineData(typeof(BowlingHandler), typeof(CommandAttribute))]
    [InlineData(typeof(PokerHandler), typeof(CommandAttribute))]
    [InlineData(typeof(PokerHandler), typeof(CallbackPrefixAttribute))]
    [InlineData(typeof(HorseHandler), typeof(CommandAttribute))]
    [InlineData(typeof(RedeemHandler), typeof(CommandAttribute))]
    [InlineData(typeof(RedeemHandler), typeof(CallbackPrefixAttribute))]
    [InlineData(typeof(AdminHandler), typeof(CommandAttribute))]
    [InlineData(typeof(LeaderboardHandler), typeof(CommandAttribute))]
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
