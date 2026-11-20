using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.11: each cause the split rule sets a step or an entry aside for worked by hand over a constructed
// table and series, a step on its own ratio read as a split and one off it set aside; the long history and the store's
// year read and counted by cause with the store left as it stood; and no source 24.11 writes one a rule's version pins.
// see: A split table's entry is read only where the closes step by its own ratio, and an entry of another size is set aside and listed
public partial class FixtureExpectations
{
    // The row of section 18 24.11 adds that this check reaches.
    internal static readonly string[] SplitCauseClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A step or an entry the split rule sets aside"),
    ];

    static readonly DateOnly SplitStart = new(2020, 1, 1);

    // A stock's closes at 100 on every day from the first, its close as traded the store's close times the factor, which
    // steps on each day named by the step given.
    static List<SweepBar> SplitSeries(int days, params (int Day, decimal Step)[] steps)
    {
        var bars = new List<SweepBar>();
        var factor = 1m;

        for (var day = 0; day < days; day++)
        {
            foreach (var step in steps.Where(step => step.Day == day))
            {
                factor *= step.Step;
            }

            bars.Add(new SweepBar(SplitStart.AddDays(day), 100m, 100m, 100m, 1000, RawClose: 100m * factor));
        }

        return bars;
    }

    static SplitEntry SplitAt(int day, decimal @new, decimal old) => new(SplitStart.AddDays(day), @new, old);

    // Over one stock: day 10's half on its own 2 for 1 read as a split and listed nowhere; day 40's half beside a 3 for 1
    // on day 41, the step and the entry each set aside beside the other; day 70's doubling undone on day 75; day 110's step
    // of 0.6 the dividend of 10 on a close of 25 makes; day 150's third, a 3 for 1 no table holds; and day 190's 2.6, no
    // split's ratio within 2 in 100 of it. Over another, flat: a 5 for 4 a small split, 1,328 for 1,000 an adjustment
    // and a 2 for 1 with no step.
    [Fact]
    public void EachCauseIsWorkedByHandAndAStepOnItsOwnRatioIsReadAsASplitAndOneOffItSetAside()
    {
        var bars = SplitSeries(220, (10, 0.5m), (40, 0.5m), (70, 2m), (75, 0.5m), (110, 0.6m), (150, 1m / 3m), (190, 2.6m));
        var found = SplitCauses.Of(bars, [SplitAt(10, 2, 1), SplitAt(41, 3, 1)], [new SplitDividend(SplitStart.AddDays(110), 10m)]);

        Assert.Equal(
            [
                (40, SplitCause.BesideEntry), (70, SplitCause.Undone), (75, SplitCause.Undone), (110, SplitCause.SpecialDividend),
                (150, SplitCause.UnheldSplit), (190, SplitCause.NoCause), (41, SplitCause.BesideStep),
            ],
            found.Select(one => (one.Day.DayNumber - SplitStart.DayNumber, one.Cause)));
        Assert.DoesNotContain(found, one => one.Day == SplitStart.AddDays(10));
        Assert.Equal([0.5, 2, 0.5, 0.6, 0.333, 2.6], found.Where(one => one.Step is not null).Select(one => Math.Round(Core.Prices.Statistic.FromRatio(one.Step!.Value), 3)));

        // With no table holding it, day 10's half is a split's ratio no table holds; with no dividend, day 110's step finds no
        // cause, 0.6 being no split's ratio either way.
        var bare = SplitCauses.Of(bars, [SplitAt(41, 3, 1)], []);

        Assert.Equal(SplitCause.UnheldSplit, Assert.Single(bare, one => one.Day == SplitStart.AddDays(10)).Cause);
        Assert.Equal(SplitCause.NoCause, Assert.Single(bare, one => one.Day == SplitStart.AddDays(110)).Cause);

        var flat = SplitCauses.Of(SplitSeries(100), [SplitAt(20, 5, 4), SplitAt(50, 1328, 1000), SplitAt(80, 2, 1), SplitAt(80, 2, 1)], []);

        Assert.Equal([(20, SplitCause.SmallSplit), (50, SplitCause.Adjustment), (80, SplitCause.NoStep)], flat.Select(one => (one.Day.DayNumber - SplitStart.DayNumber, one.Cause)));
        Assert.Equal("a stock dividend's or a spin-off's adjustment", SplitCauses.Words(SplitCause.Adjustment));
    }

    // The long history read against the three split tables and both dividend tables and the store's year against those the
    // night reads, each counted by cause, and the store file left byte for byte as it stood.
    [Fact]
    public async Task TheLongHistoryAndTheStoresYearAreCountedByCauseAndNothingIsWritten()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var (day, close, raw) in new[] { (0, "100", "100"), (1, "100", "50"), (30, "100", "50"), (60, "100", "50") })
        {
            store.Execute(FormattableString.Invariant($"INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES ('LNG', '{SplitStart.AddDays(day):yyyy-MM-dd}', '{close}', '{close}', '{close}', '{close}', '{raw}', 1000, 'pull');"));
        }

        store.Execute(FormattableString.Invariant($"INSERT INTO long_split (ticker, ex_date, new_shares, old_shares, run_id) VALUES ('LNG', '{SplitStart.AddDays(30):yyyy-MM-dd}', '5', '4', 'pull');"));
        store.Execute(FormattableString.Invariant($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('NOW', '2026-09-01', '50', '50', '50', '50', 1000, 'constructed', '2026-09-01T21:00:00Z', '100'), ('NOW', '2026-09-02', '51', '51', '51', '51', 1000, 'constructed', '2026-09-02T21:00:00Z', '51');"));

        static byte[] Bytes(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();

            stream.CopyTo(copy);

            return copy.ToArray();
        }

        var before = Bytes(store.DatabaseFile);
        var found = await SplitCausesRun.ReadAsync(store.DatabaseFile);

        Assert.Equal(
            [(SplitCausesRun.LongSpan, "LNG", SplitCause.UnheldSplit), (SplitCausesRun.LongSpan, "LNG", SplitCause.SmallSplit), (SplitCausesRun.StoreSpan, "NOW", SplitCause.UnheldSplit)],
            found.Select(one => (one.Span, one.Ticker, one.SetAside.Cause)));
        Assert.Equal(
            [
                "the long history: 1 step(s) and 1 entr(ies) set aside; a split's ratio no table holds: 1; a small split under the split step: 1",
                "the store's year: 1 step(s) and 0 entr(ies) set aside; a split's ratio no table holds: 1",
            ],
            SplitCausesRun.Counts(found));
        Assert.Equal("the long history: LNG 2020-01-31, an entry of 5 for 4: a small split under the split step", SplitCausesRun.Line(found[1]));
        Assert.Equal(before, Bytes(store.DatabaseFile));
    }

    // Every shipped source 24.11 writes or changes, none of them one a rule's version pins, so no rule's code version moves.
    internal static readonly string[] SplitCauseSources =
    [
        "src/EquityBrief.Core/Ledger/SplitCauses.cs",
        "src/EquityBrief.Worker/History/SplitCausesRun.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Chart.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.SplitSteps.cs",
    ];

    [Fact]
    public void NoSource24Point11WritesIsOneARulesVersionPins()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(SplitCauseSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(SplitCauseSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Core/Ledger/StoredSplits.cs", pinned);
    }
}
