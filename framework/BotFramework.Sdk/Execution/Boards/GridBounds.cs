namespace BotFramework.Sdk.Execution.Boards;

/// <summary>Dimensions and valid positions of a zero-based rectangular grid.</summary>
public sealed record GridBounds
{
    public GridBounds(int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        Rows = rows;
        Columns = columns;
    }

    public int Rows { get; }

    public int Columns { get; }

    public bool Contains(GridPosition position) =>
        position.Row >= 0 && position.Row < Rows
        && position.Column >= 0 && position.Column < Columns;

    public IEnumerable<GridPosition> Positions()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                yield return new GridPosition(row, column);
            }
        }
    }
}
