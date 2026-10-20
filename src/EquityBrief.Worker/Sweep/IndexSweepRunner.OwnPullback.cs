using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Worker.Sweep;

public sealed partial class IndexSweepRunner
{
    // The pullback's own history on an index as its search and the tester's procedure both read it: every candidate's
    // plan bought at the close restated bought at the open after it through the core's one pricing function, its
    // benchmark each member's own gap, each exit's round trip at the published table ready to charge, the own grid and its
    // space, the provisional base the index's night reads, and the second stage's levels, each defined once for both.
    public sealed class OwnPullbackRead
    {
        readonly float[][] costs;
        readonly double[] largeBreadth;
        readonly bool[] fundTrend;
        readonly int atTheirFloor;

        OwnPullbackRead(LoopRead laid, SweepCandidate[] all, SweepCandidate[] pool, float[][] costs, (long AtOpen, long Stepped, long NoOpen) restated, double[] largeBreadth, bool[] fundTrend)
        {
            Laid = laid;
            All = all;
            Pool = pool;
            this.costs = costs;
            (AtOpen, Stepped, NoOpen) = restated;
            this.largeBreadth = largeBreadth;
            this.fundTrend = fundTrend;
            Candidates = [.. pool.Where(candidate => ClearsAt(candidate, IndexQuality.Profit, 1m))];
            Space = SweepSpace.For(ConditionSetting.Off.On, Grid, OwnPullbackTested);
            BaseSetting = PullbackBase(Grid);
            High = SweepAxes.ReferenceHighs.ToList().IndexOf(Design.ReferenceHigh);
            atTheirFloor = SweepGrid.IndexOf(Grid.MarketFloors, LargeBreadthFloor);
            LargeLevel = new("market", FormattableString.Invariant($"the S&P 500's breadth at {LargeBreadthFloor * 100:0} per cent"), Switch: true, LargeBreadth: true);
            FundLevel = new("market", FormattableString.Invariant($"{IndexFunds[laid.Index]} above its {FundTrendSessions}-session average"), Switch: true, LargeBreadth: true);
            Cover = new("quality", "profit and cover", Quality: IndexQuality.Cover);
        }

        public LoopRead Laid { get; }

        public string Index => Laid.Index;

        public int Nights => Laid.Calendar.Length - Laid.FirstScored;

        // Every candidate the members made, those clearing the loosest floors any level reads, and those clearing the
        // provisional floors and the profit gate.
        public SweepCandidate[] All { get; }

        public SweepCandidate[] Pool { get; }

        public SweepCandidate[] Candidates { get; }

        // The exits restated at the next open, those of a stepped plan left as they were, and those holding no open.
        public long AtOpen { get; }

        public long Stepped { get; }

        public long NoOpen { get; }

        public SweepDesign Design => SweepDesign.Live;

        public SweepGrid Grid => OwnPullbackGrid;

        public SweepSpace Space { get; }

        // The provisional base is the setting the index's night reads, the S&P 500 sweep's base at a reward to risk of 2.
        public DialSetting BaseSetting { get; }

        public int[] BasePoint => Space.Point(BaseSetting, ConditionSetting.Off);

        // The reference high the design reads a pullback from, as a place among the three.
        public int High { get; }

        public DialLevel<SweepCandidate> LargeLevel { get; }

        public DialLevel<SweepCandidate> FundLevel { get; }

        public DialLevel<SweepCandidate> Cover { get; }

        public IReadOnlyList<DialLevel<SweepCandidate>> Levels { get; private set; } = [];

