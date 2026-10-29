using System.Collections.Concurrent;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Worker.Sweep;

public sealed partial class IndexSweepRunner
{
    // The breakout's or the drift's own history on an index as its sweep and the tester's procedure both read it: the
    // family's grid with the market check's seven levels as its last dial, the provisional setting the one its night reads
    // today, every listing bought at the next open after its round trip, and the second stage's levels, each defined once
    // for both.
    public sealed class OwnSwingRead : OwnProcedures.IOwnSwing
    {
        readonly ConcurrentDictionary<string, IReadOnlyList<QuarterRevenue>> quartersOf = new(StringComparer.Ordinal);
        readonly ConcurrentDictionary<(int Name, int Sessions), double[]> highs = new();
        readonly ConcurrentDictionary<int, double?> analystMedians = new();
        readonly IReadOnlyDictionary<string, IReadOnlyList<FiledRevenue>> revenue;
        readonly IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Fetched, int Analysts)>> analysts;
        readonly IReadOnlyDictionary<string, string> industries;
        readonly IReadOnlyDictionary<string, (SweepName Name, SweepSurprise Surprise)[]> reports;
        readonly IReadOnlyDictionary<(int Name, int Bar), int> backOf;
        readonly IReadOnlyList<DriftReading> wideReadings;
        readonly SweepIndustries industryReturns;
        readonly SweepSwitches otherSwitches;

