using System.Globalization;
using System.Net;
using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Worker.Sweep;

// From 20.11 each family is read on its own index's history at the next session's open: every trade bought at the open
// after its listing through the core's one pricing function, as the tester's walks are, after its round trip at the
// published table, on membership as it stood; the market check read at seven levels; and the research's dials read as
// levels of each family's second stage. Nothing here is read by a night or pinned by a rule, so no rule's version moves
// for it.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
// see: Each family on the S&P 400 and 600 is swept again on its own history at the next open, the market check a dial of each swing grid
public sealed partial class IndexSweepRunner
{
    public const string MarketCheck = "market-check";

    // The market check's seven levels a run reads: none; the index's own breadth at four floors; the S&P 500's breadth at
    // the S&P 500's own floor; and the index's own fund above its average.
    public sealed record MarketLevel(string Key, string Words);

    public static IReadOnlyList<double> OwnBreadthFloors { get; } = Core.Candidates.IndexRuleCandidate.BreadthFloors;

    public const double LargeBreadthFloor = Core.Candidates.IndexRuleCandidate.LargeBreadthFloor;

    // The fund's average a session's close is read against, in the fund's own sessions.
    public const int FundTrendSessions = 200;

    // The sessions at the history's end over which a level's closed sessions are counted beside the whole history's.
    public const int RecentClosedSessions = 60;

    public static IReadOnlyList<MarketLevel> MarketLevels(string indexCode) =>
    [
        new("off", "no market check"),
        .. OwnBreadthFloors.Select(floor => new MarketLevel(
            FormattableString.Invariant($"own {floor:0.00}"),
            FormattableString.Invariant($"the index's own breadth at {floor * 100:0} per cent"))),
        new(FormattableString.Invariant($"S&P 500 {LargeBreadthFloor:0.00}"), FormattableString.Invariant($"the S&P 500's breadth at {LargeBreadthFloor * 100:0} per cent")),
        new("fund trend", FormattableString.Invariant($"{IndexFunds[indexCode]} above its {FundTrendSessions}-session average")),
    ];

    // The place among the seven of the level each index's provisional rules read today, its own breadth at 45 per cent.
    public static int ProvisionalMarketLevel { get; } = 1 + OwnBreadthFloors.ToList().IndexOf(0.45);

    // Whether each level holds the lists open on each session, in the order the levels are named: a breadth not held, a
    // fund close not held and an average its fund's sessions do not yet reach each close the session.
    public static bool[][] MarketOpen(IReadOnlyList<SweepColumns.Session> sessions, IReadOnlyList<double?> largeBreadth, IReadOnlyList<DateOnly> calendar, SweepMarketSeries? fund)
    {
        var count = sessions.Count;
        var levels = new List<bool[]> { Enumerable.Repeat(true, count).ToArray() };

        foreach (var floor in OwnBreadthFloors)
        {
            levels.Add([.. sessions.Select(one => one.Breadth >= floor)]);
        }

        levels.Add([.. Enumerable.Range(0, count).Select(at => at < largeBreadth.Count && largeBreadth[at] >= LargeBreadthFloor)]);
        levels.Add(FundAboveItsAverage(calendar, fund));

        return [.. levels];
    }

    // Each session's fund close above the mean of the fund's own closes to it, that many of them, the session's close among
    // them; a session the fund holds no close on, or fewer closes than the average reads, is closed.
    public static bool[] FundAboveItsAverage(IReadOnlyList<DateOnly> calendar, SweepMarketSeries? fund, int over = FundTrendSessions)
    {
        var open = new bool[calendar.Count];

        if (fund is null)
        {
            return open;
        }

        var closes = fund.Closes;
        var at = closes.Select((one, place) => (one.Session, place)).ToDictionary(pair => pair.Session, pair => pair.place);
        var sums = new double[closes.Count + 1];

        for (var place = 0; place < closes.Count; place++)
        {
            sums[place + 1] = sums[place] + closes[place].Close;
        }

        for (var session = 0; session < calendar.Count; session++)
        {
            if (at.TryGetValue(calendar[session], out var place) && place + 1 >= over)
            {
                var mean = (sums[place + 1] - sums[place + 1 - over]) / over;

                open[session] = closes[place].Close > mean;
            }
        }

        return open;
    }

    // The sessions a level closes the lists on, over the scored history and over its last sessions.
    public static (int History, int Recent) ClosedSessions(bool[] open, int firstScored)
    {
        var history = Enumerable.Range(firstScored, Math.Max(0, open.Length - firstScored)).Count(session => !open[session]);
        var recent = Enumerable.Range(Math.Max(firstScored, open.Length - RecentClosedSessions), Math.Min(RecentClosedSessions, Math.Max(0, open.Length - firstScored))).Count(session => !open[session]);

        return (history, recent);
    }

    // One level's figures as a market check's run states them, every trade at the next open after its round trip.
    public sealed record MarketCheckRow(
        string Family,
        string Level,
        string Words,
        int Trades,
        double? Edge,
        int YearsAboveNothing,
        double?[] YearEdge,
        int[] YearTrades,
        double? EdgeWithoutLargest,
        int ClosedOverHistory,
        int ClosedOverRecent);

