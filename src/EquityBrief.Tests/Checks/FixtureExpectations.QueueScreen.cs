using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.6: the queue page over the fixture's research pass run from the worker's command under the
// name a pass started by hand carries. No request names it, so it is the one report written by hand on its New York
// day, at the cost of its paid calls, which research-record.json works by hand; and the page read on the suite's own day
// holds that day outside its fourteen, behind the link to every day.
// see: The queue page draws settled requests a row a stock and New York day over fourteen days, and the passes no request names a row a day
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesPassRunByHandIsOneRowOfItsDayWithTheCostOfItsPaidCalls()
    {
        var expected = Expected("queue-screen");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["run_log"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var run = expected.GetProperty("run").GetString()!;
        var ticker = expected.GetProperty("ticker").GetString()!;

        // The run is the one a pass started at the fixture's research instant carries for its name.
        Assert.Equal(run, PassRun.IdFor(FixtureReplay.Night, ticker));

        // The cost is the two parts research-record.json works by hand, summed.
        var record = Expected("research-record");
        var parts = expected.GetProperty("parts").EnumerateArray().Select(part => decimal.Parse(part.GetString()!, CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal([record.GetProperty("spend").GetString(), record.GetProperty("theme").GetProperty("spend").GetString()], expected.GetProperty("parts").EnumerateArray().Select(part => part.GetString()));

        var cost = decimal.Parse(expected.GetProperty("cost").GetString()!, CultureInfo.InvariantCulture);

        Assert.Equal(cost, parts.Sum());

        using var store = await FixtureReplay.ResearchedAsync(runId: run);

        // The derivation's ground: no request is stored, and the pass's answered paid calls are its run's.
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM research_request;"));
        Assert.Equal(cost, Query(store, $"SELECT spend FROM run_log WHERE run_id = '{run}' AND stage LIKE 'research call:%' AND stage NOT LIKE '%, trial%' AND stage NOT LIKE '%, review%' AND spend <> '0';").Sum(spend => decimal.Parse(spend, CultureInfo.InvariantCulture)));

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        // Every day: the pass's day is one row with its one report and its cost.
        var every = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/queue?{Web.App.SinglePageApp.QueueAllKey}=1"));
        var rows = Regex.Matches(every, "<tr data-day=\"([^\"]+)\" data-reports=\"(\\d+)\" data-cost=\"([^\"]+)\"><td class=\"q-day\">[^<]+</td><td class=\"num\">\\d+</td><td class=\"num\">([^<]+)</td></tr>")
            .Select(row => (Day: row.Groups[1].Value, Reports: int.Parse(row.Groups[2].Value, CultureInfo.InvariantCulture), Cost: decimal.Parse(row.Groups[3].Value, CultureInfo.InvariantCulture), Stated: row.Groups[4].Value))
            .ToArray();

        Assert.Equal(
            [(expected.GetProperty("day").GetString()!, expected.GetProperty("reports").GetInt32(), cost, expected.GetProperty("stated").GetString()!)],
            rows);
        Assert.Contains($"data-region=\"settled\" data-rows=\"{expected.GetProperty("settled").GetInt32()}\"", every, StringComparison.Ordinal);

        // The window to the suite's own day: no row of either table, and the link naming the one older row.
        var window = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/queue"));

        Assert.Contains("data-region=\"by-hand\" data-rows=\"0\"", window, StringComparison.Ordinal);
        Assert.Contains($"data-all=\"false\" data-older=\"{expected.GetProperty("olderInWindow").GetInt32()}\"", window, StringComparison.Ordinal);
    }
}
