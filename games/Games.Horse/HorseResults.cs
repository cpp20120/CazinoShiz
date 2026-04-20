namespace Games.Horse;

public enum HorseError
{
    None = 0,
    InvalidHorseId,
    AmountNotSpecified,
    InvalidAmount,
    NotAdmin,
    NotEnoughBets,
}

public sealed record BetResult(HorseError Error, int HorseId, int Amount, int RemainingCoins);

public sealed record RaceInfo(int BetsCount, Dictionary<int, double> Koefs);

public sealed record RacerSummary(long UserId, int TotalBet, int Payout);

public sealed record RaceOutcome(
    HorseError Error,
    int Winner,
    byte[] GifBytes,
    List<(long UserId, int Amount)> Transactions,
    List<RacerSummary> Participants);

public sealed record TodayRaceResult(int? Winner, string? FileId);

public static class HorseResultHelpers
{
    public static BetResult BetFail(HorseError e, int horseId = 0, int coins = 0) => new(e, horseId, 0, coins);
    public static RaceOutcome RaceFail(HorseError e) => new(e, -1, [], [], []);
}
