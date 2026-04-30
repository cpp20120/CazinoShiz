using BotFramework.Host;
using SkiaSharp;

namespace Games.Horse.Generators;

public static class HorseRaceRenderer
{
    private const int Width = 500;
    private const int IterCount = 100;
    private const int YPadding = 50;
    private const int StartX = 30;
    private const int StartY = 30;
    private const int Radius = 10;
    private const int MenuWidth = 140;
    private const int FinishHoldFrames = 90;
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

        var maxFrames = series.Max(s => s.Length) + FinishHoldFrames;

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
        using var placeFont = new SKFont(monoTypeface, 14);
        placeFont.Embolden = true;

        for (var frameId = 0; frameId < maxFrames; frameId++)
        {
            using var surface = SKSurface.Create(new SKImageInfo(Width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            for (var horseId = 0; horseId < horsesCount; horseId++)
            {
                var horse = horses[horseId];
                var y = horse.Y;
                var velocity = frameId < series[horseId].Length ? series[horseId][frameId] : 0;
                if (velocity > 0)
                    horse.Add(velocity, Modifier);

                if (horse.Distance >= 100 && horse.Place == 0)
                    horse.Place = currentPlace++;

                canvas.DrawLine(StartX, y, Width - MenuWidth - StartX, y, trackPaint);

                using var progressPaint = new SKPaint();
                progressPaint.Color = horse.Color;
                progressPaint.StrokeWidth = 2;
                progressPaint.IsAntialias = true;
                canvas.DrawLine(StartX, y, horse.X, y, progressPaint);

                using var circlePaint = new SKPaint();
                circlePaint.Color = horse.Color;
                circlePaint.IsAntialias = true;
                canvas.DrawCircle(horse.X, y, Radius, circlePaint);

                using var numPaint = new SKPaint();
                numPaint.Color = SKColors.White;
                numPaint.IsAntialias = true;
                canvas.DrawText($"{horseId + 1}", horse.X - 4, y + 5, SKTextAlign.Left, numFont, numPaint);

                var distToRender = Math.Min(horse.Distance, 100).ToString("F1");
                using var pctPaint = new SKPaint();
                pctPaint.Color = horse.Color;
                pctPaint.IsAntialias = true;
                canvas.DrawText($"{distToRender}%", Width - MenuWidth - StartX + 30, y + 5, SKTextAlign.Left, pctFont, pctPaint);

                if (horse.Place > 0)
                {
                    var label = FormatPlace(horse.Place);
                    var shade = (byte)Math.Min((horse.Place - 1) * 15, 170);

                    var labelX = Math.Min(horse.X + Radius + 8, Width - StartX - 76);
                    DrawPlaceBadge(canvas, label, labelX, y, placeFont, new SKColor(shade, shade, shade));
                    DrawPlaceText(canvas, label, Width - StartX - 42, y + 5, boldFont, new SKColor(shade, shade, shade));
                }
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 80);
            buffers[frameId] = data.ToArray();
        }

        return (buffers, height, Width);
    }

    private static void DrawPlaceBadge(
        SKCanvas canvas,
        string label,
        float x,
        float y,
        SKFont font,
        SKColor textColor)
    {
        var textWidth = font.MeasureText(label);
        var rect = SKRect.Create(x - 6, y - 28, textWidth + 12, 24);

        using var fillPaint = new SKPaint();
        fillPaint.Color = new SKColor(255, 255, 255, 230);
        fillPaint.IsAntialias = true;
        canvas.DrawRoundRect(rect, 6, 6, fillPaint);

        using var borderPaint = new SKPaint();
        borderPaint.Color = textColor;
        borderPaint.IsAntialias = true;
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.StrokeWidth = 2;
        canvas.DrawRoundRect(rect, 6, 6, borderPaint);

        DrawPlaceText(canvas, label, x, y - 10, font, textColor);
    }

    private static void DrawPlaceText(
        SKCanvas canvas,
        string label,
        float x,
        float y,
        SKFont font,
        SKColor textColor)
    {
        using var outlinePaint = new SKPaint();
        outlinePaint.Color = SKColors.White;
        outlinePaint.IsAntialias = true;
        outlinePaint.Style = SKPaintStyle.Stroke;
        outlinePaint.StrokeWidth = 3;
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, outlinePaint);

        using var textPaint = new SKPaint();
        textPaint.Color = textColor;
        textPaint.IsAntialias = true;
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, textPaint);
    }

    private static string FormatPlace(int place)
    {
        var suffix = (place % 100) is 11 or 12 or 13
            ? "th"
            : (place % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };

        return $"{place}{suffix}";
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
