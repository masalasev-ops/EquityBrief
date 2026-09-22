using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Returns;
using ReturnBlocks = EquityBrief.Core.Returns.Blocks;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 10.1: the run page's measure of the order tonight's list is drawn in, against the
// order it replaced, counted in blocks of sessions and drawing no comparison below the floor.
// see: The order tonight's list is drawn in is compared against the order it replaces, declared before any record is read
// see: A listing records the band strength the old order read, and the three orders are compared over the nights that recorded it
public partial class ReadSurface
{
    // The exchange's sessions from the first the closure table covers, so a night is named by how
    // many sessions it stands after another.
    static IReadOnlyList<DateOnly> SessionsFromTheTablesStart()
    {
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2025, 1, 2); day <= new DateOnly(2027, 6, 30); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return sessions;
    }

    // One night of 22 fired rows, built so the three orders draw different twenties. Nineteen rows
    // fire two reasons with a reward to risk between 1 and 2.8 and a band strength of 50; one fires
    // two with no traded exit and the strongest bands; one fires two at a ratio of 0.5 with the
    // weakest; and one fires a single reason at the highest ratio of all. So the old order draws the
    // row with no reward to risk and nineteen setups, the ruled order the nineteen and the weak one,
    // twenty setups, and reward to risk alone the single reason and the nineteen, twenty setups.
    static IEnumerable<ListingRow> NightOfTwentyTwo(DateOnly night, int? strength = 0)
    {
        static string Plan(string? target) =>
            $"{{\"entryLow\":\"100\",\"entryHigh\":\"110\",\"stop\":\"95\",\"firstTradedTarget\":{(target is null ? "null" : $"\"{target}\"")}}}";

        int? Band(int value) => strength is null ? null : value;

        for (var at = 0; at < 19; at++)
        {
            yield return new ListingRow($"A{at:00}", night, Fired(2), 2, Plan((115 + at).ToString(CultureInfo.InvariantCulture)), Band(50));
        }

        yield return new ListingRow("NONE", night, Fired(2), 2, Plan(null), Band(99));
        yield return new ListingRow("LOW", night, Fired(2), 2, Plan("110"), Band(1));
        yield return new ListingRow("BIG", night, Fired(1), 1, Plan("204"), Band(1));
    }

    [Fact]
    public void ASessionsBlockAndAWholeWindowAreCountedInExchangeSessions()
    {
        var sessions = SessionsFromTheTablesStart();
        var first = sessions[10];

        // The last session of block 0 and the first of block 1.
        Assert.Equal(0, ReturnBlocks.Of(first, first));
        Assert.Equal(0, ReturnBlocks.Of(first, sessions[10 + ReturnBlocks.Sessions - 1]));
        Assert.Equal(1, ReturnBlocks.Of(first, sessions[10 + ReturnBlocks.Sessions]));
        Assert.Equal(7, ReturnBlocks.Of(first, sessions[10 + (8 * ReturnBlocks.Sessions) - 1]));

        // A window closes on the sixty-third session traded after the listing and not the one before.
        Assert.False(ReturnBlocks.Closed(first, sessions[10 + ForwardReturnSeries.SetupSessionCap - 1]));
        Assert.True(ReturnBlocks.Closed(first, sessions[10 + ForwardReturnSeries.SetupSessionCap]));

        // Counted on the exchange's sessions, so the days after a block's last session that are no
        // session are still that block and the next session opens the next. Here the last session
        // is a Thursday before a closure and a weekend.
        var last = sessions[10 + ReturnBlocks.Sessions - 1];
        var next = sessions[10 + ReturnBlocks.Sessions];

        Assert.True(next.DayNumber - last.DayNumber > 3, $"{last} and {next} have no closed weekday between them.");

        for (var day = last.AddDays(1); day < next; day = day.AddDays(1))
        {
            Assert.Equal(0, ReturnBlocks.Of(first, day));
        }

        Assert.Equal(1, ReturnBlocks.Of(first, next));
    }

    [Fact]
    public void TheRunPageCountsEachOrdersBlocksAndDrawsNoComparisonBelowTheFloor()
    {
        var sessions = SessionsFromTheTablesStart();
        var first = sessions[10];

        DateOnly After(int count) => sessions[10 + count];

        // Nine recorded nights: two in block 0, one either side of its last session, and one a
        // block apart from then on through block 7. A night before them that recorded no band
        // strength, which would move every block if it were read, and one after the night shown.
        var listings = new List<ListingRow>();

        listings.AddRange(NightOfTwentyTwo(sessions[0], strength: null));

        foreach (var at in new[] { 0, 62, 63, 126, 189, 252, 315, 378, 441 })
        {
            listings.AddRange(NightOfTwentyTwo(After(at)));
        }

        listings.AddRange(NightOfTwentyTwo(After(505)));

        // Worked by hand. The night shown 503 sessions after the first: the night at 441 has had 62
        // sessions and its window is open, so eight nights have closed, over blocks 0 to 6, seven
        // blocks. The old order drew 19 setups a night, the other two 20.
        var below = RunScreen.Orders(listings, After(503));

        Assert.Equal(first, below.From);
        Assert.Equal(9, below.Nights);
        Assert.Equal(["fired-then-band-strength", "fired-then-reward-to-risk", "reward-to-risk-alone"], [.. below.Orders.Select(order => order.Key)]);
        Assert.Equal([true, false, false], [.. below.Orders.Select(order => order.Benchmark)]);
        Assert.Equal([171, 180, 180], [.. below.Orders.Select(order => order.Setups)]);
        Assert.Equal([152, 160, 160], [.. below.Orders.Select(order => order.Closed)]);
        Assert.Equal([7, 7, 7], [.. below.Orders.Select(order => order.Blocks)]);

        var drawn = new MarkRenderer().TonightsOrder(below);

        Assert.Contains("data-compared=\"false\"", drawn, StringComparison.Ordinal);
        Assert.Contains("the fewest any order holds is 7", drawn, StringComparison.Ordinal);
        Assert.Contains("<tr data-order=\"fired-then-band-strength\" data-benchmark=\"true\" data-setups=\"171\" data-closed=\"152\" data-blocks=\"7\">", drawn, StringComparison.Ordinal);
        Assert.Contains("(the benchmark)", drawn, StringComparison.Ordinal);
        Assert.True(
            drawn.IndexOf("fired-then-band-strength", StringComparison.Ordinal) < drawn.IndexOf("fired-then-reward-to-risk", StringComparison.Ordinal),
            "the benchmark is not drawn first.");

        // One session on, the night at 441 has had its 63 and the eighth block holds a closed setup,
        // so every order reaches the floor and the comparison is due.
        var floor = RunScreen.Orders(listings, After(504));

        Assert.Equal([171, 180, 180], [.. floor.Orders.Select(order => order.Closed)]);
        Assert.Equal([8, 8, 8], [.. floor.Orders.Select(order => order.Blocks)]);

        var due = new MarkRenderer().TonightsOrder(floor);

        Assert.Contains("data-compared=\"due\"", due, StringComparison.Ordinal);
        Assert.DoesNotContain("no comparison is drawn", due, StringComparison.Ordinal);

        // A store no night of which recorded the band strength measures nothing and says so.
        var none = RunScreen.Orders([.. NightOfTwentyTwo(first, strength: null)], After(504));

        Assert.Null(none.From);
        Assert.Contains("data-orders=\"none\"", new MarkRenderer().TonightsOrder(none), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunRouteDrawsTheOrdersRegionOverTheFixture()
    {
        // The route composes the region from every listing the store holds, and the fixture's one
        // night recorded its band strength, so the region measures from it and compares nothing.
        using var store = await FixtureExpectations.WithListings();

        var night = (await Api(store).NewestNightAsync())!.Value;

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync("/screens/run");
        var region = Regex.Match(page, "<section class=\"tonights-order\"[^>]*>").Value;

        Assert.Contains($"data-from=\"{Stamp(night)}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-drawn=\"{SinglePageApp.TonightDrawn}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-block-sessions=\"{ReturnBlocks.Sessions}\" data-floor=\"{ReturnBlocks.Floor}\" data-compared=\"false\"", region, StringComparison.Ordinal);
    }
}
