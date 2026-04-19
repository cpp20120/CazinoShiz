using System.Security.Cryptography;

namespace Games.Horse.Generators;

public static class SpeedGenerator
{
    private const double BaseVelocity = 0.3;
    private const double MaxDeviation = 0.3 * BaseVelocity;
    private const double Distance = 100;

    public static int GenPlaces(int horseCount)
    {
        var bytes = new byte[1];
        RandomNumberGenerator.Fill(bytes);
        return bytes[0] % horseCount;
    }

    public static double[][] CreateSpeeds(int horsesCount, int winnerPosition)
    {
        var serieses = new double[horsesCount][];
        for (var i = 0; i < horsesCount; i++)
            serieses[i] = CreateDataSeries(Distance, BaseVelocity, MaxDeviation);

        var actualWinnerId = 0;
        var minLen = serieses[0].Length;
        for (var i = 1; i < horsesCount; i++)
        {
            if (serieses[i].Length < minLen)
            {
                minLen = serieses[i].Length;
                actualWinnerId = i;
            }
        }

        (serieses[actualWinnerId], serieses[winnerPosition]) = (serieses[winnerPosition], serieses[actualWinnerId]);
        return serieses;
    }

    private static double[] CreateDataSeries(double distance, double velocity, double deviation)
    {
        var prevSpeed = velocity;
        var sum = 0.0;
        var series = new List<double> { velocity };

        while (sum < distance)
        {
            var raw = prevSpeed + (CryptoRandom() - 0.5) * 2 * deviation;
            var clamped = Math.Min(velocity + deviation, Math.Max(velocity - deviation, raw));
            var newSpeed = Math.Round(clamped, 3);
            sum += newSpeed;
            series.Add(newSpeed);
            prevSpeed = newSpeed;
        }

        return [.. series];
    }

    private static double CryptoRandom()
    {
        var bytes = new byte[2];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt16(bytes) / 65535.0;
    }
}
