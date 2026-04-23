using System.Threading.Channels;

namespace Games.Darts;

public sealed class DartsRollQueue : IDartsRollQueue
{
    private readonly Channel<DartsRollJob> _channel = Channel.CreateUnbounded<DartsRollJob>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public void Enqueue(in DartsRollJob job)
    {
        if (!_channel.Writer.TryWrite(job))
            throw new InvalidOperationException("darts roll queue is completed");
    }

    public ValueTask<DartsRollJob> ReadAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
