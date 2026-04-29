namespace Games.PixelBattle;

public static class PixelBattleConstants
{
    public const int Width = 200;
    public const int Height = 160;
    public const string DefaultColor = "#FFFFFF";

    public static readonly string[] Colors =
    [
        "#FFFFFF",
        "#000000",
        "#EF4444",
        "#F97316",
        "#F59E0B",
        "#84CC16",
        "#22C55E",
        "#14B8A6",
        "#06B6D4",
        "#0EA5E9",
        "#3B82F6",
        "#6366F1",
        "#8B5CF6",
        "#A855F7",
        "#D946EF",
        "#EC4899",
        "#F43F5E",
        "#64748B",
        "#78716C",
        "#451A03",
    ];

    public static bool IsValidIndex(int index) => index >= 0 && index < Width * Height;

    public static bool IsValidColor(string? color) =>
        color is not null && Colors.Contains(color, StringComparer.OrdinalIgnoreCase);
}
