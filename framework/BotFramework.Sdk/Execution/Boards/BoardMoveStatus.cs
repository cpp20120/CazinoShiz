namespace BotFramework.Sdk.Execution.Boards;

/// <summary>Stable outcome of applying a generic board move.</summary>
public enum BoardMoveStatus
{
    Applied,
    PieceNotFound,
    SourceMismatch,
    DestinationOutsideBoard,
    DestinationOccupied,
}
