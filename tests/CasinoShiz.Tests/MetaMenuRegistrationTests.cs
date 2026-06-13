using BotFramework.Sdk;
using Games.Meta;
using Telegram.Bot.Types;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class MetaMenuRegistrationTests
{
    [Fact]
    public void MetaModule_AdvertisesMenuCommand()
    {
        var module = new MetaModule();

        var menu = Assert.Single(module.GetBotCommands(), x => x.Command == "/menu");

        Assert.Equal("meta.cmd.menu", menu.DescriptionKey);
        Assert.Contains(
            module.GetLocales().Single(x => x.CultureCode == "ru").Strings,
            x => x.Key == "cmd.menu" && !string.IsNullOrWhiteSpace(x.Value));
    }

    [Fact]
    public void MetaMenuHandler_RoutesCommandAndOwnCallbacks()
    {
        var attributes = typeof(MetaMenuHandler)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .ToArray();
        var command = Assert.Single(attributes.OfType<CommandAttribute>());
        var callback = Assert.Single(attributes.OfType<CallbackPrefixAttribute>());

        Assert.True(command.Matches(new Update
        {
            Message = new Message { Text = "/menu", Chat = new Chat { Id = 100 } },
        }));
        Assert.True(callback.Matches(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "1",
                Data = "mm:42:profile",
                From = new User { Id = 42, FirstName = "Test" },
            },
        }));
        Assert.False(callback.Matches(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "2",
                Data = "bj:hit",
                From = new User { Id = 42, FirstName = "Test" },
            },
        }));
    }
}
