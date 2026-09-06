using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution.Boards;

/// <summary>
/// Immutable rectangular board with one piece per position. It validates
/// placement and stale/collision-safe movement; game rules still decide which
/// pieces may move, ownership, paths, check and win conditions.
/// </summary>
public sealed class GridBoard<TPieceId, TValue>
    where TPieceId : notnull
    where TValue : notnull
{
    private readonly Dictionary<TPieceId, BoardPiece<TPieceId, TValue>> _piecesById;
    private readonly Dictionary<GridPosition, BoardPiece<TPieceId, TValue>> _piecesByPosition;

    public GridBoard(GridBounds bounds, IEnumerable<BoardPiece<TPieceId, TValue>> pieces)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(pieces);
        var copy = pieces.ToArray();
        if (copy.Any(static piece => piece is null))
            throw new ArgumentException("A board cannot contain null pieces.", nameof(pieces));
        if (copy.Any(piece => !bounds.Contains(piece.Position)))
            throw new ArgumentException("Every board piece must be inside board bounds.", nameof(pieces));

        if (copy.Select(piece => piece.Id).Distinct().Count() != copy.Length)
            throw new ArgumentException("A board cannot contain the same piece id twice.", nameof(pieces));
        if (copy.Select(piece => piece.Position).Distinct().Count() != copy.Length)
            throw new ArgumentException("A board cannot contain more than one piece at a position.", nameof(pieces));

        _piecesById = copy.ToDictionary(piece => piece.Id);
        _piecesByPosition = copy.ToDictionary(piece => piece.Position);

        Bounds = bounds;
        Pieces = new ReadOnlyCollection<BoardPiece<TPieceId, TValue>>(copy);
    }

    public GridBounds Bounds { get; }

    public IReadOnlyList<BoardPiece<TPieceId, TValue>> Pieces { get; }

    public bool TryGetPiece(TPieceId pieceId, out BoardPiece<TPieceId, TValue>? piece)
    {
        ArgumentNullException.ThrowIfNull(pieceId);
        return _piecesById.TryGetValue(pieceId, out piece);
    }

    public bool TryGetPieceAt(GridPosition position, out BoardPiece<TPieceId, TValue>? piece) =>
        _piecesByPosition.TryGetValue(position, out piece);

    public GridBoard<TPieceId, TValue> WithPiece(BoardPiece<TPieceId, TValue> piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (!Bounds.Contains(piece.Position))
            throw new ArgumentOutOfRangeException(nameof(piece), "A board piece must be inside board bounds.");
        if (_piecesById.ContainsKey(piece.Id))
            throw new InvalidOperationException($"Board already contains piece '{piece.Id}'.");
        if (_piecesByPosition.ContainsKey(piece.Position))
            throw new InvalidOperationException($"Board position '{piece.Position}' is already occupied.");

        return new GridBoard<TPieceId, TValue>(Bounds, [.. Pieces, piece]);
    }

    public GridBoard<TPieceId, TValue> WithoutPiece(TPieceId pieceId)
    {
        ArgumentNullException.ThrowIfNull(pieceId);
        if (!_piecesById.ContainsKey(pieceId))
            throw new KeyNotFoundException($"Board does not contain piece '{pieceId}'.");

        return new GridBoard<TPieceId, TValue>(
            Bounds,
            Pieces.Where(piece => !EqualityComparer<TPieceId>.Default.Equals(piece.Id, pieceId)));
    }

    public BoardMoveResult<TPieceId, TValue> TryMove(
        BoardMove<TPieceId> move,
        BoardMoveMode mode = BoardMoveMode.RequireEmptyDestination)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!_piecesById.TryGetValue(move.PieceId, out var piece))
            return new(BoardMoveStatus.PieceNotFound, this);
        if (piece.Position != move.From)
            return new(BoardMoveStatus.SourceMismatch, this);
        if (!Bounds.Contains(move.To))
            return new(BoardMoveStatus.DestinationOutsideBoard, this);

        _piecesByPosition.TryGetValue(move.To, out var captured);
        if (captured is not null && mode == BoardMoveMode.RequireEmptyDestination)
            return new(BoardMoveStatus.DestinationOccupied, this);

        var moved = new BoardPiece<TPieceId, TValue>(piece.Id, piece.Value, move.To);
        var updated = Pieces
            .Where(candidate => !EqualityComparer<TPieceId>.Default.Equals(candidate.Id, piece.Id)
                && (captured is null || !EqualityComparer<TPieceId>.Default.Equals(candidate.Id, captured.Id)))
            .Append(moved);
        return new(BoardMoveStatus.Applied, new GridBoard<TPieceId, TValue>(Bounds, updated), captured);
    }
}
