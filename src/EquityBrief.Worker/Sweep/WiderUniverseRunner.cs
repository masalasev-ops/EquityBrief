using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// The wider universe's first test, by hand: the history read twice from the live store, read-only, the S&P 500's alone
// and the 1,500 with today's S&P 400 and 600 members beside it, each swing family's rule as frozen replayed over both,
// and the report and the figures written into a run folder of its own under the sweep's folder. It writes nothing to
// the store, and it does not start while the night holds the store or inside the night's window, nor before a members
// pull has stored today's members.
// see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class WiderUniverseRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-wider";

    public const string FiguresFile = "figures.json";

    // How long a run is allowed for before the night's window, which it does not start inside: two histories and three
    // families over each.
    public static readonly TimeSpan Expected = TimeSpan.FromHours(2);

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
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var fiveHundred = await history.ReadAsync(through, output.WriteLine, cancellation);
        var fifteenHundred = await history.ReadAsync(through, output.WriteLine, cancellation, wider: true);

        if (fifteenHundred.WiderMembers == 0)
        {
            output.WriteLine($"{Verb}: the store holds no member of the S&P 400 or 600; pull them first with 'history-pull --members --index MID' and 'SML', then their history");

            return 2;
        }

        var folder = Path.Combine(SweepFolder.Resolve(configuredFolder, dataRoot), SweepFolder.RunName(started));

        Directory.CreateDirectory(folder);
        output.WriteLine("run " + Path.GetFileName(folder));

        var market = await history.MarketAsync(through, cancellation);
        var readings = Read(fiveHundred, fifteenHundred, market, output.WriteLine);
        var run = new WiderRun(
            fiveHundred.Sessions[Array.FindIndex(fiveHundred.Sessions, session => session >= SweepColumns.FirstScored)],
            through,
            fiveHundred.Names.Count,
            fifteenHundred.Names.Count,
            fifteenHundred.WiderMembers,
            fifteenHundred.Survivors,
            fiveHundred.Sessions.Count(session => session >= SweepColumns.FirstScored),
            started,
            clock.UtcNow);
        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, WiderUniverse.Build(run, readings));
        File.WriteAllText(figures, JsonSerializer.Serialize(
            new
            {
                run,
                dropped = WiderUniverse.Dropped(readings),
                luck = WiderUniverse.Luck(readings.Count),
                families = readings.Select(reading => new
                {
                    reading.Family,
                    fiveHundred = reading.FiveHundred,
                    fifteenHundred = new { survivorsOnly = WiderUniverse.SurvivorsOnly, figures = reading.FifteenHundred },
                    reading.Test,
                    reading.Improves,
                }),
            },
            SweepRunner.Json));

        output.WriteLine(WiderUniverse.InWords(readings));
        output.WriteLine("report " + report);

        return 0;
    }

    // Each family's rule as frozen over both universes: the pullback's base through the ideas' run's replay, the breakout
    // and the earnings drift through the frozen families' own, each 1,500 tested against its 500.
    public static IReadOnlyList<WiderReading> Read(SweepHistoryInputs fiveHundred, SweepHistoryInputs fifteenHundred, IReadOnlyList<SweepMarketSeries> market, Action<string>? progress = null)
    {
        IdeaFigures Pullback(SweepHistoryInputs inputs) => SweepIdeasRunner.Read(inputs, market, progress).Replay.Evaluate("the base", SweepIdeas.BaseRule);

        IdeaFigures Frozen(string family, SweepHistoryInputs inputs) => FamilyIdeasRunner.AsFrozen(family, inputs, market, progress);

        return
        [
            .. WiderUniverse.Families.Select(family =>
            {
                progress?.Invoke("reading the " + family.Words + " rule on the 500 and on the 1,500");

                return family.Family == "pullback"
                    ? WiderUniverse.Read(family.Family, family.Words, Pullback(fiveHundred), Pullback(fifteenHundred))
                    : WiderUniverse.Read(family.Family, family.Words, Frozen(family.Family, fiveHundred), Frozen(family.Family, fifteenHundred));
            }),
        ];
    }
}