        public static async Task<OwnPullbackRead> ReadAsync(LoopRead laid, SweepHistoryInputs large, SweepHistory history, DateOnly through, Action<string> progress, CancellationToken cancellation)
        {
            var (indexCode, series, calendar, firstScored) = (laid.Index, laid.Series, laid.Calendar, laid.FirstScored);
            var industries = await history.IndustriesAsync(cancellation);
            var market = await history.SeriesOfAsync(["SPY", IndexFunds[indexCode], "HYG"], through, cancellation);
            var parallelism = Environment.ProcessorCount;
            var fills = new OpenFills(series, laid.Members);
            var found = new List<SweepCandidate>[series.Length];

            progress(FormattableString.Invariant($"reading the candidates of {series.Length:N0} name(s) over {calendar.Length - firstScored:N0} session(s)"));
            Parallel.For(0, series.Length, name => found[name] = SweepCandidates.For(series[name], name, laid.Sessions, calendar, firstScored, firstScored, calendar.Length));

            int BarOf(SweepCandidate candidate) => Array.BinarySearch(series[candidate.Name].SessionAt, candidate.Session);

            var all = found.SelectMany(list => list).ToArray();
            var pool = all
                .Where(candidate => BarOf(candidate) >= 0 && Clears(indexCode, series[candidate.Name], BarOf(candidate), laid.Income.GetValueOrDefault(series[candidate.Name].Name.Ticker) ?? [], IndexQuality.Off, DollarVolumeHalf, laid.Companies.Companies.GetValueOrDefault(series[candidate.Name].Name.Ticker).Sector))
                .OrderBy(candidate => candidate.Session)
                .ThenBy(candidate => candidate.Name)
                .ToArray();

            SweepBenchmark.Fill(pool, series, laid.Members, parallelism);

            // From 19.10 a plan bought at the close is bought at the open after it: each exit's result moved by the stock's
            // own gap over the risk its close put up and its benchmark by the members' gaps, none where no open is held
            // after it; a stepped plan, bought at its own fill on a later session, stands as it was.
            var exits = SweepAxes.Exits;
            var (atOpen, stepped, noOpen) = (0L, 0L, 0L);

            foreach (var candidate in pool)
            {
                var one = series[candidate.Name];
                var bar = BarOf(candidate);
                var close = Statistic.FromPrice(one.Bars[bar].Close);
                var open = OpenFills.OpenAfter(one, bar);

                foreach (var plan in candidate.Plans)
                {
                    if (plan is null)
                    {
                        continue;
                    }

                    var risk = plan.StopMoves * one.Atr[bar];

                    for (var exit = 0; exit < exits; exit++)
                    {
                        if (float.IsNaN(plan.Multiple[exit]))
                        {
                            continue;
                        }

                        if (!float.IsNaN(plan.FillMoves[exit]))
                        {
                            stepped++;

                            continue;
                        }

                        var restated = open is { } price ? TradeFills.AtOpen(plan.Multiple[exit], close, risk, price) : null;

                        plan.Multiple[exit] = restated is { } moved ? (float)moved : float.NaN;
                        plan.Benchmark[exit] = (float)fills.Benchmark(plan.Benchmark[exit], candidate.Session, plan.StopMoves);
                        atOpen += restated is null ? 0 : 1;
                        noOpen += restated is null ? 1 : 0;
                    }
                }
            }

            var costs = new float[pool.Length][];

            Parallel.For(0, pool.Length, at =>
            {
                var candidate = pool[at];
                var one = series[candidate.Name];
                var bar = BarOf(candidate);
                var entry = Statistic.FromPrice(one.Bars[bar].Close);
                var row = new float[candidate.Plans.Length * exits];

                for (var plan = 0; plan < candidate.Plans.Length; plan++)
                {
                    if (candidate.Plans[plan] is not { } outcomes)
                    {
                        continue;
                    }

                    var stop = entry - (outcomes.StopMoves * one.Atr[bar]);

                    for (var exit = 0; exit < exits; exit++)
                    {
                        if (!float.IsNaN(outcomes.Multiple[exit]) && entry > stop)
                        {
                            row[(plan * exits) + exit] = (float)CostInRisk(one, bar, entry, stop, outcomes.Multiple[exit], laid.Companies, 1);
                        }
                    }
                }

                costs[at] = row;
            });

            var read = new OwnPullbackRead(
                laid,
                all,
                pool,
                costs,
                (atOpen, stepped, noOpen),
                [.. SweepSwitches.LargeBreadth(large, calendar).Select(share => share ?? double.NaN)],
                FundAboveItsAverage(calendar, market.FirstOrDefault(one => one.Series == IndexFunds[indexCode])));

            read.Levels = read.LevelsOf(new SweepIndustries(large, laid.Companies, industries), new SweepSwitches(calendar, market, IndexFunds[indexCode]));

            return read;
        }

        int BarOf(SweepCandidate candidate) => Array.BinarySearch(Laid.Series[candidate.Name].SessionAt, candidate.Session);

        string? SectorOf(int name) => Laid.Companies.Companies.GetValueOrDefault(Laid.Ticker(name)).Sector;

        public bool ClearsAt(SweepCandidate candidate, IndexQuality quality, decimal floors) =>
            Clears(Index, Laid.Series[candidate.Name], BarOf(candidate), Laid.Income.GetValueOrDefault(Laid.Ticker(candidate.Name)) ?? [], quality, floors, SectorOf(candidate.Name));

        // Each exit's round trip taken off its result at a multiple of the table, or given back at a negative one.
        public void Charge(int multiple)
        {
            var exits = SweepAxes.Exits;

            for (var at = 0; at < Pool.Length; at++)
            {
                for (var plan = 0; plan < Pool[at].Plans.Length; plan++)
                {
                    if (Pool[at].Plans[plan] is not { } outcomes)
                    {
                        continue;
                    }

                    for (var exit = 0; exit < exits; exit++)
                    {
                        outcomes.Multiple[exit] -= multiple * costs[at][(plan * exits) + exit];
                    }
                }
            }
        }

