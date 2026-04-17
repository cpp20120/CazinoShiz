using Telegram.Bot;
using Telegram.Bot.Types;

namespace CasinoShiz.Services.Pipeline;

public interface IUpdateHandler
{
    Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct);
}