    // The market check on the index alone, each swing family apart at the provisional setting its night reads, over the
    // history on membership as it stood, after costs, every trade bought at the next open: at each of the seven levels,
    // its trades, edge, years above nothing, each year's edge, the edge without the five largest results, and the sessions
    // it would have closed the lists over the history and over the last sixty. It proposes nothing.
    async Task<int> MarketCheckAsync(string indexCode, string named, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, DateTimeOffset started, CancellationToken cancellation)
    {
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var tickers = inputs.Names.Select(name => name.Ticker).ToArray();
        var fund = (await history.SeriesOfAsync([IndexFunds[indexCode]], through, cancellation)).FirstOrDefault();

        output.WriteLine("reading the S&P 500 as it stood for its breadth");

        var large = await history.ReadAsync(through, null, cancellation);
        var levels = MarketLevels(indexCode);
        var open = MarketOpen(sessions, SweepSwitches.LargeBreadth(large, calendar), calendar, fund);
        var fills = new OpenFills(series, members);
        var rows = new List<MarketCheckRow>();

        int YearOf(int session) => calendar[session].Year - SweepColumns.FirstScored.Year;

        MarketCheckRow Row(string family, int level, int trades, double? edge, double?[] yearEdge, int[] yearTrades, double? withoutLargest)
        {
            var (overHistory, overRecent) = ClosedSessions(open[level], firstScored);

            return new MarketCheckRow(family, levels[level].Key, levels[level].Words, trades, edge, yearEdge.Count(one => one > 0), yearEdge, yearTrades, withoutLargest, overHistory, overRecent);
        }

        // The breakout and the drift at the settings the night reads, their listings read with every session open and each
        // level then keeping the sessions it holds open, as the check closes a night's whole list.
        var allOpen = sessions.Select(one => one with { Breadth = 1.0 }).ToArray();

        foreach (var (family, provisional) in new[] { (BreakoutRule.Name, IndexNightRead.BreakoutAsFrozen), (DriftRule.Name, IndexNightRead.DriftAsFrozen) })
        {
            output.WriteLine($"reading the {family} at its provisional setting");

            var adapter = FamilySweepRunner.For(family, series, allOpen, members, firstScored, calendar);
            var listings = adapter.Listings([.. provisional])
                .Where(listing => Clears(indexCode, series[listing.Name], listing.Bar, income.GetValueOrDefault(tickers[listing.Name]) ?? []))
                .ToArray();

            for (var level = 0; level < levels.Count; level++)
            {
                var kept = open[level];
                var trades = FamilySweep.Walk(
                    listings.Where(listing => kept[listing.Session]),
                    tickers,
                    YearOf,
                    listing => fills.Exit(adapter.Exit(listing), listing),
                    listing => fills.Benchmark(adapter.Benchmark(listing), listing));
                var figures = FamilySweep.Figures(
                    levels[level].Key,
                    [.. trades.Select(trade => trade.Result is { } result ? trade with { Result = result - CostInRisk(series[trade.Listing.Name], trade.Listing, result, companies, 1) } : trade)],
                    nights);

                rows.Add(Row(family, level, figures.Trades, figures.Edge, figures.YearEdge, figures.YearTrades, figures.EdgeWithoutLargest));
            }
        }

        // The pullback's base as the night reads it, its own market floor off and each level held by the walk's filter on the
        // listing's session, so a closed session lists none of it.
        output.WriteLine("reading the pullback's base at its provisional setting");

        var market = await history.MarketAsync(through, cancellation);
        var (replay, ideas) = SweepIdeasRunner.Read(inputs, market, output.WriteLine);
        var replayed = replay.Series;
        var replayFills = new OpenFills(replayed, SweepBenchmark.On(replayed, calendar.Length));
        var checkedByLevel = SweepIdeas.BaseRule with { Setting = SweepIdeas.BaseRule.Setting with { Market = SweepGrid.IndexOf(SweepGrid.Extended.MarketFloors, SweepGrid.MarketOff) } };

        for (var level = 0; level < levels.Count; level++)
        {
            var kept = open[level];
            var trades = replay.Trades(checkedByLevel, (name, bar) =>
                kept[replayed[name].SessionAt[bar]] && Clears(indexCode, replayed[name], bar, income.GetValueOrDefault(replayed[name].Name.Ticker) ?? []));
            var figures = SweepIdeas.Figures(levels[level].Key, [.. trades.Select(trade => AtOpenAfterCosts(trade, replayed, replayFills, companies))], ideas.ScoredNights);

            rows.Add(Row(SetupFamilies.Pullback, level, figures.Trades, figures.Edge, figures.YearEdge, figures.YearTrades, figures.EdgeWithoutLargest));
        }

        var note = FormattableString.Invariant($"{Membership(named, inputs)} Each family is read at the provisional setting its night reads today, its listings kept only where they clear the index's floors and its profit gate, every trade bought at the open of the session after its listing through the core's one pricing function, its benchmark the same plan on every member bought at each member's own next open, and every edge after the trade's round trip at the published table, the benchmark paying none. A level closes a night's whole list on a session it is closed.");
        var page = MarketCheckPage(named, note, levels, rows, calendar[firstScored], through, started);
        var report = WriteRun(folder, page, new { index = indexCode, family = MarketCheck, note, levels, rows }, null);

        foreach (var row in rows)
        {
            output.WriteLine(FormattableString.Invariant($"{row.Family}, {row.Level}: {row.Trades:N0} trades, edge {FamilySweepReport.Number(row.Edge)} after costs, {row.YearsAboveNothing} of 8 years above nothing, 2025 {FamilySweepReport.Number(row.YearEdge[^2])}, 2026 {FamilySweepReport.Number(row.YearEdge[^1])}, without the five largest {FamilySweepReport.Number(row.EdgeWithoutLargest)}, closed {row.ClosedOverHistory:N0} sessions, {row.ClosedOverRecent} of the last {RecentClosedSessions}"));
        }

        output.WriteLine("report " + report);

        return 0;
    }

    // A replayed pullback's trade bought at the next open and, from 21.14, sold at the open after the close its stop or its
    // target ended it on, under the base's own exit: its result restated over the risk its close put up, its benchmark
    // each member's own gap over its own typical moves, and its round trip taken off its result.
    static IdeaTrade AtOpenAfterCosts(IdeaTrade trade, IReadOnlyList<SweepSeries> series, OpenFills fills, HeavyweightHistory companies)
    {
        var listing = trade.Listing;
        var one = series[listing.Name];
        var close = Statistic.FromPrice(one.Bars[listing.Bar].Close);
        var risk = listing.StopMoves * one.Atr[listing.Bar];
        var result = fills.Pullback(fills.Result(trade.Result, listing.Name, listing.Bar, close, risk), listing.Name, listing.Bar, listing.Plan, SweepAxes.ExitIndex(SweepIdeas.Cap, breakEven: false), risk);

        return trade with
        {
            Result = result is { } made ? made - CostInRisk(one, listing.Bar, close, close - risk, made, companies, 1) : null,
            Benchmark = fills.Benchmark(trade.Benchmark, listing.Session, listing.StopMoves),
        };
    }

    public const string BreakoutOwn = "breakout-own";

    public const string DriftOwn = "drift-own";

    // The research's dials on the breakout and the drift: the largest move a breakout day may make in its typical moves
    // before it, and the sessions before its next report a drift is sold at the close of.
    public const double LargestDayMoves = 3;

    public const int SessionsBeforeReport = 3;

    // The second stage's dial a level moving a listing's last session belongs to, read after the hold any other level sets.
    public const string ExitDial = "exit";

    // The newest report as it stood on a bar's session: the newest whose reaction the bar has reached, none before any.
    public static SweepSurprise? NewestReport(SweepSeries series, int bar) =>
        bar >= 0 && series.NewestSurprise[bar] is var at && at >= 0 ? series.Name.Surprises[at] : null;

