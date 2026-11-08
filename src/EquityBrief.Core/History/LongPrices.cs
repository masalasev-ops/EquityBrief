using EquityBrief.Core.Providers;

namespace EquityBrief.Core.History;

// One fund's quarter end over its holdings' spans: its holdings and their value, and those whose codes hold a close on enough
// of the sessions of their spans.
public sealed record LongSpanCoverage(string Index, DateOnly Period, int Holdings, int Priced, decimal Value, decimal PricedValue)
{
    public double HoldingsShare => Holdings == 0 ? 0 : 1.0 * Priced / Holdings;

    public decimal ValueShare => Value == 0m ? 0m : PricedValue / Value;

    public bool Stands => HoldingsShare >= LongFloors.HoldingsStop && ValueShare >= LongFloors.ValueStop;
}

// One of IVV's quarter ends from 2012 against the provider's dates: the codes the provider's spans hold on it, the companies
// they come to once two codes carrying one series are counted once, the holdings the fund filed, and the codes counted once.
public sealed record LongSeriesCount(DateOnly Period, int Codes, int Companies, int Fund, IReadOnlyList<string> CountedOnce)
{
    public int Apart => Companies - Fund;
}

// What the long history's prices are read from and held to, and what is read from them: every code the membership matched and
// every code the pull holds, asked for its prices from 2004-01-02 so the first session of 2005 reads a year behind it, its splits
// and dividends; the earnings calendar's months before the pull's first; each holding priced again over every session of its
// span; the S&P 500's provider dates from 2012 counted against IVV's own holdings, the cutover moving to IVV's last schedule
// where any quarter end stands more than 5 apart; and a departed company's end read from its filer's filings, those no filing
// settles read to their last session with volume and listed for the operator.
// see: The long history's prices are asked once a code from 2004-01-02 and kept apart from the bars, the pull's tables and every night
// see: The S&P 500 before 2012 is read from IVV's filed holdings and from 2012 from the provider's dates
// see: The S&P 500's provider dates from 2012 are held against IVV's holdings and read from IVV's last schedule where they stand more than 5 apart
// see: A stock's history ends by rule on its company's last regular session read from its Form 25 or the 8-K completing its merger
// see: A departed company's end no filing settles reads its last session with volume and goes to the operator on one list
public static class LongPrices
{
    public static DateOnly From { get; } = new(2004, 1, 2);

    // The first month the pull holds a surprise: the calendar's prints are asked for each month before it, the pull holding
    // those after. The pull's own calendar starts in 2015-10 with the days alone, and its surprises in 2018-01.
    // see: The long history's earnings calendar runs to the first month the pull holds a surprise
    public static DateOnly PrintsBefore { get; } = new(2018, 1, 1);

    // The requests a code costs: its prices, its splits and its dividends, each one weighted call.
    public const int RequestsACode = 3;

    // The seconds a request takes, its pause and its answer, measured on 23.3's asks of single codes, for the statement of time.
    public const double SecondsARequest = 1.2;

    // A holding is priced over its span where its code holds a close on at least this share of the span's sessions, counted
    // from its first to the earlier of its leave and its last close.
    public const double SpanPricedShare = 0.98;

    // Two codes carry one series where their closes stand at the cent on at least this share of at least this many sessions
    // both hold.
    public const double OneSeriesShare = 0.95;

    public const int OneSeriesSessions = 20;

    // The most IVV's holdings and the provider's companies may stand apart at a quarter end from 2012 before the S&P 500 reads
    // IVV's schedules to their last.
    public const int CountMargin = 5;

    // The day the S&P 500's provider dates take over where IVV's schedules are read to their last, the day after it.
    public static DateOnly FallbackFrom { get; } = new(2018, 10, 1);

    // A code whose last close falls this many days or more before the newest session any code holds, or one the provider files
    // as an older code, has departed and its end is read.
    public const int DepartedDays = 10;

    public const string ProviderPrint = "provider";

    // From 23.5 a report day read off a filer's 8-K carrying item 2.02, its results announced, for a gate reading a date
    // alone: the day it was filed, its timing unstated and no figure.
    // see: A report day the provider's calendar does not hold is read off the filer's 8-K carrying item 2.02, for a gate reading a date alone
    public const string FilingPrint = "8-K item 2.02";

    public const string ResultsItem = "2.02";

