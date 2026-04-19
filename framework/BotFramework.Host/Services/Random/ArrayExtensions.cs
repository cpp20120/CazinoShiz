namespace BotFramework.Host.Services.Random;

public static class ArrayExtensions
{
    public static T[] Shuffle<T>(T[] array, Func<double> getRandom)
    {
        var result = (T[])array.Clone();
        var currentIndex = result.Length;

        while (currentIndex != 0)
        {
            var randomIndex = (int)Math.Floor(getRandom() * currentIndex);
            currentIndex--;
            (result[currentIndex], result[randomIndex]) = (result[randomIndex], result[currentIndex]);
        }

        return result;
    }

    public static T GetRandom<T>(T[] arr, Func<double>? getRandom = null)
    {
        getRandom ??= System.Random.Shared.NextDouble;
        return arr[(int)Math.Floor(getRandom() * arr.Length)];
    }

    public static T[] GetSomeRandom<T>(T[] arr, int count, Func<double> getRandom)
    {
        var shuffled = Shuffle([.. arr], getRandom);
        var start = (int)Math.Floor(getRandom() * (arr.Length - count - 1));
        return shuffled[start..(start + count)];
    }
}
