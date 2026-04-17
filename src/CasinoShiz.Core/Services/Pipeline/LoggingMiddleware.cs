using System.Diagnostics;

namespace CasinoShiz.Services.Pipeline;

public sealed partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IUpdateMiddleware
{
    public async Task InvokeAsync(UpdateContext ctx, UpdateDelegate next)
    {
        var update = ctx.Update;
        var kind = update switch
        {
            { Message.Text: { } }   => "text",
            { Message.Dice: { } }   => "dice",
            { CallbackQuery: { } }  => "callback",
            { ChannelPost: { } }    => "channel_post",
            { EditedMessage: { } }  => "edited_message",
            { InlineQuery: { } }    => "inline_query",
            _                       => "other",
        };

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["update_id"] = update.Id,
            ["user_id"]   = ctx.UserId,
            ["chat_id"]   = ctx.ChatId,
            ["kind"]      = kind,
        });

        var started = Stopwatch.GetTimestamp();
        LogUpdateIn(kind, ctx.UserId, Truncate(ctx.Text), Truncate(ctx.CallbackData));

        try
        {
            await next(ctx);
            LogUpdateOut(kind, ctx.UserId, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, failed: false);
        }
        catch
        {
            LogUpdateOut(kind, ctx.UserId, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, failed: true);
            throw;
        }
    }

    private static string? Truncate(string? s) => s == null ? null : s.Length <= 80 ? s : s[..80] + "…";

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
        Message = "update.in kind={Kind} user={UserId} text={Text} cb={Cb}")]
    partial void LogUpdateIn(string kind, long userId, string? text, string? cb);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "update.out kind={Kind} user={UserId} duration_ms={Ms} failed={Failed}")]
    partial void LogUpdateOut(string kind, long userId, long ms, bool failed);
}
