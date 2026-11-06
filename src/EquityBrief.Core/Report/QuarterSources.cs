using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Report;

// Where a quarter's lines are drawn from.
public enum QuarterSource
{
    Filing,
    Release,
    NotOnFile,
}

// The quarters a page draws, each quarter's lines from its source, the source of each by the provider's period end, and the
// provider's fetch as it stored them, which the figures checked and the dividend's safety read.
public sealed record SourcedQuarters(IReadOnlyList<QuarterRow> Quarters, IReadOnlyDictionary<DateOnly, QuarterSource> Sources, IReadOnlyList<QuarterRow> Fetched)
{
    // The day the filing a filed quarter is drawn from was first filed, by the provider's period end.
    public IReadOnlyDictionary<DateOnly, DateOnly> FiledOn { get; init; } = new Dictionary<DateOnly, DateOnly>();

    // The filed quarters whose revenue concept states a part of the company's revenue, which is not drawn.
    public IReadOnlySet<DateOnly> PartRevenue { get; init; } = new HashSet<DateOnly>();

    public QuarterSource? SourceOf(DateOnly quarter) => Sources.TryGetValue(quarter, out var source) ? source : null;

    // Why a line of a quarter reads nothing, by where the quarter is drawn from; none for a quarter not handed.
    public string? AbsentOf(DateOnly quarter, string line) =>
        SourceOf(quarter) is not { } source ? null
        : line == QuarterLines.Revenue && PartRevenue.Contains(quarter) ? QuarterSources.PartOfTheCompany
        : QuarterSources.Absent(source, line);
}

// A quarter's revenue, gross profit, operating income, net income and cash from operations, in this order: the SEC
// filing's facts as first filed where a filing stating the quarter is stored; else, for the quarter its newest results
// release reports, the release's own figures read by fixed line names; else none, the quarter not on file until its
// filing. The provider's fetch keeps the quarters a page draws, their report days and earnings a share against the
// estimate, and its income and cash lines are drawn for no quarter. A filing states a quarter where it states its revenue,
// operating income or net income for a period ending within a month of the provider's label and nearer it than any other
// label of the fetch or the year before; a line the filing does not state reads none, gross profit standing as revenue
// less the cost of sales where only those are filed, cash from operations being the year to date less the year to date a
// quarter before, and free cash flow none, since the filings stored carry no capital spending. A filed revenue at or below
// nought, or below the same quarter's filed net income where that is above nought, is a concept a filer states for a part
// of the company, a bank's fees alone among them, and is drawn for neither the quarter nor its year-earlier column.
// see: A quarter's lines are drawn from its filing where stored and then from its results release's own tables, and are not on file until the filing otherwise
public static class QuarterSources
{
    public const string FromRelease = "from the results release";

    public const string NotOnFile = "not on file until the filing";

    public const string NotInFiling = "not stated in the filing";

    public const string NoCapitalSpending = "not read: the filings stored carry no capital spending";

    public const string PartOfTheCompany = "not read: the revenue the filing states is below its net income, a part of the company's revenue";

    // Days a release may follow its quarter's end and be the report of that quarter.
    public const int ReleaseWithin = 120;

    // Days a filed quarter's end may sit from the provider's label and be that quarter: a fiscal calendar ending its
    // quarters on a weekday weeks from a month's end, a 52 and 53 week year among them.
    public const int LabelWithin = 31;

