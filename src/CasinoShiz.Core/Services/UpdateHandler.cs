using CasinoShiz.Services.Pipeline;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CasinoShiz.Services;

public sealed class UpdateHandler(UpdatePipeline pipeline, IServiceProvider services)
{
    public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        var ctx = new UpdateContext(botClient, update, services, ct);
        return pipeline.InvokeAsync(ctx);
    }
}
