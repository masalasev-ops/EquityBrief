using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// The ideas' run, by hand: the history and the market series read once from the live store, read-only, the
// sweep's candidates and their benchmark computed over it, every idea read on the base, the proposal, and the
// report and the figures written into a run folder of its own under the sweep's folder. It writes nothing to the
// store, and it does not start while the night holds the store or inside the night's window.
// see: A new idea is added to the base one at a time and kept only where it is better in six of eight years
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class SweepIdeasRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-ideas";

    public const string FiguresFile = "figures.json";

    // How long a run is allowed for before the night's window, which it does not start inside.
    public static readonly TimeSpan Expected = TimeSpan.FromHours(1);

    public async Task<int> RunAsync(CancellationToken cancellation = default)
    {
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
        var market = await history.MarketAsync(through, cancellation);
        var (replay, run) = Read(inputs, market, output.WriteLine);
        var ideas = SweepIdeas.All.Where(idea => !run.LeftOut.Contains(idea.Key)).ToArray();
        var proposal = SweepIdeas.Propose(ideas, replay.Evaluate);
        var finished = run with { Finished = clock.UtcNow, Started = started };
        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, SweepIdeasReport.Build(finished, proposal, market));
        File.WriteAllText(figures, JsonSerializer.Serialize(
            new
            {
                run = finished,
                kept = proposal.Kept,
                start = proposal.StartFigures,
                today = proposal.Today,
                @base = proposal.Base,
                ideas = proposal.Ideas.Select(reading => new { reading.Idea.Key, reading.Figures, reading.Test, reading.Test.Passes }),
                together = proposal.Together is { } together ? new { together.Idea.Key, together.Figures, together.Test, together.Test.Passes } : null,
                variants = proposal.Variants,
            },
            SweepRunner.Json));

        output.WriteLine(SweepIdeasReport.StartInWords(proposal));
        output.WriteLine("report " + report);

        return 0;
    }

    // The history as the ideas read it: each name's series, the sessions, the members, the sweep's candidates
    // over every scored session with their benchmark, the market switches on each session, and the ideas left out
    // for a series the store does not hold.
    public static (IdeaReplay Replay, SweepIdeasRun Run) Read(SweepHistoryInputs inputs, IReadOnlyList<SweepMarketSeries> market, Action<string>? progress = null)
    {
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var found = new List<SweepCandidate>[series.Length];

        progress?.Invoke("reading the candidates");
        Parallel.For(0, series.Length, name => found[name] = SweepCandidates.For(series[name], name, sessions, calendar, firstScored, firstScored, calendar.Length));

        var candidates = found.SelectMany(list => list).ToArray();

        progress?.Invoke(FormattableString.Invariant($"read {candidates.Length} candidates; filling their benchmark"));
        SweepBenchmark.Fill(candidates, series, members, Environment.ProcessorCount);

        var breadth = SweepIdeas.BreadthAboveTheSlowAverage(series, members, calendar.Length);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);
        var index = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "GSPC"), calendar);
        var vix = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "VIX"), calendar);
        var switches = SweepIdeas.Switches(breadth, highs, lows, index, vix);
        var replay = new IdeaReplay(series, candidates, members, switches, nights);

        return (replay, new SweepIdeasRun(calendar[firstScored], inputs.Through, inputs.Names.Count, nights, candidates.Length, replay.Picks, SweepIdeas.LeftOut(market), default, default));
    }
}
