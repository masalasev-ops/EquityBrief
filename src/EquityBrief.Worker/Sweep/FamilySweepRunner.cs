using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// A setup family's sweep, run by hand: the history read once from the live store, read-only, the family's
// readings over it, every setting of its grid walked, the proposal, and the report and the figures written
// into a run folder of its own under the sweep's folder, beside the pullback sweep's runs. It writes nothing to
// the store, and it does not start while the night holds the store or inside the night's window, since it runs
// for minutes and a night would wait on its reads.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class FamilySweepRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-family";

    public const string FiguresFile = "figures.json";

    // The families a sweep is built for, and the words the report names each by.
    public static IReadOnlyDictionary<string, string> Families { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BreakoutRule.Name] = "breakouts'",
        [DriftRule.Name] = "earnings drift's",
        [LeaderRule.Name] = "sector leaders'",
        [HeavyweightRule.Name] = "sector heavyweights'",
    };

    // One family's sweep as the runner reads it: its grid, how many member-sessions its loosest setting could
    // list, the listings a setting makes, its exit and its benchmark, and a line of what it read that only this
    // family's report states.
    public sealed record Adapter(
        FamilyGrid Grid,
        int Readings,
        Func<int[], IEnumerable<FamilyListing>> Listings,
        Func<FamilyListing, (double? Result, int Sessions)> Exit,
        Func<FamilyListing, double> Benchmark,
        string? Note = null);

    public static Adapter For(string family, IReadOnlyList<SweepSeries> series, IReadOnlyList<SweepColumns.Session> sessions, SweepBenchmark.Members members, int firstScored, IReadOnlyList<DateOnly>? calendar = null, double driftStopFloor = 0)
    {
        switch (family)
        {
            case BreakoutRule.Name:
            {
                var sweep = new BreakoutSweep(series, members);
                var readings = sweep.Readings(sessions, firstScored);

                return new(BreakoutSweep.Grid, readings.Count, setting => BreakoutSweep.Listings(readings, setting), sweep.Exit, sweep.Benchmark);
            }

            case DriftRule.Name:
            {
                var sweep = new DriftSweep(series, members);
                var readings = sweep.Readings(sessions, firstScored);

                return new(DriftSweep.Grid, readings.Count, setting => DriftSweep.Listings(readings, setting, driftStopFloor), sweep.Exit, sweep.Benchmark);
            }

            case LeaderRule.Name:
            {
                var days = calendar ?? throw new ArgumentException("The sector leaders' sweep reads the history's calendar.", nameof(calendar));
                var (leaders, carrying, notCarrying) = LeaderSweep.Standings(series, sessions, members, firstScored);
                var bars = leaders.GroupBy(one => one.Name).ToDictionary(group => group.Key, group => (IReadOnlySet<int>)group.Select(one => one.Bar).ToHashSet());
                var found = new List<SweepCandidate>[series.Count];

                Parallel.For(0, series.Count, name => found[name] = bars.TryGetValue(name, out var only)
                    ? SweepCandidates.For(series[name], name, sessions, days, firstScored, firstScored, days.Count, only)
                    : []);

                var candidates = found.SelectMany(list => list).ToArray();

                SweepBenchmark.Fill(candidates, series, members, Environment.ProcessorCount);

                var readings = LeaderSweep.Readings(leaders, candidates, series);
                var byListing = readings.ToDictionary(reading => (reading.Name, reading.Session));
                var heldToday = series.Count(one => one.Name.Sector is not { Length: > 0 } && one.Name.MemberOn(days[^1]));

                return new(
                    LeaderSweep.Grid,
                    readings.Count,
                    setting => LeaderSweep.Listings(readings, setting),
                    listing => LeaderSweep.ExitOf(byListing[(listing.Name, listing.Session)]),
                    listing => byListing[(listing.Name, listing.Session)].Benchmark,
                    FormattableString.Invariant($"Of the history's {series.Count:N0} names, {carrying:N0} carry a sector as the membership files it today and {notCarrying:N0} do not, {heldToday:N0} of them members on the history's last session; a name with none is in no sector's ranking and lists nothing, so where the names without one are the ones the index has let go, the record reads the members that stayed. {leaders.Count:N0} member-sessions stood inside the loosest setting's sectors and share, and the pullback's setup, trigger and trade passed on {readings.Count:N0} of them."));
            }

            default:
                throw new ArgumentException($"No sweep is built for the family '{family}'.", nameof(family));
        }
    }

    // How long a run is allowed for before the night's window, which it does not start inside.
    public static readonly TimeSpan Expected = TimeSpan.FromHours(1);

    public async Task<int> RunAsync(string family, CancellationToken cancellation = default)
    {
        if (!Families.TryGetValue(family, out var words))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: name a family with '--family', one of {string.Join(", ", Families.Keys)}"));

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var folder = Path.Combine(SweepFolder.Resolve(configuredFolder, dataRoot), SweepFolder.RunName(started));

        Directory.CreateDirectory(folder);
        output.WriteLine("run " + Path.GetFileName(folder));

        // The sector heavyweights rotate on rebalances rather than list a night, so their sweep walks a book of its own.
        if (family == HeavyweightRule.Name)
        {
            return await new HeavyweightSweepRunner(clock, databaseFile, output).RunAsync(folder, started, cancellation);
        }

        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation);
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var tickers = inputs.Names.Select(name => name.Ticker).ToArray();
        var open = Enumerable.Range(firstScored, nights).Count(session => sessions[session].Breadth >= FamilySweep.MarketFloor);

        int YearOf(int session) => calendar[session].Year - SweepColumns.FirstScored.Year;

        var adapter = For(family, series, sessions, members, firstScored, calendar);
        var read = new List<(int[] Setting, FamilyFigures Figures)>();

        foreach (var setting in adapter.Grid.Settings)
        {
            var trades = FamilySweep.Walk(adapter.Listings(setting), tickers, YearOf, adapter.Exit, adapter.Benchmark);

            read.Add((setting, FamilySweep.Figures(adapter.Grid.Key(setting), trades, nights)));
        }

        var proposal = FamilySweep.Propose(adapter.Grid, read);
        var run = new FamilySweepRun(family, words, calendar[firstScored], through, inputs.Names.Count, nights, open, adapter.Readings, started, clock.UtcNow, adapter.Note);
        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, FamilySweepReport.Build(run, adapter.Grid, read, proposal));
        File.WriteAllText(figures, JsonSerializer.Serialize(new { run, proposal = proposal.Proposed?.Key, settings = read.Select(one => one.Figures) }, SweepRunner.Json));

        output.WriteLine(proposal.Proposed is { } proposed
            ? "proposed " + proposed.Key + ", edge " + FamilySweepReport.Number(proposed.Edge) + " over " + proposed.Trades.ToString(CultureInfo.InvariantCulture) + " trades"
            : "none passed: no setting meets the floors, and the report states the strongest settings and what could be tried next");
        output.WriteLine("report " + report);

        return 0;
    }
}
