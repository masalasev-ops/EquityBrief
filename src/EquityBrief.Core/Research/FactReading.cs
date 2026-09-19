using System.Globalization;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Indicators;

namespace EquityBrief.Core.Research;

// A fact as the key under each figure is handed it: named as a reader reads it, and its
// value rounded as a reader reads it, money and counts in millions, billions or trillions
// with the word, a growth or a margin as a percentage, and anything else to two decimal
// places.
//
// Code rounds rather than the model because the model did not: over the names whose key
// the checker refused on 2026-09-18 it cut digits off where it meant to round, 271.9963
// written as 271.99, and where it did not round it copied six decimal places onto the
// page. A reading is kept only where the claim checker matches it to the stored value, so
// a figure copied from here is one the checker accepts, and a reading it would not match
// is handed as stored.
// see: Code owns every number
// see: The key under each figure is handed its facts as a reader reads them, rounded by code
public static class FactReading
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    static readonly (decimal Size, string Word)[] Scales =
    [
        (1_000_000_000_000m, "trillion"),
        (1_000_000_000m, "billion"),
        (1_000_000m, "million"),
    ];

    public static Fact Read(Fact fact) =>
        fact with { Name = ReadName(fact.Name), Value = Checked(fact, Value(fact.Name, fact.Value)) };

    // Each indicator as a reader reads it, the window written as days before it, which
    // is the form the claim checker reads a window in and the form the name page labels
    // the averages with. A model handed `rsi14` wrote "RSI-14", and the checker read the
    // 14 as a figure nothing held.
    public static string ReadName(string name) => name switch
    {
        IndicatorSeries.Sma20 => "20-day average",
        IndicatorSeries.Sma50 => "50-day average",
        IndicatorSeries.Sma200 => "200-day average",
        IndicatorSeries.Rsi14 => "14-day relative strength index",
        IndicatorSeries.Macd => "MACD",
        IndicatorSeries.MacdSignal => "MACD signal line",
        IndicatorSeries.MacdHist => "MACD histogram",
        IndicatorSeries.Atr14 => "14-day average true range",
        IndicatorSeries.VolAvg20 => "20-day average volume",
        IndicatorSeries.VolAvg50 => "50-day average volume",
        _ => name,
    };

    // A growth and a margin are stored as fractions, and a reader reads both as a
    // percentage, which is also what the instructions ask a margin to be written as.
    static bool IsAFraction(string name) =>
        name.Contains("growth", StringComparison.Ordinal) || name.Contains("margin", StringComparison.Ordinal);

    static string Value(string name, string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant, out var number))
        {
            return value;
        }

        if (IsAFraction(name))
        {
            return Math.Round(number * 100m, 1, MidpointRounding.AwayFromZero).ToString("0.0", Invariant) + " per cent";
        }

        var size = Math.Abs(number);

        foreach (var (scale, word) in Scales)
        {
            if (size >= scale)
            {
                return (number < 0 ? "-" : string.Empty)
                    + Math.Round(size / scale, 2, MidpointRounding.AwayFromZero).ToString("0.##", Invariant) + " " + word;
            }
        }

        var point = value.IndexOf('.', StringComparison.Ordinal);

        return point >= 0 && value.Length - point - 1 > 2
            ? Math.Round(number, 2, MidpointRounding.AwayFromZero).ToString("0.00", Invariant)
            : value;
    }

    static string Checked(Fact fact, string reading) =>
        reading == fact.Value || (ClaimRules.Figures(reading) is [var figure] && ClaimRules.Matches(figure, [fact]))
            ? reading
            : fact.Value;
}
