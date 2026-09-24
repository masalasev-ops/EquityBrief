using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Web.Marks;

// A stored figure as a reader reads it.
//
// Each is the stored value rounded to the places it is read at: a price, a ratio and an amount
// per share to two places, an amount of money and a count of shares in its scale, a fraction as
// a percentage to one place, and a multiple to one place. Nothing is worked out that the store
// does not hold, and the element a figure is drawn in carries the stored value whole, so a check
// reads what was stored rather than what was drawn.
// see: A figure is drawn at the places it is read at, and its element carries the stored value whole
public static partial class Figures
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Price(decimal value) => value.ToString("#,##0.00", Invariant);

    public static string PerShare(decimal value) => value.ToString("#,##0.00", Invariant);

    public static string Ratio(decimal value) => value.ToString("#,##0.00", Invariant);

    public static string Multiple(decimal value) => value.ToString("#,##0.0", Invariant);

    // 0.749753 is 75.0%.
    public static string Percent(decimal fraction) => (fraction * 100).ToString("#,##0.0", Invariant) + "%";

    // A share that is a statistic rather than a stored figure, such as a reason's share of the
    // index, read as a percentage to one place: 0.0159 is 1.6%.
    public static string Share(double fraction) => (fraction * 100).ToString("#,##0.0", Invariant) + "%";

    // An amount of money in its scale, after the currency's sign, or its code where it has none
    // here: 96221000000 is $96.2B.
    public static string Money(decimal value, string? currency = null) =>
        (value < 0 ? "-" : string.Empty)
        + (currency is null or "" or "USD" ? "$" : currency + " ")
        + Scaled(Math.Abs(value));

    // A count of shares in its scale: 190287428 is 190.3M.
    public static string Shares(decimal value) =>
        (value < 0 ? "-" : string.Empty) + Scaled(Math.Abs(value));

    // A stored value from a list of values, as it is read: a number of a million or more in its
    // scale, a number written to more than two places to two, and anything else as stored.
    public static string Read(string stored)
    {
        if (!decimal.TryParse(stored, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant, out var value))
        {
            return stored;
        }

        if (Math.Abs(value) >= 1_000_000m)
        {
            return Shares(value);
        }

        var point = stored.IndexOf('.', StringComparison.Ordinal);

        return point >= 0 && stored.Length - point - 1 > 2 ? Price(value) : stored;
    }

    // A sentence the nightly store wrote, with every number in it written to more than two
    // places read to two, which is the places its prices are read at.
    public static string InSentence(string text) =>
        Long().Replace(text, number => Price(decimal.Parse(number.Value, Invariant)));

    // The scale is the largest at which the figure rounds to at least one, so a figure a shade
    // under a billion reads as $1.0B rather than $1000.0M.
    static string Scaled(decimal value)
    {
        foreach (var (size, suffix, places) in Scales)
        {
            var scaled = Math.Round(value / size, places, MidpointRounding.AwayFromZero);

            if (scaled >= 1)
            {
                return scaled.ToString(places == 2 ? "0.00" : "0.0", Invariant) + suffix;
            }
        }

        return value.ToString("#,##0", Invariant);
    }

    static readonly (decimal Size, string Suffix, int Places)[] Scales =
    [
        (1_000_000_000_000m, "T", 2),
        (1_000_000_000m, "B", 1),
        (1_000_000m, "M", 1),
    ];

    [GeneratedRegex(@"(?<![\d.])\d+\.\d{3,}(?![\d.])")]
    private static partial Regex Long();
}
