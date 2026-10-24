using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Worker.Sweep;

// From 22.2 the short-term dip swept on each index's own history, the S&P 500's among them: on membership as it stood, the
// floors its night reads, every listing bought at the next session's open through the core's one pricing function and
// walked by the rule's own walk to its sale, after its round trip at the published table, five a night in the family's
// order and one open trade a stock. The first stage crosses the drop, the trend, the exit, the time limit, the stop and
// the market setting; the second stage reads the order and the research's three filters alone on the ten strongest
// settings, the survivors crossed; and the setting it would run is stated with the same setting read with the report
// gate removed and with its exits sold at the triggering close, changing nothing. Nothing here is read by a night or
// pinned by a rule, so no rule's version moves for it.
// see: The short-term dip buys a stock in an uptrend after a sharp drop no report caused and sells the bounce within days
// see: The dip buys at the next session's open and sells at the next open after its exit's close or its stop's, and at its last session's close
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public sealed partial class IndexSweepRunner
{
    public const string Dip = DipRule.Name;

    public const string DipTester = "dip-tester";

    // The indices the dip is swept on, the S&P 500 among them since the dip reads its own rule on every index.
    public static IReadOnlyDictionary<string, string> DipIndices { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [IndexFamilies.LargeIndex] = "S&P 500",
        ["MID"] = "S&P 400",
        ["SML"] = "S&P 600",
    };

    // The drop dial's six levels as its keys state them: the 2-session RSI under 5, 10 or 15, and a fall over the drop's
    // three sessions of at least 2, 3 or 4 typical moves, written as the moves less than nothing.
    public static IReadOnlyList<double> DipDrops { get; } = [5, 10, 15, -2, -3, -4];

    // The trend dial's two levels: the close above its 200-session average, and that with the 50-session average above
    // the 200's.
    public static IReadOnlyList<double> DipTrends { get; } = [DipRule.TrendSessions, DipRule.FiftySessions];

    // The exit dial's three levels: the first close above the 5-session average of closes, the 2-session RSI closing back
    // above 65, and the first close above the close before.
    public const double DipExitAverage = 5;

    public const double DipExitRsi = 65;

    public const double DipExitUp = 1;

    public static IReadOnlyList<double> DipExits { get; } = [DipExitAverage, DipExitRsi, DipExitUp];

    public static IReadOnlyList<int> DipLimits { get; } = [5, 7, 10];

    // The stop in typical moves under the listing close, the wider standing in for no stop, since every swing result is in
    // multiples of the risk the stop sets.
    public static IReadOnlyList<double> DipStops { get; } = [3, 4];

    // The sessions the drop is read over, and the report gate's window back from the listing, the ledger's own.
    public const int DipDropSessions = Core.Ledger.LedgerReadings.DropSessions;

    // The VIX's level read against its own median of the sessions before.
    public const int VixMedianSessions = 252;

    // The second stage's two filters' levels: no session of the drop down more than this many typical moves, and the
    // drop's mean volume under this multiple of the 50 sessions' before it.
    public const double DipLargestSessionMoves = 3;

    public const double DipDropVolume = 1.5;

    public const int DipVolumeSessions = Core.Ledger.LedgerReadings.DropVolumeSessions;

    // The provisional setting's place on each dial: the 2-session RSI under 10, the close above its 200-session average, the
    // 5-session average's exit, a time limit of 10 sessions, a stop of 4 typical moves and no market setting.
    public static IReadOnlyList<int> DipProvisional { get; } = [1, 0, 0, 2, 1, 0];

    // The market dial's levels on an index: none; the index's own breadth at the four floors; on the S&P 400 and 600 the S&P
    // 500's breadth at its floor; the index's own fund above its 200-session average; and the VIX above its own median of
    // the 252 sessions before.
    public static IReadOnlyList<MarketLevel> DipMarketLevels(string indexCode) =>
    [
        new("off", "no market setting"),
        .. OwnBreadthFloors.Select(floor => new MarketLevel(
            FormattableString.Invariant($"own {floor:0.00}"),
            FormattableString.Invariant($"the index's own breadth at {floor * 100:0} per cent"))),
        .. indexCode == IndexFamilies.LargeIndex
            ? Array.Empty<MarketLevel>()
            : [new MarketLevel(FormattableString.Invariant($"S&P 500 {LargeBreadthFloor:0.00}"), FormattableString.Invariant($"the S&P 500's breadth at {LargeBreadthFloor * 100:0} per cent"))],
        new("fund trend", FormattableString.Invariant($"{DipFund(indexCode)} above its {FundTrendSessions}-session average")),
        new("VIX", FormattableString.Invariant($"the VIX above its own median of the {VixMedianSessions} sessions before")),
    ];

    // The fund each index's own trend is read on.
    public static string DipFund(string indexCode) => indexCode == IndexFamilies.LargeIndex ? "SPY" : IndexFunds[indexCode];

    // Whether each session's VIX close stands above the median of its closes over the sessions before it on the exchange's
    // calendar; a session missing its own close or any of those before it, or reached by fewer of them, reads nothing and
    // is closed. Days the VIX is quoted that are no session of the exchange's are passed over.
    public static bool[] VixAboveItsMedian(IReadOnlyList<DateOnly> calendar, SweepMarketSeries? vix, int over = VixMedianSessions)
    {
        var open = new bool[calendar.Count];

        if (vix is null)
        {
            return open;
        }

        var quoted = vix.Closes.ToDictionary(one => one.Session, one => one.Close);
        var closes = calendar.Select(session => quoted.TryGetValue(session, out var close) ? close : (double?)null).ToArray();
        var window = new double[over];

        for (var session = over; session < calendar.Count; session++)
        {
            if (closes[session] is not { } today)
            {
                continue;
            }

            var whole = true;

            for (var back = 0; back < over; back++)
            {
                if (closes[session - over + back] is { } close)
                {
                    window[back] = close;
                }
                else
                {
                    whole = false;

                    break;
                }
            }

            if (!whole)
            {
                continue;
            }

            Array.Sort(window);

            var median = over % 2 == 1 ? window[over / 2] : (window[(over / 2) - 1] + window[over / 2]) / 2;

            open[session] = today > median;
        }

        return open;
    }

    // The dip on an index's own history as its sweep and the tester's procedure both read it: the grid, each setting's
    // listings and their trades at the next open after their round trips, the benchmark of the same plan on every member,
    // the provisional setting the night reads today, and the second stage's levels.
    public sealed class DipRead : OwnProcedures.IOwnSwing
    {
        readonly double[][] closes;
        readonly double[][] opens;
        readonly double?[][] rsi;
        readonly int[][] gaps;
        readonly bool[][] clears;
        readonly int[][] reports;
        readonly ConcurrentDictionary<(int Drop, int Trend, int Limit, int Stop, bool Reports), FamilyListing[]> listings = new();
        readonly ConcurrentDictionary<(int Name, int Bar, int Exit, int Limit, int Stop, bool AtTrigger), Walked> walks = new();
        readonly ConcurrentDictionary<(int Session, int Exit, int Limit, int Stop), double> benchmarks = new();
        readonly ConcurrentDictionary<(int Session, string Sector), double?> sectorFalls = new();

        DipRead(LoopRead read, IReadOnlyList<MarketLevel> marketLevels, bool[][] open)
        {
            Index = read.Index;
            Read = read;
            Series = read.Series;
            Calendar = read.Calendar;
            FirstScored = read.FirstScored;
            Nights = Calendar.Length - FirstScored;
            Tickers = [.. read.Inputs.Names.Select(name => name.Ticker)];
            MarketLevels = marketLevels;
            Open = open;
            Fills = new OpenFills(read.Series, read.Members);

            var count = Series.Length;

            closes = new double[count][];
            opens = new double[count][];
            rsi = new double?[count][];
            gaps = new int[count][];
            clears = new bool[count][];
            reports = new int[count][];

            Parallel.For(0, count, name =>
            {
                var one = Series[name];
                var bars = one.Bars;

                closes[name] = [.. bars.Select(bar => Statistic.FromPrice(bar.Close))];
                opens[name] = [.. bars.Select(bar => bar.Open > 0m ? Statistic.FromPrice(bar.Open) : 0)];
                rsi[name] = DipRule.Rsi(closes[name]);
                gaps[name] = new int[bars.Length + 1];
                clears[name] = new bool[bars.Length];

                for (var bar = 0; bar < bars.Length; bar++)
                {
                    gaps[name][bar + 1] = gaps[name][bar] + (one.Gap[bar] ? 1 : 0);
                    clears[name][bar] = ClearsTheDipsFloors(read.Index, one, bar);
                }

                reports[name] =
                [
                    .. one.Name.Prints
                        .Concat(one.Name.Surprises.Select(surprise => surprise.EventDate))
                        .Select(day => SessionOnOrAfter(Calendar, day))
                        .Where(session => session < Calendar.Length)
                        .Distinct()
                        .Order(),
                ];
            });

            var marketDial = Enumerable.Range(0, marketLevels.Count).Select(place => (double)place).ToArray();

            Grid = new FamilyGrid(
                [("drop", DipDrops), ("trend", DipTrends), ("exit", DipExits), ("limit", [.. DipLimits.Select(limit => (double)limit)]), ("stop", DipStops), ("market", marketDial)],
                DipProvisional);
            Levels = LevelsOf();
        }

        public static DipRead Of(LoopRead read, SweepHistoryInputs large, IReadOnlyList<SweepMarketSeries> market)
        {
            var levels = DipMarketLevels(read.Index);
            var fund = market.FirstOrDefault(one => one.Series == DipFund(read.Index));
            var vix = market.FirstOrDefault(one => one.Series == "VIX");
            var open = new List<bool[]> { Enumerable.Repeat(true, read.Calendar.Length).ToArray() };

            foreach (var floor in OwnBreadthFloors)
            {
                open.Add([.. read.Sessions.Select(one => one.Breadth >= floor)]);
            }

            if (read.Index != IndexFamilies.LargeIndex)
            {
                var largeBreadth = SweepSwitches.LargeBreadth(large, read.Calendar);

                open.Add([.. Enumerable.Range(0, read.Calendar.Length).Select(at => at < largeBreadth.Length && largeBreadth[at] >= LargeBreadthFloor)]);
            }

            open.Add(FundAboveItsAverage(read.Calendar, fund));
            open.Add(VixAboveItsMedian(read.Calendar, vix));

            return new DipRead(read, levels, [.. open]);
        }

        public string Index { get; }

        public LoopRead Read { get; }

        public string Family => Dip;

        public SweepSeries[] Series { get; }

        public DateOnly[] Calendar { get; }

        public int FirstScored { get; }

        public int Nights { get; }

        public string[] Tickers { get; }

        public IReadOnlyList<MarketLevel> MarketLevels { get; }

        public bool[][] Open { get; }

        public OpenFills Fills { get; }

        public FamilyGrid Grid { get; }

        public bool LastTwo => false;

        public bool Floored => false;

        public IReadOnlyList<DialLevel<FamilyListing>> Levels { get; }

        public int YearOf(int session) => Calendar[session].Year - SweepColumns.FirstScored.Year;

        // The first session of the exchange's calendar on or after a day, which a report dated on a closed day is read on.
        static int SessionOnOrAfter(DateOnly[] calendar, DateOnly day)
        {
            var at = Array.BinarySearch(calendar, day);

            return at >= 0 ? at : ~at;
        }

        // The floors the dip's night reads on a member: its index's floors through the shared function, and the least close
        // on every index, the S&P 500's among them.
        static bool ClearsTheDipsFloors(string indexCode, SweepSeries one, int bar)
        {
            var held = one.Bars[bar];
            var traded = held.RawClose > 0m ? held.RawClose : held.Close;

            return Clears(indexCode, one, bar, [], IndexQuality.Off) && traded >= MemberReadings.LowestPrice;
        }

        // A member's fall over the drop's sessions in its typical moves on the listing session, through the ledger's own
        // reading of it, none where it holds too few sessions or no typical move.
        public double FallOf(int name, int bar) => Ledger.LedgerContext.DropFall(Series[name], bar);

        // A setting's listings, the market dial aside: each member-session of the scored history its index held, holding the
        // 200 sessions its average reads with none missing, clearing the floors, its close above its average, its drop at
        // the setting's level, no report in the drop or before its time limit ends where the gate is read, and a stop the
        // setting's typical moves under its close above nothing; ordered by the lowest 2-session RSI first.
        public FamilyListing[] ListingsOf(int[] setting, bool readReports = true) =>
            listings.GetOrAdd((setting[0], setting[1], setting[3], setting[4], readReports), key =>
            {
                var drop = DipDrops[key.Drop];
                var fifty = DipTrends[key.Trend] == DipRule.FiftySessions;
                var limit = DipLimits[key.Limit];
                var stopMoves = DipStops[key.Stop];
                var found = new ConcurrentBag<FamilyListing>();

                Parallel.For(0, Series.Length, name =>
                {
                    var one = Series[name];

                    for (var bar = DipRule.TrendSessions - 1; bar < one.Bars.Length; bar++)
                    {
                        var session = one.SessionAt[bar];
                        var close = closes[name][bar];
                        var move = one.Atr[bar];

                        if (session < FirstScored || !one.Member[bar] || !clears[name][bar]
                            || gaps[name][bar + 1] - gaps[name][bar + 1 - DipRule.TrendSessions] > 0
                            || !(close > one.Sma200[bar]) || (fifty && !(one.Sma50[bar] > one.Sma200[bar]))
                            || !(move > 0) || !(close - (stopMoves * move) > 0)
                            || rsi[name][bar] is not { } read)
                        {
                            continue;
                        }

                        var dropped = drop > 0 ? read < drop : FallOf(name, bar) >= -drop;

                        if (!dropped || (readReports && ReportInside(name, session, limit)))
                        {
                            continue;
                        }

                        found.Add(new FamilyListing(name, bar, session, -read, 0, close, close - (stopMoves * move), double.NaN, double.NaN, limit, move));
                    }
                });

                return [.. found.OrderBy(listing => listing.Session).ThenBy(listing => listing.Name)];
            });

        bool ReportInside(int name, int session, int limit)
        {
            var held = reports[name];
            var from = Array.BinarySearch(held, session - DipDropSessions + 1);

            from = from >= 0 ? from : ~from;

            return from < held.Length && held[from] <= session + limit;
        }

        // One walk of a listing: its result bought at the listing close, the sessions it holds its stock for the rule of one
        // trade a stock, the sessions to its sale, and its result where an exit is sold at its triggering close; none of the
        // results while it is not bought or still held where the history ends.
        public sealed record Walked(double? AtClose, int Held, int Sale, double? AtTrigger);

        public Walked WalkOf(int name, int bar, int exit, int limit, int stop, bool atTrigger = false) =>
            walks.GetOrAdd((name, bar, exit, limit, stop, atTrigger), key =>
            {
                var one = Series[key.Name];
                var close = closes[key.Name][key.Bar];
                var risk = DipStops[key.Stop] * one.Atr[key.Bar];
                var level = DipExits[key.Exit];
                var sessions = DipLimits[key.Limit];
                var after = new List<DipSession>(sessions + 1);

                for (var at = key.Bar + 1; at < one.Bars.Length && after.Count <= sessions; at++)
                {
                    if (one.SessionAt[at] - one.SessionAt[at - 1] > 1)
                    {
                        break;
                    }

                    after.Add(new DipSession(
                        one.Bars[at].Session,
                        opens[key.Name][at],
                        closes[key.Name][at],
                        level == DipExitAverage ? DipRule.Average(closes[key.Name], at, (int)DipExitAverage) : null,
                        level == DipExitRsi ? rsi[key.Name][at] > DipExitRsi : level == DipExitUp ? closes[key.Name][at] > closes[key.Name][at - 1] : null));
                }

                var path = DipRule.Walk(close, close - risk, sessions, after);

                return path.End switch
                {
                    _ when path.BoughtOn is null => new Walked(null, 0, 0, null),
                    DipEnd.Open => new Walked(null, after.Count, 0, null),
                    DipEnd.Limit => new Walked(path.ResultAtClose, path.Sessions - 1, path.Sessions, path.ResultAtClose),
                    _ => new Walked(path.ResultAtClose, path.Sessions, path.Sessions + 1, (after[path.Sessions - 1].Close - close) / risk),
                };
            });

        // The same plan on every member the index held with a bar and no gap that session, each bought at its own next open:
        // the mean of their results at the close, none where none could be bought or any still held where the history
        // ends, since a benchmark is settled only once every path has ended.
        double BenchmarkAtClose(int session, int exit, int limit, int stop) =>
            benchmarks.GetOrAdd((session, exit, limit, stop), key =>
            {
                var (names, bars) = (Read.Members.Names[key.Session], Read.Members.Bars[key.Session]);
                var (sum, count) = (0.0, 0);

                for (var place = 0; place < names.Length; place++)
                {
                    var (name, bar) = (names[place], bars[place]);
                    var move = Series[name].Atr[bar];

                    if (!(move > 0) || !(closes[name][bar] - (DipStops[key.Stop] * move) > 0))
                    {
                        continue;
                    }

                    var walked = WalkOf(name, bar, key.Exit, key.Limit, key.Stop);

                    if (walked.AtClose is { } result)
                    {
                        sum += result;
                        count++;
                    }
                    else if (walked.Held > 0)
                    {
                        return double.NaN;
                    }
                }

                return count > 0 ? sum / count : double.NaN;
            });

        // A setting's trades at the next open before costs, five a night in the family's order and one open trade a stock,
        // each walked by the rule's walk and priced at its own next open; where a map is handed in, the sessions to each
        // trade's sale are kept in it by its stock and its session.
        public IReadOnlyList<FamilyTrade> AtOpen(IEnumerable<FamilyListing> chosen, int[] setting, ConcurrentDictionary<(int Name, int Session), int>? held = null, bool atTrigger = false)
        {
            var (exit, limit, stop) = (setting[2], setting[3], setting[4]);

            return FamilySweep.Walk(
                chosen,
                Tickers,
                YearOf,
                listing =>
                {
                    var walked = WalkOf(listing.Name, listing.Bar, exit, limit, stop, atTrigger);

                    if (held is not null)
                    {
                        held[(listing.Name, listing.Session)] = walked.Sale;
                    }

                    return Fills.Exit((atTrigger ? walked.AtTrigger : walked.AtClose, walked.Held), listing);
                },
                listing => Fills.Benchmark(BenchmarkAtClose(listing.Session, exit, limit, stop), listing));
        }

        // Each trade's result after its round trip at a multiple of the published table, the benchmark paying none.
        public IReadOnlyList<FamilyTrade> Costed(IReadOnlyList<FamilyTrade> trades, int multiple) =>
            [.. trades.Select(trade => trade.Result is { } result ? trade with { Result = result - CostInRisk(Series[trade.Listing.Name], trade.Listing, result, Read.Companies, multiple) } : trade)];

        // A setting's listings at its market level.
        public FamilyListing[] FirstStage(int[] setting)
        {
            var level = Open[setting[^1]];

            return [.. ListingsOf(setting).Where(listing => level[listing.Session])];
        }

        // A setting's trades under a set of the second stage's levels together, after costs at the next open: each level's
        // test kept, each level's move of the order or the drop applied, and the market level read on the listing's session;
        // the report gate read or not, and the exits sold at the next open or at their triggering close.
        public IReadOnlyList<FamilyTrade> TradesUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together, ConcurrentDictionary<(int Name, int Session), int>? held = null) =>
            TradesUnder(setting, together, held, readReports: true, atTrigger: false);

        public IReadOnlyList<FamilyTrade> TradesUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together, ConcurrentDictionary<(int Name, int Session), int>? held, bool readReports, bool atTrigger)
        {
            var level = Open[setting[^1]];
            var moves = together.Select(one => one.Instead).OfType<Func<FamilyListing, int[], IEnumerable<FamilyListing>>>().ToArray();
            var chosen = ListingsOf(setting, readReports)
                .Where(listing => level[listing.Session] && together.All(one => one.Keep is not { } keep || keep(listing)))
                .SelectMany(listing => moves.Aggregate((IEnumerable<FamilyListing>)[listing], (current, move) => current.SelectMany(one => move(one, setting))))
                .ToArray();

            return Costed(AtOpen(chosen, setting, held, atTrigger), 1);
        }

        // The rule the index's night reads today: the provisional setting, after each trade's round trip at the next open.
        public IReadOnlyList<FamilyTrade> NightsRule(ConcurrentDictionary<(int Name, int Session), int>? held = null) =>
            TradesUnder([.. Grid.Provisional], [], held);

        // The strongest settings the second stage reads: the highest edges among those holding the trades it holds, then
        // the most trades.
        public int[][] Strongest(IReadOnlyList<(int[] Setting, FamilyFigures Figures)> read, int tradesHeld = SweepDials.TradesHeld) =>
        [
            .. read
                .OrderBy(one => one.Figures.Trades >= tradesHeld ? 0 : 1)
                .ThenByDescending(one => one.Figures.Trades >= tradesHeld ? one.Figures.Edge ?? double.MinValue : one.Figures.Trades)
                .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
                .Take(SweepDials.Settings)
                .Select(one => one.Setting),
        ];

        // A listing's company's value on its session, as the order by the largest reads it; none where no count is filed.
        double ValueOf(FamilyListing listing)
        {
            var one = Series[listing.Name];
            var bar = one.Bars[listing.Bar];

            return CompanyValue.On(new SessionClose(bar.Session, bar.Close, bar.RawClose > 0m ? bar.RawClose : bar.Close), Read.Companies.Counts.GetValueOrDefault(one.Name.Ticker) ?? [], Read.Companies.Splits.GetValueOrDefault(one.Name.Ticker) ?? []) is { } value
                ? Statistic.FromPrice(value)
                : double.NegativeInfinity;
        }

        // Whether no session of the drop fell more than the moves in the typical move standing before it, through the
        // ledger's own reading of the drop's largest session.
        public bool NoSessionFellOver(FamilyListing listing, double moves) =>
            Ledger.LedgerSetups.DropLargest(Series[listing.Name], closes[listing.Name], listing.Bar) is not { } largest || largest <= moves;

        // The drop's mean volume over the mean of the sessions before it, through the ledger's own reading of it, none where
        // too few stand before it.
        public double DropVolume(FamilyListing listing) => Ledger.LedgerSetups.DropVolume(Series[listing.Name], listing.Bar) ?? double.NaN;

        // The median fall over the drop's sessions, in each member's own typical moves, of the members of a sector the index
        // held that session; none where fewer than two read one.
        double? SectorFall(int session, string sector) =>
            sectorFalls.GetOrAdd((session, sector), key =>
            {
                var (names, bars) = (Read.Members.Names[key.Session], Read.Members.Bars[key.Session]);
                var falls = new List<double>();

                for (var place = 0; place < names.Length; place++)
                {
                    if (string.Equals(SectorOf(names[place]), key.Sector, StringComparison.Ordinal) && FallOf(names[place], bars[place]) is var fall && !double.IsNaN(fall))
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

        public string? SectorOf(int name) => Read.Companies.Companies.GetValueOrDefault(Tickers[name]).Sector;

        // The drop measured against the sector: the stock's fall less its sector's median member's over the same sessions, in
        // typical moves, at the setting's own fall where it reads one and above nothing where it reads the RSI; a member
        // filing no sector, or a sector too thin to read, keeps none.
        public bool FallsAgainstItsSector(FamilyListing listing, int[] setting)
        {
            if (SectorOf(listing.Name) is not { } sector || SectorFall(listing.Session, sector) is not { } median || FallOf(listing.Name, listing.Bar) is var fall && double.IsNaN(fall))
            {
                return false;
            }

            var drop = DipDrops[setting[0]];

            return drop < 0 ? fall - median >= -drop : fall - median > 0;
        }

        // The second stage's levels: the order, the deepest fall or the largest company first in place of the lowest RSI; no
        // session of the drop down more than three typical moves; the drop's mean volume under one and a half times the 50
        // sessions' before it; and the drop read against its sector's median member's.
        List<DialLevel<FamilyListing>> LevelsOf() =>
        [
            new("order", "the deepest fall in typical moves first", Instead: (listing, _) => [listing with { Order = FallOf(listing.Name, listing.Bar) is var fall && double.IsNaN(fall) ? double.NegativeInfinity : fall }]),
            new("order", "the largest company first", Instead: (listing, _) => [listing with { Order = ValueOf(listing) }]),
            new("the drop's largest session", FormattableString.Invariant($"no session down more than {DipLargestSessionMoves:0} typical moves"), Keep: listing => NoSessionFellOver(listing, DipLargestSessionMoves)),
            new("the drop's volume", FormattableString.Invariant($"under {DipDropVolume:0.#} times its {DipVolumeSessions}-session average"), Keep: listing => DropVolume(listing) < DipDropVolume),
            new("the drop against its sector", "its fall less its sector's median member's", Instead: (listing, setting) => FallsAgainstItsSector(listing, setting) ? [listing] : []),
        ];
    }

    // The dip's sweep on one index: the grid's first stage at every market level, its proposal, the second stage over the ten
    // strongest settings with the survivors crossed, the setting it would run with the two figures beside it that change
    // nothing, and the report with its figures and its answer.
    // see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    async Task<int> DipAsync(string indexCode, bool tester, CancellationToken cancellation)
    {
        if (!DipIndices.TryGetValue(indexCode, out var named))
        {
            output.WriteLine($"{Verb}: name an index with '--index', one of {string.Join(", ", DipIndices.Keys)}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, FamilySweepRunner.Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var large = indexCode == IndexFamilies.LargeIndex ? null : await history.ReadAsync(through, null, cancellation);
        var inputs = indexCode == IndexFamilies.LargeIndex
            ? await history.ReadAsync(through, output.WriteLine, cancellation)
            : await history.ReadAsync(through, output.WriteLine, cancellation, index: indexCode, asItStood: true);

        if (inputs.Names.Count == 0)
        {
            output.WriteLine($"{Verb}: the store holds no member of the {named}; pull them first with 'history-pull --members --index {indexCode}', then their history");

            return 2;
        }

        var companies = await history.HeavyweightAsync(through, cancellation);
        var income = await history.IncomeAsync(through, cancellation);
        var market = await history.SeriesOfAsync([DipFund(indexCode), "VIX"], through, cancellation);
        var folder = Claim(SweepFolder.Resolve(configuredFolder, dataRoot), started);
        var laid = OwnSwingRead.Laid(indexCode, inputs, companies, income, through);
        var dip = DipRead.Of(laid, large ?? inputs, market);

        output.WriteLine("run " + Path.GetFileName(folder));

        if (tester)
        {
            if (laid.Folds.Count == 0)
            {
                output.WriteLine(FormattableString.Invariant($"{Verb}: the history ends {through:yyyy-MM-dd}, before the first test year, {LoopFolds.FirstTestYear}"));

                return 2;
            }

            var tested = WalkForwardTester.Judge(laid, [OwnProcedures.Swing(laid, dip, output.WriteLine)]);

            return Tested(indexCode, named, "short-term dip's search on its own history, run year by year,", inputs, laid, through, started, folder, tested, "The dip's grid crosses its drop, trend, exit, time limit and stop with its market setting as a dial, every trade walked by the rule's own walk and bought at the next open; each fold reads its own second stage over its own ten strongest settings, a level kept where it is higher in six of every eight of the fold's years, rounded up, with two of the last three, on six of the ten, and its survivors crossed, the fold choosing the stronger of its grid's choice and its crossings meeting the floors in proportion.");
        }

        return DipSweep(indexCode, named, inputs, dip, through, folder, started);
    }

    int DipSweep(string indexCode, string named, SweepHistoryInputs inputs, DipRead dip, DateOnly through, string folder, DateTimeOffset started)
    {
        var (grid, nights, levels) = (dip.Grid, dip.Nights, dip.Levels);
        var settings = grid.Settings;
        var read = new (int[] Setting, FamilyFigures Figures)[settings.Count];
        var before = new FamilyFigures[settings.Count];
        var doubled = new FamilyFigures[settings.Count];
        var listed = new long[settings.Count];

        output.WriteLine(FormattableString.Invariant($"walking the dip's {settings.Count} setting(s), its grid at each of its {dip.MarketLevels.Count} market levels, at the next open"));

        // Each setting's listings walked once a setting; the walks and the benchmarks they read are kept as they are read, so
        // the settings sharing a plan read them once.
        Parallel.For(0, settings.Count, at =>
        {
            var setting = settings[at];
            var chosen = dip.FirstStage(setting);
            var trades = dip.AtOpen(chosen, setting);
            var key = grid.Key(setting);

            listed[at] = chosen.Length;
            before[at] = FamilySweep.Figures(key, trades, nights);
            read[at] = (setting, FamilySweep.Figures(key, dip.Costed(trades, 1), nights));
            doubled[at] = FamilySweep.Figures(key, dip.Costed(trades, TradeCost.Doubled), nights);
        });

        var proposal = FamilySweep.Propose(grid, read);
        var ten = dip.Strongest(read);

        output.WriteLine(FormattableString.Invariant($"the second stage: {levels.Count} level(s) alone on the {ten.Length} strongest settings"));

        var withoutDials = ten.Select(setting => SweepDials.Of(dip.TradesUnder(setting, []))).ToArray();
        var tries = levels
            .SelectMany(level => ten.Select((setting, at) => new DialTry(level.Dial, level.Name, level.Switch, at, withoutDials[at], SweepDials.Of(dip.TradesUnder(setting, [level])))))
            .ToList();
        var dialsRead = SweepDials.Read(tries);
        var crossedLevels = dialsRead
            .Where(one => one.KeptOn >= SweepDials.KeptOn)
            .GroupBy(one => one.Dial)
            .Select(dial => dial.OrderByDescending(one => one.KeptOn).ThenByDescending(one => one.MedianChange ?? double.MinValue).First())
            .Select(best => levels.First(level => level.Dial == best.Dial && level.Name == best.Level))
            .ToArray();
        var crossedTrades = crossedLevels.Length > 0 ? ten.Select(setting => dip.TradesUnder(setting, crossedLevels)).ToArray() : [];
        var crossed = crossedTrades.Select(SweepDials.Of).ToArray();

        // The setting the dip would run on the index: the best edge after costs among its proposal and its crossings meeting
        // the floors, or where none does, the strongest setting its first stage read.
        // see: The setting a family on the S&P 400 or 600 would run is the best edge among its passing settings, or the strongest read where none passes
        (FamilyFigures Figures, int[] Setting, IReadOnlyList<DialLevel<FamilyListing>> Levels, bool Passed) WouldRun()
        {
            var passing = new List<(FamilyFigures Figures, int[] Setting, IReadOnlyList<DialLevel<FamilyListing>> Levels)>();

            if (proposal.Proposed is { } proposedFigures)
            {
                passing.Add((proposedFigures, read.First(one => one.Figures.Key == proposedFigures.Key).Setting, []));
            }

            for (var at = 0; at < crossed.Length; at++)
            {
                if (MeetsTheFloors(crossed[at]))
                {
                    passing.Add((FamilySweep.Figures(OwnProcedures.KeyOf(grid, ten[at], crossedLevels), crossedTrades[at], nights), ten[at], crossedLevels));
                }
            }

            if (passing.Count > 0)
            {
                var best = passing.OrderByDescending(one => one.Figures.Edge ?? double.MinValue).ThenBy(one => one.Figures.Key, StringComparer.Ordinal).First();

                return (best.Figures, best.Setting, best.Levels, true);
            }

            var strongest = read
                .OrderBy(one => one.Figures.Trades >= FamilySweep.TradeFloor ? 0 : 1)
                .ThenByDescending(one => one.Figures.Trades >= FamilySweep.TradeFloor ? one.Figures.Edge ?? double.MinValue : one.Figures.Trades)
                .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
                .First();

            return (strongest.Figures, strongest.Setting, [], false);
        }

        var wouldRun = WouldRun();
        var noReportGate = FamilySweep.Figures(wouldRun.Figures.Key, dip.TradesUnder(wouldRun.Setting, wouldRun.Levels, null, readReports: false, atTrigger: false), nights);
        var atTheTrigger = FamilySweep.Figures(wouldRun.Figures.Key, dip.TradesUnder(wouldRun.Setting, wouldRun.Levels, null, readReports: true, atTrigger: true), nights);
        var provisional = grid.Key([.. grid.Provisional]);
        var nightsRule = FamilySweep.Figures(provisional, dip.NightsRule(), nights);
        var legend = FormattableString.Invariant($"drop=5, 10 or 15 is the 2-session RSI under that level and drop=-2, -3 or -4 a fall over the drop's {DipDropSessions} sessions of at least that many typical moves; trend=200 the close above its 200-session average and trend=50 that with the 50-session average above the 200's; exit=5 the first close above the 5-session average of closes, exit=65 the 2-session RSI closing back above 65 and exit=1 the first up close; limit the sessions held at most; stop the typical moves under the listing close; ")
            + string.Join("; ", dip.MarketLevels.Select((level, at) => FormattableString.Invariant($"market={at} {level.Words}")));
        var note = FormattableString.Invariant(
            $"{(indexCode == IndexFamilies.LargeIndex ? FormattableString.Invariant($"As it stood: the {inputs.Names.Count:N0} names the S&P 500 held on any session of the history, each read as a member over the spans the index held it.") : Membership(named, inputs))} Every listing is bought at the open of the session after it through the core's one pricing function and walked by the rule's own walk, sold at the next open after the close that triggers its exit or its stop and at the close of its last session where neither came, its result in multiples of the risk the listing close set; its benchmark is the same plan on every member the index held with a bar that session, each bought at its own next open, settled only once every member's path has ended; and every edge is after the trade's round trip at the published table, the benchmark paying none. A listing is kept only where its close was at least $5{(MemberReadings.DollarVolumeFloor(indexCode) is { } floor ? FormattableString.Invariant($" and its mean dollar volume over the 50 sessions to it at least {floor:N0} dollars") : string.Empty)}, it held the {DipRule.TrendSessions} sessions its average reads with none of them missing, and no report on file fell on the drop's {DipDropSessions} sessions or before its time limit ended, a report dated on a closed day read at the session after it. The dials: {legend}. The market series are read from the history pulled for them, so a market level reads a session after its series' last as closed. The provisional setting is the one the index's night reads today, {provisional}.");
        var run = new FamilySweepRun(Dip, $"{named} short-term dip's at the next open", dip.Calendar[dip.FirstScored], through, inputs.Names.Count, nights, nights, 0, started, clock.UtcNow, note);
        var shown = new List<string> { provisional };

        if (proposal.Proposed is { } proposed)
        {
            shown.Add(proposed.Key);
        }
        else
        {
            shown.AddRange(SweepNonePassed.StrongestOf(read.Select(one => new Strongest(one.Figures.Key, one.Figures.Trades, one.Figures.YearsBeating, one.Figures.Edge))).Select(one => one.Key));
        }

        var afterCosts = read.ToDictionary(one => one.Figures.Key, one => one.Figures, StringComparer.Ordinal);
        var beforeCosts = before.ToDictionary(one => one.Key, StringComparer.Ordinal);
        var atDouble = doubled.ToDictionary(one => one.Key, StringComparer.Ordinal);
        var stage = StageSection(
            dialsRead,
            [.. ten.Select(setting => grid.Key(setting))],
            [.. crossedLevels.Select(level => level.Dial + ", " + level.Name)],
            crossed,
            FormattableString.Invariant($"The order is read with the deepest fall in typical moves first or the largest company by its value on the listing's session first in place of the lowest RSI; the drop's largest session at most {DipLargestSessionMoves:0} typical moves of the typical move standing before it; the drop's mean volume under {DipDropVolume:0.#} times the mean of the {DipVolumeSessions} sessions before it; and the drop against its sector as the stock's fall over the drop's sessions less its sector's median member's on the index over the same sessions, each in its own typical moves, at the setting's own fall where it reads one and above nothing where it reads the RSI."));
        var report = WriteRun(
            folder,
            FamilySweepReport.Build(run, grid, read, proposal)
                + Costs([.. shown.Distinct(StringComparer.Ordinal).Where(afterCosts.ContainsKey)], beforeCosts, afterCosts, atDouble)
                + stage
                + WouldRunSection(wouldRun.Figures.Key, wouldRun.Passed, wouldRun.Figures.Trades, wouldRun.Figures.Edge, wouldRun.Figures.YearEdge, wouldRun.Figures.EdgeWithoutLargest)
                + DipAnswerSection(named, wouldRun.Figures, wouldRun.Passed, nightsRule, noReportGate, atTheTrigger, read.Length)
                + Luck(read.Length, dialsRead.Count),
            new
            {
                index = indexCode,
                family = Dip,
                atTheNextOpen = true,
                marketLevels = dip.MarketLevels,
                run,
                listed = listed.Sum(),
                provisional,
                nightsRule,
                proposal = proposal.Proposed?.Key,
                afterCosts = read.Select(one => one.Figures),
                beforeCosts = before,
                atDoubleCost = doubled,
                strongestTen = ten.Select((setting, at) => new { setting = grid.Key(setting), after = withoutDials[at] }),
                dials = dialsRead,
                tries = tries.Select(one => new { one.Dial, one.Level, one.Switch, one.Setting, one.Kept, with = one.With }),
                crossedLevels = crossedLevels.Select(level => level.Dial + ", " + level.Name),
                crossed = crossed.Select((measures, at) => new { setting = grid.Key(ten[at]), after = measures, meets = MeetsTheFloors(measures) }),
                luck = new { settings = HeavyweightSweep.Luck(read.Length), levels = dialsRead.Count * SweepDials.LuckOfALevel },
                wouldRun = new { key = wouldRun.Figures.Key, passed = wouldRun.Passed, figures = wouldRun.Figures },
                besideIt = new { withoutTheReportGate = noReportGate, soldAtTheTriggeringClose = atTheTrigger },
            },
            new SweepAnswer(indexCode, Dip, null, proposal.Proposed is not null || crossed.Any(MeetsTheFloors)));

        output.WriteLine(proposal.Proposed is { } shownProposal
            ? FormattableString.Invariant($"proposed {shownProposal.Key}, edge after costs {FamilySweepReport.Number(shownProposal.Edge)} over {shownProposal.Trades} trades, {shownProposal.YearsBeating} of 8 years, before costs {FamilySweepReport.Number(beforeCosts[shownProposal.Key].Edge)}, at double {FamilySweepReport.Number(atDouble[shownProposal.Key].Edge)}")
            : "none passed: no setting meets the floors after costs, and the report states the strongest settings and what could be tried next");
        output.WriteLine(FormattableString.Invariant($"provisional {provisional}: edge after costs {FamilySweepReport.Number(nightsRule.Edge)} over {nightsRule.Trades} trades, {nightsRule.YearsBeating} of 8 years"));
        output.WriteLine(crossedLevels.Length == 0
            ? FormattableString.Invariant($"second stage: no level of {levels.Count} survives on {SweepDials.KeptOn} of the {ten.Length} strongest settings")
            : FormattableString.Invariant($"second stage: {string.Join("; ", crossedLevels.Select(level => level.Dial + ", " + level.Name))} survive; crossed, {crossed.Count(MeetsTheFloors)} of {ten.Length} meet the floors, the strongest reading {FamilySweepReport.Number(crossed.Max(one => one.Edge))} after costs"));
        output.WriteLine(WouldRunLine(wouldRun.Figures.Key, wouldRun.Passed, wouldRun.Figures.Trades, wouldRun.Figures.Edge, wouldRun.Figures.YearEdge, wouldRun.Figures.EdgeWithoutLargest));
        output.WriteLine(FormattableString.Invariant($"beside it: with the report gate removed {FamilySweepReport.Number(noReportGate.Edge)} over {noReportGate.Trades:N0} trades; with its exits sold at the triggering close {FamilySweepReport.Number(atTheTrigger.Edge)} over {atTheTrigger.Trades:N0}"));
        output.WriteLine("report " + report);

        return 0;
    }

    // The index's answer as the operator reads it: the setting the dip would run with its trades, its edge after costs, each
    // year's edge with 2020, 2022, 2025 and 2026 named, the edge without its five largest results and the years above
    // nothing; the rule its night reads today beside it; and beside them, changing nothing, the same setting with the
    // report gate removed and with its exits sold at the triggering close.
    static string DipAnswerSection(string named, FamilyFigures runs, bool passed, FamilyFigures nightsRule, FamilyFigures noReportGate, FamilyFigures atTheTrigger, int settings)
    {
        string Year(FamilyFigures figures, int year) => FamilySweepReport.Number(figures.YearEdge[year - SweepColumns.FirstScored.Year]);

        string Row(string what, FamilyFigures figures) =>
            FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(what)}</td><td>{WebUtility.HtmlEncode(figures.Key)}</td><td class=\"num\">{figures.Trades:N0}</td><td class=\"num\">{FamilySweepReport.Number(figures.Edge)}</td><td class=\"num\">{Year(figures, 2020)}</td><td class=\"num\">{Year(figures, 2022)}</td><td class=\"num\">{Year(figures, 2025)}</td><td class=\"num\">{Year(figures, 2026)}</td><td class=\"num\">{FamilySweepReport.Number(figures.EdgeWithoutLargest)}</td><td class=\"num\">{figures.YearsBeating} of 8</td></tr>");

        var page = new StringBuilder(FormattableString.Invariant($"<h2>The {WebUtility.HtmlEncode(named)}'s answer</h2><p class=\"dip-answer\" data-passed=\"{(passed ? "yes" : "no")}\">{(passed ? "The setting it would run passes the floors after costs." : "No setting of the " + settings.ToString("N0", CultureInfo.InvariantCulture) + " read passes the floors after costs; the strongest read is stated.")} The tester's year-by-year verdict with its adjusted p is its own run, the dip-tester family of this command.</p>"));

        page.Append("<div class=\"table\"><table class=\"dip-answer\"><thead><tr><th></th><th>Setting</th><th>Trades</th><th>Edge after costs</th><th>2020</th><th>2022</th><th>2025</th><th>2026</th><th>Without the five largest</th><th>Years above nothing</th></tr></thead><tbody>");
        page.Append(Row("It would run", runs));
        page.Append(Row("The rule its night reads today", nightsRule));
        page.Append(Row("Beside it, the report gate removed", noReportGate));
        page.Append(Row("Beside it, exits sold at the triggering close", atTheTrigger));

        return page.Append("</tbody></table></div>").ToString();
    }
}
