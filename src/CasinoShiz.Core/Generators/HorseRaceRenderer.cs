using SkiaSharp;

namespace CasinoShiz.Generators;

public static class HorseRaceRenderer
{
    private const int Width = 500;
    private const int IterCount = 100;
    private const int YPadding = 50;
    private const int StartX = 30;
    private const int StartY = 30;
    private const int Radius = 10;
    private const int MenuWidth = 140;
    private static readonly float Modifier = (Width - 2 * StartX - MenuWidth) / (float)IterCount;

    private static readonly string[] Colors =
    [
        "#f87171", "#fb923c", "#fbbf24", "#facc15",
        "#a3e635", "#4ade80", "#059669", "#2dd4bf",
        "#22d3ee", "#818cf8", "#c084fc", "#e879f9",
        "#ec4899", "#fb7185"
    ];

    private static SKColor GetColor(int i)
    {
        var idx = i % 2 != 0 ? i : -i;
        idx = ((idx % Colors.Length) + Colors.Length) % Colors.Length;
        return SKColor.Parse(Colors[idx]);
    }

    public static (byte[][] buffers, int height, int width) DrawHorses(double[][] series)
    {
        var horsesCount = series.Length;
        var height = 2 * StartY + (horsesCount - 1) * YPadding;

        var maxFrames = series.Max(s => s.Length) + 10;

        var horses = new HorseState[horsesCount];
        for (var i = 0; i < horsesCount; i++)
            horses[i] = new HorseState(StartX, StartY + YPadding * i, GetColor(i));

        var buffers = new byte[maxFrames][];
        var currentPlace = 1;
        using var trackPaint = new SKPaint();
        trackPaint.Color = new SKColor(40, 40, 40);
        trackPaint.StrokeWidth = 1;
        trackPaint.IsAntialias = true;

        using var monoTypeface = SKTypeface.FromFamilyName("monospace");
        using var numFont = new SKFont(monoTypeface, 14);
        using var pctFont = new SKFont(monoTypeface, 16);
        using var boldFont = new SKFont(monoTypeface, 16);
        boldFont.Embolden = true;

        for (var frameId = 0; frameId < maxFrames; frameId++)
        {
            using var surface = SKSurface.Create(new SKImageInfo(Width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            for (var horseId = 0; horseId < horsesCount; horseId++)
            {
                var horse = horses[horseId];
                var y = horse.Y;

                // Draw track line
                canvas.DrawLine(StartX, y, Width - MenuWidth - StartX, y, trackPaint);

                // Draw progress line
                using var progressPaint = new SKPaint();
                progressPaint.Color = horse.Color;
                progressPaint.StrokeWidth = 2;
                progressPaint.IsAntialias = true;
                canvas.DrawLine(StartX, y, horse.X, y, progressPaint);

                // Draw horse circle
                using var circlePaint = new SKPaint();
                circlePaint.Color = horse.Color;
                circlePaint.IsAntialias = true;
                canvas.DrawCircle(horse.X, y, Radius, circlePaint);

                // Draw horse number
                using var numPaint = new SKPaint();
                numPaint.Color = SKColors.White;
                numPaint.IsAntialias = true;
                canvas.DrawText($"{horseId + 1}", horse.X - 4, y + 5, SKTextAlign.Left, numFont, numPaint);

                // Draw percentage
                var distToRender = Math.Min(horse.Distance, 100).ToString("F1");
                using var pctPaint = new SKPaint();
                pctPaint.Color = horse.Color;
                pctPaint.IsAntialias = true;
                canvas.DrawText($"{distToRender}%", Width - MenuWidth - StartX + 30, y + 5, SKTextAlign.Left, pctFont, pctPaint);

                // Add velocity
                var velocity = frameId < series[horseId].Length ? series[horseId][frameId] : 0;
                if (velocity > 0)
                    horse.Add(velocity, Modifier);

                // Check finish
                if (horse.Distance >= 100 && horse.Place == 0)
                    horse.Place = currentPlace++;

                // Draw place badge
                if (horse.Place > 0)
                {
                    var shade = (byte)Math.Min((horse.Place - 1) * 15, 170);
                    using var placePaint = new SKPaint();
                    placePaint.Color = new SKColor(shade, shade, shade);
                    placePaint.IsAntialias = true;
                    canvas.DrawText(horse.Place.ToString(), Width - StartX - 12, y + 5, SKTextAlign.Left, boldFont, placePaint);
                }
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 80);
            buffers[frameId] = data.ToArray();
        }

        return (buffers, height, Width);
    }

    private sealed class HorseState(float x, float y, SKColor color)
    {
        public float X { get; set; } = x;
        public float Y { get; } = y;
        public SKColor Color { get; } = color;
        public double Distance { get; private set; }
        public int Place { get; set; }

        public void Add(double velocity, float mod)
        {
            Distance += velocity;
            X += (float)(velocity * mod);
        }
    }
}
