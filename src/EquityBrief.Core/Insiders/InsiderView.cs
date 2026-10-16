namespace EquityBrief.Core.Insiders;

// One open-market purchase or sale as the report's region draws it: who, their role, the day traded and the day it counts
// on, the shares, the price and the dollars where a price is stated, and for a sale whether its filing marks it as planned.
public sealed record InsiderTradeLine(
    string Owners,
    string Role,
    DateOnly Traded,
    DateOnly Filed,
    bool Purchase,
    decimal Shares,
    decimal? Price,
    decimal? Dollars,
    bool Planned);

// The open-market totals over a span: the dollars bought and sold, and the insiders who bought and who sold.
public sealed record InsiderTotals(decimal Bought, decimal Sold, int Buyers, int Sellers)
{
    public decimal Net => Bought - Sold;
}

// What the report's Insiders region and the chart page's Insiders group draw for a stock on a night, worked once here:
// the year's open-market purchases and sales newest first, the totals over the ninety days and the year to the night,
// how many transactions of every other kind the year held, which neither total counts, and the newest purchase's day.
// None where the history does not hold the year, which both pages draw as not stored.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public sealed record InsiderView(
    IReadOnlyList<InsiderTradeLine> Lines,
    InsiderTotals Ninety,
    InsiderTotals Year,
    int Awards,
    int Exercises,
    int Withholding,
    int Others,
    DateOnly? NewestPurchase)
{
    public const int YearDays = 365;

    // The codes counted apart on the region's line: awards and grants, exercises and conversions, and the tax withheld.
    public static IReadOnlyList<string> AwardCodes { get; } = ["A"];

    public static IReadOnlyList<string> ExerciseCodes { get; } = ["M", "C", "X", "O"];

    public static IReadOnlyList<string> WithholdingCodes { get; } = ["F"];

    // The view on a night: the filings filed on or before it, as its next session's readings read them.
    public static InsiderView? Of(IReadOnlyList<InsiderRow> rows, InsiderCoverage coverage, DateOnly night)
    {
        // A night no year can stand before or no session follow, the page's own for a stock holding no bar, holds none.
        if (night.DayNumber <= YearDays || night == DateOnly.MaxValue)
        {
            return null;
        }

        var session = night.AddDays(1);
        var yearFrom = session.AddDays(-YearDays);

        if (!coverage.Holds(yearFrom, night))
        {
            return null;
        }

        var year = InsiderReadings.InEffect(rows, session).Where(one => one.Day >= yearFrom).ToArray();
        var ninetyFrom = session.AddDays(-InsiderReadings.WindowDays);

        static InsiderTotals Totals(IEnumerable<InsiderRow> span)
        {
            var held = span.ToArray();
            var purchases = held.Where(row => row.Code == InsiderReadings.Purchase).ToArray();
            var sales = held.Where(row => row.Code == InsiderReadings.Sale).ToArray();

            return new InsiderTotals(
                InsiderReadings.Dollars(purchases),
                InsiderReadings.Dollars(sales),
                purchases.Select(row => row.OwnerKey).Distinct(StringComparer.Ordinal).Count(),
                sales.Select(row => row.OwnerKey).Distinct(StringComparer.Ordinal).Count());
        }

        int Count(IReadOnlyList<string> codes) => year.Count(one => codes.Contains(one.Row.Code, StringComparer.Ordinal));

        var open = year.Where(one => one.Row.Code is InsiderReadings.Purchase or InsiderReadings.Sale).ToArray();
        var counted = AwardCodes.Concat(ExerciseCodes).Concat(WithholdingCodes).Append(InsiderReadings.Purchase).Append(InsiderReadings.Sale).ToHashSet(StringComparer.Ordinal);

        return new InsiderView(
            [
                .. open
                    .OrderByDescending(one => one.Row.Traded)
                    .ThenByDescending(one => one.Day)
                    .ThenBy(one => one.Row.Accession, StringComparer.Ordinal)
                    .ThenBy(one => one.Row.Sequence)
                    .Select(one => new InsiderTradeLine(
                        one.Row.Owners,
                        Role(one.Row),
                        one.Row.Traded,
                        one.Day,
                        one.Row.Code == InsiderReadings.Purchase,
                        one.Row.Shares,
                        one.Row.Price,
                        one.Row.Dollars,
                        one.Row.Code == InsiderReadings.Sale && one.Row.Planned == true)),
            ],
            Totals(year.Where(one => one.Day >= ninetyFrom).Select(one => one.Row)),
            Totals(year.Select(one => one.Row)),
            Count(AwardCodes),
            Count(ExerciseCodes),
            Count(WithholdingCodes),
            year.Count(one => !counted.Contains(one.Row.Code)),
            open.Where(one => one.Row.Code == InsiderReadings.Purchase).Select(one => (DateOnly?)one.Row.Traded).Max());
    }

    // The role as the filing states it: a director, an officer with the title, a ten per cent owner, joined.
    public static string Role(InsiderRow row) => string.Join(", ", new[]
    {
        row.Director ? "director" : null,
        row.Officer ? row.Title ?? "officer" : null,
        row.TenPercent ? "10% owner" : null,
    }.Where(part => part is not null));
}
