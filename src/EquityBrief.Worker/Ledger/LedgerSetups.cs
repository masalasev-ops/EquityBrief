using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Insiders;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Ledger;

// One setup as the ledger stores it: its identity, the words of the live rule's setting its pass was read at, whether
// that setting passes it, its anchor and its plan in typical moves, and its readings in the catalogue's order.
public sealed record SetupRow(
    string Index,
    string Family,
    string Ticker,
    DateOnly Session,
    string Rule,
    bool LivePass,
    SetupAnchor Anchor,
    SetupPlan Plan,
    double?[] Readings)
{
    public double? Reading(string column) => Readings[LedgerReadings.IndexOf(column)];
}

// One family's setups on one session of one index, with the members the gates were read over.
public sealed record FamilySetups(string Family, int Members, IReadOnlyList<SetupRow> Rows)
{
    public int LivePasses => Rows.Count(row => row.LivePass);
}

// One sector's size cut on a rebalance as the heavyweights' rule read it: the sector, its largest companies' listings in
// order of value, and the leaders the rule bought among them.
public sealed record HeavyweightCut(string Sector, IReadOnlyList<string> Largest, IReadOnlySet<string> Leaders);

// The market series the readings take, each over its own sessions: the index's own series, or its fund's on the S&P
// 400 and 600, the VIX, SPY, IJH, IJR and HYG, and from 19.12 the eleven GICS sector funds, any of them absent where the
// store holds none.
public sealed record LedgerMarket(IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, double Close)>> Series)
{
    public const string Index = "GSPC";
    public const string Vix = "VIX";
    public const string Large = "SPY";
    public const string Mid = "IJH";
    public const string Small = "IJR";
    public const string Credit = "HYG";

    public static IReadOnlyList<string> Named { get; } = [Index, Vix, Large, Mid, Small, Credit, .. GicsSectors.Funds.Values.Order(StringComparer.Ordinal)];

    public static LedgerMarket None { get; } = new(new Dictionary<string, IReadOnlyList<(DateOnly, double)>>(StringComparer.Ordinal));

    public IReadOnlyList<(DateOnly Session, double Close)>? Of(string name) => Series.TryGetValue(name, out var held) ? held : null;

    // The series an index reads its own level against: the S&P 500 its index, the 400 and 600 their funds.
    public static string IndexSeriesOf(string indexCode) => indexCode switch
    {
        "MID" => Mid,
        "SML" => Small,
        _ => Index,
    };

    // The fund an index's own trend is read on: SPY for the S&P 500, and the 400's and 600's own funds.
    public static string FundOf(string indexCode) => indexCode switch
    {
        "MID" => Mid,
        "SML" => Small,
        _ => Large,
    };
}

// From 19.13 each member's insiders' transactions by ticker and the spans the store's insider history holds, as the
// ledger read them for its four insider readings.
public sealed record LedgerInsiders(IReadOnlyDictionary<string, IReadOnlyList<InsiderRow>> Rows, InsiderCoverage Spans);

