namespace Games.PixelBattle;

public interface ITelegramWebAppInitDataValidator
{
    bool TryValidate(string? initData, out TelegramWebAppAuth auth);
}