    // A session's own move in the typical moves standing before it: its close less the close before, over the session
    // before's typical move; none where no session or no typical move stands before it, read as past any cap.
    public static double DayMoves(SweepSeries series, int bar) =>
        bar >= 1 && series.Atr[bar - 1] > 0 ? Statistic.FromPrice(series.Bars[bar].Close - series.Bars[bar - 1].Close) / series.Atr[bar - 1] : double.PositiveInfinity;

    // Whether no session of those to a bar, the bar among them, rose more than the moves in the typical moves before it; a
    // session with no typical move before it is not read.
    public static bool NoRiseOver(SweepSeries series, int bar, int sessions, double moves)
    {
        for (var day = Math.Max(1, bar - sessions + 1); day <= bar; day++)
        {
            if (DayMoves(series, day) is var move && !double.IsPositiveInfinity(move) && move > moves)
            {
                return false;
            }
        }

        return true;
    }

    // A listing held to the close the sessions before the next report the history holds after its session, dated as it
    // happened: its last session moved there where that comes before its own, none where no session is left to hold, and
    // the listing as it was where the history reaches no later report.
    public static FamilyListing? BeforeTheNextReport(SweepSeries series, DateOnly day, FamilyListing listing, int sessionsBefore)
    {
        var next = series.Name.Surprises.Where(surprise => surprise.EventDate > day).Select(surprise => surprise.EventDate).DefaultIfEmpty(DateOnly.MaxValue).Min();
        var reportBar = next == DateOnly.MaxValue ? -1 : Array.FindIndex(series.Bars, bar => bar.Session >= next);

        if (reportBar < 0)
        {
            return listing;
        }

        var held = reportBar - sessionsBefore - listing.Bar;

        return held >= 1 ? listing with { Cap = Math.Min(listing.Cap, held) } : null;
    }

    // The analysts following a company as they stood on a session: the newest count fetched on an earlier day, the night
    // fetching after its session's close; none before the first count stored.
    public static int? AnalystsOn(IReadOnlyList<(DateOnly Fetched, int Analysts)>? counts, DateOnly day)
    {
        int? standing = null;

        foreach (var (fetched, analysts) in counts ?? [])
        {
            if (fetched < day)
            {
                standing = analysts;
            }
        }

        return standing;
    }

    // No report reacted inside a pullback's dip: the newest report's reaction came before the reference high, or there is
    // none; one on the high's own session or on the listing's is inside it.
    public static bool NoReportInTheDip(SweepCandidate candidate, int high) =>
        candidate.SurpriseSessions < 0 || candidate.SurpriseSessions > candidate.PullbackSessions[high];

    // A series' return over the sessions back to a session, none where it holds no bar on it or none that far back.
    public static double? ReturnOver(SweepSeries series, int session, int back) =>
        Array.BinarySearch(series.SessionAt, session) is var bar && bar - back >= 0 && back > 0 && series.Bars[bar - back].Close > 0m
            ? Statistic.FromRatio(series.Bars[bar].Close / series.Bars[bar - back].Close) - 1.0
            : null;

    // A sector's mean return over those sessions, its members the index held on the session with a return, none under two.
    public static double? SectorMean(IEnumerable<SweepSeries> sector, int session, int back)
    {
        var returns = sector
            .Where(one => Array.BinarySearch(one.SessionAt, session) is var bar && bar >= 0 && one.Member[bar])
            .Select(one => ReturnOver(one, session, back))
            .OfType<double>()
            .ToArray();

        return returns.Length >= 2 ? returns.Average() : null;
    }

    // The breakout's or the drift's grid on the index's own history at the next open, the market check a dial of seven
    // levels on it, the provisional setting the one its night reads today; the second stage reads every level the index's
    // sweep read before, the S&P 500's breadth now a level of the dial, and the research's dials, each tried alone on the
    // ten strongest settings and kept by the rule the second stage has always kept a level by, the survivors crossed.
    // see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    async Task<int> SwingOwnAsync(string indexCode, string named, string words, string family, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, DateTimeOffset started, double stopFloor, CancellationToken cancellation)
    {
        var laid = OwnSwingRead.Laid(indexCode, inputs, companies, income, through);
        var large = await OwnSwingRead.LargeAsync(history, through, output.WriteLine, cancellation);
        var own = await OwnSwingRead.ReadAsync(laid, large, family, history, through, stopFloor, cancellation);
        var (isDrift, calendar, firstScored, nights, lastTwo) = (own.IsDrift, own.Calendar, own.FirstScored, own.Nights, own.LastTwo);
        var (marketLevels, open, grid, levels, adapter) = (own.MarketLevels, own.Open, own.Grid, own.Levels, own.Adapter);
        var (withIncome, firstCounted, floored) = (own.MembersWithIncome, own.FirstCounted, own.Floored);
        var (listed, kept) = (0L, 0L);
        var read = new List<(int[] Setting, FamilyFigures Figures)>();
        var before = new Dictionary<string, FamilyFigures>(StringComparer.Ordinal);
        var doubled = new Dictionary<string, FamilyFigures>(StringComparer.Ordinal);

        output.WriteLine(FormattableString.Invariant($"walking the {family}'s {grid.Settings.Count} setting(s), its grid at each of the market check's {marketLevels.Count} levels, at the next open"));

        foreach (var setting in grid.Settings)
        {
            var (listings, clearing) = own.FirstStage(setting);
            var trades = own.AtOpen(clearing);
            var key = grid.Key(setting);

            listed += listings.Length;
            kept += clearing.Length;
            before[key] = FamilySweep.Figures(key, trades, nights);
            read.Add((setting, FamilySweep.Figures(key, own.Costed(trades, 1), nights)));
            doubled[key] = FamilySweep.Figures(key, own.Costed(trades, TradeCost.Doubled), nights);
        }

        bool Passes(FamilyFigures figures) => own.Passes(figures);

        IReadOnlyList<FamilyTrade> TradesUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together) => own.TradesUnder(setting, together);

        var proposal = FamilySweep.Propose(grid, [.. read.Where(one => !lastTwo || Passes(one.Figures))]);

        // The second stage over the strongest settings, each listing read at the market level its setting holds.
        var ten = own.Strongest(read);

        output.WriteLine(FormattableString.Invariant($"the second stage: {levels.Count} level(s) and switch(es) alone on the {ten.Length} strongest settings"));

