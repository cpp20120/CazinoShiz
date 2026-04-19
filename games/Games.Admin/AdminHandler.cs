using System.Text.Json;
using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Admin;

[Command("/run")]
[Command("/rename")]
public sealed class AdminHandler(
    IAdminService service,
    ILocalizer localizer,
    IOptions<AdminOptions> options) : IUpdateHandler
{
    private readonly AdminOptions _opts = options.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        if (msg.Text.StartsWith("/rename"))
        {
            await HandleRenameAsync(ctx, msg, userId);
            return;
        }

        if (!_opts.Admins.Contains(userId))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("err.not_admin"),
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
            service.ReportNotAdmin(userId);
            return;
        }

        var parts = StripFirst(msg.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 0 ? parts[0] : "";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        switch (action)
        {
            case "usersync":
                await service.UserSyncAsync(userId, ctx.Ct);
                await ctx.Bot.SendMessage(msg.Chat.Id, Loc("usersync.done"),
                    replyParameters: reply, cancellationToken: ctx.Ct);
                break;

            case "userinfo":
                if (msg.ReplyToMessage == null)
                {
                    await ctx.Bot.SendMessage(msg.Chat.Id, Loc("userinfo.reply_hint"),
                        replyParameters: reply, cancellationToken: ctx.Ct);
                    break;
                }
                var targetId = msg.ReplyToMessage.From?.Id.ToString() ?? "unknown";
                await ctx.Bot.SendMessage(msg.Chat.Id,
                    string.Format(Loc("userinfo.result"), targetId),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                service.ReportUserInfo(userId, targetId);
                break;

            case "pay":
                await HandlePayAsync(ctx, msg, userId, parts[1..]);
                break;

            case "getUser":
                await HandleGetUserAsync(ctx, msg, parts[1..]);
                break;

            default:
                await ctx.Bot.SendMessage(msg.Chat.Id, Loc("run.help"),
                    replyParameters: reply, cancellationToken: ctx.Ct);
                break;
        }
    }

    private async Task HandlePayAsync(UpdateContext ctx, Message msg, long userId, string[] args)
    {
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        if (args.Length < 2 || !long.TryParse(args[0], out var forUserId) || !int.TryParse(args[1], out var amount))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("pay.usage"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var r = await service.PayAsync(userId, forUserId, amount, ctx.Ct);
        if (r == null)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("pay.not_found"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var diff = amount >= 0 ? $"+{amount}" : amount.ToString();
        await ctx.Bot.SendMessage(msg.Chat.Id,
            string.Format(Loc("pay.result"), r.DisplayName, forUserId, r.OldCoins, diff, r.NewCoins),
            replyParameters: reply, cancellationToken: ctx.Ct);
    }

    private async Task HandleGetUserAsync(UpdateContext ctx, Message msg, string[] args)
    {
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        if (args.Length == 0 || !long.TryParse(args[0], out var forUserId))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("getuser.usage"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var r = await service.GetUserAsync(forUserId, ctx.Ct);
        var json = r != null
            ? JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true })
            : "null";
        await ctx.Bot.SendMessage(msg.Chat.Id, json, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    private async Task HandleRenameAsync(UpdateContext ctx, Message msg, long userId)
    {
        if (!_opts.Admins.Contains(userId)) return;

        var parts = msg.Text!.Split(' ', 3);
        if (parts.Length < 3)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("rename.usage"), cancellationToken: ctx.Ct);
            return;
        }

        var r = await service.RenameAsync(parts[1], parts[2], ctx.Ct);
        var text = r.Op switch
        {
            RenameOp.Cleared => string.Format(Loc("rename.cleared"), r.OldName),
            RenameOp.NoChange => string.Format(Loc("rename.nochange"), r.OldName),
            _ => string.Format(Loc("rename.set"), r.OldName, r.NewName),
        };
        await ctx.Bot.SendMessage(msg.Chat.Id, text, cancellationToken: ctx.Ct);
    }

    private static string StripFirst(string str)
    {
        var parts = str.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1].Trim() : "";
    }

    private string Loc(string key) => localizer.Get("admin", key);
}
