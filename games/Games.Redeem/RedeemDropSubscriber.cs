using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Games.Redeem;

public sealed partial class RedeemDropSubscriber(
    IServiceProvider services,
    ITelegramBotClient bot,
    ILocalizer localizer,
    ILogger<RedeemDropSubscriber> logger) : IDomainEventSubscriber
{
    private const string DropEventType = "telegram_dice.redeem_code_drop_requested";

    public async Task HandleAsync(IDomainEvent ev, CancellationToken ct)
    {
        if (ev.EventType != DropEventType) return;
        if (!TryReadDrop(ev, out _, out var chatId, out var gameId)) return;

        try
        {
            await using var scope = services.CreateAsyncScope();
            var redeem = scope.ServiceProvider.GetRequiredService<IRedeemService>();

            var code = await redeem.IssueAdminCodeAsync(userId: 0, ct, gameId);
            await bot.SendMessage(
                chatId,
                string.Format(localizer.Get("redeem", "drop.message"), code),
                parseMode: ParseMode.Html,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            LogDropFailed(ex);
        }
    }

    private static bool TryReadDrop(IDomainEvent ev, out long userId, out long chatId, out string gameId)
    {
        var type = ev.GetType();
        userId = ReadLong(type, ev, "UserId");
        chatId = ReadLong(type, ev, "ChatId");
        gameId = ReadString(type, ev, "GameId");
        return userId != 0 && chatId != 0 && !string.IsNullOrWhiteSpace(gameId);
    }

    private static long ReadLong(Type type, object instance, string propertyName) =>
        type.GetProperty(propertyName)?.GetValue(instance) switch
        {
            long value => value,
            int value => value,
            _ => 0,
        };

    private static string ReadString(Type type, object instance, string propertyName) =>
        type.GetProperty(propertyName)?.GetValue(instance) as string ?? "";

    [LoggerMessage(LogLevel.Warning, "redeem.drop_failed")]
    partial void LogDropFailed(Exception ex);
}
