using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Challenges.Application.Wagering;

/// <summary>
/// Outcome-only challenge game. The two reservations are owned by the
/// generic multiparty coordinator; this adapter only produces deterministic
/// player facts so a retry cannot roll a different winner.
/// </summary>
public sealed class ChallengeMultiPartyWagerAdapter : IMultiPartyWagerGameAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "challenge";

    public Task<IReadOnlyList<MultiPartyWagerGameOutcome>> ExecuteAsync(
        MultiPartyWagerGameRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.Participants.Count != 2)
            throw new InvalidOperationException("A challenge requires exactly two participants.");

        var input = JsonSerializer.Deserialize<Input>(request.GameInput, JsonOptions) ?? new Input();
        var first = Roll(request.WorkflowId, request.Participants[0].BetId, input.MaxRoll);
        var second = Roll(request.WorkflowId, request.Participants[1].BetId, input.MaxRoll);
        var tie = first == second;
        var winner = first > second ? 0 : 1;
        var evidence = JsonSerializer.Serialize(new { first, second }, JsonOptions);

        IReadOnlyList<MultiPartyWagerGameOutcome> outcomes = request.Participants.Select((x, index) => new MultiPartyWagerGameOutcome(
                x.BetId,
                x.PlayerId,
                tie ? "tie" : index == winner ? "win" : "loss",
                evidence)).ToArray();
        return Task.FromResult(outcomes);
    }

    private static int Roll(string workflowId, string betId, int maxRoll)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{workflowId}:{betId}"));
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return checked((int)(value % (uint)Math.Clamp(maxRoll, 2, 64)) + 1);
    }

    private sealed record Input(int MaxRoll = 6);
}
