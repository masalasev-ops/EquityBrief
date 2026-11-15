using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Readings;

// Where the rules read a quarter's figures from: its filing as first filed, or its results release's own tables.
public enum QuarterFrom
{
    Filing,
    Release,
}

// One results release as the store keeps it: its text and the instant the SEC accepted it.
public sealed record ReleaseAccepted(string Text, DateTimeOffset Accepted);

// One quarter as the rules read it on a session: the provider's label for it, the document its figures are read from and
// the day they became known, every session after it reading them, its revenue, operating income, net income and cash from operations,
// each none where the document states none, its interest expense, the filing's alone, and the same quarter's revenue and
// operating income a year earlier and its revenue two years earlier as first filed, which its growth and margin read.
public sealed record ReadQuarter(
    DateOnly PeriodEnd,
    QuarterFrom From,
    DateOnly Known,
    decimal? Revenue,
    decimal? OperatingIncome,
    decimal? NetIncome,
    decimal? OperatingCashFlow,
    decimal? InterestExpense,
    decimal? RevenueYearEarlier,
    decimal? OperatingIncomeYearEarlier,
    decimal? RevenueTwoYearsEarlier)
{
    // The quarter as the profit check and the cover read it, read on every session after its known day.
    public FiledIncome Income => new(PeriodEnd, Known, NetIncome, OperatingIncome, InterestExpense);
}

// One quarter of the history and the day it became known, every session after it reading it: from its filing where one
// stating it is stored, else from its results release, else from the later of its report day and the provider's filing
// date, which reads from no document and is counted.
public sealed record HistoryQuarter(DateOnly PeriodEnd, DateOnly Known, QuarterFrom? From);

