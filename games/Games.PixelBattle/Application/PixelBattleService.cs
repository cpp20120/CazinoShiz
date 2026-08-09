using System.Security.Cryptography;
using System.Text;
using BotFramework.Host.Execution;
using Games.PixelBattle.Application.Execution;
using Games.PixelBattle.Contracts;

namespace Games.PixelBattle.Application;

public sealed class PixelBattleService(
    IPixelBattleStore store,
    IGameStateExecutor<PixelBattleCommand, PixelBattleExecutionState, PixelUpdateResult> executor)
    : IPixelBattleService, IPixelBattleCommandService
{
    public Task<PixelBattleGrid> GetGridAsync(CancellationToken ct) => store.GetGridAsync(ct);

    public Task<PixelUpdateResult> UpdateAsync(
        long userId, int index, string color, CancellationToken ct) =>
        UpdateAsync(userId, index, color, Guid.NewGuid().ToString("N"), ct);

    public Task<PixelUpdateResult> UpdateAsync(
        long userId, int index, string color, string commandId, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commandId)));
        return executor.ExecuteAsync(new(new PixelBattleCommand(
            userId, index, color, $"pixelbattle:update:{userId}:{hash}")), ct);
    }
}
