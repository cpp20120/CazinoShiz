namespace Games.Darts;

public interface IDartsRollQueue
{
    void Enqueue(in DartsRollJob job);
    ValueTask<DartsRollJob> ReadAsync(CancellationToken ct);
}
