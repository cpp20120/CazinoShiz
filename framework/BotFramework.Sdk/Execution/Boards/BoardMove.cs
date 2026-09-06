namespace BotFramework.Sdk.Execution.Boards;

/// <summary>
/// A requested move with the expected source position. Including it makes stale
/// player input detectable when a board has already changed.
/// </summary>
public sealed record BoardMove<TPieceId>
    where TPieceId : notnull
{
    public BoardMove(TPieceId pieceId, GridPosition from, GridPosition to)
    {
        ArgumentNullException.ThrowIfNull(pieceId);
        if (from == to)
            throw new ArgumentException("A board move must change position.", nameof(to));

        PieceId = pieceId;
        From = from;
        To = to;
    }

    public TPieceId PieceId { get; }

    public GridPosition From { get; }

    public GridPosition To { get; }
}
