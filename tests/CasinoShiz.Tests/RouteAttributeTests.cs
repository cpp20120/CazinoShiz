using BotFramework.Sdk;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace CasinoShiz.Tests;

public class RouteAttributeTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Update TextUpdate(string text, long userId = 1) => new()
    {
        Id = 1,
        Message = new Message
        {
            Id = 1,
            Text = text,
            From = new User { Id = userId, IsBot = false, FirstName = "T" },
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

    private static Update DiceUpdate(string emoji) => new()
    {
        Id = 1,
        Message = new Message
        {
            Id = 1,
            Dice = new Dice { Emoji = emoji, Value = 3 },
            From = new User { Id = 1, IsBot = false, FirstName = "T" },
            Chat = new Chat { Id = 1, Type = ChatType.Private },
            Date = DateTime.UtcNow,
        }
    };

    private static Update ChannelPostUpdate() => new()
    {
        Id = 1,
        ChannelPost = new Message
        {
            Id = 1,
            Text = "hello",
            Chat = new Chat { Id = -100, Type = ChatType.Channel },
            Date = DateTime.UtcNow,
        }
    };

    // ── CommandAttribute ─────────────────────────────────────────────────────

    [Fact]
    public void Command_MatchesExactPrefix()
    {
        var attr = new CommandAttribute("/poker");
        Assert.True(attr.Matches(TextUpdate("/poker")));
    }

    [Fact]
    public void Command_MatchesPrefixWithArgs()
    {
        var attr = new CommandAttribute("/poker");
        Assert.True(attr.Matches(TextUpdate("/poker create")));
    }

    [Fact]
    public void Command_DoesNotMatchDifferentCommand()
    {
        var attr = new CommandAttribute("/poker");
        Assert.False(attr.Matches(TextUpdate("/sh")));
    }

    [Fact]
    public void Command_DoesNotMatchCallback()
    {
        var attr = new CommandAttribute("/poker");
        Assert.False(attr.Matches(CallbackUpdate("/poker")));
    }

    [Fact]
    public void Command_PriorityIsBasePlusLength()
    {
        var attr = new CommandAttribute("/poker");
        Assert.Equal(100 + "/poker".Length, attr.Priority);
    }

    [Fact]
    public void Command_LongerPrefixHigherPriority()
    {
        var short_ = new CommandAttribute("/p");
        var long_ = new CommandAttribute("/poker");
        Assert.True(long_.Priority > short_.Priority);
    }

    [Fact]
    public void Command_NameContainsPrefix()
    {
        var attr = new CommandAttribute("/poker");
        Assert.Contains("/poker", attr.Name);
    }

    // ── CallbackPrefixAttribute ──────────────────────────────────────────────

    [Fact]
    public void CallbackPrefix_MatchesCallback()
    {
        var attr = new CallbackPrefixAttribute("poker:");
        Assert.True(attr.Matches(CallbackUpdate("poker:check")));
    }

    [Fact]
    public void CallbackPrefix_DoesNotMatchDifferentPrefix()
    {
        var attr = new CallbackPrefixAttribute("poker:");
        Assert.False(attr.Matches(CallbackUpdate("sh:vote:ja")));
    }

    [Fact]
    public void CallbackPrefix_DoesNotMatchTextMessage()
    {
        var attr = new CallbackPrefixAttribute("poker:");
        Assert.False(attr.Matches(TextUpdate("poker:check")));
    }

    [Fact]
    public void CallbackPrefix_PriorityIs200()
    {
        var attr = new CallbackPrefixAttribute("poker:");
        Assert.Equal(200, attr.Priority);
    }

    // ── MessageDiceAttribute ─────────────────────────────────────────────────

    [Fact]
    public void MessageDice_MatchesCorrectEmoji()
    {
        var attr = new MessageDiceAttribute("🎰");
        Assert.True(attr.Matches(DiceUpdate("🎰")));
    }

    [Fact]
    public void MessageDice_DoesNotMatchDifferentEmoji()
    {
        var attr = new MessageDiceAttribute("🎰");
        Assert.False(attr.Matches(DiceUpdate("🎲")));
    }

    [Fact]
    public void MessageDice_DoesNotMatchTextMessage()
    {
        var attr = new MessageDiceAttribute("🎰");
        Assert.False(attr.Matches(TextUpdate("🎰")));
    }

    [Fact]
    public void MessageDice_PriorityIs250()
    {
        var attr = new MessageDiceAttribute("🎰");
        Assert.Equal(250, attr.Priority);
    }

    // ── ChannelPostAttribute ─────────────────────────────────────────────────

    [Fact]
    public void ChannelPost_MatchesChannelPost()
    {
        var attr = new ChannelPostAttribute();
        Assert.True(attr.Matches(ChannelPostUpdate()));
    }

    [Fact]
    public void ChannelPost_DoesNotMatchRegularMessage()
    {
        var attr = new ChannelPostAttribute();
        Assert.False(attr.Matches(TextUpdate("hello")));
    }

    [Fact]
    public void ChannelPost_PriorityIs300()
    {
        var attr = new ChannelPostAttribute();
        Assert.Equal(300, attr.Priority);
    }

    // ── CallbackFallbackAttribute ────────────────────────────────────────────

    [Fact]
    public void CallbackFallback_MatchesAnyCallback()
    {
        var attr = new CallbackFallbackAttribute();
        Assert.True(attr.Matches(CallbackUpdate("anything")));
    }

    [Fact]
    public void CallbackFallback_DoesNotMatchTextMessage()
    {
        var attr = new CallbackFallbackAttribute();
        Assert.False(attr.Matches(TextUpdate("hello")));
    }

    [Fact]
    public void CallbackFallback_PriorityIs1()
    {
        var attr = new CallbackFallbackAttribute();
        Assert.Equal(1, attr.Priority);
    }

    // ── Priority ordering ────────────────────────────────────────────────────

    [Fact]
    public void Priority_ChannelPost_BeatsAll()
    {
        var channel = new ChannelPostAttribute().Priority;
        var dice    = new MessageDiceAttribute("🎰").Priority;
        var cb      = new CallbackPrefixAttribute("x:").Priority;
        var cmd     = new CommandAttribute("/x").Priority;
        var fall    = new CallbackFallbackAttribute().Priority;

        Assert.True(channel > dice);
        Assert.True(dice > cb);
        Assert.True(cb > cmd);
        Assert.True(cmd > fall);
    }
}