// A member's quarters as the rules read them on a session, never the provider's figures: each quarter the provider's fetch
// labels is read from its SEC filing as first filed from the session after the day the filing was made, else from its
// results release's own tables from the first night whose late slot starts after the SEC accepted it, as a release reading
// is read, and otherwise not at all. A filing states a quarter where it states its revenue, operating income or net income
// for a period ending within a month of the label and nearer it than any other label or the year before's; a filed revenue
// at or below nought, or below a net income above nought, states a part of the company and is not read. A quarter none of
// whose filings is in reads the newest release the session may read that came after its end and within 120 days of it and
// whose tables read, each row checked against the same quarter a year earlier as filed. The pages read the same rule
// through files of their own, so a change here moves a rule and never a page.
// see: The rules read each quarter from its filing and then its release from the moment the SEC made it public, and the cover from the four newest quarters stating interest
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public static class QuartersAsRead
{
    // Days a filed quarter's end may sit from the provider's label and be that quarter.
    public const int LabelWithin = 31;

    // Days a release may follow its quarter's end and be the report of that quarter.
    public const int ReleaseWithin = 120;

    // The day a release the SEC accepted at the instant given becomes known, every session after it reading it: the night
    // of the day it was accepted reads it where it came before that night's late slot, and the next session's otherwise.
    public static DateOnly KnownFrom(DateTimeOffset accepted)
    {
        var day = DateOnly.FromDateTime(accepted.UtcDateTime);

        return ReleaseAvailability.ReadableOn(accepted, day) ? day.AddDays(-1) : day;
    }

    // The labels no filing the session may read states, which only a release can read.
    public static IReadOnlyList<DateOnly> Unfiled(IEnumerable<DateOnly> labels, IReadOnlyList<FiledFactRow> filed, DateOnly session)
    {
        var ends = labels.Distinct().Order().ToArray();
        var lines = new FiledLines(ends, filed, session);

        return [.. ends.Where(end => lines.FiledOn(end) is null)];
    }

    // The quarters a session reads, newest first: each label from its filing, then its release, and a label neither reads
    // left out.
    public static IReadOnlyList<ReadQuarter> Of(IEnumerable<DateOnly> labels, IReadOnlyList<FiledFactRow> filed, IReadOnlyList<ReleaseAccepted> releases, DateOnly session)
    {
        var ends = labels.Distinct().Order().ToArray();
        var lines = new FiledLines(ends, filed, session);
        var read = new List<ReadQuarter>();

        foreach (var end in ends)
        {
            var (before, twoBefore) = (end.AddYears(-1), end.AddYears(-2));
            var (revenueBefore, operatingBefore, netBefore) = (lines.Whole(before), lines.Operating(before), lines.Net(before));

            if (lines.FiledOn(end) is { } filedOn)
            {
                read.Add(new ReadQuarter(end, QuarterFrom.Filing, filedOn, lines.Whole(end), lines.Operating(end), lines.Net(end), lines.Cash(end), lines.Interest(end), revenueBefore, operatingBefore, lines.Whole(twoBefore)));

                continue;
            }

            var released = releases
                .Where(release => DateOnly.FromDateTime(release.Accepted.UtcDateTime) is var on
                    && on > end
                    && on.DayNumber - end.DayNumber <= ReleaseWithin
                    && ReleaseAvailability.ReadableOn(release.Accepted, session))
                .OrderByDescending(release => release.Accepted)
                .Select(release => (Release: release, Lines: ReleaseLines.Read(release.Text, revenueBefore, operatingBefore, netBefore)))
                .FirstOrDefault(one => one.Lines is not null);

            if (released.Lines is { } stated)
            {
                read.Add(new ReadQuarter(end, QuarterFrom.Release, KnownFrom(released.Release.Accepted), stated.Revenue, stated.OperatingIncome, stated.NetIncome, null, null, revenueBefore, operatingBefore, lines.Whole(twoBefore)));
            }
        }

        return [.. read.OrderByDescending(quarter => quarter.PeriodEnd)];
    }

    // Each quarter of the history with the first day it is read from: the day its first filing stating it was made where
    // one is stored, else the readable night of the first release accepted after its end within 120 days, else the later
    // of its report day, the first print after its end within the same days, and the provider's filing date.
    public static IReadOnlyList<HistoryQuarter> InTheHistory(
        IReadOnlyList<(DateOnly PeriodEnd, DateOnly ProviderFiled)> quarters,
        IReadOnlyList<FiledFactRow> filed,
        IReadOnlyList<DateTimeOffset> accepted,
        IReadOnlyList<DateOnly> prints)
    {
        var lines = new FiledLines([.. quarters.Select(quarter => quarter.PeriodEnd).Distinct().Order()], filed, DateOnly.MaxValue);

        bool After(DateOnly on, DateOnly end) => on > end && on.DayNumber - end.DayNumber <= ReleaseWithin;

        return
        [
            .. quarters.Select(quarter =>
            {
                var end = quarter.PeriodEnd;

                if (lines.FiledOn(end) is { } filedOn)
                {
                    return new HistoryQuarter(end, filedOn, QuarterFrom.Filing);
                }

                if (accepted.Where(at => After(DateOnly.FromDateTime(at.UtcDateTime), end)).Order().Select(at => (DateTimeOffset?)at).FirstOrDefault() is { } first)
                {
                    return new HistoryQuarter(end, KnownFrom(first), QuarterFrom.Release);
                }

                var reported = prints.Where(day => After(day, end)).Order().Select(day => (DateOnly?)day).FirstOrDefault();

                return new HistoryQuarter(end, reported is { } on && on > quarter.ProviderFiled ? on : quarter.ProviderFiled, null);
            }),
        ];
    }

    // A filer's quarters as first filed by a session, each measure's matched to the labels as the pages match them.
    sealed class FiledLines
    {
        readonly DateOnly[] near;
        readonly IReadOnlyList<QuarterRevenue> revenue;
        readonly IReadOnlyList<QuarterRevenue> operating;
        readonly IReadOnlyList<QuarterRevenue> net;
        readonly IReadOnlyList<QuarterRevenue> cash;
        readonly IReadOnlyList<QuarterRevenue> interest;

        public FiledLines(IReadOnlyList<DateOnly> labels, IReadOnlyList<FiledFactRow> filed, DateOnly session)
        {
            FiledRevenue[] known =
            [
                .. filed
                    .Where(fact => fact.Filed < session)
                    .Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)),
            ];

            near = [.. labels.SelectMany(end => new[] { end, end.AddYears(-1) }).Distinct()];
            revenue = FirstFiledRevenue.Quarters(known, FiledFacts.Revenue);
            operating = FirstFiledRevenue.Quarters(known, FiledFacts.OperatingIncome);
            net = FirstFiledRevenue.Quarters(known, FiledFacts.NetIncome);
            interest = FirstFiledRevenue.Quarters(known, FiledFacts.InterestExpense);
            cash = CashQuarters(known, FiledFacts.OperatingCash);
        }

        public decimal? Operating(DateOnly end) => At(operating, end);

        public decimal? Net(DateOnly end) => At(net, end);

        public decimal? Cash(DateOnly end) => At(cash, end);

        public decimal? Interest(DateOnly end) => At(interest, end);

        // The quarter's filed revenue where it is the company's whole, none where it states a part.
        public decimal? Whole(DateOnly end) =>
            At(revenue, end) is { } sales && (sales <= 0m || (At(net, end) is { } earned && earned > 0m && sales < earned)) ? null : At(revenue, end);

        // The day the first filing stating the quarter's revenue, operating income or net income was made, none where no
        // filing read states any.
        public DateOnly? FiledOn(DateOnly end)
        {
            QuarterRevenue[] stating = [.. new[] { Near(revenue, end), Near(operating, end), Near(net, end) }.OfType<QuarterRevenue>()];

            return stating.Length == 0 ? null : stating.Min(quarter => quarter.Filed);
        }

        decimal? At(IReadOnlyList<QuarterRevenue> quarters, DateOnly end) => Near(quarters, end)?.Value;

        static int Apart(DateOnly one, DateOnly other) => Math.Abs(one.DayNumber - other.DayNumber);

        // The filed quarter ending nearest a label within a month, where no other label, or the label a year before, stands
        // nearer to it.
        QuarterRevenue? Near(IReadOnlyList<QuarterRevenue> quarters, DateOnly end) => quarters
            .Where(quarter => Apart(quarter.End, end) <= LabelWithin && near.All(label => Apart(quarter.End, label) >= Apart(quarter.End, end)))
            .OrderBy(quarter => Apart(quarter.End, end))
            .FirstOrDefault();

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
}
