using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// The ideas' run on a frozen family, by hand: the history and the market series read once from the live store,
// read-only, the family's rule as frozen replayed over it, every idea that fits it read alone, and the report and
// the figures written into a run folder of its own under the sweep's folder. It writes nothing to the store, and it
// does not start while the night holds the store or inside the night's window.
// see: The frozen families are read with the pullback's ideas one at a time, and nothing they show is frozen or registered
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class FamilyIdeasRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-family-ideas";

    public const string FiguresFile = "figures.json";

    // The families the ideas are read on, and the words the report names each by.
    public static IReadOnlyDictionary<string, string> Families { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BreakoutRule.Name] = "breakouts'",
        [DriftRule.Name] = "earnings drift's",
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
        var market = await history.MarketAsync(through, cancellation);
        var (read, listings) = Read(family, inputs, market, output.WriteLine);
        var run = new FamilyIdeasRun(
            family,
            words,
            inputs.Sessions[Array.FindIndex(inputs.Sessions, session => session >= SweepColumns.FirstScored)],
            inputs.Through,
            inputs.Names.Count,
            inputs.Sessions.Count(session => session >= SweepColumns.FirstScored),
            listings,
            SweepIdeas.LeftOut(market),
            started,
            clock.UtcNow);
        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, FamilyIdeasReport.Build(run, read));
        File.WriteAllText(figures, JsonSerializer.Serialize(
            new
            {
                run,
                read.Frozen,
                read.Tries,
                read.Passes,
                read.Luck,
                @base = read.Base,
                ideas = read.Ideas.Select(reading => new { reading.Idea.Key, reading.Figures, reading.Test, reading.Test.Passes }),
            },
            SweepRunner.Json));

        output.WriteLine(FamilyIdeasReport.InWords(read));
        output.WriteLine("report " + report);

        return 0;
    }

    // The family's rule as frozen and every idea read on it over the history and the market series, with how many
    // listings the rule as frozen makes.
    public static (FamilyIdeasRead Read, int Listings) Read(string family, SweepHistoryInputs inputs, IReadOnlyList<SweepMarketSeries> market, Action<string>? progress = null)
    {
        var (replay, adapter, frozen) = Replay(family, inputs, market, progress);
        var leftOut = SweepIdeas.LeftOut(market);
        var asFrozen = replay.Evaluate("as frozen", null);
        var readings = FamilyIdeas.For(family)
            .Where(idea => !leftOut.Contains(idea.Key))
            .Select(idea =>
            {
                var figures = replay.Evaluate(idea.Key, idea);

                return new FamilyIdeaReading(idea, figures, SweepIdeas.Test(figures, asFrozen, idea.Market));
            })
            .ToArray();

        return (new FamilyIdeasRead(family, adapter.Grid.Key(frozen), asFrozen, readings), replay.Listings);
    }

    // The family's rule as frozen alone over the history and the market series, with no idea read on it.
    public static IdeaFigures AsFrozen(string family, SweepHistoryInputs inputs, IReadOnlyList<SweepMarketSeries> market, Action<string>? progress = null) =>
        Replay(family, inputs, market, progress).Replay.Evaluate("as frozen", null);

    // The replay every reading of a frozen family runs through: each name's series, the members, the family's own
    // listings at its frozen setting and the market switches on each session.
    static (FamilyIdeaReplay Replay, FamilySweepRunner.Adapter Adapter, int[] Frozen) Replay(string family, SweepHistoryInputs inputs, IReadOnlyList<SweepMarketSeries> market, Action<string>? progress)
    {
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);

        int YearOf(int session) => calendar[session].Year - SweepColumns.FirstScored.Year;

        progress?.Invoke("reading the " + family + " family's listings");

        var adapter = FamilySweepRunner.For(family, series, sessions, members, firstScored, calendar);
        var frozen = FamilyIdeas.Frozen(family, adapter.Grid);
        var breadth = SweepIdeas.BreadthAboveTheSlowAverage(series, members, calendar.Length);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);
        var index = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "GSPC"), calendar);
        var vix = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "VIX"), calendar);
        var switches = SweepIdeas.Switches(breadth, highs, lows, index, vix);
        var replay = new FamilyIdeaReplay(adapter, frozen, series, members, switches, YearOf, nights);

        return (replay, adapter, frozen);
    }
}
