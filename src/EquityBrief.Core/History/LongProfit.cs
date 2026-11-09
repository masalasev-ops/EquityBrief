using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;

namespace EquityBrief.Core.History;

// The profit check over the long history: each filer's quarters of net income, operating income and interest expense read off
// the SEC's facts as first filed, the long facts before 2015 and the standing facts after, a fiscal fourth quarter the year
// less its first nine months; and the first year the check is read on an index, the first whose first session sees 90 in
// every 100 of its members read the check off the quarters filed before it, as 23.5 read the score's first year. The years
// before it are not checked, and say so.
// see: The profit check over the long history reads the long facts from the first year they cover 90 in every 100 members, the years before it unchecked
public static class LongProfit
{
    // The share of an index's members whose quarters must read the check for its year to be checked: 90 in every 100.
    public const int CoveredPerHundred = 90;

    // A filer's quarters as the profit gate reads them, one a period end, each measure as first filed.
    public static IReadOnlyList<FiledIncome> Quarters(IEnumerable<FiledFactRow> facts)
    {
        FiledRevenue[] read = [.. facts.Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))];

        IReadOnlyDictionary<DateOnly, QuarterRevenue> ByEnd(IReadOnlyList<string> order) =>
            FirstFiledRevenue.Quarters(read, order).GroupBy(quarter => quarter.End).ToDictionary(group => group.Key, group => group.OrderBy(quarter => quarter.Filed).First());

        var net = ByEnd(FiledFacts.NetIncome);
        var operating = ByEnd(FiledFacts.OperatingIncome);
        var interest = ByEnd(FiledFacts.InterestExpense);

        return
        [
            .. net.Values
                .OrderBy(quarter => quarter.End)
                .Select(quarter => new FiledIncome(
                    quarter.End,
                    quarter.Filed,
                    quarter.Value,
                    operating.TryGetValue(quarter.End, out var held) ? held.Value : null,
                    interest.TryGetValue(quarter.End, out var paid) ? paid.Value : null)),
        ];
    }

    // Whether a member's quarters read the check on a session: four quarters filed before it, each stating its net income.
    public static bool Reads(IEnumerable<FiledIncome> quarters, DateOnly session)
    {
        var read = MemberReadings.FiledBefore(quarters, session);

        return read.Count == MemberReadings.Quarters && read.All(quarter => quarter.NetIncome is not null);
    }

    // The first year whose share of members reading the check reaches 90 in every 100, none where no year does.
    public static int? FirstCheckedYear(IEnumerable<(int Year, int Members, int Read)> years) =>
        years
            .Where(one => one.Members > 0 && one.Read * 100 >= CoveredPerHundred * one.Members)
            .OrderBy(one => one.Year)
            .Select(one => (int?)one.Year)
            .FirstOrDefault();
}
