namespace BotFramework.Host.Services.Random;

public sealed class Mulberry32(int seed)
{
    private int _seed = seed;

    public double Next()
    {
        unchecked
        {
            _seed |= 0;
            _seed = (_seed + 0x6D2B79F5) | 0;
            var t = Imul(_seed ^ (_seed >>> 15), 1 | _seed);
            t = (t + Imul(t ^ (t >>> 7), 61 | t)) ^ t;
            return ((uint)(t ^ (t >>> 14))) / 4294967296.0;
        }
    }

    private static int Imul(int a, int b)
    {
        unchecked
        {
            return (int)((long)a * b);
        }
    }

    public static long UuidToSeed(string uuid)
    {
        var hex = uuid.Replace("-", "")[..12];
        return long.Parse(hex, System.Globalization.NumberStyles.HexNumber);
    }
}
