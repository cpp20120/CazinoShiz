using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.PixelBattle;

[Command("/pixelbattle")]
public sealed class PixelBattleHandler(
    ILocalizer localizer,
    IOptions<PixelBattleOptions> options) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg is null) return;

        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var webAppUrl = options.Value.WebAppUrl;
        if (string.IsNullOrWhiteSpace(webAppUrl))
        {
            await ctx.Bot.SendMessage(
                msg.Chat.Id,
                Loc("open.not_configured"),
                replyParameters: reply,
                cancellationToken: ctx.Ct);
            return;
        }

        var markup = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithWebApp(Loc("open.button"), new WebAppInfo(webAppUrl))]
        ]);

        await ctx.Bot.SendMessage(
            msg.Chat.Id,
            Loc("open.text"),
            replyMarkup: markup,
            replyParameters: reply,
            cancellationToken: ctx.Ct);
    }

    private string Loc(string key) => localizer.Get("pixelbattle", key);
}