    // Whether a filing of the long list announces its results: an 8-K whose items carry 2.02. An amendment is not read, since
    // it is filed on a later day than the announcement it amends.
    public static bool AnnouncesResults(string form, string? items) =>
        form == "8-K" && items is not null
        && items.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(ResultsItem, StringComparer.Ordinal);

    public static int Weighted(int codes, int companiesAsked, int months, int series) =>
        (codes * RequestsACode * ProviderWeights.HistoricalPerTicker) + (companiesAsked * ProviderWeights.Fundamentals) + (months * ProviderWeights.EarningsCalendar) + series;

    public static int Requests(int codes, int companiesAsked, int months, int series) => (codes * RequestsACode) + companiesAsked + months + series;

    public static TimeSpan Time(int requests) => TimeSpan.FromSeconds(requests * SecondsARequest);

    // The statement printed before the first request: what is asked, its weight and its time.
    public static string Stated(int codes, int companiesAsked, int months, int series) =>
        FormattableString.Invariant($"{codes:N0} code(s), each its prices from {From:yyyy-MM-dd}, splits and dividends; {companiesAsked:N0} company record(s) for a filer; {months:N0} month(s) of the earnings calendar; {series:N0} market series: {Requests(codes, companiesAsked, months, series):N0} request(s), about {Weighted(codes, companiesAsked, months, series):N0} weighted call(s), about {Time(Requests(codes, companiesAsked, months, series)).TotalHours:0.0} hour(s)");

    // The calendar's months from the first to the month before the pull's first, each its first and last day.
    public static IReadOnlyList<(DateOnly First, DateOnly Last)> Months()
    {
        var months = new List<(DateOnly, DateOnly)>();

        for (var first = new DateOnly(From.Year, From.Month, 1); first < PrintsBefore; first = first.AddMonths(1))
        {
            months.Add((first, first.AddMonths(1).AddDays(-1)));
        }

        return months;
    }

    // Whether a code's closes reach enough of a span's sessions, the span read from its start to the earlier of its leave and the
    // code's last close; none priced that holds no close at all or whose closes stop before the span starts.
    public static bool PricedOver(IReadOnlyList<DateOnly> sessions, IReadOnlySet<DateOnly> held, DateOnly joined, DateOnly? left, DateOnly? lastClose)
    {
        if (lastClose is not { } last || last < joined)
        {
            return false;
        }

        var (inSpan, closed) = (0, 0);

        foreach (var session in sessions)
        {
            if (session < joined || left is { } leave && session >= leave || session > last)
            {
                continue;
            }

            inSpan++;

            if (held.Contains(session))
            {
                closed++;
            }
        }

        return inSpan == 0 ? held.Contains(last) : closed >= SpanPricedShare * inSpan;
    }