        var withoutDials = ten.Select(setting => SweepDials.Of(TradesUnder(setting, []))).ToArray();
        var tries = levels
            .SelectMany(level => ten.Select((setting, at) => new DialTry(level.Dial, level.Name, level.Switch, at, withoutDials[at], SweepDials.Of(TradesUnder(setting, [level])))))
            .ToList();
        var dialsRead = SweepDials.Read(tries);
        var crossedLevels = dialsRead
            .Where(one => one.KeptOn >= SweepDials.KeptOn)
            .GroupBy(one => one.Dial)
            .Select(dial => dial.OrderByDescending(one => one.KeptOn).ThenByDescending(one => one.MedianChange ?? double.MinValue).First())
            .Select(best => levels.First(level => level.Dial == best.Dial && level.Name == best.Level))
            .ToArray();
        var crossedTrades = crossedLevels.Length > 0 ? ten.Select(setting => TradesUnder(setting, crossedLevels)).ToArray() : [];
        var crossed = crossedTrades.Select(SweepDials.Of).ToArray();

        bool PassesCrossed(SweepMeasures measures) => MeetsTheFloors(measures) && (!lastTwo || LastTwoYears(measures.YearScored, measures.YearEdge) > 0);

        // The setting the family would run on the index: the best edge after costs among its proposal and its crossings
        // meeting the floors, or where none does, the strongest setting its first stage read, the highest edge among those
        // holding the trade floor and the most trades where none holds it, with its figures whole.
        // see: The setting a family on the S&P 400 or 600 would run is the best edge among its passing settings, or the strongest read where none passes
        (FamilyFigures Figures, bool Passed) WouldRun()
        {
            var passing = new List<FamilyFigures>();

            if (proposal.Proposed is { } proposedFigures)
            {
                passing.Add(proposedFigures);
            }

            for (var at = 0; at < crossed.Length; at++)
            {
                if (PassesCrossed(crossed[at]))
                {
                    passing.Add(FamilySweep.Figures(OwnProcedures.KeyOf(grid, ten[at], crossedLevels), crossedTrades[at], nights));
                }
            }

            if (passing.Count > 0)
            {
                return (passing.OrderByDescending(one => one.Edge ?? double.MinValue).ThenBy(one => one.Key, StringComparer.Ordinal).First(), true);
            }

            var strongest = read
                .OrderBy(one => one.Figures.Trades >= FamilySweep.TradeFloor ? 0 : 1)
                .ThenByDescending(one => one.Figures.Trades >= FamilySweep.TradeFloor ? one.Figures.Edge ?? double.MinValue : one.Figures.Trades)
                .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
                .First();

            return (strongest.Figures, false);
        }

        var wouldRun = WouldRun();
        var legend = string.Join("; ", marketLevels.Select((level, at) => FormattableString.Invariant($"market={at} {level.Words}")));
        var countsFrom = firstCounted == DateOnly.MaxValue ? "none is stored" : FormattableString.Invariant($"the first fetch storing one was on {firstCounted:yyyy-MM-dd}, so a count stands from the session after it");
        var stage = StageSection(
            dialsRead,
            [.. ten.Select(setting => grid.Key(setting))],
            [.. crossedLevels.Select(level => level.Dial + ", " + level.Name)],
            crossed,
            (isDrift
                ? FormattableString.Invariant($"The holds are read at 10, 21, 42 and 63 sessions beside the drift's own 60, its window at 20 sessions beside the grid's, its peer reading over the 20 sessions before the reaction, its print's revenue growth as first filed over the quarters filed before the session before the listing's, as the night reads them once its filings refresh has stored them, an entry on the first session within 10 after the reaction closing below the close before it, its stop still the reaction's low, a sale at the close {SessionsBeforeReport} sessions before the next report the history holds, dated as it happened, which companies normally announce weeks ahead, and the analysts following it on its session against the index's median, read only where a count stood: {countsFrom}.")
                : FormattableString.Invariant($"The holds are read at 21 and 42 sessions beside the family's own 63, the year's high over the 251 sessions before the listing, the breakout day's close in the top quarter of its range, an entry on the first session within 10 after the listing whose low reaches the level it broke and whose close stands above it, its stop and trail the setting's typical moves in that session's own, the newest report beating its estimate, as it stood on the breakout's session, or its revenue growing on the year as first filed over the quarters filed before the session before it, as the night reads them, and the breakout day's move at most {LargestDayMoves:0} typical moves before it."))
                + " The S&P 500's breadth is read as the market check's sixth level on the grid rather than as a switch here, and the quality's fourth level, the state the reported quarters read, is not read, since the history holds no quarters as they stood for it.");
        var note = FormattableString.Invariant(
            $"{Membership(named, inputs)} Every trade is bought at the open of the session after its listing through the core's one pricing function, its benchmark the same plan on every member bought at each member's own next open, and every edge is after the trade's round trip at the published table, the benchmark paying none. A listing is kept only where its close was at least $5, its mean dollar volume over the 50 sessions to it at least {MemberReadings.DollarVolumeFloor(indexCode)!.Value:N0} dollars and its four newest quarters filed before it summed above nothing; {kept:N0} of the {listed:N0} listings every setting made together cleared them, and {withIncome:N0} of the {inputs.Names.Count:N0} members hold quarters of income. The market check is the grid's last dial: {legend}. The provisional setting is the one the index's night reads today.");
        var provisionalOpen = Enumerable.Range(firstScored, nights).Count(session => open[ProvisionalMarketLevel][session]);
        var provisional = grid.Key(grid.Provisional.ToArray());

        // The rule the night reads, the provisional setting with no stop floor, which a floored grid reads with one.
        var nightsRule = FamilySweep.Figures(provisional, own.NightsRule(), nights);
        var floorWords = FormattableString.Invariant($" The stop is held at least {stopFloor:0.##} typical moves under the buy where the reaction's low sits nearer, on every setting the grid reads; the rule the night reads today is the provisional setting with no floor, at {FamilySweepReport.Number(nightsRule.Edge)} after costs over {nightsRule.Trades:N0} trades, {nightsRule.YearsBeating} of 8 years above nothing.");
        var run = new FamilySweepRun(family, $"{named} {words}", calendar[firstScored], through, inputs.Names.Count, nights, provisionalOpen, adapter.Readings, started, clock.UtcNow, floored ? note + floorWords : note);
        var shown = new List<string> { provisional };

        if (proposal.Proposed is { } proposed)
        {
            shown.Add(proposed.Key);
        }
        else
        {
            shown.AddRange(SweepNonePassed.StrongestOf(read.Select(one => new Strongest(one.Figures.Key, one.Figures.Trades, one.Figures.YearsBeating, one.Figures.Edge))).Select(one => one.Key));
        }

