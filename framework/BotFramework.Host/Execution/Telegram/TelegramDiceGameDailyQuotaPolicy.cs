using BotFramework.Host.Composition.Builder;
using BotFramework.Host.Configuration.RuntimeTuning;
using Microsoft.Extensions.Options;

namespace BotFramework.Host.Execution.Telegram;

/// <summary>
/// Telegram-specific adapter for the existing native-dice quota configuration.
/// Games use <see cref="IGameDailyQuotaPolicy"/> and never depend on this adapter.
/// </summary>
public sealed class TelegramDiceGameDailyQuotaPolicy(
    IRuntimeTuningAccessor tuning,
    IOptions<BotFrameworkOptions> botOptions)
    : IGameDailyQuotaPolicy
{
    public IReadOnlyList<QuotaIdentity> Create(GameDailyQuotaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = tuning.TelegramDiceDailyLimit;
        var isPrivateChatAdmin = request.UserId == request.BalanceScopeId
            && botOptions.Value.Admins.Contains(request.UserId);
        var limit = isPrivateChatAdmin ? 0 : options.GetMaxRollsPerUserPerDay(request.GameId);
        if (limit <= 0 && !request.IncludeWhenUnlimited)
            return [];

        var localDate = DateOnly.FromDateTime(request.UtcNow.AddHours(options.TimezoneOffsetHours).DateTime);
        return [new(request.QuotaId, request.GameId, request.UserId, request.BalanceScopeId, localDate, limit)];
    }
}
