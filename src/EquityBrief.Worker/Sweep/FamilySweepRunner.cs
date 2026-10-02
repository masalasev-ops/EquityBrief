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
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class FamilySweepRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-family";

    public const string FiguresFile = "figures.json";

    // The families a sweep is built for, and the words the report names each by.
    public static IReadOnlyDictionary<string, string> Families { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BreakoutRule.Name] = "breakouts'",
    };

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

        var sweep = new BreakoutSweep(series, members);
        var readings = sweep.Readings(sessions, firstScored);
        var read = new List<(int[] Setting, FamilyFigures Figures)>();

        foreach (var setting in BreakoutSweep.Grid.Settings)
        {
            var trades = FamilySweep.Walk(BreakoutSweep.Listings(readings, setting), tickers, YearOf, sweep.Exit, sweep.Benchmark);

            read.Add((setting, FamilySweep.Figures(BreakoutSweep.Grid.Key(setting), trades, nights)));
        }

        var proposal = FamilySweep.Propose(BreakoutSweep.Grid, read);
        var run = new FamilySweepRun(family, words, calendar[firstScored], through, inputs.Names.Count, nights, open, readings.Count, started, clock.UtcNow);
        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, FamilySweepReport.Build(run, BreakoutSweep.Grid, read, proposal));
        File.WriteAllText(figures, JsonSerializer.Serialize(new { run, proposal = proposal.Proposed?.Key, settings = read.Select(one => one.Figures) }, SweepRunner.Json));

        output.WriteLine(proposal.Proposed is { } proposed
            ? "proposed " + proposed.Key + ", edge " + FamilySweepReport.Number(proposed.Edge) + " over " + proposed.Trades.ToString(CultureInfo.InvariantCulture) + " trades"
            : "set aside: no setting meets the floors");
        output.WriteLine("report " + report);

        return 0;
    }
}
