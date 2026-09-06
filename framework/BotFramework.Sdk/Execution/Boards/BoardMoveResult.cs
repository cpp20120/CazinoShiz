namespace BotFramework.Sdk.Execution.Boards;

/// <summary>Immutable board transition and optional captured piece.</summary>
public sealed record BoardMoveResult<TPieceId, TValue>(
    BoardMoveStatus Status,
    GridBoard<TPieceId, TValue> Board,
    BoardPiece<TPieceId, TValue>? CapturedPiece = null)
    where TPieceId : notnull
    where TValue : notnull
{
    public bool Applied => Status == BoardMoveStatus.Applied;
}
