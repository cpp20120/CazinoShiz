namespace CasinoShiz.Helpers;

public static class RussianPlural
{
    private static readonly int[] Cases = [2, 0, 1, 1, 1, 2];

    public static string Plural(int number, string[] titles, bool includeNumber = false)
    {
        var absolute = Math.Abs(number);
        var idx = absolute % 100 > 4 && absolute % 100 < 20
            ? 2
            : Cases[absolute % 10 < 5 ? absolute % 10 : 5];
        var text = titles[idx];
        return includeNumber ? $"{number} {text}" : text;
    }
}
