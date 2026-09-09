using System.Globalization;

namespace EquityBrief.Core.Prices;

// The shape every price this system writes has.
//
// Four places at most, with trailing zeros removed. Both halves matter and for
// different reasons. The places are the precision the provider's own prices
// carry, so anything longer is arithmetic noise wearing the shape of a price.
// The trimming is because storage is text and decimal carries its scale: the
// same number rounded from two different expressions would otherwise be written
// "327.5740" and "327.574" and compared as different strings, which matters when
// the column is part of a primary key.
//
// This was ProviderBarReader's private helper until 3.4, when the level builder
// needed the same rule for a retracement and a moving average. Two
// implementations of one fact is the defect this corpus refuses everywhere else,
// so it is one implementation with the reasoning attached.
public static class PriceForm
{
    public const int Places = 4;

    public static decimal Round(decimal value) => Round(value, Places);

    public static decimal Round(decimal value, int places) =>
        Trim(decimal.Round(value, places, MidpointRounding.ToEven), places);

    static decimal Trim(decimal value, int places) =>
        decimal.Parse(
            value.ToString("0." + new string('#', places), CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
}
