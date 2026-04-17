namespace CasinoShiz.Helpers;

public static class NameDecorators
{
    private static readonly (int threshold, string emoji)[] Decorators =
    [
        (0, "💀"),
        (50, "🔪"),
        (100, ""),
        (120, "🏃‍♂️"),
        (200, "🐘"),
        (300, "🪙"),
        (500, "👛"),
        (750, "🍆"),
        (1000, "😎"),
        (1500, "⭐"),
    ];

    private static string GetDecorator(int balance)
    {
        for (var i = 1; i < Decorators.Length; i++)
        {
            if (balance < Decorators[i].threshold)
                return Decorators[i - 1].emoji;
        }
        return Decorators[0].emoji;
    }

    public static string DecorateName(string name, int balance, string? customDecorator = null)
    {
        var decorator = customDecorator ?? GetDecorator(balance);
        return $"{decorator} {name} {decorator}";
    }
}