        // The design and the picks a set of levels reads together, the provisional floors, gate and hold where none of them
        // sets its own; a market level read in place of the index's own breadth places each candidate by its breadth for
        // the picks alone.
        public (SweepDesign Held, List<SweepPick> Picks) Under(IReadOnlyList<DialLevel<SweepCandidate>> together)
        {
            var quality = together.Select(level => level.Quality).OfType<IndexQuality>().DefaultIfEmpty(IndexQuality.Profit).First();
            var floors = together.Select(level => level.Floors).OfType<decimal>().DefaultIfEmpty(1m).First();
            var held = Design with { Hold = together.Select(level => level.Hold).OfType<int>().DefaultIfEmpty(Design.Hold).First() };
            var instead = together.FirstOrDefault(level => level.LargeBreadth);
            var kept = Pool.Where(candidate => ClearsAt(candidate, quality, floors) && together.All(level => level.Keep is not { } keep || keep(candidate))).ToArray();
            var breadths = instead is null ? null : kept.Select(candidate => candidate.Breadth).ToArray();

            if (instead is not null)
            {
                foreach (var candidate in kept)
                {
                    candidate.Breadth = instead == LargeLevel ? largeBreadth[candidate.Session] : fundTrend[candidate.Session] ? 1.0 : 0.0;
                }
            }

            try
            {
                return (held, SweepStages.Picks(kept, held, Grid));
            }
            finally
            {
                if (breadths is not null)
                {
                    for (var at = 0; at < kept.Length; at++)
                    {
                        kept[at].Breadth = breadths[at];
                    }
                }
            }
        }

        // A point's setting under a set of levels: a market level read in place of the index's own breadth sets its floor
        // to the S&P 500's.
        public DialSetting SettingUnder(int[] point, IReadOnlyList<DialLevel<SweepCandidate>> together) =>
            together.Any(level => level.LargeBreadth) ? Space.Setting(point) with { Market = atTheirFloor } : Space.Setting(point);

        // Points read under a set of levels together over the whole history.
        public SweepMeasures[] ReadUnder(IReadOnlyList<DialLevel<SweepCandidate>> together, IReadOnlyList<int[]> points)
        {
            var (held, picks) = Under(together);

            return [.. points.Select(point => SweepStages.Measures(picks, held, SettingUnder(point, together), Space.Conditions(point), Nights))];
        }

        decimal Traded(SweepCandidate candidate)
        {
            var bar = Laid.Series[candidate.Name].Bars[BarOf(candidate)];

            return bar.RawClose > 0m ? bar.RawClose : bar.Close;
        }

        // The second stage's levels: the index sweep's own, the market switches but the S&P 500's breadth, the market check's
        // two levels the search's dial does not read, and the research's three dials.
        IReadOnlyList<DialLevel<SweepCandidate>> LevelsOf(SweepIndustries industryReturns, SweepSwitches switches)
        {
            var (series, calendar) = (Laid.Series, Laid.Calendar);
            var sectorReturns = new System.Collections.Concurrent.ConcurrentDictionary<(string Sector, int Session, int Sessions), double?>();
            var bySector = Enumerable.Range(0, series.Length)
                .Where(name => SectorOf(name) is not null)
                .GroupBy(name => SectorOf(name)!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(name => series[name]).ToArray(), StringComparer.Ordinal);

            // The dip read against the sector's own members on the index: the stock fell more than their mean over the
            // sessions since its reference high, its sector carrying two members or more with a return that session.
            bool FellMoreThanItsSector(SweepCandidate candidate)
            {
                var back = candidate.PullbackSessions[High];

                return back > 0
                    && SectorOf(candidate.Name) is { } sector
                    && ReturnOver(series[candidate.Name], candidate.Session, back) is { } own
                    && sectorReturns.GetOrAdd((sector, candidate.Session, back), key => SectorMean(bySector[key.Sector], key.Session, key.Sessions)) is { } theirs
                    && own < theirs;
            }

            return
            [
                new("dollar volume floor", "half the provisional", Floors: DollarVolumeHalf),
                new("dollar volume floor", "twice the provisional", Floors: 2m),
                new("minimum price", "$10", Keep: candidate => Traded(candidate) >= HigherPrice),
                new("quality", "off", Quality: IndexQuality.Off),
                Cover,
                new("hold", "20 sessions", Hold: 20),
                new("hold", "40 sessions", Hold: 40),
                new("industry fall", "21 sessions", Keep: candidate => !industryReturns.Fell(series[candidate.Name].Name.Ticker, calendar, candidate.Session, 21)),
                new("industry fall", "63 sessions", Keep: candidate => !industryReturns.Fell(series[candidate.Name].Name.Ticker, calendar, candidate.Session, 63)),
                .. Switches<SweepCandidate>(Index, switches, candidate => candidate.Session).Where(level => !level.LargeBreadth),
                LargeLevel,
                FundLevel,
                new("the dip", "fell more than its sector's own members on the index", Keep: FellMoreThanItsSector),
                new("a report in the dip", "none reacted in the dip's sessions", Keep: candidate => NoReportInTheDip(candidate, High)),
                new("the largest daily rise", FormattableString.Invariant($"at most {LargestDayMoves:0} typical moves over the {JumpSessions} sessions"), Keep: candidate => NoRiseOver(series[candidate.Name], BarOf(candidate), JumpSessions, LargestDayMoves)),
            ];
        }
    }
}
