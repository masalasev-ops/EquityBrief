namespace EquityBrief.Core.Insiders;

// One stored transaction as the figures read it: its filing's accession, day filed and form, for an amendment the filing
// it was matched to and the day it states that filing was filed, the filing's owners as one insider with their names and
// roles, and the transaction itself with the filing's box.
public sealed record InsiderRow(
    string Accession,
    DateOnly Filed,
    string Form,
    string? Amends,
    DateOnly? OriginalFiled,
    string OwnerKey,
    string Owners,
    bool Director,
    bool Officer,
    string? Title,
    bool TenPercent,
    int Sequence,
    DateOnly Traded,
    string Code,
    decimal Shares,
    decimal? Price,
    bool Acquired,
    bool? Planned,
    bool Direct)
{
    // The dollars the transaction moved, none where it states no price.
    public decimal? Dollars => Price is { } price ? Shares * price : null;
}

// The ledger's four insider readings of a member on a session, in the catalogue's order.
public sealed record InsiderFour(double? NetBuy, double? Buyers, double? OfficerBuy, double? UnplannedSale)
{
    public static InsiderFour None { get; } = new(null, null, null, null);
}

// The spans the store's insider history holds, each a quarter of the SEC's data sets or a day of the archive's index the
// nightly reader read, a weekday the archive posted none for among them.
public sealed record InsiderCoverage(IReadOnlyList<(DateOnly First, DateOnly Last)> Spans)
{
    public static InsiderCoverage None { get; } = new([]);

    // Whether every weekday from the first day to the last is held, the archive taking no filing on a weekend.
    public bool Holds(DateOnly first, DateOnly last)
    {
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (!Spans.Any(span => span.First <= day && day <= span.Last))
            {
                return false;
            }
        }

        return true;
    }
}

// The insiders' figures the ledger's four readings, the report's region and the chart page's group read, each worked
// here once. Open-market buying is code P and open-market selling code S; every other code is kept and shown and counted
// in neither, since the signal Lakonishok and Lee (2001) and Cohen, Malloy and Pomorski (2012) find is in discretionary
// trades in the open market, and awards and withholding are the mechanics of pay. A window is of the days filed, so a
// filing is read from the day the market could read it and never before: only those filed before the session, from the
// ninety days before it.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public static class InsiderReadings
{
    public const string Purchase = "P";

    public const string Sale = "S";

    public const int WindowDays = 90;

    // The day the Rule 10b5-1 box joined Form 4, before which a filing ticks none.
    public static DateOnly PlanBoxFrom { get; } = new(2023, 4, 1);

    // The ledger's four readings of a member on a session from the transactions in effect over the ninety days before it,
    // each none where the history does not hold those days: the purchases' dollars less the sales', over the company's
    // value; the insiders who bought, counted once each; 1 where an officer bought, 0 where none did; and the dollars of
    // the sales whose filing does not tick the box, over the company's value, none where the days reach before the box
    // joined the form. A transaction stating no price adds no dollars and its insider is still counted; `overValue` is the
    // dollars over the company's value as the valuation reading works it, none where it cannot.
    public static InsiderFour Read(IReadOnlyList<InsiderRow> rows, InsiderCoverage coverage, DateOnly session, Func<decimal, double?> overValue)
    {
        var from = session.AddDays(-WindowDays);

        if (!coverage.Holds(from, session.AddDays(-1)))
        {
            return InsiderFour.None;
        }

        var window = InEffect(rows, session).Where(one => one.Day >= from).Select(one => one.Row).ToArray();
        var purchases = window.Where(row => row.Code == Purchase).ToArray();
        var sales = window.Where(row => row.Code == Sale).ToArray();

        return new InsiderFour(
            overValue(Dollars(purchases) - Dollars(sales)),
            purchases.Select(row => row.OwnerKey).Distinct(StringComparer.Ordinal).Count(),
            purchases.Any(row => row.Officer) ? 1 : 0,
            from < PlanBoxFrom ? null : overValue(Dollars(sales.Where(row => row.Planned != true))));
    }

    public static decimal Dollars(IEnumerable<InsiderRow> rows) => rows.Sum(row => row.Dollars ?? 0m);

    // Each transaction read for a session with the day it counts on, from the filings filed before it. An amendment
    // matched to a filing stands in that filing's place and counts on its day, the newest such filed before the session,
    // and an older amendment of it is not read; one matched to none counts on its own day. An amendment carrying no row of
    // the table of shares stores none, so it is never read and the filing it amends stands. The day an amendment counts on
    // is the day it states its original was filed, which it was matched by, so a reader may load only the days it reads.
    public static IReadOnlyList<(DateOnly Day, InsiderRow Row)> InEffect(IReadOnlyList<InsiderRow> rows, DateOnly session)
    {
        var filings = rows
            .Where(row => row.Filed < session)
            .GroupBy(row => row.Accession, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.Sequence).ToArray(), StringComparer.Ordinal);

        var newest = filings.Values
            .Select(filing => filing[0])
            .Where(first => first.Amends is not null)
            .GroupBy(first => first.Amends!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(amendment => amendment.Filed).ThenBy(amendment => amendment.Accession, StringComparer.Ordinal).Last().Accession,
                StringComparer.Ordinal);

        var read = new List<(DateOnly Day, InsiderRow Row)>();

        foreach (var (accession, filing) in filings.OrderBy(pair => pair.Value[0].Filed).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var first = filing[0];

            if (newest.ContainsKey(accession))
            {
                continue;
            }

            if (first.Amends is { } amended)
            {
                if (newest[amended] == accession)
                {
                    read.AddRange(filing.Select(row => (first.OriginalFiled ?? first.Filed, row)));
                }

                continue;
            }

            read.AddRange(filing.Select(row => (first.Filed, row)));
        }

        return read;
    }
}