        var report = WriteRun(
            folder,
            FamilySweepReport.Build(run, grid, read, proposal)
                + Costs([.. shown.Distinct(StringComparer.Ordinal)], before, read.ToDictionary(one => one.Figures.Key, one => one.Figures, StringComparer.Ordinal), doubled)
                + stage
                + (lastTwo ? LastTwoYearsSection(read, [.. ten.Select(setting => grid.Key(setting))], crossed) : string.Empty)
                + WouldRunSection(wouldRun.Figures.Key, wouldRun.Passed, wouldRun.Figures.Trades, wouldRun.Figures.Edge, wouldRun.Figures.YearEdge, wouldRun.Figures.EdgeWithoutLargest)
                + Luck(read.Count, dialsRead.Count),
            new
            {
                index = indexCode,
                family,
                atTheNextOpen = true,
                marketLevels,
                run,
                listed,
                kept,
                provisional,
                nightsRule,
                proposal = proposal.Proposed?.Key,
                afterCosts = read.Select(one => one.Figures),
                beforeCosts = before.Values,
                atDoubleCost = doubled.Values,
                strongestTen = ten.Select((setting, at) => new { setting = grid.Key(setting), after = withoutDials[at] }),
                dials = dialsRead,
                tries = tries.Select(one => new { one.Dial, one.Level, one.Switch, one.Setting, one.Kept, with = one.With }),
                crossedLevels = crossedLevels.Select(level => level.Dial + ", " + level.Name),
                crossed = crossed.Select((measures, at) => new { setting = grid.Key(ten[at]), after = measures, meets = PassesCrossed(measures) }),
                analystCountsFrom = firstCounted == DateOnly.MaxValue ? null : (DateOnly?)firstCounted,
                lastTwoYears = lastTwo ? new { settings = read.Count(one => Passes(one.Figures)), crossed = crossed.Count(PassesCrossed) } : null,
                luck = new { settings = HeavyweightSweep.Luck(read.Count), levels = dialsRead.Count * SweepDials.LuckOfALevel },
                wouldRun = new { key = wouldRun.Figures.Key, passed = wouldRun.Passed, figures = wouldRun.Figures },
            },
            new SweepAnswer(indexCode, family, null, proposal.Proposed is not null || crossed.Any(PassesCrossed)));

        output.WriteLine(proposal.Proposed is { } shownProposal
            ? FormattableString.Invariant($"proposed {shownProposal.Key}, edge after costs {FamilySweepReport.Number(shownProposal.Edge)} over {shownProposal.Trades} trades, {shownProposal.YearsBeating} of 8 years, before costs {FamilySweepReport.Number(before[shownProposal.Key].Edge)}, at double {FamilySweepReport.Number(doubled[shownProposal.Key].Edge)}")
            : "none passed: no setting meets the floors after costs, and the report states the strongest settings and what could be tried next");
        output.WriteLine(FormattableString.Invariant($"provisional {provisional}{(floored ? " with no stop floor, as the night reads it" : string.Empty)}: edge after costs {FamilySweepReport.Number(nightsRule.Edge)} over {nightsRule.Trades} trades, {nightsRule.YearsBeating} of 8 years"));
        output.WriteLine(crossedLevels.Length == 0
            ? FormattableString.Invariant($"second stage: no level of {levels.Count} survives on {SweepDials.KeptOn} of the {ten.Length} strongest settings")
            : FormattableString.Invariant($"second stage: {string.Join("; ", crossedLevels.Select(level => level.Dial + ", " + level.Name))} survive; crossed, {crossed.Count(PassesCrossed)} of {ten.Length} meet the floors, the strongest reading {FamilySweepReport.Number(crossed.Max(one => one.Edge))} after costs"));
        output.WriteLine(WouldRunLine(wouldRun.Figures.Key, wouldRun.Passed, wouldRun.Figures.Trades, wouldRun.Figures.Edge, wouldRun.Figures.YearEdge, wouldRun.Figures.EdgeWithoutLargest));
        output.WriteLine("report " + report);

