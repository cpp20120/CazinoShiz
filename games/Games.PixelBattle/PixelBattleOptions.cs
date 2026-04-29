namespace Games.PixelBattle;

public sealed class PixelBattleOptions
{
    public const string SectionName = "Games:pixelbattle";

    public string WebAppUrl { get; set; } = "";
    public int FullUpdateIntervalMs { get; set; } = 10_000;
    public int MaxInitDataAgeSeconds { get; set; } = 86_400;

    public TimeSpan FullUpdateInterval =>
        TimeSpan.FromMilliseconds(Math.Max(1_000, FullUpdateIntervalMs));

    public TimeSpan MaxInitDataAge =>
        TimeSpan.FromSeconds(Math.Max(60, MaxInitDataAgeSeconds));
}
