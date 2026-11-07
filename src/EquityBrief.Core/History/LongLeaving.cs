namespace EquityBrief.Core.History;

// Why a company the long history leaves out left its fund, read off its own SEC filer's filings around its last quarter end
// in that fund, and the filing that says so.
public sealed record LongLeft(string As, string? Accession, DateOnly? Filed, string Read);

// Each year's left-out value of one fund, by why the companies left, against the value of everything the fund held that year.
public sealed record LongLeftYear(string Index, int Year, decimal Value, IReadOnlyDictionary<string, decimal> LeftOut)
{
    public decimal Share(string why) => Value == 0m ? 0m : LeftOut.GetValueOrDefault(why) / Value;
}

// The left-out holdings split by why they left, since companies bought out while healthy leaving makes a result read slightly
// worse and failures leaving makes it read better. A company the operator named failed or bought out stands as named, one
// named unpriced is read as any other is; any other is read off its
// filer's filings from a year before its last quarter end in the fund to two years after it: an 8-K carrying item 1.03 is a
// failure; a merger proxy, a tender offer, or an 8-K carrying item 5.01 is a buy-out; an annual report filed more than 15
// months after it is a company that went on and was dropped; anything else, a filer not found among them, is unknown.
// see: The holdings the long history leaves out are split by why they left with their share of value each year
public static class LongLeaving
{
    public const string BoughtOut = LongFloors.BoughtOut;

    public const string Failed = LongFloors.Failed;

    public const string Dropped = "dropped";

    public const string Unknown = "unknown";

    public static IReadOnlyList<string> Kinds { get; } = [BoughtOut, Failed, Dropped, Unknown];

    public const int MonthsBefore = 12;

    public const int MonthsAfter = 24;

    public const int WentOnAfterMonths = 15;

    static readonly string[] BuyOutForms = ["DEFM14A", "PREM14A", "DEFM14C", "PREM14C", "SC 14D9", "SC TO-T", "SC 13E3"];

    // Why a company left: the operator's naming where it named one, else its filer's filings around its last quarter end.
    public static LongLeft Of(string name, IReadOnlyList<FilerFiling>? filings, DateOnly last)
    {
        if (LongFloors.LeftOutAs(name) is { NamedAs: not LongFloors.Unpriced } named)
        {
            return new LongLeft(named.NamedAs, null, null, "named by the operator: " + named.Found);
        }

        if (filings is null)
        {
            return new LongLeft(Unknown, null, null, "no filer of the name found");
        }

        var around = filings.Where(filing => filing.Filed >= last.AddMonths(-MonthsBefore) && filing.Filed <= last.AddMonths(MonthsAfter)).OrderBy(filing => filing.Filed).ToArray();

        if (around.FirstOrDefault(filing => filing.Form.StartsWith("8-K", StringComparison.Ordinal) && filing.Items.Contains("1.03")) is { } failure)
        {
            return new LongLeft(Failed, failure.Accession, failure.Filed, "an 8-K carrying item 1.03, bankruptcy or receivership");
        }

        if (around.FirstOrDefault(filing => BuyOutForms.Contains(filing.Form) || (filing.Form.StartsWith("8-K", StringComparison.Ordinal) && filing.Items.Contains("5.01"))) is { } buyOut)
        {
            return new LongLeft(BoughtOut, buyOut.Accession, buyOut.Filed, buyOut.Form.StartsWith("8-K", StringComparison.Ordinal) ? "an 8-K carrying item 5.01, a change in control" : "a " + buyOut.Form);
        }

        if (filings.FirstOrDefault(filing => filing.Form is "10-K" or "10-K405" && filing.Filed > last.AddMonths(WentOnAfterMonths)) is { } wentOn)
        {
            return new LongLeft(Dropped, wentOn.Accession, wentOn.Filed, "an annual report filed more than 15 months after, the company going on");
        }

        return new LongLeft(Unknown, null, null, "none of the filings around its last quarter end says why");
    }

    // Each fund's left-out value by year and why, against the value of everything it held at the year's quarter ends.
    public static IReadOnlyList<LongLeftYear> ByYear(IReadOnlyList<LongSchedule> schedules, Func<LongSchedule, int, bool> matched, Func<string, string, LongLeft> why) =>
    [
        .. schedules
            .GroupBy(schedule => (schedule.Index, schedule.Period.Year))
            .OrderBy(group => group.Key.Index, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Year)
            .Select(group =>
            {
                var left = new Dictionary<string, decimal>(StringComparer.Ordinal);

                foreach (var schedule in group)
                {
                    for (var place = 0; place < schedule.Holdings.Count; place++)
                    {
                        if (!matched(schedule, place))
                        {
                            var kind = why(schedule.Index, schedule.Holdings[place].Name).As;

                            left[kind] = left.GetValueOrDefault(kind) + (schedule.Holdings[place].Value ?? 0m);
                        }
                    }
                }

                return new LongLeftYear(group.Key.Index, group.Key.Year, group.Sum(schedule => schedule.Holdings.Sum(holding => holding.Value ?? 0m)), left);
            }),
    ];
}
