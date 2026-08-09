using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Pick.Application.Wagering;

/// <summary>Pool-style Pick draw: one winner receives the settled pot.</summary>
public sealed class PickPoolMultiPartyWagerAdapter : IMultiPartyWagerGameAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "pick-pool";

    public Task<IReadOnlyList<MultiPartyWagerGameOutcome>> ExecuteAsync(
        MultiPartyWagerGameRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.Participants.Count < 2)
            throw new InvalidOperationException("A pick pool requires at least two participants.");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(request.WorkflowId));
        var winner = (int)(BinaryPrimitives.ReadUInt32LittleEndian(bytes) % (uint)request.Participants.Count);
        var evidence = JsonSerializer.Serialize(new { winner = request.Participants[winner].BetId }, JsonOptions);
        IReadOnlyList<MultiPartyWagerGameOutcome> outcomes = request.Participants.Select((x, index) => new MultiPartyWagerGameOutcome(
            x.BetId,
            x.PlayerId,
            index == winner ? "win" : "loss",
            evidence)).ToArray();
        return Task.FromResult(outcomes);
    }
}
