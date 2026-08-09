using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Execution;
using Games.Horse.Application.Execution;
using Games.Horse.Application.Services;
using Games.Horse.Domain.Configuration;
using Games.Horse.Domain.Rules;
using System.Security.Cryptography;

namespace Games.Horse.Application.Wagering;

public sealed class HorseWagerCommandHandler(
    IOutcomeOnlyGameExecutor<HorsePlaceBetCommand, HorseWagerState, BetResult> place,
    IRuntimeTuningAccessor tuning,
    IIntegrationEventPublisher outcomes)
    : IIntegrationCommandHandler<HorseWagerCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(HorseWagerCommand command, CancellationToken ct)
    {
        try
        {
            var input = JsonSerializer.Deserialize<BetInput>(command.Payload, JsonOptions)
                ?? throw new InvalidOperationException("Horse bet input is required.");
            var options = tuning.GetSection<HorseOptions>(HorseOptions.SectionName);
            var raceDate = string.IsNullOrWhiteSpace(input.RaceDate)
                ? HorseTimeHelper.GetRaceDate(options.TimezoneOffsetHours)
                : input.RaceDate;
            var playerId = ParsePlayer(command.PlayerId);
            var amount = checked((int)command.Stake);
            var result = await place.ExecuteAsync(new GameExecutionEnvelope<HorsePlaceBetCommand>(
                new(playerId, command.DisplayName, command.ChatId, input.HorseId, amount, raceDate,
                    StableGuid(command.BetId), command.CommandId, options.HorseCount, command.BetId)), ct);
            if (result.Error != HorseError.None)
                await PublishRejectedAsync(command, result.Error.ToString(), ct);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or OverflowException)
        {
            await PublishRejectedAsync(command, "invalid_input", ct);
        }
    }

    private Task PublishRejectedAsync(HorseWagerCommand command, string error, CancellationToken ct) =>
        outcomes.PublishAsync(new GameOutcomeDeclared(command.BetId, "horse", command.PlayerId,
            "rejected", JsonSerializer.Serialize(new { payout = 0, error }), command.OccurredAt), ct);

    private static long ParsePlayer(string playerId) =>
        long.TryParse(playerId, out var value) && value > 0
            ? value
            : throw new ArgumentException("Horse wager playerId must be numeric.");

    private static Guid StableGuid(string value) =>
        new(MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"horse:{value}")));

    private sealed record BetInput(int HorseId, string? RaceDate);
}
