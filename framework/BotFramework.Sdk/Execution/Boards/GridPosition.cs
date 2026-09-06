using System.Runtime.InteropServices;

namespace BotFramework.Sdk.Execution.Boards;

/// <summary>Zero-based row/column position. Bounds are validated by a grid or board.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GridPosition(int Row, int Column)
{
    public GridPosition Offset(int rowOffset, int columnOffset) =>
        new(checked(Row + rowOffset), checked(Column + columnOffset));

    public GridPosition Move(GridDirection direction) => direction switch
    {
        GridDirection.North => Offset(-1, 0),
        GridDirection.NorthEast => Offset(-1, 1),
        GridDirection.East => Offset(0, 1),
        GridDirection.SouthEast => Offset(1, 1),
        GridDirection.South => Offset(1, 0),
        GridDirection.SouthWest => Offset(1, -1),
        GridDirection.West => Offset(0, -1),
        GridDirection.NorthWest => Offset(-1, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public long ManhattanDistanceTo(GridPosition other) =>
        Math.Abs((long)Row - other.Row) + Math.Abs((long)Column - other.Column);

    public bool IsOrthogonallyAdjacentTo(GridPosition other) => ManhattanDistanceTo(other) == 1;

    public bool IsAdjacentTo(GridPosition other) =>
        this != other
        && Math.Abs((long)Row - other.Row) <= 1
        && Math.Abs((long)Column - other.Column) <= 1;
}
