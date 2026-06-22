using System.Collections.Concurrent;

namespace BotFramework.Sdk;
public sealed class NullMiniGameRollGateStore : IMiniGameRollGateStore
{
    public static readonly NullMiniGameRollGateStore Instance = new();

    private NullMiniGameRollGateStore() { }

    public Task ExpectBotRollAsync(string gameId, long userId, long chatId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task ClearAsync(string gameId, long userId, long chatId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<bool> ShouldIgnoreUserThrowAsync(string gameId, long userId, long chatId, CancellationToken ct) =>
        Task.FromResult(false);
}
