namespace BotFramework.Sdk.Execution.Boards;

/// <summary>One uniquely identified game-defined piece placed on a grid board.</summary>
public sealed record BoardPiece<TPieceId, TValue>
    where TPieceId : notnull
    where TValue : notnull
{
    public BoardPiece(TPieceId id, TValue value, GridPosition position)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(value);
        Id = id;
        Value = value;
        Position = position;
    }

    public TPieceId Id { get; }

    public TValue Value { get; }

    public GridPosition Position { get; }
}