        OwnSwingRead(
            string index,
            string family,
            SweepHistoryInputs inputs,
            HeavyweightHistory companies,
            IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
            SweepSeries[] series,
            IReadOnlyList<SweepColumns.Session> sessions,
            SweepBenchmark.Members members,
            IReadOnlyList<MarketLevel> marketLevels,
            bool[][] open,
            double stopFloor,
            FamilySweepRunner.Adapter adapter,
            FamilySweepRunner.Adapter nightsAdapter,
            string otherFund,
            IReadOnlyDictionary<string, IReadOnlyList<FiledRevenue>> revenue,
            IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Fetched, int Analysts)>> analysts,
            IReadOnlyDictionary<string, string> industries,
            IReadOnlyDictionary<string, (SweepName Name, SweepSurprise Surprise)[]> reports,
            SweepIndustries industryReturns,
            SweepSwitches switches,
            SweepSwitches otherSwitches,
            IReadOnlyList<DriftReading> wideReadings)
        {
            Index = index;
            Family = family;
            Inputs = inputs;
            Companies = companies;
            Income = income;
            Series = series;
            Sessions = sessions;
            Members = members;
            MarketLevels = marketLevels;
            Open = open;
            StopFloor = stopFloor;
            Adapter = adapter;
            NightsAdapter = nightsAdapter;
            OtherFund = otherFund;
            this.revenue = revenue;
            this.analysts = analysts;
            this.industries = industries;
            this.reports = reports;
            this.industryReturns = industryReturns;
            this.otherSwitches = otherSwitches;
            this.wideReadings = wideReadings;
            Calendar = inputs.Sessions;
            FirstScored = Array.FindIndex(Calendar, session => session >= SweepColumns.FirstScored);
            Nights = Calendar.Length - FirstScored;
            Tickers = [.. inputs.Names.Select(name => name.Ticker)];
            Fills = new OpenFills(series, members);
            AllOpen = [.. sessions.Select(one => one with { Breadth = 1.0 })];
            backOf = wideReadings.GroupBy(one => (one.Name, one.Bar)).ToDictionary(group => group.Key, group => group.First().Back);
            FirstCounted = analysts.Values.SelectMany(list => list).Select(one => one.Fetched).DefaultIfEmpty(DateOnly.MaxValue).Min();

            var marketDial = new List<double>();

            for (double place = 0; place < marketLevels.Count; place++)
            {
                marketDial.Add(place);
            }

            int[] provisional = IsBreakout ? [.. IndexNightRead.BreakoutAsFrozen] : [.. IndexNightRead.DriftAsFrozen];

            Grid = new FamilyGrid([.. adapter.Grid.Dials, ("market", marketDial)], [.. provisional, ProvisionalMarketLevel]);
            Levels = LevelsOf(switches);
        }

        public string Index { get; }

        public string Family { get; }

        public bool IsBreakout => Family == BreakoutRule.Name;

        public bool IsDrift => Family == DriftRule.Name;

        public SweepHistoryInputs Inputs { get; }

        public HeavyweightHistory Companies { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income { get; }

        public SweepSeries[] Series { get; }

        public IReadOnlyList<SweepColumns.Session> Sessions { get; }

        public SweepBenchmark.Members Members { get; }

        public DateOnly[] Calendar { get; }

        public int FirstScored { get; }

        public int Nights { get; }

        public string[] Tickers { get; }

        public IReadOnlyList<MarketLevel> MarketLevels { get; }

        // Whether each market level holds the lists open on each session.
        public bool[][] Open { get; }

        public IReadOnlyList<SweepColumns.Session> AllOpen { get; }

        public double StopFloor { get; }

        public bool Floored => IsDrift && StopFloor > 0;

        public FamilySweepRunner.Adapter Adapter { get; }

        // The family's rule as the index's night reads it, with no stop floor, which a floored search is read against.
        public FamilySweepRunner.Adapter NightsAdapter { get; }

        public FamilyGrid Grid { get; }

        public OpenFills Fills { get; }

        public string OtherFund { get; }

        // The first day a fetch stored the analysts' counts, none where no fetch did.
        public DateOnly FirstCounted { get; }

        public IReadOnlyList<DialLevel<FamilyListing>> Levels { get; }

        // The S&P 400's drift passes only where its last two years together stand above nothing as well.
        public bool LastTwo => IsDrift && Index == DriftLastTwoYearsIndex;

        public int MembersWithIncome => Tickers.Count(ticker => Income.ContainsKey(ticker));

        // The history laid out once for an index: its series, sessions and members, and the folds the tester reads.
        public static LoopRead Laid(string indexCode, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, DateOnly through)
        {
            var calendar = inputs.Sessions;
            var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
            var series = new SweepSeries[inputs.Names.Count];

            Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

            return new LoopRead(
                indexCode,
                inputs,
                series,
                SweepColumns.Sessions(series, calendar),
                SweepBenchmark.On(series, calendar.Length),
                companies,
                income,
                calendar,
                Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored),
                LoopFolds.Of(calendar, through));
        }

        // The S&P 500 as it stood, which the market check's sixth level, the industries' falls and the drift's peers read.
        public static async Task<SweepHistoryInputs> LargeAsync(SweepHistory history, DateOnly through, Action<string> progress, CancellationToken cancellation)
        {
            progress("reading the S&P 500 as it stood for its industries, its breadth and its reports");

            return await history.ReadAsync(through, null, cancellation);
        }

        public static async Task<OwnSwingRead> ReadAsync(
            LoopRead read,
            SweepHistoryInputs large,
            string family,
            SweepHistory history,
            DateOnly through,
            double stopFloor,
            CancellationToken cancellation)
        {
            var (indexCode, inputs, companies, income) = (read.Index, read.Inputs, read.Companies, read.Income);
            var (series, sessions, members, calendar, firstScored) = (read.Series, read.Sessions, read.Members, read.Calendar, read.FirstScored);
            var otherFund = IndexFunds.Single(pair => pair.Key != indexCode).Value;
            var market = await history.SeriesOfAsync(["SPY", IndexFunds[indexCode], otherFund, "HYG"], through, cancellation);
            var industries = await history.IndustriesAsync(cancellation);
            var marketLevels = MarketLevels(indexCode);
            var open = MarketOpen(sessions, SweepSwitches.LargeBreadth(large, calendar), calendar, market.FirstOrDefault(one => one.Series == IndexFunds[indexCode]));
            var allOpen = sessions.Select(one => one with { Breadth = 1.0 }).ToArray();
            var isDrift = family == DriftRule.Name;
            var adapter = FamilySweepRunner.For(family, series, allOpen, members, firstScored, calendar, isDrift && stopFloor > 0 ? stopFloor : 0);
            var nightsAdapter = isDrift && stopFloor > 0 ? FamilySweepRunner.For(family, series, allOpen, members, firstScored, calendar) : adapter;
            var revenue = await history.RevenueAsync(through, cancellation);
            var analysts = isDrift ? await history.AnalystCountsAsync(cancellation) : new Dictionary<string, IReadOnlyList<(DateOnly Fetched, int Analysts)>>(StringComparer.Ordinal);
            var reports = large.Names
                .Where(name => industries.ContainsKey(name.Ticker))
                .SelectMany(name => name.Surprises.Select(surprise => (Name: name, Surprise: surprise)))
                .GroupBy(pair => industries[pair.Name.Ticker], StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            IReadOnlyList<DriftReading> wideReadings = isDrift ? new DriftSweep(series, members).Readings(allOpen, firstScored, DriftWideWindow) : [];

            return new OwnSwingRead(
                indexCode,
                family,
                inputs,
                companies,
                income,
                series,
                sessions,
                members,
                marketLevels,
                open,
                stopFloor,
                adapter,
                nightsAdapter,
                otherFund,
                revenue,
                analysts,
                industries,
                reports,
                new SweepIndustries(large, companies, industries),
                new SweepSwitches(calendar, market, IndexFunds[indexCode]),
                new SweepSwitches(calendar, market, otherFund),
                wideReadings);
        }

        public int YearOf(int session) => Calendar[session].Year - SweepColumns.FirstScored.Year;

        public string? SectorOf(int name) => Companies.Companies.GetValueOrDefault(Tickers[name]).Sector;

        // A setting's listings at its market level, every one the family's rule made on a session the level holds open, and
        // those clearing the index's floors and its profit gate.
        public (FamilyListing[] Listed, FamilyListing[] Clearing) FirstStage(int[] setting)
        {
            var level = Open[setting[^1]];
            var listed = Adapter.Listings(setting[..^1]).Where(listing => level[listing.Session]).ToArray();

            return (listed, [.. listed.Where(listing => Clears(Index, Series[listing.Name], listing.Bar, Income.GetValueOrDefault(Tickers[listing.Name]) ?? []))]);
        }

        // A setting's trades at the next open before costs, one open trade a stock, under the search's exits or another
        // adapter's; where a map is handed in, the sessions each trade was held are kept in it by its stock and the session
        // it was bought on.
        public IReadOnlyList<FamilyTrade> AtOpen(IEnumerable<FamilyListing> listings, ConcurrentDictionary<(int Name, int Session), int>? held = null, FamilySweepRunner.Adapter? under = null)
        {
            var adapter = under ?? Adapter;

            return FamilySweep.Walk(
                listings,
                Tickers,
                YearOf,
                listing =>
                {
                    var walked = Fills.Exit(adapter.Exit(listing), listing);

                    if (held is not null)
                    {
                        held[(listing.Name, listing.Session)] = walked.Sessions;
                    }

                    return walked;
                },
                listing => Fills.Benchmark(adapter.Benchmark(listing), listing));
        }

        // The rule the index's night reads today: the provisional setting at the index's own breadth of 45 per cent with no
        // stop floor, its listings clearing the floors and the profit gate, after each trade's round trip at the next open.
        public IReadOnlyList<FamilyTrade> NightsRule(ConcurrentDictionary<(int Name, int Session), int>? held = null)
        {
            int[] setting = [.. Grid.Provisional];
            var level = Open[setting[^1]];
            var listings = NightsAdapter.Listings(setting[..^1])
                .Where(listing => level[listing.Session] && Clears(Index, Series[listing.Name], listing.Bar, Income.GetValueOrDefault(Tickers[listing.Name]) ?? []))
                .ToArray();

            return Costed(AtOpen(listings, held, NightsAdapter), 1);
        }

        // Each trade's result after its round trip at a multiple of the published table, the benchmark paying none.
        public IReadOnlyList<FamilyTrade> Costed(IReadOnlyList<FamilyTrade> trades, int multiple) =>
            [.. trades.Select(trade => trade.Result is { } result ? trade with { Result = result - CostInRisk(Series[trade.Listing.Name], trade.Listing, result, Companies, multiple) } : trade)];

        // A setting's trades under a set of levels together, after costs at the next open: the provisional floors, gate,
        // hold and window where none of them sets its own, the entry a level moves bought at its own session, the hold then
        // set, a level moving the last session read after it, and the market level the setting holds read on the session
        // bought.
        public IReadOnlyList<FamilyTrade> TradesUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together, ConcurrentDictionary<(int Name, int Session), int>? held = null) =>
            Costed(AtOpen(ListingsUnder(setting, together), held), 1);

        // From 21.1 the listings a setting keeps under a set of levels together, before the walk, so a condition read after
        // the levels, the drift's tone condition among them, keeps or leaves each before one open trade a stock is read.
        // see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
        public IReadOnlyList<FamilyListing> ListingsUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together)
        {
            var own = setting[..^1];
            var level = Open[setting[^1]];
            var quality = together.Select(one => one.Quality).OfType<IndexQuality>().DefaultIfEmpty(IndexQuality.Profit).First();
            var floors = together.Select(one => one.Floors).OfType<decimal>().DefaultIfEmpty(1m).First();
            var hold = together.Select(one => one.Hold).OfType<int>().DefaultIfEmpty(0).First();
            var window = together.Select(one => one.Window).OfType<int>().DefaultIfEmpty(0).First();
            var entries = together.Where(one => one.Dial != ExitDial).Select(one => one.Instead).OfType<Func<FamilyListing, int[], IEnumerable<FamilyListing>>>().ToArray();
            var exits = together.Where(one => one.Dial == ExitDial).Select(one => one.Instead).OfType<Func<FamilyListing, int[], IEnumerable<FamilyListing>>>().ToArray();
            var listings = window > 0 && IsDrift ? DriftSweep.Listings(wideReadings, own, Floored ? StopFloor : 0, window) : Adapter.Listings(own);

            IEnumerable<FamilyListing> Through(FamilyListing listing, IEnumerable<Func<FamilyListing, int[], IEnumerable<FamilyListing>>> moves) =>
                moves.Aggregate((IEnumerable<FamilyListing>)[listing], (current, move) => current.SelectMany(one => move(one, own)));

            return listings
                .Where(listing => together.All(one => one.Keep is not { } keep || keep(listing)))
                .SelectMany(listing => Through(listing, entries))
                .DistinctBy(listing => (listing.Name, listing.Session))
                .Where(listing => level[listing.Session] && Clears(Index, Series[listing.Name], listing.Bar, Income.GetValueOrDefault(Tickers[listing.Name]) ?? [], quality, floors, SectorOf(listing.Name)))
                .Select(listing => hold > 0 ? listing with { Cap = hold } : listing)
                .SelectMany(listing => Through(listing, exits))
                .ToArray();
        }

        // Whether a setting's figures pass: the floors, and on the S&P 400's drift its last two years above nothing.
        public bool Passes(FamilyFigures figures) => figures.MeetsFloors && (!LastTwo || LastTwoYears(figures.YearTrades, figures.YearEdge) > 0);

        public bool PassesCrossed(SweepMeasures measures) => MeetsTheFloors(measures) && (!LastTwo || LastTwoYears(measures.YearScored, measures.YearEdge) > 0);

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

        decimal Traded(FamilyListing listing)
        {
            var bar = Series[listing.Name].Bars[listing.Bar];

            return bar.RawClose > 0m ? bar.RawClose : bar.Close;
        }

        (double High, int Since) YearHigh(FamilyListing listing) =>
            MemberReadings.YearHigh(Series[listing.Name].Bars, listing.Bar) is { } found ? (Statistic.FromPrice(found.High), found.Since) : (double.NaN, int.MaxValue);

        double HighOf(FamilyListing listing, int[] setting)
        {
            var back = BreakoutSweep.Grid.Value(setting, 0) >= BreakoutRule.ProvisionalHighSessions ? BreakoutRule.ProvisionalHighSessions : BreakoutSweep.ShortHighSessions;

            return highs.GetOrAdd((listing.Name, back), key => BreakoutSweep.HighsBefore(Series[key.Name].Bars, key.Sessions))[listing.Bar];
        }

        SweepSurprise? Newest(FamilyListing listing) => NewestReport(Series[listing.Name], listing.Bar);

        // A report's revenue growth on the year as first filed, read from 20.11 as the night reads it: over the quarters first
        // filed before the session before the listing's, which are those the night's filings refresh has stored by the
        // listing's night, so a quarter filed the day of its report is read from the second session after it; none where
        // the filer stated none the reading finds by then.
        // see: A drift print's revenue growth is its quarter's revenue as first filed against the same quarter a year before
        // see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
        PrintRevenue? RevenueOf(FamilyListing listing)
        {
            var one = Series[listing.Name];

            if (Newest(listing) is not { } newest || !revenue.TryGetValue(one.Name.Ticker, out var facts))
            {
                return null;
            }

            var stored = Calendar[Math.Max(0, listing.Session - 1)];

            return RevenueGrowth.Of(newest.EventDate, [.. quartersOf.GetOrAdd(one.Name.Ticker, _ => FirstFiledRevenue.Quarters(facts)).Where(quarter => quarter.Filed < stored)]);
        }

        bool PeersBeat(FamilyListing listing)
        {
            if (!backOf.TryGetValue((listing.Name, listing.Bar), out var back)
                || listing.Session - back - PeerSessions < 0
                || !industries.TryGetValue(Tickers[listing.Name], out var industry)
                || !reports.TryGetValue(industry, out var reported))
            {
                return false;
            }

            var reaction = listing.Session - back;
            var (from, to) = (Calendar[reaction - PeerSessions], Calendar[reaction - 1]);
            var peers = new List<(double Value, double Figure)>();

            foreach (var (name, surprise) in reported)
            {
                if (surprise.EventDate < from || surprise.EventDate > to || !name.MemberOn(surprise.EventDate)
                    || SweepIndustries.BarOn(name, to) is not { } bar
                    || CompanyValue.On(new SessionClose(bar.Session, bar.Close, bar.RawClose > 0m ? bar.RawClose : bar.Close), Companies.Counts.GetValueOrDefault(name.Ticker) ?? [], Companies.Splits.GetValueOrDefault(name.Ticker) ?? []) is not { } value
                    || value <= 0m)
                {
                    continue;
                }

                peers.Add((Statistic.FromPrice(value), surprise.Percent));
            }

            return MemberReadings.ValueWeighted(peers) is > 0;
        }

        // A drift's trade sold at the close three sessions before the next report the history holds after its listing, on
        // the day that report is dated, which is the date as it happened and one companies normally announce weeks ahead.
        IEnumerable<FamilyListing> BeforeTheReport(FamilyListing listing) =>
            BeforeTheNextReport(Series[listing.Name], Calendar[listing.Session], listing, SessionsBeforeReport) is { } held ? [held] : [];

        // The analysts following a listing's stock on its session as the fetches stored them, against the median of the
        // index's members holding a count then; none before the first count stored, so no later count is read into an
        // earlier session.
        double? MedianOn(int session) => analystMedians.GetOrAdd(session, at =>
        {
            var day = Calendar[at];
            var counts = Enumerable.Range(0, Series.Length)
                .Where(name => Array.BinarySearch(Series[name].SessionAt, at) is var bar && bar >= 0 && Series[name].Member[bar])
                .Select(name => AnalystsOn(analysts.GetValueOrDefault(Tickers[name]), day))
                .OfType<int>()
                .Order()
                .ToArray();

            return counts.Length == 0 ? null : counts.Length % 2 == 1 ? counts[counts.Length / 2] : (counts[(counts.Length / 2) - 1] + counts[counts.Length / 2]) / 2.0;
        });

        bool FewerAnalysts(FamilyListing listing) =>
            AnalystsOn(analysts.GetValueOrDefault(Tickers[listing.Name]), Calendar[listing.Session]) is { } count && MedianOn(listing.Session) is { } median && count < median;

        // The second stage's levels: the index sweep's own, the family's, the research's dials and the market switches but
        // the S&P 500's breadth, which the grid's market dial reads.
        List<DialLevel<FamilyListing>> LevelsOf(SweepSwitches switches)
        {
            var levels = new List<DialLevel<FamilyListing>>
            {
                new("dollar volume floor", "half the provisional", Floors: DollarVolumeHalf),
                new("dollar volume floor", "twice the provisional", Floors: 2m),
                new("minimum price", "$10", Keep: listing => Traded(listing) >= HigherPrice),
                new("quality", "off", Quality: IndexQuality.Off),
                new("quality", "profit and cover", Quality: IndexQuality.Cover),
                new("hold", "21 sessions", Hold: 21),
                new("hold", "42 sessions", Hold: 42),
            };

            if (IsDrift)
            {
                levels.Add(new("hold", "63 sessions", Hold: 63));
            }

            levels.Add(new("industry fall", "21 sessions", Keep: listing => !industryReturns.Fell(Tickers[listing.Name], Calendar, listing.Session, 21)));
            levels.Add(new("industry fall", "63 sessions", Keep: listing => !industryReturns.Fell(Tickers[listing.Name], Calendar, listing.Session, 63)));

            if (IsBreakout)
            {
                levels.Add(new("nearness to the year's high", "at least 0.95", Keep: listing => listing.Entry / YearHigh(listing).High >= NearHigh));
                levels.Add(new("nearness to the year's high", "at it or above", Keep: listing => listing.Entry / YearHigh(listing).High >= 1));
                levels.Add(new("recency of the year's high", "within 63 sessions", Keep: listing => YearHigh(listing).Since <= RecentHighSessions));
                levels.Add(new("recency of the year's high", "older than 63 sessions", Keep: listing => YearHigh(listing).Since > RecentHighSessions));
                levels.Add(new("volume multiple", "3.0", Keep: listing => listing.Order >= HighestVolume));
                levels.Add(new("the breakout day's close", "in the top quarter of its range", Keep: listing => SweepEntries.InTheTopQuarter(Series[listing.Name].Bars[listing.Bar])));
                levels.Add(new("entry", $"the first pullback to the level within {SweepEntries.PullbackSessions} sessions", Instead: (listing, setting) =>
                    SweepEntries.BreakoutPullback(listing, Series[listing.Name], HighOf(listing, setting), BreakoutSweep.Grid.Value(setting, 3), AllOpen)));
                levels.Add(new("the newest report", "beat its estimate", Keep: listing => Newest(listing) is { Percent: > 0 }));
                levels.Add(new("the newest report", "revenue grew on the year as first filed and stored before the night", Keep: listing => RevenueOf(listing) is { Growth: > 0 }));
                levels.Add(new("the breakout day's move", FormattableString.Invariant($"at most {LargestDayMoves:0} typical moves"), Keep: listing => DayMoves(Series[listing.Name], listing.Bar) <= LargestDayMoves));
            }

            if (IsDrift)
            {
                levels.Add(new("window", $"{DriftWideWindow} sessions", Window: DriftWideWindow));
                levels.Add(new("peer version", "on", Keep: PeersBeat));
                levels.Add(new("hold", "10 sessions", Hold: 10));
                levels.Add(new("surprise", "at least 5 per cent", Keep: listing => listing.Order >= LeastSurprise));
                levels.Add(new("surprise", "at least 10 per cent", Keep: listing => listing.Order >= LargeSurprise));
                levels.Add(new("revenue", "grew on the quarter a year before, both as first filed and stored before the night", Keep: listing => RevenueOf(listing) is { Growth: > 0 }));
                levels.Add(new("entry", $"the first pullback after the reaction within {SweepEntries.PullbackSessions} sessions", Instead: (listing, setting) =>
                    backOf.TryGetValue((listing.Name, listing.Bar), out var back)
                        ? SweepEntries.DriftPullback(listing, Series[listing.Name], listing.Bar - back, DriftSweep.Grid.Value(setting, 3), AllOpen)
                        : []));
                levels.Add(new("small against large", $"{OtherFund} over SPY up over 126 sessions", Switch: true, Keep: listing => otherSwitches.SmallLeads(listing.Session, 126)));
                levels.Add(new("small against large", $"{OtherFund} over SPY up over 252 sessions", Switch: true, Keep: listing => otherSwitches.SmallLeads(listing.Session, 252)));
                levels.Add(new(ExitDial, FormattableString.Invariant($"sold at the close {SessionsBeforeReport} sessions before the next report"), Instead: (listing, _) => BeforeTheReport(listing)));
                levels.Add(new("analysts", "fewer following than the index's median", Keep: FewerAnalysts));
            }

            levels.AddRange(Switches<FamilyListing>(Index, switches, listing => listing.Session).Where(level => !level.LargeBreadth));

            return levels;
        }
    }
}
