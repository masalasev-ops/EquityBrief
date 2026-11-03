using System.Globalization;

namespace EquityBrief.Core.Ledger;

// One company that re-formed under a new SEC number: the stock, the number it filed under before and the one it files under
// now.
public sealed record FilerSuccession(string Ticker, string Predecessor, string Successor);

// A fact as the store holds it, as much as finding the day a filer's first report starts reads.
public sealed record FiledPeriod(string Accession, string Form, string Filed, string PeriodStart, string PeriodEnd);

// The companies whose facts carry on from a predecessor filer. Each is named from the SEC's own filer records, read on
// 2026-11-03: a successor's 8-K12B or 8-K12G3 or a plain change of form, and the figures both filers state for a period
// before the changeover read against each other. A stock is left off where the old number was never its company's, where
// the figures show another company, as DOW's old filer is a subsidiary filing still, or where it would gain nothing, as AESI.
// see: A company that re-filed under a new SEC number carries its predecessor's facts up to the day its new filer's first report starts
public static class FilerSuccessions
{
    public static IReadOnlyList<FilerSuccession> Stated { get; } =
    [
        new("APA", "0000006769", "0001841666"),
        new("APO", "0001411494", "0001858681"),
        new("BG", "0001144519", "0001996862"),
        new("BLK", "0001364742", "0002012383"),
        new("DINO", "0000048039", "0001915657"),
        new("FERG", "0001832433", "0002011641"),
        new("FUN", "0000811532", "0001999001"),
        new("LQDA", "0001330436", "0001819576"),
        new("MRVL", "0001058057", "0001835632"),
        new("NE", "0001458891", "0001895262"),
        new("NWE", "0000073088", "0001993004"),
        new("PNFP", "0001115055", "0002082866"),
        new("QDEL", "0000353569", "0001906324"),
        new("TPL", "0000097517", "0001811074"),
        new("UNIT", "0001620280", "0002020795"),
        new("VNOM", "0001602065", "0002074176"),
        new("XOM", "0000034088", "0002115436"),
    ];

    static readonly string[] Reports = ["10-Q", "10-K", "20-F"];

    // How long a period of flows a report's own period may be: a quarter's, from about two months, to a year's.
    const int FewestDays = 60;
    const int MostDays = 380;

    // The day the new filer's first report starts: the first report it filed, and within it the newest period of flows, the
    // shortest of those ending that day. A cover page's dates and a balance's instants are no period of flows. None where it
    // has filed no report stating flows.
    public static DateOnly? FirstReportStarts(IEnumerable<FiledPeriod> successor)
    {
        var reports = successor.Where(fact => Reports.Contains(fact.Form, StringComparer.Ordinal)).ToArray();

        if (reports.Length == 0)
        {
            return null;
        }

        var first = reports.OrderBy(fact => fact.Filed, StringComparer.Ordinal).ThenBy(fact => fact.Accession, StringComparer.Ordinal).First().Accession;
        var flows = reports
            .Where(fact => fact.Accession == first)
            .Select(fact => (Start: Day(fact.PeriodStart), End: Day(fact.PeriodEnd)))
            .Where(period => period.End.DayNumber - period.Start.DayNumber is >= FewestDays and <= MostDays)
            .ToArray();

        return flows.Length == 0 ? null : flows.Where(period => period.End == flows.Max(one => one.End)).Max(period => period.Start);
    }

    static DateOnly Day(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