        return 0;
    }

    // The setting a family would run on the index with its figures, as a run's console line and as its report's section.
    static string WouldRunLine(string key, bool passed, int trades, double? edge, IReadOnlyList<double?> yearEdge, double? withoutLargest) =>
        FormattableString.Invariant($"would run {key}{(passed ? string.Empty : ", the strongest read with none passing")}: edge after costs {FamilySweepReport.Number(edge)} over {trades:N0} trades, {yearEdge.Count(one => one > 0)} of 8 years above nothing, 2025 {FamilySweepReport.Number(yearEdge[^2])}, 2026 {FamilySweepReport.Number(yearEdge[^1])}, without the five largest {FamilySweepReport.Number(withoutLargest)}");

    static string WouldRunSection(string key, bool passed, int trades, double? edge, IReadOnlyList<double?> yearEdge, double? withoutLargest) =>
        FormattableString.Invariant($"<h2>The setting it would run</h2><p class=\"would-run\" data-passed=\"{(passed ? "yes" : "no")}\">{WebUtility.HtmlEncode(WouldRunLine(key, passed, trades, edge, yearEdge, withoutLargest))}.</p>");

    public const string PullbackOwn = "pullback-own";

    // The sessions to a pullback's listing its largest daily rise is read over, Bali, Cakici and Whitelaw's month.
    public const int JumpSessions = 21;

    // The pullback search's grid on an index's own history: the widened grid with its market floor read at the index's own
    // breadth off and at the four floors the market check reads; the S&P 500's breadth and the fund's trend are the
    // market check's two other levels, read as levels of the second stage since the search's dial reads one breadth.
    public static SweepGrid OwnPullbackGrid { get; } = SweepGrid.Widened with { MarketFloors = [SweepGrid.MarketOff, .. OwnBreadthFloors] };

    // The range the search tests each dial over: the fine grid's, its market floors the own grid's five.
    public static SweepGrid OwnPullbackTested { get; } = SweepGrid.Fine with { MarketFloors = [SweepGrid.MarketOff, .. OwnBreadthFloors] };

    // The pullback's nine dials searched on the index's own history at the next open: every candidate's plan bought at the
    // close restated bought at the open after it through the core's one pricing function, its benchmark each member's own
    // gap; the market check's own breadth a dial of the grid; the second stage the index sweep's levels with the S&P 500's
    // breadth at its floor, the fund's trend and the research's three dials, each tried alone on the ten strongest
    // settings, the survivors crossed with and without the profit and interest-cover check.
    // see: A trade's cost comes off the trade and not its benchmark
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    async Task<int> PullbackOwnAsync(string indexCode, string named, string words, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, DateTimeOffset started, CancellationToken cancellation)
    {
        var laid = OwnSwingRead.Laid(indexCode, inputs, companies, income, through);
        var large = await OwnSwingRead.LargeAsync(history, through, output.WriteLine, cancellation);
        var own = await OwnPullbackRead.ReadAsync(laid, large, history, through, output.WriteLine, cancellation);
        var (all, candidates, nights, parallelism) = (own.All, own.Candidates, own.Nights, Environment.ProcessorCount);
        var (atOpen, stepped, noOpen) = (own.AtOpen, own.Stepped, own.NoOpen);
        var (design, grid, space, baseSetting) = (own.Design, own.Grid, own.Space, own.BaseSetting);

        void Charge(int multiple) => own.Charge(multiple);

        Charge(1);

        var live = SweepStages.Direct(candidates, design, baseSetting, ConditionSetting.Off, nights, grid: grid);
        var picks = SweepStages.Picks(candidates, design, grid);
        var search = new SweepDesignSearch(design, picks, nights, space);

        output.WriteLine(FormattableString.Invariant($"{candidates.Length:N0} of {all.Length:N0} candidates clear the floors and the gate, {atOpen:N0} exits restated at the next open, {noOpen:N0} holding no open after their listing, {stepped:N0} stepped; searching for up to {SearchBudget.TotalHours:0} hours"));
        search.EvaluateCoarse(ConditionSetting.Off, parallelism);

        var (sampleSize, gridSize, perPoint) = search.EvaluateSample(SearchBudget, SweepSearch.Seed, parallelism);

        search.FixTheLine(SweepSearch.PlateauMargin);

        var leaders = search.LeadersOf(SweepSearch.Leaders);
        var (proposal, trailing) = search.Propose(leaders, live);
        var refinement = new List<string>();
        var extensions = new List<string>();
        var limits = new List<string>();

        if (proposal is not null)
        {
            proposal = search.Refine(proposal, live, refinement);
            proposal = search.Extend(proposal, live, refinement, extensions, limits, LooksBeyond);
        }

        var strongest = search.Strongest(StrongestRead);
        var shown = new List<(string Label, int[] Point)> { ("the provisional base", space.Point(baseSetting, ConditionSetting.Off)) };

        if (proposal is not null)
        {
            shown.Add(("the proposal", proposal.Point));
        }

        shown.AddRange(strongest.Select((point, at) => (FormattableString.Invariant($"the strongest setting read, {at + 1}"), point)));

        SweepMeasures MeasuresOf(int[] point) => SweepStages.Direct(candidates, design, space.Setting(point), space.Conditions(point), nights, grid: grid);

        var after = shown.Select(one => MeasuresOf(one.Point)).ToArray();

        Charge(-1);

        var before = shown.Select(one => MeasuresOf(one.Point)).ToArray();

        Charge(2);

        var doubled = shown.Select(one => MeasuresOf(one.Point)).ToArray();

        Charge(-1);

        // The second stage over the ten strongest settings.
        var ten = search.Strongest(SweepDials.Settings, SweepDials.TradesHeld);
        var withoutDials = ten.Select(MeasuresOf).ToArray();
        var (levels, cover) = (own.Levels, own.Cover);

        // The ten settings read under a set of levels together.
        SweepMeasures[] ReadUnder(IReadOnlyList<DialLevel<SweepCandidate>> together) => own.ReadUnder(together, ten);

        output.WriteLine(FormattableString.Invariant($"the second stage: {levels.Count} level(s) and switch(es) alone on the {ten.Count} strongest settings"));

        var tries = levels
            .SelectMany(level => ReadUnder([level]).Select((with, at) => new DialTry(level.Dial, level.Name, level.Switch, at, withoutDials[at], with)))
            .ToList();
        var dialsRead = SweepDials.Read(tries);
        var crossedLevels = dialsRead
            .Where(one => one.KeptOn >= SweepDials.KeptOn)
            .GroupBy(one => one.Dial)
            .Select(dial => dial.OrderByDescending(one => one.KeptOn).ThenByDescending(one => one.MedianChange ?? double.MinValue).First())
            .Select(best => levels.First(level => level.Dial == best.Dial && level.Name == best.Level))
            .ToArray();
        var crossed = crossedLevels.Length > 0 ? ReadUnder(crossedLevels) : [];
        DialLevel<SweepCandidate>[] withCover = [.. crossedLevels.Where(level => level.Dial != cover.Dial), cover];
        var crossedWithCover = ReadUnder(withCover);
        var proposed = proposal is not null && MeetsTheFloors(after[1]);

        // The setting the pullback would run on the index: the best edge after costs among its proposal and the crossings
        // meeting the floors, with and without the cover check, or where none does, the strongest setting its search read,
        // the highest edge among those holding the trade floor and the most trades where none holds it; each with its edge
        // without its five largest results read off its own picks.
        // see: The setting a family on the S&P 400 or 600 would run is the best edge among its passing settings, or the strongest read where none passes
        (string Key, SweepMeasures Measures, SweepTrimmed Trimmed, bool Passed) WouldRun()
        {
            var passing = new List<(string Key, SweepMeasures Measures, SweepTrimmed Trimmed)>();

            if (proposed)
            {
                passing.Add((space.Describe(proposal!.Point), after[1], SweepStages.WithoutTheLargest(picks, design, space.Setting(proposal.Point), space.Conditions(proposal.Point))));
            }

            void Crossed(IReadOnlyList<DialLevel<SweepCandidate>> together, SweepMeasures[] measures)
            {
                var (held, levelPicks) = own.Under(together);

                for (var at = 0; at < ten.Count; at++)
                {
                    if (MeetsTheFloors(measures[at]))
                    {
                        passing.Add((
                            space.Describe(ten[at]) + " with " + string.Join("; ", together.Select(level => level.Dial + ", " + level.Name)),
                            measures[at],
                            SweepStages.WithoutTheLargest(levelPicks, held, own.SettingUnder(ten[at], together), space.Conditions(ten[at]))));
                    }
                }
            }

            if (crossedLevels.Length > 0)
            {
                Crossed(crossedLevels, crossed);
            }

            Crossed(withCover, crossedWithCover);

            if (passing.Count > 0)
            {
                var best = passing.OrderByDescending(one => one.Measures.Edge ?? double.MinValue).ThenBy(one => one.Key, StringComparer.Ordinal).First();

                return (best.Key, best.Measures, best.Trimmed, true);
            }

            var strongest = Enumerable.Range(0, shown.Count)
                .OrderBy(at => after[at].Scored >= FamilySweep.TradeFloor ? 0 : 1)
                .ThenByDescending(at => after[at].Scored >= FamilySweep.TradeFloor ? after[at].Edge ?? double.MinValue : after[at].Scored)
                .ThenBy(at => space.Describe(shown[at].Point), StringComparer.Ordinal)
                .First();

            return (space.Describe(shown[strongest].Point), after[strongest], SweepStages.WithoutTheLargest(picks, design, space.Setting(shown[strongest].Point), space.Conditions(shown[strongest].Point)), false);
        }

        var wouldRun = WouldRun();
        var note = FormattableString.Invariant($"{Membership(named, inputs)} A candidate is kept only where its close was at least $5, its mean dollar volume over the 50 sessions to it at least {MemberReadings.DollarVolumeFloor(indexCode)!.Value:N0} dollars and its four newest quarters filed before it summed above nothing: {candidates.Length:N0} of the {all.Length:N0} candidates the members made. Every plan bought at the close is bought at the open of the session after its listing through the core's one pricing function, {atOpen:N0} exits restated, {noOpen:N0} holding no open after their listing and so no trade, and {stepped:N0} of a stepped plan bought at its own fill left as they were; its benchmark the same plan on every member of the index bought at each member's own next open, paying no cost. The market check's own breadth is a dial of the grid, off or at {string.Join(", ", OwnBreadthFloors.Select(floor => floor.ToString("0.00", CultureInfo.InvariantCulture)))}; the S&P 500's breadth at {LargeBreadthFloor:0.00} and {IndexFunds[indexCode]} above its {FundTrendSessions}-session average are read as levels of the second stage.");
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " pullback on its own history</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} {WebUtility.HtmlEncode(words)} searched on its own members</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p>"));
        page.Append(FormattableString.Invariant($"<p>{sampleSize:N0} settings sampled of {gridSize:N0} on its grid at {perPoint * 1000:0.00} ms each, {search.Evaluations:N0} read in all; the best edge {FamilySweepReport.Number(search.BestEdge)}, the plateau's line {FamilySweepReport.Number(search.Line)}, {leaders.Count} leader(s) meeting the sweep's own floors, {trailing} trailing the provisional base in a recent year.</p>"));
        page.Append(FormattableString.Invariant($"<p class=\"floors\">{(proposed ? "The proposal meets the floors after costs" : proposal is null ? "The search proposes nothing" : "The search's proposal falls short of the floors after costs")}: at least {FamilySweep.TradeFloor} trades and an edge above nothing in at least {FamilySweep.YearsBeating} of the 8 years.</p>"));
        page.Append("<div class=\"table\"><table><thead><tr><th>Read</th><th>Setting</th><th>Trades</th><th>Edge after costs</th><th>Years above nothing</th><th>2025</th><th>2026</th><th>Before costs</th><th>At double the cost</th><th>Without the five largest</th></tr></thead><tbody>");

        for (var at = 0; at < shown.Count; at++)
        {
            var trimmed = SweepStages.WithoutTheLargest(picks, design, space.Setting(shown[at].Point), space.Conditions(shown[at].Point));

            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(shown[at].Label)}</td><td>{WebUtility.HtmlEncode(space.Describe(shown[at].Point))}</td><td class=\"num\">{after[at].Scored:N0}</td><td class=\"num\">{FamilySweepReport.Number(after[at].Edge)}</td><td class=\"num\">{YearsAbove(after[at])} of 8</td><td class=\"num\">{FamilySweepReport.Number(after[at].YearEdge[^2])}</td><td class=\"num\">{FamilySweepReport.Number(after[at].YearEdge[^1])}</td><td class=\"num\">{FamilySweepReport.Number(before[at].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(doubled[at].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(trimmed.Edge)}</td></tr>"));
        }

        page.Append("</tbody></table></div>");
        page.Append(StageSection(
            dialsRead,
            [.. ten.Select(point => space.Describe(point))],
            [.. crossedLevels.Select(level => level.Dial + ", " + level.Name)],
            crossed,
            "The holds are read at 20, 40 and 63 sessions, the holds the candidates' exits carry; the dip is read against the mean return of its sector's own members on the index over the sessions since its reference high, the stock's own among them and the sector as its company fetch files it today; the report in the dip is the newest report's reaction inside those sessions; and the largest daily rise is each of the 21 sessions to the listing against the typical move before it."));
        page.Append(FormattableString.Invariant($"<h3>The survivors crossed with the profit and interest-cover check</h3><div class=\"table\"><table class=\"with-cover\"><thead><tr><th>Setting</th><th>Trades</th><th>Edge after costs</th><th>Years above nothing</th><th>Meets the floors</th></tr></thead><tbody>"));

        for (var at = 0; at < ten.Count; at++)
        {
            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(space.Describe(ten[at]))}</td><td class=\"num\">{crossedWithCover[at].Scored:N0}</td><td class=\"num\">{FamilySweepReport.Number(crossedWithCover[at].Edge)}</td><td class=\"num\">{YearsAbove(crossedWithCover[at])} of 8</td><td>{(MeetsTheFloors(crossedWithCover[at]) ? "yes" : "no")}</td></tr>"));
        }

        page.Append("</tbody></table></div>");
        page.Append(WouldRunSection(wouldRun.Key, wouldRun.Passed, wouldRun.Measures.Scored, wouldRun.Measures.Edge, wouldRun.Measures.YearEdge, wouldRun.Trimmed.Edge));
        page.Append(Luck(search.Evaluations, dialsRead.Count));

        if (!proposed)
        {
            var rows = shown.Skip(proposal is null ? 1 : 2).Select((one, at) => new Strongest(one.Label + ": " + space.Describe(one.Point), after[at + (proposal is null ? 1 : 2)].Scored, YearsAbove(after[at + (proposal is null ? 1 : 2)]), after[at + (proposal is null ? 1 : 2)].Edge));
            var five = SweepNonePassed.StrongestOf(rows);

            page.Append(SweepNonePassed.Section(five, [.. SweepNonePassed.FloorsMissed(five)], string.Empty, FamilySweepReport.Number));
        }

        page.Append("</main></body></html>");

        var report = WriteRun(
            folder,
            page.ToString(),
            new
            {
                index = indexCode,
                family = PullbackOwn,
                atTheNextOpen = true,
                note,
                candidates = all.Length,
                kept = candidates.Length,
                atOpen,
                noOpen,
                stepped,
                sampleSize,
                gridSize,
                evaluations = search.Evaluations,
                bestEdge = search.BestEdge,
                line = search.Line,
                leaders = leaders.Count,
                proposed,
                started,
                read = shown.Select((one, at) => new { one.Label, setting = space.Describe(one.Point), after = after[at], before = before[at], doubled = doubled[at] }),
                extensions,
                limits,
                strongestTen = ten.Select((point, at) => new { setting = space.Describe(point), after = withoutDials[at] }),
                dials = dialsRead,
                tries = tries.Select(one => new { one.Dial, one.Level, one.Switch, one.Setting, one.Kept, with = one.With }),
                crossedLevels = crossedLevels.Select(level => level.Dial + ", " + level.Name),
                crossed = crossed.Select((measures, at) => new { setting = space.Describe(ten[at]), after = measures, meets = MeetsTheFloors(measures) }),
                crossedWithCover = crossedWithCover.Select((measures, at) => new { setting = space.Describe(ten[at]), after = measures, meets = MeetsTheFloors(measures) }),
                wouldRun = new { key = wouldRun.Key, passed = wouldRun.Passed, after = wouldRun.Measures, withoutTheLargest = wouldRun.Trimmed },
            },
            new SweepAnswer(indexCode, SetupFamilies.Pullback, null, proposed || crossed.Any(MeetsTheFloors) || crossedWithCover.Any(MeetsTheFloors)));

        output.WriteLine(proposed
            ? FormattableString.Invariant($"proposed {space.Describe(proposal!.Point)}, edge after costs {FamilySweepReport.Number(after[1].Edge)} over {after[1].Scored:N0} trades, {YearsAbove(after[1])} of 8 years above nothing, before costs {FamilySweepReport.Number(before[1].Edge)}, at double {FamilySweepReport.Number(doubled[1].Edge)}")
            : FormattableString.Invariant($"none passed: the provisional base reads {FamilySweepReport.Number(after[0].Edge)} after costs over {after[0].Scored:N0} trades; the report states the strongest settings"));
        output.WriteLine(crossedLevels.Length == 0
            ? FormattableString.Invariant($"second stage: no level of {levels.Count} survives on {SweepDials.KeptOn} of the {ten.Count} strongest settings; with the cover check alone {crossedWithCover.Count(MeetsTheFloors)} of {ten.Count} meet the floors")
            : FormattableString.Invariant($"second stage: {string.Join("; ", crossedLevels.Select(level => level.Dial + ", " + level.Name))} survive; crossed, {crossed.Count(MeetsTheFloors)} of {ten.Count} meet the floors, with the cover check {crossedWithCover.Count(MeetsTheFloors)}"));
        output.WriteLine(WouldRunLine(wouldRun.Key, wouldRun.Passed, wouldRun.Measures.Scored, wouldRun.Measures.Edge, wouldRun.Measures.YearEdge, wouldRun.Trimmed.Edge));
        output.WriteLine("report " + report);

        return 0;
    }

    public const string HeavyweightsOwn = "heavyweights-own";

    public const string FollowersOwn = "heavyweights-b-own";

    // A holding bought at the open of the session after its rebalance, as the tester's books buy one: its return from that
    // open through the core's one pricing function, a fraction of the sum the rebalance's close put to it, and its size
    // cut's return each member from its own open after the rebalance, a member holding none counted as nothing as the walk
    // counts it; a holding with no open held after its rebalance is not bought, and one still held stands as walked.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    public static HeavyweightTrade AtTheOpen(HeavyweightTrade trade, HeavyweightTape tape, IReadOnlyList<SweepSeries> series, IReadOnlyDictionary<int, HeavyweightRebalance> rebalances)
    {
        if (trade.Result is not { } result || trade.End is not { } end)
        {
            return trade;
        }

        double? FillOver(int name) =>
            Array.BinarySearch(series[name].SessionAt, trade.Entry) is var bar && bar >= 0 && series[name].Bars[bar].Close > 0m && OpenFills.OpenAfter(series[name], bar) is { } open
                ? open / Statistic.FromPrice(series[name].Bars[bar].Close)
                : null;

        double? FromFill(int name)
        {
            var to = tape.LastMemberClose[name][end];

            return to >= trade.Entry && tape.Close[name][trade.Entry] > 0 && !double.IsNaN(tape.Close[name][to]) && FillOver(name) is { } fill
                ? TradeFills.AtOpen((tape.Close[name][to] / tape.Close[name][trade.Entry]) - 1.0, 1, 1, fill)
                : null;
        }

        var cut = rebalances.TryGetValue(trade.Entry, out var rebalance) ? rebalance.Sectors.FirstOrDefault(sector => sector.Sector == trade.Sector).Cut ?? [] : [];

        return trade with
        {
            Result = FillOver(trade.Name) is { } own ? TradeFills.AtOpen(result, 1, 1, own) : null,
            Cut = cut.Length > 0 ? cut.Average(name => FromFill(name) ?? 0.0) : trade.Cut,
        };
    }

    // The market check's report: each family's seven levels with their figures, 2025's and 2026's edges shown apart.
    static string MarketCheckPage(string named, string note, IReadOnlyList<MarketLevel> levels, IReadOnlyList<MarketCheckRow> rows, DateOnly from, DateOnly through, DateTimeOffset started)
    {
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " market check</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)}'s market check at seven levels</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p><p>Scored from {from:yyyy-MM-dd} through {through:yyyy-MM-dd}, run {started.UtcDateTime:yyyy-MM-dd HH:mm} UTC. The levels: {WebUtility.HtmlEncode(string.Join("; ", levels.Select(level => level.Key + ", " + level.Words)))}.</p>"));

        foreach (var family in rows.Select(row => row.Family).Distinct(StringComparer.Ordinal))
        {
            page.Append(FormattableString.Invariant($"<h2>The {WebUtility.HtmlEncode(family)}</h2><div class=\"table\"><table class=\"market-check\" data-family=\"{WebUtility.HtmlEncode(family)}\"><thead><tr><th>Level</th><th>Trades</th><th>Edge after costs</th><th>Years above nothing</th><th>2025</th><th>2026</th><th>Without the five largest</th><th>Sessions closed</th><th>Of the last {RecentClosedSessions}</th></tr></thead><tbody>"));

            foreach (var row in rows.Where(row => row.Family == family))
            {
                page.Append(FormattableString.Invariant($"<tr data-level=\"{WebUtility.HtmlEncode(row.Level)}\"><td>{WebUtility.HtmlEncode(row.Words)}</td><td class=\"num\">{row.Trades:N0}</td><td class=\"num\">{FamilySweepReport.Number(row.Edge)}</td><td class=\"num\">{row.YearsAboveNothing} of 8</td><td class=\"num\">{FamilySweepReport.Number(row.YearEdge[^2])} ({row.YearTrades[^2]:N0})</td><td class=\"num\">{FamilySweepReport.Number(row.YearEdge[^1])} ({row.YearTrades[^1]:N0})</td><td class=\"num\">{FamilySweepReport.Number(row.EdgeWithoutLargest)}</td><td class=\"num\">{row.ClosedOverHistory:N0}</td><td class=\"num\">{row.ClosedOverRecent}</td></tr>"));
            }

            page.Append("</tbody></table></div>");
        }

        return page.Append("</main></body></html>").ToString();
    }
}
