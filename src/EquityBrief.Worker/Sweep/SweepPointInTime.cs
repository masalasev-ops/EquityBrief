using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Membership;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Sweep;

// One difference between what the sweep read of a name-session and what the night's own components compute
// over the year of bars the store would have held that evening.
public sealed record PointInTimeDifference(string Ticker, DateOnly Session, string What, string Sweep, string Night);

// What the check found: the name-sessions sampled, those from the live list, the readings compared and every
// difference, with the time it took.
public sealed record PointInTimeResult(int Samples, int FromTheLiveList, int Compared, IReadOnlyList<PointInTimeDifference> Differences, double Seconds)
{
    public bool Clean => Differences.Count == 0;
}

// The point-in-time check: before stage 1 runs, a sample of name-sessions is rebuilt with the night's own
// components, the membership loader, the backfill, the indicator engine, the swing finder, the volume profile
// builder, the level builder and the trend classifier, over one small scratch store a worker, holding that
// name's year of bars to the session as the store held it that evening, and every reading the sweep took of
// the session is compared exactly: the averages, ATR, RSI and fifty-day volume, the swings the year holds, each
// band's edges, role, strength and anchor flag, and the live label. Any difference stops the run before stage
// 1, and the report lists every one. The scratch stores are under the machine's temporary folder and deleted
// at the end; the night's components write them, this class reads them, and no store of the operator's is
// touched.
// see: The sweep ranks on the edge over the same plan entered on every member, with one open trade a stock and seven conditions tested in steps
public sealed class SweepPointInTime : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
        ],
        Feeds: []);

    // The sample: 25 name-sessions a scored year, 13 of them candidate sessions and 12 member-sessions holding a
    // year of bars, drawn with the stated seed, and every name-session the live list listed added.
    public const int SamplesAYear = 25;
    public const int CandidateSamplesAYear = 13;

    const string IndexCode = "GSPC";

    // The bars a member-session must hold behind it to be a member-session with a year of bars.
    public const int YearOfBars = 200;

    readonly string scratchRoot;
    readonly int parallelism;

    public SweepPointInTime(string scratchRoot, int parallelism)
    {
        this.scratchRoot = scratchRoot;
        this.parallelism = Math.Max(1, parallelism);
    }

    public static IReadOnlyList<(int Name, int Bar)> Sample(
        IReadOnlyList<SweepSeries> series,
        IReadOnlyList<SweepCandidate> candidates,
        IReadOnlyList<DateOnly> calendar,
        IReadOnlyList<(string Ticker, DateOnly Session)> liveListed,
        int seed)
    {
        var random = new Random(seed);
        var chosen = new List<(int Name, int Bar)>();
        var taken = new HashSet<(int, int)>();
        var byTicker = series.Select((one, at) => (one.Name.Ticker, at)).ToDictionary(pair => pair.Ticker, pair => pair.at, StringComparer.Ordinal);

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            var fromCandidates = candidates.Where(candidate => candidate.Year == year).ToArray();
            var members = new List<(int Name, int Bar)>();

            for (var name = 0; name < series.Count; name++)
            {
                var one = series[name];

                for (var bar = 0; bar < one.Bars.Length; bar++)
                {
                    if (one.Member[bar] && !one.Gap[bar] && calendar[one.SessionAt[bar]].Year == SweepColumns.FirstScored.Year + year && bar - one.WindowStart[bar] >= YearOfBars)
                    {
                        members.Add((name, bar));
                    }
                }
            }

            // Drawn without replacement: each list shuffled with the seed and taken from the front until the
            // count is reached or the list runs out.
            var drawn = 0;

            foreach (var candidate in Shuffled(fromCandidates, random))
            {
                if (drawn == CandidateSamplesAYear)
                {
                    break;
                }

                var bar = Array.IndexOf(series[candidate.Name].SessionAt, candidate.Session);

                if (bar >= 0 && taken.Add((candidate.Name, bar)))
                {
                    chosen.Add((candidate.Name, bar));
                    drawn++;
                }
            }

            drawn = 0;

            foreach (var member in Shuffled(members, random))
            {
                if (drawn == SamplesAYear - CandidateSamplesAYear)
                {
                    break;
                }

                if (taken.Add(member))
                {
                    chosen.Add(member);
                    drawn++;
                }
            }
        }

        foreach (var (ticker, session) in liveListed)
        {
            if (!byTicker.TryGetValue(ticker, out var name))
            {
                continue;
            }

            var bar = Array.FindIndex(series[name].Bars, one => one.Session == session);

            if (bar >= 0 && taken.Add((name, bar)))
            {
                chosen.Add((name, bar));
            }
        }

        return chosen;
    }

    static T[] Shuffled<T>(IReadOnlyList<T> items, Random random)
    {
        var shuffled = items.ToArray();

        for (var at = shuffled.Length - 1; at > 0; at--)
        {
            var other = random.Next(at + 1);

            (shuffled[at], shuffled[other]) = (shuffled[other], shuffled[at]);
        }

        return shuffled;
    }

    public async Task<PointInTimeResult> CheckAsync(IReadOnlyList<SweepSeries> series, IReadOnlyList<(int Name, int Bar)> samples, int fromTheLiveList, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        var differences = new System.Collections.Concurrent.ConcurrentBag<PointInTimeDifference>();
        var compared = 0;
        var workers = Math.Min(parallelism, Math.Max(1, samples.Count));
        var tasks = new List<Task>();

        Directory.CreateDirectory(scratchRoot);

        for (var worker = 0; worker < workers; worker++)
        {
            var own = worker;

            tasks.Add(Task.Run(async () =>
            {
                var folder = Path.Combine(scratchRoot, FormattableString.Invariant($"worker-{own}"));

                for (var at = own; at < samples.Count; at += workers)
                {
                    var (name, bar) = samples[at];

                    foreach (var difference in await RebuildAsync(series[name], bar, folder, cancellation))
                    {
                        differences.Add(difference);
                    }

                    Interlocked.Increment(ref compared);
                }
            }, cancellation));
        }

        await Task.WhenAll(tasks);

        try
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
        catch (IOException)
        {
            // A handle the pool still holds closes on its own; the folder is under the machine's temporary
            // folder and holds nothing of the operator's.
        }

        return new PointInTimeResult(
            samples.Count,
            fromTheLiveList,
            compared,
            [.. differences.OrderBy(one => one.Ticker, StringComparer.Ordinal).ThenBy(one => one.Session).ThenBy(one => one.What, StringComparer.Ordinal)],
            watch.Elapsed.TotalSeconds);
    }

    // One name-session rebuilt: a fresh scratch store migrated, the name loaded as a member and its year of bars
    // backfilled through the night's own components, the four computing stages run as of that evening, and every
    // reading read back against the sweep's.
    public static async Task<IReadOnlyList<PointInTimeDifference>> RebuildAsync(SweepSeries series, int bar, string folder, CancellationToken cancellation = default)
    {
        var ticker = series.Name.Ticker;
        var session = series.Bars[bar].Session;
        var differences = new List<PointInTimeDifference>();

        void Differs(string what, string sweep, string night) => differences.Add(new PointInTimeDifference(ticker, session, what, sweep, night));

        Directory.CreateDirectory(folder);

        // A file of its own for each rebuild, since a handle the pool still holds on the last one would refuse
        // its deletion; each is released and deleted once read, and the folder goes at the end.
        var file = Path.Combine(folder, FormattableString.Invariant($"{ticker}-{session:yyyyMMdd}-{Guid.NewGuid():n}.db"));

        MigrationRunner.Standard().Apply(file);

        var evening = new DateTimeOffset(session.Year, session.Month, session.Day, 21, 10, 0, TimeSpan.Zero);
        var clock = FixedClock.At(evening, SessionZones.UnitedStates);
        var bars = series.Bars.Where(one => one.Session >= session.AddYears(-BarFetcher.RetentionYears) && one.Session <= session).ToArray();

        await new MembershipLoader(new OneMember(ticker, series.Name.Sector), clock, file).LoadAsync(IndexCode, "point-in-time-membership", cancellation);
        await new Backfill(new SeriesBars(ticker, bars), clock, file).RunAsync(IndexCode, "point-in-time-backfill", cancellation);
        await new IndicatorEngine(clock, file).RunAsync("point-in-time-indicators", cancellation);
        await new SwingFinder(clock, file).RunAsync("point-in-time-swings", cancellation);
        await new VolumeProfileBuilder(clock, file).RunAsync("point-in-time-profile", cancellation);
        await new LevelBuilder(clock, file).RunAsync("point-in-time-levels", cancellation);

        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using (var connection = new SqliteConnection(StoreConnection.For(file)))
        {
            await connection.OpenAsync(cancellation);

            // The indicators, each named as the night names it.
            var stored = new Dictionary<string, double>(StringComparer.Ordinal);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name, value FROM indicator WHERE ticker = $ticker AND session_date = $session AND value IS NOT NULL;";
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session", day);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    stored[reader.GetString(0)] = reader.GetDouble(1);
                }
            }

            Compare(IndicatorSeries.Sma20, series.Sma20[bar]);
            Compare(IndicatorSeries.Sma50, series.Sma50[bar]);
            Compare(IndicatorSeries.Sma200, series.Sma200[bar]);
            Compare(IndicatorSeries.Atr14, series.Atr[bar]);
            Compare(IndicatorSeries.Rsi14, series.Rsi[bar]);
            Compare(IndicatorSeries.VolAvg50, series.Volume50[bar]);

            void Compare(string name, double sweep)
            {
                var night = stored.TryGetValue(name, out var value) ? value : double.NaN;

                if (double.IsNaN(sweep) != double.IsNaN(night) || (!double.IsNaN(sweep) && Math.Abs(sweep - night) > 1e-9 * Math.Max(1, Math.Abs(night))))
                {
                    Differs(name, Number(sweep), Number(night));
                }
            }

            // The swings the year holds, confirmed by the session.
            var nightSwings = new List<Swing>();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT session_date, direction, price, confirmed_on FROM swing WHERE ticker = $ticker AND confirmed_on <= $session ORDER BY session_date, direction;";
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session", day);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    nightSwings.Add(new Swing(Date(reader.GetString(0)), reader.GetString(1), Money.FromStorage(reader.GetString(2)), Date(reader.GetString(3))));
                }
            }

            var sweepSwings = SweepCandidates.SwingsHeld(series, bar).OrderBy(swing => swing.SessionDate).ThenBy(swing => swing.Direction, StringComparer.Ordinal).ToArray();

            if (!sweepSwings.SequenceEqual(nightSwings))
            {
                Differs("swings", Swings(sweepSwings), Swings(nightSwings));
            }

            // Every band, edge for edge.
            var nightBands = new List<string>();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT low_edge, high_edge, role, strength, has_non_average_anchor FROM level WHERE ticker = $ticker AND as_of = $session;";
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session", day);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    nightBands.Add(Band(Money.FromStorage(reader.GetString(0)), Money.FromStorage(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3), reader.GetInt64(4) != 0));
                }
            }

            var sweepBands = SweepCandidates.LevelsOn(series, bar).Select(level => Band(level.LowEdge, level.HighEdge, level.Role, level.Strength, level.HasNonAverageAnchor)).ToList();

            sweepBands.Sort(StringComparer.Ordinal);
            nightBands.Sort(StringComparer.Ordinal);

            if (!sweepBands.SequenceEqual(nightBands, StringComparer.Ordinal))
            {
                Differs("bands", string.Join("; ", sweepBands), string.Join("; ", nightBands));
            }

            // The live label.
            var trend = await TrendClassifier.ForAsync(connection, ticker, session, series.Bars[bar].Close, cancellation);

            if (!string.Equals(trend.State, series.Label[bar], StringComparison.Ordinal))
            {
                Differs("label", series.Label[bar], trend.State);
            }
        }

        Release(file);

        return differences;
    }

    // The scratch store's pooled connections let go, under each connection string a component opens it with,
    // and the file deleted where nothing holds it.
    static void Release(string file)
    {
        SqliteConnection.ClearPool(new SqliteConnection(StoreConnection.For(file)));
        SqliteConnection.ClearPool(new SqliteConnection(MigrationRunner.ConnectionStringFor(file)));

        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // Deleted with the folder at the end of the check.
        }
    }

    static string Number(double value) => double.IsNaN(value) ? "none" : value.ToString("R", CultureInfo.InvariantCulture);

    static DateOnly Date(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Swings(IEnumerable<Swing> swings) =>
        string.Join("; ", swings.Select(swing => FormattableString.Invariant($"{swing.Direction} {swing.Price} on {swing.SessionDate:yyyy-MM-dd} confirmed {swing.ConfirmedOn:yyyy-MM-dd}")));

    static string Band(decimal low, decimal high, string role, int strength, bool anchored) =>
        FormattableString.Invariant($"{low} to {high} {role} strength {strength}{(anchored ? " anchored" : string.Empty)}");

    // The one name the scratch store holds, as the index feed would file it: a member throughout, with the
    // sector it carries today.
    sealed class OneMember(string ticker, string? sector) : IIndexMembershipFeed
    {
        public int Requests => 0;

        public Task<IReadOnlyList<IndexConstituent>> ConstituentsAsync(string indexCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexConstituent>>([new IndexConstituent(ticker, null, null, sector)]);
    }

    // The name's year of bars, as the historical feed would serve them to the backfill.
    sealed class SeriesBars(string ticker, IReadOnlyList<SweepBar> bars) : IHistoricalBarFeed
    {
        public int Requests => 0;

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string asked, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderBar>>(
                string.Equals(asked, ticker, StringComparison.OrdinalIgnoreCase)
                    ? [.. bars.Where(bar => bar.Session >= from && bar.Session <= to).Select(bar => new ProviderBar(bar.Session, bar.Open > 0 ? bar.Open : bar.Close, bar.High, bar.Low, bar.Close, bar.Close, bar.Volume))]
                    : []);
    }
}
