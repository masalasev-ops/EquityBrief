using System.Globalization;
using System.Text.Json;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Ladders;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 4.6 correction: each exit says what it does with the position. The traded exits'
// fractions are equal shares of the whole position and sum to all of it, so the exits below the top
// sell their share at their band and the top sells none at a price: it holds the last share on a stop
// trailed under the price. The words said "sell 1/1 and trail the rest" at the top, a sale of
// everything followed by a trail of nothing.
public partial class ReadSurface
{
    const string HeldOnTheTrail = "is held on a stop trailed under the price, not sold at a price";

    [Fact]
    public void AnExitAtTheTopOfTheLadderSaysItHoldsWhatIsLeftOnATrailedStopAndSellsNothingAtAPrice()
    {
        // CVX's plan as the night of 2026-10-08 stored it: one traded exit, the top of its ladder.
        var cvx = new LadderRow(
            "CVX",
            new DateOnly(2026, 10, 8),
            "range",
            """
            {"tranches":[{"lowEdge":"200.7800","highEdge":"208.9800","condition":"FirstCloseBackAbove","stop":"195.8409"}],
             "exits":[{"lowEdge":"217.7800","highEdge":"217.7800","traded":true,"trailing":true,"fraction":"1/1","reason":null}],
             "invalidation":"195.8409","events":[]}
            """);

        var rows = NameScreen.PlanRows(cvx);
        var exit = Assert.Single(rows, row => row.Kind == PlanKind.Exit);

        Assert.Equal($"the whole position {HeldOnTheTrail}", exit.Detail);
        Assert.True(exit.Trails);
        Assert.DoesNotContain("trail the rest", exit.Detail, StringComparison.Ordinal);

        var marks = new MarkRenderer();
        var column = marks.PlanColumn("CVX", 211.55m, rows);

        Assert.Contains(">Trail from 217.78</text>", column, StringComparison.Ordinal);
        Assert.DoesNotContain(">Sell at 217.78</text>", column, StringComparison.Ordinal);
        Assert.Contains($"the whole position {HeldOnTheTrail}", marks.PlanTables("CVX", rows), StringComparison.Ordinal);
    }

    [Fact]
    public void TheExitsBelowTheTopEachSellTheirShareAndTheirSharesWithTheTopsAccountForTheWholePosition()
    {
        // Four exits worked by hand: three traded at a third each, the highest of them the top that trails,
        // and one listed and not traded with its reason, which takes no share.
        var plan = new LadderRow(
            "ZZZZ",
            new DateOnly(2026, 10, 8),
            "uptrend",
            """
            {"tranches":[{"lowEdge":"90.0000","highEdge":"92.0000","condition":"ReachesTheZone","stop":"88.0000"}],
             "exits":[{"lowEdge":"95.0000","highEdge":"95.5000","traded":false,"trailing":false,"fraction":"0","reason":"closer than two typical days' moves"},
                      {"lowEdge":"100.0000","highEdge":"101.0000","traded":true,"trailing":false,"fraction":"1/3","reason":null},
                      {"lowEdge":"105.0000","highEdge":"106.0000","traded":true,"trailing":false,"fraction":"1/3","reason":null},
                      {"lowEdge":"110.0000","highEdge":"111.0000","traded":true,"trailing":true,"fraction":"1/3","reason":null}],
             "invalidation":"88.0000","events":[]}
            """);

        var exits = NameScreen.PlanRows(plan).Where(row => row.Kind == PlanKind.Exit).ToArray();

        Assert.Equal(
            [
                "closer than two typical days' moves",
                "sell 1/3 of the position",
                "sell 1/3 of the position",
                $"the last 1/3 {HeldOnTheTrail}",
            ],
            exits.Select(row => row.Detail));
        Assert.Equal([false, false, false, true], exits.Select(row => row.Trails));

        // The shares the words name sum to the whole position, the skipped exit naming none.
        Assert.Equal(1m, Math.Round(SharesNamed(exits), 6));
    }

    [Fact]
    public async Task EveryFixturePlansExitsNameTheirSharesOnceAndExactlyOneTrailsWhereAnyIsTraded()
    {
        using var store = await WithLadders();

        var api = Api(store);
        var traded = 0;

        foreach (var name in new[] { "AAPL", "MSFT", "NFLX", "KEYS" })
        {
            var ladder = await api.LadderAsync(name);
            var exits = NameScreen.PlanRows(ladder).Where(row => row.Kind == PlanKind.Exit).ToArray();

            using var stored = JsonDocument.Parse(ladder!.Plan);
            var tradedHere = stored.RootElement.GetProperty("exits").EnumerateArray().Count(exit => exit.GetProperty("traded").GetBoolean());

            Assert.DoesNotContain(exits, row => row.Detail.Contains("trail the rest", StringComparison.Ordinal));
            Assert.Equal(tradedHere > 0 ? 1 : 0, exits.Count(row => row.Trails));

            if (tradedHere > 0)
            {
                Assert.Equal(1m, Math.Round(SharesNamed(exits), 6));
                Assert.EndsWith(HeldOnTheTrail, Assert.Single(exits, row => row.Trails).Detail, StringComparison.Ordinal);
            }

            traded += tradedHere;
        }

        // AAPL's two, NFLX's three and KEYS's two, read off the ladder expectation; MSFT trades none.
        Assert.Equal(7, traded);
    }

    // The sum of the shares an exit's words name, read from "1/n" in each sentence, the
    // whole position counting one.
    static decimal SharesNamed(IEnumerable<PlanRow> exits) =>
        exits.Sum(row =>
        {
            if (row.Detail.StartsWith("the whole position", StringComparison.Ordinal))
            {
                return 1m;
            }

            var match = System.Text.RegularExpressions.Regex.Match(row.Detail, @"\b1/(\d+)\b");

            return match.Success ? 1m / decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0m;
        });
}
