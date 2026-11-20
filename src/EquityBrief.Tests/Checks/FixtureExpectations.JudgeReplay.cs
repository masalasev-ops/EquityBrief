using System.Globalization;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Judge;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.7: the AI judge's replay of the nights after the model's cutoff: its plan worked by hand, a night
// on or before the cutoff and a night already run left out, the total hours the product of the count and the minutes a
// night; nothing replayed without the operator's word and the model yielded before each night to any process waiting on
// it; a night's facts built again held to those stored, each difference named; and the replay's runs carrying the prefix
// every surface reads apart.
// see: The AI judge's replays print their hours before they start on the operator's word, a new version's over the same 60 nights
public partial class FixtureExpectations
{
    // The row of section 18 24.7 adds that this check reaches.
    internal static readonly string[] JudgeReplayClaims =
    [
        CheckReach.Key(Scope.FailureTable, "An AI judge replay without the operator's word, over a night on or before the model's cutoff, or with another process waiting on the model"),
    ];

    static DateOnly ReplayDay(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public void TheReplaysPlanLeavesOutTheNightsOnOrBeforeTheCutoffAndThoseRunAndItsHoursAreTheCountTimesTheMinutes()
    {
        var cutoff = ReplayDay("2025-01-31");
        DateOnly[] buildable = [ReplayDay("2025-01-30"), ReplayDay("2025-01-31"), ReplayDay("2025-02-03"), ReplayDay("2026-10-13"), ReplayDay("2026-10-14"), ReplayDay("2026-11-19")];

        // Two live nights measured at 4 and 6 minutes: three nights left, 5 minutes a night, a quarter of an hour in all.
        var plan = JudgeReplay.Plan(buildable, new HashSet<DateOnly> { ReplayDay("2026-11-19") }, cutoff, [4.0, 6.0]);

        Assert.Equal([ReplayDay("2025-02-03"), ReplayDay("2026-10-13"), ReplayDay("2026-10-14")], plan.Nights);
        Assert.Equal((5.0, 0.25, (string?)null), (plan.MinutesANight!.Value, plan.Hours!.Value, plan.Refused));
        Assert.Equal(plan.Nights.Count * plan.MinutesANight!.Value / 60.0, plan.Hours!.Value);
        Assert.Equal("3 nights from 2025-02-03 to 2026-10-14, 5 minutes a night as the judge's live runs measured them, 0.25 hours of the local model in all", JudgeReplay.Words(plan));

        // The cutoff's own night and the day before it are refused; the day after is not.
        Assert.True(JudgeReplay.OnOrBeforeCutoff(cutoff, cutoff));
        Assert.True(JudgeReplay.OnOrBeforeCutoff(cutoff.AddDays(-1), cutoff));
        Assert.False(JudgeReplay.OnOrBeforeCutoff(cutoff.AddDays(1), cutoff));

        // No live night measured states no hours and does not start; nothing left to replay does not start either.
        Assert.Equal((JudgeReplay.NotMeasured, (double?)null), (JudgeReplay.Plan(buildable, new HashSet<DateOnly>(), cutoff, []).Refused, JudgeReplay.Plan(buildable, new HashSet<DateOnly>(), cutoff, []).Hours));
        Assert.Equal(JudgeReplay.NothingLeft, JudgeReplay.Plan([cutoff], new HashSet<DateOnly>(), cutoff, [5.0]).Refused);

        // A replay's runs carry the prefix the judge's page reads apart from the live record.
        Assert.Equal(EquityBrief.Api.Reading.ReadApi.JudgeReplayPrefix, JudgeReplay.Prefix);
    }

    [Fact]
    public async Task NothingIsReplayedWithoutTheWordAndTheModelIsYieldedToAnyProcessWaitingOnIt()
    {
        var plan = JudgeReplay.Plan([ReplayDay("2026-10-13"), ReplayDay("2026-10-14")], new HashSet<DateOnly>(), ReplayDay("2025-01-31"), [5.0]);
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var asked = new List<DateOnly>();

        Task<string> One(DateOnly night)
        {
            asked.Add(night);

            return Task.FromResult("picked");
        }

        // Without the word nothing runs; with it each night in turn.
        Assert.Empty(await JudgeReplayRun.RunAsync(plan, false, clock.UtcNow.AddHours(4), clock, () => null, One));
        Assert.Empty(asked);
        Assert.Equal([(ReplayDay("2026-10-13"), "picked"), (ReplayDay("2026-10-14"), "picked")], await JudgeReplayRun.RunAsync(plan, true, clock.UtcNow.AddHours(4), clock, () => null, One));

        // A night waiting on the model, or the instant given reached, stops it before the next night asks anything.
        asked.Clear();

        Assert.Equal([(ReplayDay("2026-10-13"), "yielded the model to the night")], await JudgeReplayRun.RunAsync(plan, true, clock.UtcNow.AddHours(4), clock, () => "the night", One));
        Assert.Equal([(ReplayDay("2026-10-13"), "stopped at the instant given")], await JudgeReplayRun.RunAsync(plan, true, clock.UtcNow, clock, () => null, One));
        Assert.Empty(asked);

        // The labeller or a local run holding the local model's lock is read as waiting on it, and a free lock as nothing.
        using var store = new TemporaryStore();
        var folder = Path.Combine(store.Root, WorkerDrainLauncher.CopiesFolder);

        Directory.CreateDirectory(folder);

        Assert.Null(JudgeReplayRun.Waiting(store.Root));

        using (new FileStream(Path.Combine(folder, LocalLocks.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal("another local process holding the local model's lock", JudgeReplayRun.Waiting(store.Root));
        }

        Assert.Null(JudgeReplayRun.Waiting(store.Root));
    }

    [Fact]
    public async Task ANightsFactsBuiltAgainAreHeldToThoseStoredAndEachDifferenceIsNamed()
    {
        using var store = JudgeStore();

        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 20, 2, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var night = ReplayDay(JudgeNight);

        // Nothing stored differs from nothing; the night written and built again is the same night.
        Assert.Empty(await JudgeReplayRun.FactsDifferAsync(store.DatabaseFile, night));

        await new JudgeFactWriter(clock, store.DatabaseFile).WriteAsync(night, "judge-facts-1");

        Assert.Empty(await JudgeReplayRun.FactsDifferAsync(store.DatabaseFile, night));

        // A stored row that no longer matches, and a member stored no more, are each named.
        store.Execute($"UPDATE judge_fact SET row_text = 'another row' WHERE ticker = 'AAA' AND session_date = '{JudgeNight}';");
        store.Execute($"DELETE FROM judge_fact WHERE ticker = 'BBB' AND session_date = '{JudgeNight}';");

        Assert.Equal(["AAA's short row differs", "BBB is built again and not stored"], await JudgeReplayRun.FactsDifferAsync(store.DatabaseFile, night));
    }
}
