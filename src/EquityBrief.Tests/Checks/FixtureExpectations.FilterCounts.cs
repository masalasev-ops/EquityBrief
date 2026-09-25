using System.Security.Cryptography;
using EquityBrief.Core.Filter;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.2: the counts the operator rules the starting settings from, read over the
// two-night store and held to the gate results worked by hand, their replay held to what the store
// kept, and the store left exactly as it was.
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheCountsOverTheTwoNightsAreTheFunnelTheGatesWorkedByHandGiveAndTheStoreIsLeftAsItWas()
    {
        using var store = await WithTwoNights();

        var before = StoreHash(store.DatabaseFile);
        var counter = new FilterCounts(store.DatabaseFile);
        var counts = await counter.CountAsync("GSPC", year: false);

        // The two nights the store keeps bands and plans for, under each of the four settings.
        Assert.Equal(FilterCounts.Settings().Keys.Order(StringComparer.Ordinal), counts.Keys.Order(StringComparer.Ordinal));
        Assert.All(counts.Values, sessions => Assert.Equal(["2026-09-03", "2026-09-04"], sessions.Select(one => one.Session.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))));

        // The funnel at section 17's proposed values, read from the ladder, is the one the gates worked by
        // hand give: each gate passing it and every one before it, then no exclusion.
        var expected = Expected("gate-results").GetProperty("nights");
        var proposed = counts["market 50%, trade from the ladder"];

        foreach (var session in proposed)
        {
            var night = expected.GetProperty(session.Session.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            var gates = night.EnumerateObject().Select(name => name.Value.GetProperty("gates")).ToArray();

            int Through(int last) => gates.Count(one => SwingGates.Order.Take(last + 1).All(gate => one.GetProperty(gate).GetBoolean()));

            Assert.Equal(
                [gates.Length, .. Enumerable.Range(0, SwingGates.Order.Length).Select(Through), night.EnumerateObject().Count(name => name.Value.GetProperty("passed").GetBoolean())],
                session.Funnel);
            Assert.Equal(
                [.. SwingGates.Order.Select(gate => gates.Count(one => one.GetProperty(gate).GetBoolean()))],
                session.Alone);
            Assert.Equal(FilterCounts.Stored, session.Source);
        }

        // The breadth over the four closes worked by hand is 0.75 on both nights, open at either floor.
        Assert.All(proposed, one => Assert.Equal(0.75, one.Breadth!.Value, 9));
        Assert.Equal(proposed.Select(one => one.Funnel), counts["market 45%, trade from the ladder"].Select(one => one.Funnel));

        // Nothing the counts did reached the store.
        Assert.Equal(before, StoreHash(store.DatabaseFile));
    }

    // The store file's bytes, read while a pooled connection may still hold it open.
    static byte[] StoreHash(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return SHA256.HashData(stream);
    }

    [Fact]
    public async Task TheYearsReplayGivesTheBandsTheTrendAndThePlanTheStoreKeptOnBothNights()
    {
        // The fixture's bars never changed under the store, so replayed as of each night the level builder's,
        // the classifier's and the ladder builder's own functions give exactly what those stages wrote.
        using var store = await WithTwoNights();

        var counter = new FilterCounts(store.DatabaseFile);
        var counts = await counter.CountAsync("GSPC", year: true);

        Assert.Equal((8, 8, 8, 8), (counter.Notes.Compared, counter.Notes.SameBands, counter.Notes.SameTrend, counter.Notes.SamePlan));

        // Sessions before the 200-day average is held by half the members are named and not counted, and
        // every counted one before the two nights is a replay.
        Assert.True(counter.Notes.Unreadable > 0, "The fixture's year holds sessions before its 200-day average, and none was named.");
        Assert.All(
            counts["market 50%, trade from the ladder"].Where(one => one.Session < EarlierNight),
            one => Assert.Equal(FilterCounts.Replayed, one.Source));
        Assert.Contains("left out: fewer than half the members hold a 200-day average", FilterCounts.Report(counts, counter.Notes), StringComparison.Ordinal);
    }
}
