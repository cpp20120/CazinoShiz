using System.Threading.Channels;

namespace Games.PixelBattle;

public sealed class PixelBattleBroadcaster
{
    private readonly Lock _gate = new();
    private readonly List<Channel<PixelBattleUpdate>> _subscribers = [];

    public PixelBattleSubscription Subscribe()
    {
        var channel = Channel.CreateUnbounded<PixelBattleUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        return new PixelBattleSubscription(channel.Reader, () => Unsubscribe(channel));
    }

    public void Broadcast(PixelBattleUpdate update)
    {
        Channel<PixelBattleUpdate>[] subscribers;
        lock (_gate)
        {
            subscribers = [.. _subscribers];
        }

        foreach (var subscriber in subscribers)
        {
            if (!subscriber.Writer.TryWrite(update))
                Unsubscribe(subscriber);
        }
    }

    private void Unsubscribe(Channel<PixelBattleUpdate> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }
}

public sealed class PixelBattleSubscription(
    ChannelReader<PixelBattleUpdate> reader,
    Action unsubscribe) : IDisposable
{
    public ChannelReader<PixelBattleUpdate> Reader { get; } = reader;

    public void Dispose() => unsubscribe();
}
