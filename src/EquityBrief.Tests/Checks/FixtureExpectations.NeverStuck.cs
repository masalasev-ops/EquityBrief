using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: never stuck worked by hand. The nearest misses are those closest to the bar, three of them, a miss
// with no adjusted p-value left out; what the researcher tried next from one is the first idea it placed on the same setup and
// index after the miss; and the next week falls on the next Sunday whose hour has not passed, whatever failed before it.
// see: A month with no idea passed names its nearest misses and what the researcher tried next, and a failed week is counted and never stops the next
public partial class FixtureExpectations
{
    internal static string[] NeverStuckClaims =>
    [
        CheckReach.Key(Scope.FailureTable, "A month in which no idea passes"),
    ];

    internal static string[] NeverStuckRows => [.. NeverStuckClaims, .. Reading.ReadSurface.NeverStuckRows];

    static NearMissRow Miss(long id, double? adjusted, int day, string index = "SML", string family = "pullback") =>
        new(id, index, family, $"miss {id}", $"line {id}", adjusted, 1.0 / 1040, new DateTimeOffset(2026, 12, day, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public void TheNearestMissesWhatTheResearcherTriedNextAndTheNextWeekAreWorkedByHand()
    {
        // Five near misses: the three at 1 in 500, 1 in 333 and 1 in 200 are the nearest, the lowest p first; one at 1 in 100 is
        // fourth, and one under the floor of blocks holds no p and is never named.
        var nearest = NeverStuck.Nearest([Miss(1, 0.01, 1), Miss(2, 0.002, 2), Miss(3, null, 3), Miss(4, 0.005, 4), Miss(5, 0.003, 5)]);

        Assert.Equal([2L, 5L, 4L], nearest.Select(miss => miss.Id));

        // On a tie the newer first.
        Assert.Equal([7L, 6L], NeverStuck.Nearest([Miss(6, 0.004, 6), Miss(7, 0.004, 7)]).Select(miss => miss.Id));

        // What the researcher tried next from miss 2, tested on 2 Dec: the first idea it placed on the S&P 600 pullback after
        // it, never one placed before, one on another setup or index, or the miss's own line again.
        PlacedIdea Placed(string line, int day, string index = "SML", string family = "pullback") =>
            new(index, family, line, $"reason {line}", new DateTimeOffset(2026, 12, day, 16, 0, 0, TimeSpan.Zero));

        var placed = new[] { Placed("before", 1), Placed("line 2", 3), Placed("elsewhere", 3, "MID"), Placed("drift", 3, family: "drift"), Placed("second", 10), Placed("first", 6) };

        Assert.Equal("first", NeverStuck.NextFrom(nearest[0], placed)!.Line);
        Assert.Null(NeverStuck.NextFrom(nearest[0], [Placed("before", 1), Placed("line 2", 3)]));

        // The next week: a Saturday reads the next day; the Sunday before 13:00 UTC reads itself, and at 13:00 the Sunday after.
        Assert.Equal(new DateOnly(2026, 11, 22), NeverStuck.NextWeek(new DateTimeOffset(2026, 11, 21, 18, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 22), NeverStuck.NextWeek(new DateTimeOffset(2026, 11, 22, 12, 59, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 29), NeverStuck.NextWeek(new DateTimeOffset(2026, 11, 22, 13, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 29), NeverStuck.NextWeek(new DateTimeOffset(2026, 11, 23, 4, 0, 0, TimeSpan.Zero)));

        // The view carries the counts as given and the nearest with what was tried next.
        var view = NeverStuck.Of(new DateOnly(2026, 12, 5), 0, [Miss(1, 0.01, 1), Miss(2, 0.002, 2)], placed, 1, 3, new DateTimeOffset(2026, 12, 7, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal((0, 1, 3, new DateOnly(2026, 12, 13)), (view.Passed, view.FailedWeeks, view.Weeks, view.NextWeek));
        Assert.Equal([(2L, "first"), (1L, "before")], view.Nearest.Select(one => (one.Miss.Id, one.Next?.Line)));
    }
}
