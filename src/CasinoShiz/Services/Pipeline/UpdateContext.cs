using Telegram.Bot;
using Telegram.Bot.Types;

namespace CasinoShiz.Services.Pipeline;

public sealed class UpdateContext(
    ITelegramBotClient bot,
    Update update,
    IServiceProvider services,
    CancellationToken ct)
{
    public ITelegramBotClient Bot { get; } = bot;
    public Update Update { get; } = update;
    public IServiceProvider Services { get; } = services;
    public CancellationToken Ct { get; } = ct;
    public Dictionary<string, object> Items { get; } = new();

    public long UserId =>
        Update.Message?.From?.Id
        ?? Update.CallbackQuery?.From.Id
        ?? Update.ChannelPost?.From?.Id
        ?? 0;

    public long ChatId =>
        Update.Message?.Chat.Id
        ?? Update.CallbackQuery?.Message?.Chat.Id
        ?? Update.ChannelPost?.Chat.Id
        ?? 0;

    public string? Text => Update.Message?.Text;
    public string? CallbackData => Update.CallbackQuery?.Data;
}
