// ─────────────────────────────────────────────────────────────────────────────
// TaxService — static fee/tax helpers, ported verbatim from
// src/CasinoShiz.Core/Services/TaxService.cs.
//
// Belongs in the framework because multiple games compute the same bank tax
// and gas tax curve; the shape is a product/policy decision, not a game-level
// one.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Host.Services;

public static class TaxService
{
    private const double GasDefault = 0.0285;
    private static readonly double GasModifier = Math.Sqrt(2);

    private static int RoundValue(double x) => (int)Math.Round(x);

    private static double GasFunction(double x) =>
        Math.Max(1, (Math.Pow(x + 1, Math.Log10(x + 1)) - 1) / 39.15);

    public static int GetGasTax(int tradeVolume) =>
        tradeVolume < 10
            ? RoundValue(GasFunction(tradeVolume) * GasModifier)
            : RoundValue(tradeVolume * GasDefault * GasModifier);

    public static int GetBankTax(double bankAmount) => bankAmount switch
    {
        < 70 => -2,
        < 120 => 0,
        _ => RoundValue(Math.Max(4, Math.Min(GetGasTax((int)bankAmount), 25)) / 2.0),
    };
}