// What the ledger reads of one index on one session besides its series: each member's quarters as filed, its sector as
// filed, the members at a new high and a new low on each session, each member's filer's facts as first filed, read for
// a session from those filed before it, and on a night each member's closes before the store's year that its valuation
// reading reaches, from the history pulled for those years at the store's scale, which no other reading reads. The
// business readings and the valuation reading's facts change only when a fact is filed, so they are kept for a member by
// how many of its facts were filed before the session, and a series' closes are joined to those before its year once.
// see: The valuation reading reads the prices before the store's year at the store's scale from the history pulled for those years
public sealed record LedgerContext(
    IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income,
    IReadOnlyDictionary<string, string?> Sectors,
    int[] Highs,
    int[] Lows,
    IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>>? Facts = null,
    IReadOnlyDictionary<string, PriceHistory>? BeforeTheYear = null)
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<(string Ticker, int Filed), BusinessReading> business = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<(string Ticker, int Filed), ValuationParts> valuation = new();
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<SweepSeries, PriceHistory> prices = new();
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<SweepSeries, double?[]> rsiTwo = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<(int Session, string Sector), double?> sectorFalls = new();

    // From 22.2 the S&P 500's breadth on each session, which an S&P 400 or 600 row reads; none where it is read without it.
    // see: The short-term dip's setups clear its drop a grid step past the widest its sweep reads with no trend condition, and the ledger's history budget is raised to hold them
    public IReadOnlyDictionary<DateOnly, double>? LargeBreadth { get; init; }

    // A member's 2-session relative strength index on each of its bars, read once a series through the dip's own function.
    public double?[] RsiTwo(SweepSeries one, double[] closes) => rsiTwo.GetValue(one, _ => DipRule.Rsi(closes));

    // A member's fall over the drop's sessions to a bar in that bar's typical moves, none where it holds too few sessions
    // or no typical move.
    public static double DropFall(SweepSeries one, int bar)
    {
        var move = one.Atr[bar];

        return bar >= LedgerReadings.DropSessions && move > 0
            ? (Statistic.FromPrice(one.Bars[bar - LedgerReadings.DropSessions].Close) - Statistic.FromPrice(one.Bars[bar].Close)) / move
            : double.NaN;
    }

    // The median of that fall over the members of a sector the index held on a session, each in its own typical moves; none
    // where fewer than two of them read one. Read once a session and sector.
    public double? SectorFall(int at, string sector, IReadOnlyList<SweepSeries> all, SweepBenchmark.Members members) =>
        sectorFalls.GetOrAdd((at, sector), key =>
        {
            var falls = new List<double>();

            for (var place = 0; place < members.Names[key.Session].Length; place++)
            {
                var one = all[members.Names[key.Session][place]];

                if (string.Equals(Sectors.GetValueOrDefault(one.Name.Ticker), key.Sector, StringComparison.Ordinal) && DropFall(one, members.Bars[key.Session][place]) is var fall && !double.IsNaN(fall))
                {
                    falls.Add(fall);
                }
            }

            if (falls.Count < 2)
            {
                return null;
            }

            falls.Sort();

            return falls.Count % 2 == 1 ? falls[falls.Count / 2] : (falls[(falls.Count / 2) - 1] + falls[falls.Count / 2]) / 2;
        });

    // The base-10 logarithm of the company's value on a session as the valuation reading works it, none where no count
    // stands before the session or the prices hold no such session.
    public double? CompanyValue(SweepSeries one, DateOnly session)
    {
        var ticker = one.Name.Ticker;
        var facts = Facts?.GetValueOrDefault(ticker) ?? [];
        var filed = facts.Count(fact => fact.Filed < session);
        var parts = valuation.GetOrAdd((ticker, filed), _ => ValuationReading.PartsOf(facts, session));
        var history = prices.GetValue(one, series => (BeforeTheYear?.GetValueOrDefault(ticker) ?? PriceHistory.None).Then(PriceHistory.Of(series.Bars)));

        return CompanyValueOf(parts, history, session);
    }

    // The base-10 logarithm of a company's value on a session from its filings' parts and its prices, none where no count
    // stands before the session or the prices hold no such session.
    public static double? CompanyValueOf(ValuationParts parts, PriceHistory history, DateOnly session) =>
        ValuationReading.OverValue(1m, parts, history, session) is { } perDollar && perDollar > 0 ? -Math.Log10(perDollar) : null;

    // From 19.12 the members' places on each session over the series the readings are read from, which the four of
    // Kakushadze's formulas that rank across the index read; none where the readings are read without them.
    public FactorRanks? Ranks { get; init; }

    // From 19.13 each member's insiders' transactions by ticker, its filer's, and the spans the store's insider history
    // holds, which the four insider readings read from the filings filed before the session; none where the readings are
    // read without them, and the readings none with them. A ticker the store matches no filer for holds no entry.
    // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
    public IReadOnlyDictionary<string, IReadOnlyList<InsiderRow>>? Insiders { get; init; }

    public InsiderCoverage InsiderSpans { get; init; } = InsiderCoverage.None;

    // From 20.8 each member's results releases by ticker, its filer's, with their readings under the newest reading
    // version whose history read is complete, which the four release readings read; none where the readings are read
    // without them, and all four none for a ticker the store matches no filer for or holds no release of.
    // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
    public IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>? Releases { get; init; }

    // A member's four release readings on a session, in the catalogue's order of their columns.
    public IReadOnlyList<double?> ReleasesOf(string ticker, DateOnly session) =>
        LedgerReleases.Readings(Releases?.GetValueOrDefault(ticker) is { } releases ? LedgerReleases.On(releases, session) : null);

    // A member's four insider readings on a session, each dollar figure over the company's value as the valuation
    // reading works it from the same facts and closes; all four none for a member the store matches no filer for, whose
    // filings no reader read, where a filer filing nothing reads nought.
    public InsiderFour InsidersOf(SweepSeries one, DateOnly session)
    {
        var ticker = one.Name.Ticker;

        if (Insiders?.GetValueOrDefault(ticker) is not { } rows)
        {
            return InsiderFour.None;
        }

        var facts = Facts?.GetValueOrDefault(ticker) ?? [];
        var filed = facts.Count(fact => fact.Filed < session);
        var parts = valuation.GetOrAdd((ticker, filed), _ => ValuationReading.PartsOf(facts, session));
        var history = prices.GetValue(one, series => (BeforeTheYear?.GetValueOrDefault(ticker) ?? PriceHistory.None).Then(PriceHistory.Of(series.Bars)));

        return InsiderReadings.Read(rows, InsiderSpans, session, dollars => ValuationReading.OverValue(dollars, parts, history, session));
    }

    public BusinessReading Business(string ticker, DateOnly session)
    {
        var facts = Facts?.GetValueOrDefault(ticker) ?? [];
        var filed = facts.Count(fact => fact.Filed < session);

        return business.GetOrAdd((ticker, filed), _ => FiledFacts.Read(facts, session));
    }

    public double? ValuePlace(SweepSeries one, DateOnly session)
    {
        var ticker = one.Name.Ticker;
        var facts = Facts?.GetValueOrDefault(ticker) ?? [];
        var filed = facts.Count(fact => fact.Filed < session);
        var parts = valuation.GetOrAdd((ticker, filed), _ => ValuationReading.PartsOf(facts, session));

        return ValuationReading.Place(parts, prices.GetValue(one, series => (BeforeTheYear?.GetValueOrDefault(ticker) ?? PriceHistory.None).Then(PriceHistory.Of(series.Bars))), session);
    }
}

