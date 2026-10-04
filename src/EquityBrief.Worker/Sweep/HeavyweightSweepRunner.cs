using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// What the heavyweights' sweep read and found, as the page and the run folder hold it: the span scored, the names, the
// sessions and the rebalances of each period, the names the companies pull filed a sector for and those it holds counts
// for, the settings read and how many met the floors, and when it ran.
public sealed record HeavyweightSweepRun(
    DateOnly From,
    DateOnly Through,
    int Names,
    int Sessions,
    int Months,
    int Weeks,
    int WithASector,
    int WithCounts,
    int Settings,
    int MeetingTheFloors,
    DateTimeOffset Started,
    DateTimeOffset Finished);

// The sector heavyweights' sweep, run by hand through the family sweep's verb: the history, the index's series and
// what the companies, splits and funds pulls stored read once from the live store, read-only, the members of every
// session a rebalance reads laid out as they stood, every setting walked, the proposal, the replay held to the
// rebalances the night's book stored, and the report and the figures written into the run folder the verb made. It
// writes nothing to the store.
// see: The heavyweights' sweep replays the book over the pulled history across its settings and proposes the best edge among those meeting the family sweeps' floors
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class HeavyweightSweepRunner(IClock clock, string databaseFile, TextWriter output)
{
    public async Task<int> RunAsync(string folder, DateTimeOffset started, CancellationToken cancellation = default)
    {
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation);
        var market = await history.MarketAsync(through, cancellation);
        var pulled = await history.HeavyweightAsync(through, cancellation);
        var calendar = inputs.Sessions;
        var first = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);
        var weeks = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Week);
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var stored = pulled.Stored.Select(row => row.Session).Distinct().Where(sessionAt.ContainsKey).Select(day => sessionAt[day]);
        var read = months.Concat(weeks).Concat(stored).ToHashSet();

        output.WriteLine(FormattableString.Invariant($"laying out {inputs.Names.Count} name(s) over {calendar.Length - first} session(s), {read.Count} read by a rebalance"));

        var (tape, sessions) = HeavyweightSweep.Lay(inputs, pulled, market.FirstOrDefault(one => one.Series == "GSPC"), read, first);

        output.WriteLine(FormattableString.Invariant($"walking {HeavyweightSweep.Settings.Count} setting(s)"));

        var settings = HeavyweightSweep.ReadAll(tape, sessions, months, weeks);
        var proposal = HeavyweightSweep.Propose(settings);
        var comparison = HeavyweightSweep.Compare(
            pulled.Stored,
            day => sessionAt.TryGetValue(day, out var session) && sessions.TryGetValue(session, out var one) ? HeavyweightSweep.Sectors(one, HeavyweightSweep.Provisional) : null);
        var run = new HeavyweightSweepRun(
            calendar[first],
            through,
            inputs.Names.Count,
            calendar.Length - first,
            months.Count,
            weeks.Count,
            pulled.Companies.Count(company => company.Value.Sector is not null),
            pulled.Counts.Count,
            settings.Count,
            settings.Count(one => one.Figures.MeetsFloors),
            started,
            clock.UtcNow);
        var report = Path.Combine(folder, Core.Sweep.SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FamilySweepRunner.FiguresFile);

        File.WriteAllText(report, HeavyweightSweepReport.Build(run, settings, proposal, comparison));
        File.WriteAllText(figures, JsonSerializer.Serialize(new { run, proposal = proposal.Proposed?.Key, comparison, settings = settings.Select(one => one.Figures) }, SweepRunner.Json));

        output.WriteLine(proposal.Proposed is { } proposed
            ? "proposed " + proposed.Key + ", edge " + FamilySweepReport.Number(proposed.Edge) + " over " + proposed.Trades.ToString(CultureInfo.InvariantCulture) + " trades"
            : "set aside: no setting meets the floors");
        output.WriteLine(FormattableString.Invariant($"the replay read {comparison.Matched} of the {comparison.Sectors} sector(s) the night stored over {comparison.Sessions} rebalance(s) as stored"));
        output.WriteLine("report " + report);

        return 0;
    }
}
