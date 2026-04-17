using Telegram.Bot.Types;

namespace CasinoShiz.Services.Pipeline;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public abstract class RouteAttribute : Attribute
{
    public abstract int Priority { get; }
    public abstract string Name { get; }
    public abstract bool Matches(Update update);
}

public sealed class CommandAttribute(string prefix) : RouteAttribute
{
    public string Prefix { get; } = prefix;
    public override int Priority => 100 + Prefix.Length;
    public override string Name => $"cmd:{Prefix}";
    public override bool Matches(Update update) =>
        update.Message?.Text is { } text && text.StartsWith(Prefix);
}

public sealed class CallbackPrefixAttribute(string prefix) : RouteAttribute
{
    public string Prefix { get; } = prefix;
    public override int Priority => 200;
    public override string Name => $"cb:{Prefix}";
    public override bool Matches(Update update) =>
        update.CallbackQuery?.Data?.StartsWith(Prefix) == true;
}

public sealed class MessageDiceAttribute(string emoji) : RouteAttribute
{
    public string Emoji { get; } = emoji;
    public override int Priority => 250;
    public override string Name => $"dice:{Emoji}";
    public override bool Matches(Update update) =>
        update.Message?.Dice?.Emoji == Emoji;
}

public sealed class ChannelPostAttribute : RouteAttribute
{
    public override int Priority => 300;
    public override string Name => "channel_post";
    public override bool Matches(Update update) => update.ChannelPost != null;
}

public sealed class CallbackFallbackAttribute : RouteAttribute
{
    public override int Priority => 1;
    public override string Name => "cb_fallback";
    public override bool Matches(Update update) => update.CallbackQuery != null;
}