    // Each fund's quarter ends against the stop over its holdings' spans: a holding priced where its code is priced over the span
    // holding the quarter end, the fund's own index's first, and over the quarter before it where no span holds it.
    public static IReadOnlyList<LongSpanCoverage> Coverage(
        IReadOnlyList<(string Index, DateOnly Period, string? Ticker, decimal Value)> holdings,
        IReadOnlyList<LongSpan> spans,
        IReadOnlyList<DateOnly> sessions,
        Func<string, IReadOnlySet<DateOnly>> held,
        Func<string, DateOnly?> lastClose)
    {
        var byCode = spans.GroupBy(span => span.Ticker, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var read = new Dictionary<(string Ticker, DateOnly Joined, DateOnly? Left), bool>();

        bool Priced(string index, DateOnly period, string ticker)
        {
            var span = byCode.TryGetValue(ticker, out var own)
                ? own.Where(one => one.Joined <= period && !(one.Left <= period)).OrderBy(one => one.Index == index ? 0 : 1).FirstOrDefault()
                : null;
            var key = span is null ? (ticker, period.AddDays(-91), (DateOnly?)period.AddDays(1)) : (ticker, span.Joined, span.Left);

            if (!read.TryGetValue(key, out var priced))
            {
                read[key] = priced = PricedOver(sessions, held(ticker), key.Item2, key.Item3, lastClose(ticker));
            }

            return priced;
        }

        return
        [
            .. holdings.GroupBy(holding => (holding.Index, holding.Period)).OrderBy(group => group.Key.Index, StringComparer.Ordinal).ThenBy(group => group.Key.Period).Select(group =>
            {
                var (count, value) = (0, 0m);

                foreach (var holding in group)
                {
                    if (holding.Ticker is { } ticker && Priced(group.Key.Index, group.Key.Period, ticker))
                    {
                        count++;
                        value += holding.Value;
                    }
                }

                return new LongSpanCoverage(group.Key.Index, group.Key.Period, group.Count(), count, group.Sum(holding => holding.Value), value);
            }),
        ];
    }

    static string Stop(LongSpanCoverage quarter) => FormattableString.Invariant($"{quarter.Index} {quarter.Period:yyyy-MM-dd}: {quarter.Priced} of {quarter.Holdings} holdings priced over their spans, {quarter.HoldingsShare:P1} and {quarter.ValueShare:P1} of their value, under the stop of {LongFloors.HoldingsStop:P0} of either");

    public static IReadOnlyList<string> Stops(IReadOnlyList<LongSpanCoverage> coverage) => [.. coverage.Where(quarter => !quarter.Stands).Select(Stop)];

    // Whether two codes carry one series over the sessions both hold in a window.
    public static bool OneSeries(IReadOnlyDictionary<DateOnly, decimal> first, IReadOnlyDictionary<DateOnly, decimal> second, DateOnly from, DateOnly through)
    {
        var (both, atTheCent) = (0, 0);

        foreach (var (day, close) in first)
        {
            if (day < from || day > through || !second.TryGetValue(day, out var other))
            {
                continue;
            }

            both++;

            if (Math.Abs(close - other) <= 0.005m)
            {
                atTheCent++;
            }
        }

        return both >= OneSeriesSessions && atTheCent >= OneSeriesShare * both;
    }

    // The provider's S&P 500 at each of IVV's quarter ends from 2012: the codes its spans hold, counted once where two carry one
    // series over the quarter before it, against the holdings the fund filed there.
    public static IReadOnlyList<LongSeriesCount> Counted(
        IReadOnlyList<ProviderSpan> provider,
        IReadOnlyDictionary<DateOnly, int> fund,
        Func<string, IReadOnlyDictionary<DateOnly, decimal>> closes)
    {
        var counts = new List<LongSeriesCount>();

        foreach (var (period, filed) in fund.Where(pair => pair.Key >= LongSpans.ProviderFrom).OrderBy(pair => pair.Key))
        {
            string[] codes = [.. provider.Where(span => !(span.Joined > period) && !(span.Left <= period)).Select(span => span.Ticker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            var first = codes.ToDictionary(code => code, code => code, StringComparer.Ordinal);
            var once = new List<string>();

            string Root(string code) => first[code] == code ? code : first[code] = Root(first[code]);

            for (var at = 0; at < codes.Length; at++)
            {
                for (var next = at + 1; next < codes.Length; next++)
                {
                    if (Root(codes[at]) != Root(codes[next]) && OneSeries(closes(codes[at]), closes(codes[next]), period.AddDays(-91), period))
                    {
                        first[Root(codes[next])] = Root(codes[at]);
                        once.Add(codes[at] + " and " + codes[next]);
                    }
                }
            }

            counts.Add(new LongSeriesCount(period, codes.Length, codes.Select(Root).Distinct(StringComparer.Ordinal).Count(), filed, once));
        }

        return counts;
    }

    // The day the S&P 500 reads the provider's dates from: 2012 where every quarter end stands within the margin, else the day
    // after IVV's last schedule.
    public static DateOnly Cutover(IReadOnlyList<LongSeriesCount> counts) =>
        counts.Any(count => Math.Abs(count.Apart) > CountMargin) ? FallbackFrom : LongSpans.ProviderFrom;

    // Whether a filer is the holding's company: its name or a name it filed under before, read by the name key, is a name the fund
    // filed the holding under or the provider files the code under.
    public static bool IsTheCompany(IEnumerable<string> filerNames, IEnumerable<string> names)
    {
        var keys = names.SelectMany(name => new[] { HoldingMatcher.NameKey(name), HoldingMatcher.NameKey(LongMatching.InOrder(name)) }).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);

        return filerNames.Any(name => keys.Contains(HoldingMatcher.NameKey(name)) || keys.Contains(HoldingMatcher.NameKey(LongMatching.InOrder(name))));
    }

    // Whether a code has departed: an older code, or one whose last close stands more than the stated days before the newest.
    public static bool Departed(string code, DateOnly lastClose, DateOnly newest) =>
        code.EndsWith("_old", StringComparison.Ordinal) || newest.DayNumber - lastClose.DayNumber >= DepartedDays;
}