// The setups of one index on one session, family by family, through the sweep's own readings of the series: the
// pullback's candidates as the sweep reads them, the breakout's highs and ranges as its sweep reads them, and the
// drift's reaction as its sweep reads it, each held to the family's loose gates and the index's floors, with the live
// rule's own setting read beside it. Every reading is taken from the series to the session and never past it.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public static class LedgerSetups
{
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, FundamentalsRule.Name, DipRule.Name];

    static readonly int LiveHigh = SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh);
    static readonly int LiveAge = SweepCandidate.AgeAt(SweepDesign.Live.Trigger, SweepDesign.Live.Support);
    static readonly int LivePlan = SweepCandidate.PlanAt(SweepDesign.Live.Plan, SweepDesign.Live.Support);
    static readonly byte LiveUptrend = (byte)(1 << (int)SweepDesign.Live.Uptrend);

    // The live pullback setting on an index: the S&P 500's filter as the ideas' run replays it, and the base the 400's
    // and 600's provisional rule reads.
    public static DialSetting LivePullback(string indexCode) => indexCode == IndexFamilies.LargeIndex ? SweepIdeas.Today : SweepIdeas.BaseRule.Setting;

    public static string PullbackWords(string indexCode) => LivePullback(indexCode).Describe(SweepGrid.Extended);

    public static string BreakoutWords { get; } = Words(BreakoutSweep.Grid, IndexNightRead.BreakoutAsFrozen);

    public static string DriftWords { get; } = Words(DriftSweep.Grid, IndexNightRead.DriftAsFrozen);

    static string Words(FamilyGrid grid, int[] setting) =>
        string.Join(", ", grid.Dials.Select((dial, at) => FormattableString.Invariant($"{dial.Dial} {grid.Value(setting, at)}")));

    // The live heavyweights' setting in words, a setting a clause.
    public static string HeavyweightWords { get; } = FormattableString.Invariant(
        $"largest {HeavyweightRule.Live.Largest}, look-back {HeavyweightRule.Live.LookBack}, leaders {HeavyweightRule.Live.Leaders}, ")
        + FormattableString.Invariant($"beta of at least one {HeavyweightRule.Live.HighBeta}, the sector's fund {HeavyweightRule.Live.FundReturn}, ")
        + FormattableString.Invariant($"sold on no longer leading {HeavyweightRule.Live.SoldOnLeading}, sold under the average {HeavyweightRule.Live.SoldUnderAverage}");

    // The heavyweights' setups of a rebalance session: each member of each sector's size cut, bought at the session's
    // close and given the heavyweights' cap, its live pass whether the rule bought it, with the readings every family's
    // setup carries. The rule places no stop, so the anchor's stop is nothing and its path is read by the heavyweights'
    // own replay.
    // see: A heavyweights' setup is each member of its sector's size cut on a rebalance of the S&P 500's book, held as the rule holds a buy
    public static FamilySetups Heavyweights(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context,
        IReadOnlyList<HeavyweightCut> cuts)
    {
        var names = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var name = 0; name < series.Count; name++)
        {
            names.TryAdd(series[name].Name.Ticker, name);
        }

        var rows = new List<SetupRow>();

        foreach (var cut in cuts)
        {
            foreach (var ticker in cut.Largest)
            {
                if (!names.TryGetValue(ticker, out var name))
                {
                    continue;
                }

                var bar = IndexNightRead.BarOf(series[name], at);

                if (bar < 0 || !(closes[name][bar] > 0))
                {
                    continue;
                }

                rows.Add(new SetupRow(
                    indexCode,
                    HeavyweightRule.Name,
                    ticker,
                    calendar[at],
                    HeavyweightWords,
                    cut.Leaders.Contains(ticker),
                    new SetupAnchor(calendar[at], closes[name][bar], 0, null, null, HeavyweightPaths.Cap, null),
                    new SetupPlan(0, null, null, HeavyweightPaths.Cap),
                    Readings(indexCode, name, series[name], closes[name], bar, sessions[at], members, calendar, at, market, context, series)));
            }
        }

        return new FamilySetups(HeavyweightRule.Name, members.Names[at].Length, Ordered(rows));
    }

    public static IReadOnlyList<FamilySetups> On(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var closes = series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();
        var (pullbacks, fundamentals) = Pullbacks(indexCode, series, closes, sessions, members, calendar, at, market, context);

        return
        [
            pullbacks,
            Breakouts(indexCode, series, closes, sessions, members, calendar, at, market, context),
            Drifts(indexCode, series, closes, sessions, members, calendar, at, market, context),
            fundamentals,
            Dips(indexCode, series, closes, sessions, members, calendar, at, market, context),
        ];
    }

    // The short-term dip's setting in words, as its pass is read beside its setups: the provisional setting.
    public static string DipWords { get; } = DipPaths.Plan.Short;

    // The plan a dip setup is stated at in typical moves: the provisional stop, no target and no trail, its time limit the
    // cap; its path is the rule's own walk at the next open and never the swing families' replay.
    public static SetupPlan DipPlan { get; } = new(DipPaths.Plan.StopMoves, null, null, DipPaths.Plan.TimeLimit);

    // The short-term dip's setups: each member clearing the index's floors whose 2-session relative strength index or fall
    // over the drop's sessions clears the dip's loose gates, whatever its trend, bought at its close at the provisional
    // plan; its pass the provisional rule's as the night reads it, through the rule's own reading of the year of closes a
    // night holds to the session, its floors whole, its history, its trend, its drop, its reports and its typical move.
    // see: The short-term dip's setups clear its drop a grid step past the widest its sweep reads with no trend condition, and the ledger's history budget is raised to hold them
    static FamilySetups Dips(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var held = members.Names[at];
        var bars = members.Bars[at];
        var rows = new List<SetupRow>();

        for (var place = 0; place < held.Length; place++)
        {
            var name = held[place];
            var bar = bars[place];
            var one = series[name];

            if (!SetupGates.Dip(context.RsiTwo(one, closes[name])[bar], LedgerContext.DropFall(one, bar))
                || !Clears(indexCode, one, bar)
                || DipPaths.Anchor(calendar[at], closes[name][bar], one.Atr[bar]) is not { } anchor)
            {
                continue;
            }

            var readings = Readings(indexCode, name, one, closes[name], bar, sessions[at], members, calendar, at, market, context, series);

            rows.Add(new SetupRow(indexCode, DipRule.Name, one.Name.Ticker, calendar[at], DipWords, DipPasses(indexCode, one, closes[name], bar, calendar, at), anchor, DipPlan, readings));
        }

        return new FamilySetups(DipRule.Name, held.Length, Ordered(rows));
    }

    // Whether the provisional dip passes a member on a session as a night reads it: over the year of closes a night holds
    // to the session, its floors whole, the first session its year behind the bar misses, and each report on file by the
    // sessions it falls from the session, counted over the history's own calendar where it reaches and over the exchange's
    // ahead of it, a report dated on a closed day read at the session after it.
    public static bool DipPasses(string indexCode, SweepSeries one, double[] closes, int bar, IReadOnlyList<DateOnly> calendar, int at)
    {
        var year = closes[Math.Max(0, bar - FactorReadings.YearBars + 1)..(bar + 1)];
        var reports = new List<int>();

        foreach (var print in one.Name.Prints)
        {
            var session = FirstOnOrAfter(calendar, print);

            if (session < calendar.Count)
            {
                reports.Add(session - at);
            }
            else if (ExchangeClosures.SessionsUntil(calendar[at], print) is { } ahead)
            {
                reports.Add(ExchangeClosures.IsSession(print) ? ahead : ahead + 1);
            }
        }

        var member = new DipMember(
            one.Name.Ticker,
            year,
            one.Atr[bar] is var move && double.IsNaN(move) ? null : move,
            ClearsTheWholeFloors(indexCode, one, bar),
            string.Empty,
            reports,
            null,
            DipRule.NoMarket,
            one.Gap[bar] ? EquityBrief.Worker.Dips.DipFamily.Missing(one, bar, calendar) : null);

        return DipRule.Read(member, DipRule.Provisional).Passed;
    }

    // The first session of a calendar on or after a day, the calendar's count where none is.
    static int FirstOnOrAfter(IReadOnlyList<DateOnly> calendar, DateOnly day)
    {
        var low = 0;
        var high = calendar.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (calendar[middle] < day)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // The fundamentals-first family's setting in words, as its pass is read beside its setups: the provisional setting,
    // which an approval on the Loop page does not move, at the pullback's base.
    public static string FundamentalsWords { get; } = FundamentalsRule.Provisional.Key + "; at the pullback's base, " + SweepIdeas.BaseRule.Setting.Describe(SweepGrid.Extended);

    // The pullback's setups, and the fundamentals-first family's among them: each the pullback's whose revenue and cash
    // clear the family's loose gates, at the pullback's own plan, its pass the provisional rule's as the night reads it,
    // the pullback's base passing it, the market check open, the index's floors whole and every part of the family's rule.
    // see: The fundamentals-first family's setups are the pullback's whose revenue and cash clear a grid step past the widest level its search tried
    static (FamilySetups Pullbacks, FamilySetups Fundamentals) Pullbacks(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var held = members.Names[at];
        var found = new List<SweepCandidate>[series.Count];

        Parallel.For(0, series.Count, name => found[name] = held.Contains(name) ? SweepCandidates.For(series[name], name, sessions, calendar, at, at, at + 1) : []);

        var live = LivePullback(indexCode);
        var words = PullbackWords(indexCode);
        var rows = new List<SetupRow>();
        var business = new List<SetupRow>();
        var open = sessions[at].Breadth >= FamilySweep.MarketFloor;

        foreach (var candidate in found.SelectMany(list => list).Where(candidate => candidate.Session == at))
        {
            var one = series[candidate.Name];
            var bar = IndexNightRead.BarOf(one, at);

            if (bar < 0
                || (candidate.Uptrend & LiveUptrend) == 0
                || candidate.Band[(int)SweepDesign.Live.Support] < 0
                || candidate.Plans[LivePlan] is not { } plan
                || !SetupGates.Pullback(candidate.Strength[(int)SweepDesign.Live.Strength], candidate.Depth[LiveHigh], candidate.Age[LiveAge], plan.RewardToRisk)
                || !Clears(indexCode, one, bar))
            {
                continue;
            }

            var setupPlan = new SetupPlan(plan.StopMoves, plan.RewardToRisk, null, SweepDesign.Live.Hold);

            if (setupPlan.On(calendar[at], closes[candidate.Name][bar], one.Atr[bar]) is not { } anchor)
            {
                continue;
            }

            var corner = SweepCorner.Of(
                SweepGrid.Extended,
                candidate.Strength[(int)SweepDesign.Live.Strength],
                candidate.Depth[LiveHigh],
                candidate.DryUp[LiveHigh],
                candidate.Age[LiveAge],
                plan.RewardToRisk,
                plan.StopMoves,
                candidate.Breadth,
                candidate.Band[(int)SweepDesign.Live.Support]);
            var inTheEarningsWindow = SweepDesign.Live.EarningsWindow > 0 && candidate.Earnings >= 0 && candidate.Earnings <= SweepDesign.Live.EarningsWindow;
            var passes = corner is { } placed && placed.Passes(live) && !inTheEarningsWindow;

            var readings = Readings(indexCode, candidate.Name, one, closes[candidate.Name], bar, sessions[at], members, calendar, at, market, context, series);

            readings[LedgerReadings.IndexOf("reward_to_risk")] = plan.RewardToRisk;
            readings[LedgerReadings.IndexOf("freshness")] = candidate.Age[LiveAge];
            readings[LedgerReadings.IndexOf("band_strength")] = candidate.Band[(int)SweepDesign.Live.Support];

            rows.Add(new SetupRow(indexCode, SetupFamilies.Pullback, one.Name.Ticker, calendar[at], words, passes, anchor, setupPlan, readings));

            if (SetupGates.Fundamentals(readings[LedgerReadings.IndexOf("revenue_growth")], readings[LedgerReadings.IndexOf("cash_over_income")]))
            {
                var listedByTheBase = corner is { } based && based.Passes(SweepIdeas.BaseRule.Setting) && !inTheEarningsWindow;
                var provisional = open
                    && listedByTheBase
                    && ClearsTheWholeFloors(indexCode, one, bar)
                    && FundamentalsRule.FailsOn(FundamentalsRule.Provisional, readings, context.Income.GetValueOrDefault(one.Name.Ticker) ?? [], calendar[at]) is null;

                business.Add(new SetupRow(indexCode, FundamentalsRule.Name, one.Name.Ticker, calendar[at], FundamentalsWords, provisional, anchor, setupPlan, [.. readings]));
            }
        }

        return (new FamilySetups(SetupFamilies.Pullback, held.Length, Ordered(rows)), new FamilySetups(FundamentalsRule.Name, held.Length, Ordered(business)));
    }

    static FamilySetups Breakouts(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var held = members.Names[at];
        var bars = members.Bars[at];
        var rows = new List<SetupRow>();
        var frozen = IndexNightRead.BreakoutAsFrozen;
        var liveHigh = BreakoutSweep.Grid.Value(frozen, 0);
        var liveVolume = BreakoutSweep.Grid.Value(frozen, 1);
        var liveCeiling = BreakoutSweep.Grid.Value(frozen, 2);
        var liveStop = BreakoutSweep.Grid.Value(frozen, 3);
        var open = sessions[at].Breadth >= FamilySweep.MarketFloor;

        for (var place = 0; place < held.Length; place++)
        {
            var name = held[place];
            var bar = bars[place];
            var one = series[name];
            var close = closes[name][bar];
            var highBefore = BreakoutSweep.HighsBefore(one.Bars, SetupGates.BreakoutHighSessions)[bar];
            var volume = one.Volume50[bar] > 0 ? Statistic.FromVolume(one.Bars[bar].Volume) / one.Volume50[bar] : double.NaN;
            var ranges = BreakoutSweep.RangesBefore(one.Bars, BreakoutRule.RangeSessions)[bar];

            if (!SetupGates.Breakout(close, highBefore, volume, ranges) || !Clears(indexCode, one, bar))
            {
                continue;
            }

            var plan = new SetupPlan(liveStop, null, liveStop, BreakoutRule.CapSessions);

            if (plan.On(calendar[at], close, one.Atr[bar]) is not { } anchor)
            {
                continue;
            }

            var liveHighBefore = BreakoutSweep.HighsBefore(one.Bars, (int)liveHigh)[bar];
            var passes = open && close > liveHighBefore && volume >= liveVolume && (double.IsPositiveInfinity(liveCeiling) || ranges <= liveCeiling);
            var readings = Readings(indexCode, name, one, closes[name], bar, sessions[at], members, calendar, at, market, context, series);

            readings[LedgerReadings.IndexOf("volume_multiple")] = LedgerReadings.Figure(volume);
            readings[LedgerReadings.IndexOf("range_ratio")] = LedgerReadings.Figure(ranges);

            rows.Add(new SetupRow(indexCode, BreakoutRule.Name, one.Name.Ticker, calendar[at], BreakoutWords, passes, anchor, plan, readings));
        }

        return new FamilySetups(BreakoutRule.Name, held.Length, Ordered(rows));
    }

    static FamilySetups Drifts(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var held = members.Names[at];
        var bars = members.Bars[at];
        var rows = new List<SetupRow>();
        var frozen = IndexNightRead.DriftAsFrozen;
        var liveWindow = DriftSweep.Grid.Value(frozen, 0);
        var liveRise = DriftSweep.Grid.Value(frozen, 1);
        var liveVolume = DriftSweep.Grid.Value(frozen, 2);
        var liveMultiple = DriftSweep.Grid.Value(frozen, 3);
        var open = sessions[at].Breadth >= FamilySweep.MarketFloor;

        for (var place = 0; place < held.Length; place++)
        {
            var name = held[place];
            var bar = bars[place];
            var one = series[name];
            var newest = one.NewestSurprise[bar];

            // The reaction session is the setup's own: one setup a report, anchored where the first entry is.
            if (newest < 0 || one.SurpriseBar[newest] != bar || bar < 1)
            {
                continue;
            }

            var close = closes[name][bar];
            var before = one.Atr[bar - 1];
            var rise = before > 0 ? (close - closes[name][bar - 1]) / before : double.NaN;
            var volume = one.Volume50[bar] > 0 ? Statistic.FromVolume(one.Bars[bar].Volume) / one.Volume50[bar] : double.NaN;
            var surprise = one.Name.Surprises[newest].Percent;
            var low = Statistic.FromPrice(one.Bars[bar].Low);
            var move = one.Atr[bar];

            if (!SetupGates.Drift(surprise, rise) || !(close > low) || !(low > 0) || !(move > 0) || !Clears(indexCode, one, bar))
            {
                continue;
            }

            var risk = close - low;
            var byRisk = close + (DriftRule.TargetRiskMultiple * risk);
            var band = BandAbove(one, bar, close);
            var target = !double.IsNaN(band) && band < byRisk ? band : byRisk;
            var plan = new SetupPlan(risk / move, (target - close) / risk, null, DriftRule.CapSessions);
            var anchor = new SetupAnchor(calendar[at], close, low, target, null, DriftRule.CapSessions, risk / move);
            var passes = open && 0 < liveWindow && rise >= liveRise && volume >= liveVolume && liveMultiple == DriftRule.TargetRiskMultiple;
            var readings = Readings(indexCode, name, one, closes[name], bar, sessions[at], members, calendar, at, market, context, series);

            readings[LedgerReadings.IndexOf("reward_to_risk")] = plan.RewardToRisk;
            readings[LedgerReadings.IndexOf("freshness")] = 0;
            readings[LedgerReadings.IndexOf("volume_multiple")] = LedgerReadings.Figure(volume);
            readings[LedgerReadings.IndexOf("reaction_moves")] = LedgerReadings.Figure(rise);

            rows.Add(new SetupRow(indexCode, DriftRule.Name, one.Name.Ticker, calendar[at], DriftWords, passes, anchor, plan, readings));
        }

        return new FamilySetups(DriftRule.Name, held.Length, Ordered(rows));
    }

    // The lowest band whose low edge sits the drift rule's typical moves or more above the close, as its sweep reads
    // it; none where none does.
    static double BandAbove(SweepSeries one, int bar, double close)
    {
        var move = one.Atr[bar];
        var edges = SweepCandidates.LevelsOn(one, bar)
            .Select(level => Statistic.FromPrice(level.LowEdge))
            .Where(edge => edge > close && (edge - close) / move >= DriftRule.TargetBandMoves)
            .ToArray();

        return edges.Length > 0 ? edges.Min() : double.NaN;
    }

    // The index's floors on the member's session: its close as traded and its dollar volume over the 50 sessions to it.
    static bool Clears(string indexCode, SweepSeries one, int bar)
    {
        var day = one.Bars[bar];
        var traded = day.RawClose > 0m ? day.RawClose : day.Close;
        var window = one.Bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(each => (each.Close, each.Volume)).ToArray();

        return SetupGates.ClearsTheFloors(indexCode, traded, MemberReadings.DollarVolume(window));
    }

    // The index's floors whole, as the night holds a family's list to them: its close as traded at least the lowest close
    // and its dollar volume over the 50 sessions to it at least the index's own floor.
    static bool ClearsTheWholeFloors(string indexCode, SweepSeries one, int bar)
    {
        var day = one.Bars[bar];
        var traded = day.RawClose > 0m ? day.RawClose : day.Close;
        var window = one.Bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(each => (each.Close, each.Volume)).ToArray();

        return MemberReadings.ClearsTheFloors(indexCode, traded, MemberReadings.DollarVolume(window)) && traded >= MemberReadings.LowestPrice;
    }

    static IReadOnlyList<SetupRow> Ordered(List<SetupRow> rows)
    {
        rows.Sort((one, other) => string.CompareOrdinal(one.Ticker, other.Ticker));

        return rows;
    }

    // Every reading the catalogue defines that is not the family's own, from the series to the bar, the session's
    // places and breadth, the market series to the session's day and the quarters filed before it; and where every
    // member's series is handed in, the dip's fall against its sector's members that session.
    public static double?[] Readings(
        string indexCode,
        int name,
        SweepSeries one,
        double[] closes,
        int bar,
        SweepColumns.Session session,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context,
        IReadOnlyList<SweepSeries>? all = null)
    {
        var readings = new double?[LedgerReadings.Count];
        var close = closes[bar];
        var high = LiveHigh;
        var highs = SweepAxes.ReferenceHighs.Count;
        var day = calendar[at];
        var ticker = one.Name.Ticker;
        var income = context.Income.GetValueOrDefault(ticker) ?? [];
        var sector = context.Sectors.GetValueOrDefault(ticker);
        var liquidityWindow = one.Bars[Math.Max(0, bar - LedgerReadings.LiquiditySessions + 1)..(bar + 1)].Select(each => (Statistic.FromPrice(each.Close), each.Volume)).ToArray();
        var newest = one.NewestSurprise[bar];
        var indexSeries = market.Of(LedgerMarket.IndexSeriesOf(indexCode));
        var vix = market.Of(LedgerMarket.Vix);
        var large = market.Of(LedgerMarket.Large);
        var mid = market.Of(LedgerMarket.Mid);
        var small = market.Of(LedgerMarket.Small);
        var credit = market.Of(LedgerMarket.Credit);

        void Set(string column, double? value) => readings[LedgerReadings.IndexOf(column)] = value;

        Set("close_over_twenty", LedgerReadings.Over(close, one.Sma20[bar]));
        Set("close_over_fifty", LedgerReadings.Over(close, one.Sma50[bar]));
        Set("close_over_long", LedgerReadings.Over(close, one.Sma200[bar]));
        Set("fifty_over_long", LedgerReadings.Over(one.Sma50[bar], one.Sma200[bar]));
        Set("move_share", LedgerReadings.Over(one.Atr[bar], close));
        Set("rsi", LedgerReadings.Figure(one.Rsi[bar]));
        Set("rsi_up", LedgerReadings.Flag(one.RsiUp[bar]));
        Set("volume_ratio", LedgerReadings.Figure(one.VolumeRatio[bar]));
        Set("return_quarter", LedgerReadings.Figure(one.Return63[bar]));
        Set("return_half_year", LedgerReadings.Figure(one.Return126[bar]));
        Set("return_twelve_less_one", LedgerReadings.Figure(one.ReturnTwelveLessOne[bar]));
        Set("strength", session.Strength[(int)StrengthMeasure.ThreeAndSixMonths].TryGetValue(name, out var live) ? live : null);
        Set("strength_twelve_less_one", session.Strength[TwelveLessOnePlace].TryGetValue(name, out var twelve) ? twelve : null);
        Set("high_ratio", LedgerReadings.Over(close, one.High252[bar]));
        Set("since_high", one.SinceHigh[(bar * highs) + high] is var since && since >= 0 ? since : null);
        Set("depth", LedgerReadings.Figure(one.Depth[(bar * highs) + high]));
        Set("dry_up", LedgerReadings.Figure(one.DryUp[(bar * highs) + high]));
        Set("gap_down", LedgerReadings.Figure(one.GapDown[(bar * highs) + high]));
        Set("rsi_low", LedgerReadings.Figure(one.RsiLow[(bar * highs) + high]));
        Set("tightness", LedgerReadings.Figure(one.Tightness[bar]));
        Set("liquidity", LedgerReadings.Liquidity(liquidityWindow));
        Set("earnings_sessions", EarningsSessions(one.Name.Prints, calendar, at));
        Set("surprise_sessions", newest >= 0 ? at - one.SessionAt[one.SurpriseBar[newest]] : null);
        Set("surprise_percent", newest >= 0 ? LedgerReadings.Figure(one.Name.Surprises[newest].Percent) : null);
        Set("breadth", session.Breadth);
        Set("highs_less_lows", members.Names[at].Length > 0 ? 1.0 * (context.Highs[at] - context.Lows[at]) / members.Names[at].Length : null);
        Set("index_over_long", indexSeries is null ? null : SeriesReadings.OverAverage(indexSeries, day, LedgerReadings.IndexAverageSessions));
        Set("vix", vix is null ? null : SeriesReadings.CloseOn(vix, day));
        Set("vix_change", vix is null ? null : SeriesReadings.OverBefore(vix, day, LedgerReadings.VixChangeSessions));
        Set("mid_over_large", mid is null || large is null ? null : SeriesReadings.RelativeReturn(mid, large, day, LedgerReadings.RelativeReturnSessions));
        Set("small_over_large", small is null || large is null ? null : SeriesReadings.RelativeReturn(small, large, day, LedgerReadings.RelativeReturnSessions));
        Set("credit_over_fifty", credit is null ? null : SeriesReadings.OverAverage(credit, day, LedgerReadings.CreditAverageSessions));
        Set("profit", LedgerReadings.Flag(MemberReadings.Profit(income, day)));
        Set("coverage", LedgerReadings.Flag(MemberReadings.Coverage(income, day, sector)));

        var business = context.Business(ticker, day);

        Set("revenue_growth", business.RevenueGrowth);
        Set("growth_change", business.GrowthChange);
        Set("gross_margin_change", business.GrossMarginChange);
        Set("operating_margin_change", business.OperatingMarginChange);
        Set("cash_over_income", business.CashOverIncome);
        Set("value_place", context.ValuePlace(one, day));

        // The published factors, each over the member's own bars to the session, the index's own series and the fund of
        // the sector its company filed as it stood on the session; the four of Kakushadze's formulas that rank across the
        // index read the members' places the context works, and none where it works none.
        // see: The published price and volume factors are ledger readings for the engines to search, and no factor chooses a stock
        var bars = one.Bars;
        var fund = GicsSectors.On(ticker, sector, day) is { } stood && GicsSectors.Funds.TryGetValue(stood, out var code) ? market.Of(code) : null;

        Set("return_week", FactorReadings.ReturnOver(bars, bar, FactorReadings.WeekSessions));
        Set("return_month", FactorReadings.ReturnOver(bars, bar, FactorReadings.MonthSessions));
        Set("largest_return", FactorReadings.LargestReturn(bars, bar, FactorReadings.MonthSessions));
        Set("volatility_month", FactorReadings.DeviationOver(bars, bar, FactorReadings.MonthSessions));
        Set("residual_volatility", indexSeries is null ? null : FactorReadings.ResidualDeviation(bars, bar, indexSeries, FactorReadings.QuarterSessions));
        Set("beta", indexSeries is null ? null : FactorReadings.Beta(bars, bar, indexSeries, FactorReadings.YearBars - 1));
        Set("skewness", FactorReadings.Skewness(bars, bar, FactorReadings.QuarterSessions));
        Set("close_place", FactorReadings.ClosePlace(bars, bar, FactorReadings.PlaceSessions));
        Set("up_volume_share", FactorReadings.UpVolumeShare(bars, bar, FactorReadings.VolumeSessions));
        Set("price_volume_correlation", FactorReadings.PriceVolumeCorrelation(bars, bar, FactorReadings.VolumeSessions));
        Set("illiquidity", FactorReadings.Illiquidity(bars, bar, FactorReadings.MonthSessions));
        Set("overnight_less_intraday", FactorReadings.OvernightLessIntraday(bars, bar, FactorReadings.MonthSessions));
        Set("sector_month", fund is null ? null : FactorReadings.RelativeReturn(bars, bar, fund, FactorReadings.MonthSessions));
        Set("sector_quarter", fund is null ? null : FactorReadings.RelativeReturn(bars, bar, fund, FactorReadings.QuarterSessions));
        Set("low_ratio", FactorReadings.LowRatio(bars, bar, FactorReadings.YearBars));
        Set("streak", FactorReadings.Streak(bars, bar));
        Set("volume_slope", FactorReadings.VolumeSlope(bars, bar, FactorReadings.VolumeSessions));
        Set("alpha_one", context.Ranks?.Alpha1(name, bar));
        Set("alpha_two", context.Ranks?.Alpha2(name, bar));
        Set("alpha_three", context.Ranks?.Alpha3(name, bar));
        Set("alpha_four", context.Ranks?.Alpha4(name, bar));
        Set("alpha_six", FactorReadings.Alpha6(bars, bar));

        // The insiders' four, from the filings filed in the 90 days before the session, none where the store's insider
        // history does not hold those days.
        // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
        var insiders = context.InsidersOf(one, day);

        Set("insider_net_buy", insiders.NetBuy);
        Set("insider_buyers", insiders.Buyers);
        Set("insider_officer_buy", insiders.OfficerBuy);
        Set("insider_unplanned_sale", insiders.UnplannedSale);

        // The release readings' four, the newest results release the night may read accepted within the window.
        // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
        var releases = context.ReleasesOf(ticker, day);

        for (var column = 0; column < LedgerReleases.Columns.Count; column++)
        {
            Set(LedgerReleases.Columns[column], releases[column]);
        }

        // The short-term dip's readings: its drop over the member's own bars to the session, against its sector's members
        // the index held that session, and the market series the dip's market setting reads, each to the session's day.
        // see: The short-term dip's setups clear its drop a grid step past the widest its sweep reads with no trend condition, and the ledger's history budget is raised to hold them
        var fall = LedgerContext.DropFall(one, bar);
        var fundSeries = market.Of(LedgerMarket.FundOf(indexCode));

        Set("rsi_two", context.RsiTwo(one, closes)[bar]);
        Set("drop_fall", LedgerReadings.Figure(fall));
        Set("drop_largest", DropLargest(one, closes, bar));
        Set("drop_volume", DropVolume(one, bar));
        Set("drop_against_sector", all is not null && sector is { } filed && !double.IsNaN(fall) && context.SectorFall(at, filed, all, members) is { } median ? fall - median : null);
        Set("large_breadth", indexCode != IndexFamilies.LargeIndex && context.LargeBreadth is { } large500 && large500.TryGetValue(day, out var largeBreadth) ? largeBreadth : null);
        Set("fund_over_long", fundSeries is null ? null : SeriesReadings.OverAverage(fundSeries, day, LedgerReadings.FundAverageSessions));
        Set("vix_over_median", vix is null || market.Of(LedgerMarket.Index) is not { } sessionsHeld ? null : SeriesReadings.OverMedianBefore(vix, day, LedgerReadings.VixMedianSessions, quoted => SeriesReadings.At(sessionsHeld, quoted) is var place && place >= 0 && sessionsHeld[place].Session == quoted));
        Set("company_value", context.CompanyValue(one, day));

        return readings;
    }

    // The largest fall of a single one of the drop's sessions, the close before it less its own, in the typical move
    // standing before it; none where none of them reads one. The dip's sweep reads its second stage's filter through it.
    public static double? DropLargest(SweepSeries one, double[] closes, int bar)
    {
        double? largest = null;

        for (var day = Math.Max(1, bar - LedgerReadings.DropSessions + 1); day <= bar; day++)
        {
            if (one.Atr[day - 1] > 0 && (closes[day - 1] - closes[day]) / one.Atr[day - 1] is var fall && (largest is not { } held || fall > held))
            {
                largest = fall;
            }
        }

        return largest;
    }

    // The drop's mean volume over the mean of the sessions before it, none where too few stand before it or they traded
    // nothing. The dip's sweep reads its second stage's filter through it.
    public static double? DropVolume(SweepSeries one, int bar)
    {
        var first = bar - LedgerReadings.DropSessions + 1;

        if (first - LedgerReadings.DropVolumeSessions < 0)
        {
            return null;
        }

        var before = 0.0;
        var drop = 0.0;

        for (var day = first - LedgerReadings.DropVolumeSessions; day < first; day++)
        {
            before += one.Bars[day].Volume;
        }

        for (var day = first; day <= bar; day++)
        {
            drop += one.Bars[day].Volume;
        }

        return before > 0 ? drop / LedgerReadings.DropSessions / (before / LedgerReadings.DropVolumeSessions) : null;
    }

    // The place among a session's places of the return over the 231 sessions ending 21 before, after the live measure's
    // and the 126-session return's.
    const int TwelveLessOnePlace = 2;

    // The weekdays from the day to the next report on file after it, the report's own day counted and the day's not;
    // none where no report follows. Weekdays rather than sessions, since on the night the calendar reaches no session
    // after the night and a reading must read the same on the night and from the history.
    public static double? EarningsSessions(IReadOnlyList<DateOnly> prints, IReadOnlyList<DateOnly> calendar, int at)
    {
        var day = calendar[at];
        DateOnly? next = null;

        foreach (var print in prints)
        {
            if (print > day && (next is not { } held || print < held))
            {
                next = print;
            }
        }

        if (next is not { } report)
        {
            return null;
        }

        var weekdays = 0;

        for (var each = day.AddDays(1); each <= report; each = each.AddDays(1))
        {
            if (each.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                weekdays++;
            }
        }

        return weekdays;
    }
}
