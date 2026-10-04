using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// The context run, by hand: the history, the market series and each name's revenue as filed read once from the live
// store, read-only, the drift as frozen and the pullback's base replayed over them, each context idea read alone,
// and the report and the figures written into a run folder of its own under the sweep's folder. It writes nothing to
// the store, and it does not start while the night holds the store or inside the night's window.
// see: The context checks are read on the frozen families one at a time, and nothing they show is frozen or registered
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class ContextIdeasRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-context";

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
        var revenue = await history.RevenueAsync(through, cancellation);
        var drift = ContextIdeas.ReadDrift(inputs, market, revenue, output.WriteLine);
        var pullback = ContextIdeas.ReadPullback(inputs, market, output.WriteLine);
        var read = new ContextIdeasRead(drift.Frozen, drift.Base, drift.Listings, drift.WithRevenue, drift.Ideas, pullback.Base, pullback.Ideas, pullback.FirstThree);
        var run = new ContextIdeasRun(
            inputs.Sessions[Array.FindIndex(inputs.Sessions, session => session >= SweepColumns.FirstScored)],
            inputs.Through,
            inputs.Names.Count,
            inputs.Sessions.Count(session => session >= SweepColumns.FirstScored),
            revenue.Count,
            started,
            clock.UtcNow);
        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, ContextIdeasReport.Build(run, read));
        File.WriteAllText(figures, JsonSerializer.Serialize(
            new
            {
                run,
                read.DriftFrozen,
                driftBase = read.DriftBase,
                read.DriftListings,
                read.DriftListingsWithRevenue,
                drift = read.Drift.Select(one => new { one.Key, one.Figures, one.Test, one.Test.Passes, one.RevenueRead, one.FiledAfterTheBuy }),
                pullbackBase = read.PullbackBase,
                pullback = read.Pullback.Select(one => new { one.Key, one.Figures, one.Test, one.Test.Passes }),
                pullbackFirstThree = read.PullbackFirstThree,
            },
            SweepRunner.Json));

        output.WriteLine(ContextIdeasReport.InWords(read));
        output.WriteLine("report " + report);

        return 0;
    }
}
