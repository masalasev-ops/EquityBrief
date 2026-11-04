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

    // A length of time in one form on every page that states one, its whole minutes and whole seconds: 1892.4 seconds is
    // 31 min 32 s, and 45 seconds 0 min 45 s.
    public static string Duration(double seconds)
    {
        var whole = (long)Math.Floor(Math.Max(seconds, 0));

        return (whole / 60).ToString("#,##0", Invariant) + " min " + (whole % 60).ToString(Invariant) + " s";
    }

    // 0.749753 is 75.0%.
    public static string Percent(decimal fraction) => (fraction * 100).ToString("#,##0.0", Invariant) + "%";

    // A share that is a statistic rather than a stored figure, such as a reason's share of the
    // index, read as a percentage to one place: 0.0159 is 1.6%, and -0.0003 is 0.0% by the format's third section, since a
    // double rounding to nothing keeps its sign where a decimal does not.
    public static string Share(double fraction) => (fraction * 100).ToString("#,##0.0;-#,##0.0;0.0", Invariant) + "%";

    // An amount of money in its scale, after the currency's sign, or its code where it has none
    // here: 96221000000 is $96.2B.
    public static string Money(decimal value, string? currency = null) =>
        (value < 0 ? "-" : string.Empty)
        + (currency is null or "" or "USD" ? "$" : currency + " ")
        + Scaled(Math.Abs(value));

    // The one scale a table's amounts are all read in: billions where every amount is a billion or more, so $2.0B stands
    // beside $3.5B; millions where the largest is a million or more, so $1,239.7M stands beside $446.1M rather than $1.2B
    // and $209.3M beside $0.04M; thousands otherwise; and none where no amount is given.
    // see: A segment table draws every amount in one scale, billions where every amount is a billion or more and millions or thousands by its largest amount
    public static (decimal Size, string Suffix)? ScaleOf(IEnumerable<decimal> amounts)
    {
        var given = amounts.Select(Math.Abs).Where(amount => amount > 0).ToArray();

        return given.Length == 0
            ? null
            : given.Min() >= Billion
                ? (Billion, "B")
                : given.Max() >= Million ? (Million, "M") : (Thousand, "K");
    }

    const decimal Billion = 1_000_000_000m;

    const decimal Million = 1_000_000m;

    const decimal Thousand = 1_000m;

    // An amount of money in a scale given, after the currency's sign, its thousands marked and one place kept: 1239700000
    // in millions is $1,239.7M; an amount one place would read under a tenth of the scale kept to two, 44000 in millions
    // $0.04M, and one two places would read as none said to be under a hundredth, 4000 in millions under $0.01M; read in
    // its own scale where none is given.
    public static string MoneyIn(decimal value, (decimal Size, string Suffix)? scale, string? currency = null)
    {
        if (scale is not { } unit)
        {
            return Money(value, currency);
        }

        var sign = currency is null or "" or "USD" ? "$" : currency + " ";
        var read = Math.Abs(value) / unit.Size;

        if (value != 0 && Math.Round(read, 2, MidpointRounding.AwayFromZero) == 0)
        {
            return (value < 0 ? "a loss under " : "under ") + sign + "0.01" + unit.Suffix;
        }

        var places = value != 0 && Math.Round(read, 1, MidpointRounding.AwayFromZero) < 0.1m ? 2 : 1;

        return (value < 0 ? "-" : string.Empty)
            + sign
            + Math.Round(read, places, MidpointRounding.AwayFromZero).ToString(places == 2 ? "#,##0.00" : "#,##0.0", Invariant) + unit.Suffix;
    }

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
    // places, or to two at a thousand or more, read as a price is read: to two places with its
    // thousands marked, so 3046.47 reads 3,046.47 as the plan beside it does.
    public static string InSentence(string text) =>
        Long().Replace(text, number => Price(decimal.Parse(number.Value, Invariant)));

    // A sentence's band whose two edges are one price as it is read, named at that price: "inside 217.78 to 217.78" is
    // "inside the band at 217.78", and "inside 5,797.67 to 5,797.67" is "inside the band at 5,797.67".
    public static string OneBandAtItsPrice(string sentence) =>
        Regex.Replace(sentence, @"\binside (\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?) to \1(?![\d.]|,\d)", "inside the band at $1");

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

    [GeneratedRegex(@"(?<![\d.])(?:\d+\.\d{3,}|\d{4,}\.\d{2})(?![\d.])")]
    private static partial Regex Long();
}