    public static SourcedQuarters Of(IReadOnlyList<QuarterRow> fetched, IReadOnlyList<FiledFactRow> filed, string? releaseText = null, DateOnly? releasedOn = null)
    {
        var known = filed.Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)).ToArray();
        var revenue = FirstFiledRevenue.Quarters(known, FiledFacts.Revenue);
        var gross = FirstFiledRevenue.Quarters(known, FiledFacts.GrossProfit);
        var costs = FirstFiledRevenue.Quarters(known, FiledFacts.CostOfSales);
        var operating = FirstFiledRevenue.Quarters(known, FiledFacts.OperatingIncome);
        var net = FirstFiledRevenue.Quarters(known, FiledFacts.NetIncome);
        var cash = CashQuarters(known, FiledFacts.OperatingCash);

        // The filed quarter ending nearest a label within a month, where no other label of the fetch, or the label a year
        // before, stands nearer to it.
        var labels = fetched.Select(quarter => quarter.PeriodEnd).SelectMany(end => new[] { end, end.AddYears(-1) }).Distinct().ToArray();

        int Apart(DateOnly one, DateOnly other) => Math.Abs(one.DayNumber - other.DayNumber);

        QuarterRevenue? Near(IReadOnlyList<QuarterRevenue> quarters, DateOnly end) => quarters
            .Where(quarter => Apart(quarter.End, end) <= LabelWithin && labels.All(label => Apart(quarter.End, label) >= Apart(quarter.End, end)))
            .OrderBy(quarter => Apart(quarter.End, end))
            .FirstOrDefault();

        decimal? At(IReadOnlyList<QuarterRevenue> quarters, DateOnly end) => Near(quarters, end)?.Value;

        var parts = new HashSet<DateOnly>();

        // The quarter's filed revenue where it is the company's whole, none where it states a part.
        decimal? Whole(DateOnly end) =>
            At(revenue, end) is { } sales && (sales <= 0m || (At(net, end) is { } earned && earned > 0m && sales < earned)) ? null : At(revenue, end);

        var sources = new Dictionary<DateOnly, QuarterSource>();
        var filedOn = new Dictionary<DateOnly, DateOnly>();
        var drawn = new List<QuarterRow>();
        var newest = fetched.Count > 0 ? fetched.Max(quarter => quarter.PeriodEnd) : DateOnly.MinValue;

        foreach (var quarter in fetched.OrderBy(quarter => quarter.PeriodEnd))
        {
            var end = quarter.PeriodEnd;
            var (stated, income, earned) = (At(revenue, end), At(operating, end), At(net, end));
            var sales = Whole(end);

            if (stated is not null && sales is null)
            {
                parts.Add(end);
            }

            if (stated is not null || income is not null || earned is not null)
            {
                var grossProfit = At(gross, end) ?? (sales is { } whole && At(costs, end) is { } cost ? whole - cost : null);

                sources[end] = QuarterSource.Filing;
                filedOn[end] = new[] { Near(revenue, end), Near(operating, end), Near(net, end) }.OfType<QuarterRevenue>().Min(read => read.Filed);
                drawn.Add(quarter with { Revenue = sales, GrossProfit = grossProfit, OperatingIncome = income, NetIncome = earned, OperatingCashFlow = At(cash, end), FreeCashFlow = null });
                continue;
            }

            var before = end.AddYears(-1);
            var released = end == newest
                && releaseText is not null
                && releasedOn is { } on
                && on > end
                && on.DayNumber - end.DayNumber <= ReleaseWithin
                    ? ReleaseFigures.Read(releaseText, end, new YearEarlierFiled(Whole(before), At(gross, before), At(operating, before), At(net, before)))
                    : null;

            sources[end] = released is null ? QuarterSource.NotOnFile : QuarterSource.Release;
            drawn.Add(quarter with
            {
                Revenue = released?.Revenue,
                GrossProfit = released?.GrossProfit,
                OperatingIncome = released?.OperatingIncome,
                NetIncome = released?.NetIncome,
                OperatingCashFlow = null,
                FreeCashFlow = null,
            });
        }

        return new SourcedQuarters(drawn, sources, fetched) { FiledOn = filedOn, PartRevenue = parts };
    }

    // Why a line of a quarter reads nothing, by where its quarter is drawn from.
    public static string Absent(QuarterSource source, string line) => source switch
    {
        QuarterSource.Filing => line == QuarterLines.FreeCashFlow ? NoCapitalSpending : NotInFiling,
        _ => NotOnFile,
    };

    // Each quarter's cash from operations as first filed: a quarter's own span, else its year to date less the year to
    // date ending a quarter before it under the same start, each as first filed.
    static IReadOnlyList<QuarterRevenue> CashQuarters(IReadOnlyList<FiledRevenue> known, IReadOnlyList<string> order)
    {
        var quarters = FirstFiledRevenue.Quarters(known, order).ToList();
        var spans = known
            .Where(fact => order.Contains(fact.Concept, StringComparer.Ordinal))
            .GroupBy(fact => (fact.Start, fact.End))
            .Select(period => period.OrderBy(fact => fact.Filed).ThenBy(fact => order.ToList().IndexOf(fact.Concept)).First())
            .ToArray();

        foreach (var longer in spans.Where(fact => FirstFiledRevenue.Days(fact.Start, fact.End) > FirstFiledRevenue.QuarterMost))
        {
            if (quarters.Any(quarter => quarter.End == longer.End)
                || spans.Where(shorter => shorter.Start == longer.Start && shorter.End < longer.End
                        && FirstFiledRevenue.Days(shorter.End.AddDays(1), longer.End) is >= FirstFiledRevenue.QuarterFewest and <= FirstFiledRevenue.QuarterMost)
                    .OrderByDescending(shorter => shorter.End)
                    .FirstOrDefault() is not { } shorter)
            {
                continue;
            }

            quarters.Add(new QuarterRevenue(shorter.End.AddDays(1), longer.End, longer.Value - shorter.Value, longer.Filed > shorter.Filed ? longer.Filed : shorter.Filed, longer.Concept, true));
        }

        return [.. quarters.OrderBy(quarter => quarter.End)];
    }
}
