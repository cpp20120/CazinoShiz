using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host.Pipeline;

public sealed partial class UpdateDeduplicationMiddleware(
    INpgsqlConnectionFactory connections,
    ILogger<UpdateDeduplicationMiddleware> logger) : IUpdateMiddleware
{
    public async Task InvokeAsync(UpdateContext ctx, UpdateDelegate next)
    {
        var updateId = ctx.Update.Id;
        if (updateId == 0)
        {
            await next(ctx);
            return;
        }

        if (!await TryBeginAsync(updateId, ctx.Ct))
        {
            LogDuplicate(updateId);
            return;
        }

        try
        {
            await next(ctx);
            await MarkCompletedAsync(updateId, ctx.Ct);
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(updateId, ex, ctx.Ct);
            throw;
        }
    }

    private async Task<bool> TryBeginAsync(long updateId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM processed_updates
            WHERE update_id = @updateId
              AND status = 'processing'
              AND started_at < now() - interval '10 minutes'
            """,
            new { updateId },
            cancellationToken: ct));

        var inserted = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            INSERT INTO processed_updates (update_id, status)
            VALUES (@updateId, 'processing')
            ON CONFLICT (update_id) DO NOTHING
            RETURNING 1
            """,
            new { updateId },
            cancellationToken: ct));
        return inserted == 1;
    }

    private async Task MarkCompletedAsync(long updateId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE processed_updates
            SET status = 'completed', completed_at = now(), error = NULL
            WHERE update_id = @updateId
            """,
            new { updateId },
            cancellationToken: ct));
    }

    private async Task MarkFailedAsync(long updateId, Exception ex, CancellationToken ct)
    {
        try
        {
            await using var conn = await connections.OpenAsync(CancellationToken.None);
            await conn.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM processed_updates
                WHERE update_id = @updateId AND status = 'processing'
                """,
                new { updateId },
                cancellationToken: CancellationToken.None));
        }
        catch (Exception cleanupEx)
        {
            LogCleanupFailed(updateId, ex.GetType().Name, cleanupEx);
        }
    }

    [LoggerMessage(EventId = 1600, Level = LogLevel.Information, Message = "update.dedup duplicate update_id={UpdateId}")]
    partial void LogDuplicate(long updateId);

    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning, Message = "update.dedup cleanup_failed update_id={UpdateId} original_error={OriginalError}")]
    partial void LogCleanupFailed(long updateId, string originalError, Exception exception);
}
