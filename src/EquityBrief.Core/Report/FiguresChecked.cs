using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Report;

// One line of the latest quarter two sources state differently: the line, the quarter's end, the filing's figure with the
// concept it was filed under and the day it was first filed, and the fetch's figure the page draws.
public sealed record FigureConflict(string Line, DateOnly PeriodEnd, decimal Filed, string Concept, DateOnly FiledOn, decimal Fetched);

// The latest quarter's lines checked against the SEC's facts as first filed: revenue, gross profit, operating income, net
// income and cash from operations, each the filing's quarter read by the revenue rule over the line's own concepts, a
// fiscal fourth quarter the year less its first nine months, beside the provider's fetch of the same quarter. Two figures
// agree where they differ by no more than half a million dollars or a tenth of a per cent of the larger, which is a
// rounding; a line they state beyond it is listed, the filing first. The page draws the fetch's figure in every region,
// since every quarter a reading reads shares one fetch's basis and a filing's concept is not always the company's total.
// see: A figure the filing and the provider's fetch state differently beyond rounding is listed under the sources with the filing first, and the page draws the fetch's
public static class FiguresChecked
{
    // The days before the night whose periods are read: a year and a quarter, which holds a fiscal fourth quarter's year and
    // its first nine months.
    public const int DaysRead = 400;

    public const decimal RoundingDollars = 500_000m;

    public const decimal RoundingShare = 0.001m;

    // The lines stated differently, and how many lines the filing and the fetch both state, which is what tells a check that
    // found every line agreeing from one that found none to compare.
    public static (IReadOnlyList<FigureConflict> Conflicts, int Compared) Of(IReadOnlyList<FiledFactRow> filed, QuarterRow? latest)
    {
        if (latest is null || filed.Count == 0)
        {
            return ([], 0);
        }

        var known = filed
            .Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))
            .ToArray();

        var conflicts = new List<FigureConflict>();
        var compared = 0;

        foreach (var (line, concepts, fetched) in new (string, IReadOnlyList<string>, decimal?)[]
        {
            ("Revenue", FiledFacts.Revenue, latest.Revenue),
            ("Gross profit", FiledFacts.GrossProfit, latest.GrossProfit),
            ("Operating income", FiledFacts.OperatingIncome, latest.OperatingIncome),
            ("Net income", FiledFacts.NetIncome, latest.NetIncome),
            ("Cash from operations", FiledFacts.OperatingCash, latest.OperatingCashFlow),
        })
        {
            if (fetched is not { } fetch
                || FirstFiledRevenue.Quarters(known, concepts).LastOrDefault(quarter => Math.Abs(quarter.End.DayNumber - latest.PeriodEnd.DayNumber) <= SameQuarterWithin) is not { } quarter)
            {
                continue;
            }

            compared++;

            if (!Agree(quarter.Value, fetch))
            {
                conflicts.Add(new FigureConflict(line, latest.PeriodEnd, quarter.Value, quarter.Concept, quarter.Filed, fetch));
            }
        }

        return (conflicts, compared);
    }

    // Days a filed quarter's end may sit from the fetch's period end and be the same quarter: a fiscal quarter ending on a
    // weekday near the month's end.
    public const int SameQuarterWithin = 7;

    public static bool Agree(decimal filed, decimal fetched) =>
        Math.Abs(filed - fetched) <= Math.Max(RoundingDollars, RoundingShare * Math.Max(Math.Abs(filed), Math.Abs(fetched)));
}
