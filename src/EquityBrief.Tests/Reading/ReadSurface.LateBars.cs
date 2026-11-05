using System.Net;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// A bar that arrived after the night's day file: its stock's page and the run page say so with the time, and a stock still
// missing is left to its suspect line. The words are the operator's, written here before the code ran.
// see: A member missing from the night's day file is asked for with its own request at the end of the fetch and once more in the off-peak run, at most 20 asks a night
public partial class ReadSurface
{
    internal static string[] LateBarRows =>
    [
        CheckReach.Key("15.9 Name", "A bar that arrived late"),
        CheckReach.Key("15.10 Run", "Members missing from the night's day file"),
    ];

    [Fact]
    public async Task ALateBarIsSaidOnItsStocksPageAndEveryAskOnTheRunPageWithTheTime()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var fired = FiredNamesOn(store, night);

        Assert.True(fired.Count >= 3, $"The night lists {fired.Count} name(s), fewer than the three this reads.");

        var (offPeak, atFetch, missing) = (fired[0], fired[1], fired[2]);

        store.Execute(
            "INSERT INTO late_bar_ask (ticker, session_date, stage, asked_at, outcome, detail, qualified, run_id) VALUES " +
            $"('{offPeak}', '{night}', 'fetch', '2026-09-09T00:00:40Z', 'still missing', 'the provider''s answer ended on 2026-09-04, before {night}', NULL, 'night-test'), " +
            $"('{offPeak}', '{night}', 'off-peak', '2026-09-09T04:05:00Z', 'arrived', 'the provider held the bar for {night}', '[\"Breakout\",\"Earnings drift\"]', 'late-test'), " +
            $"('{atFetch}', '{night}', 'fetch', '2026-09-09T00:00:41Z', 'arrived', 'the provider held the bar for {night}', NULL, 'night-test'), " +
            $"('{missing}', '{night}', 'fetch', '2026-09-09T00:00:42Z', 'still missing', 'the provider''s answer held nothing to {night}', NULL, 'night-test'), " +
            $"('ZZZ', '{night}', 'fetch', NULL, 'past the limit', 'past the 20 asks a night makes', NULL, 'night-test');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var arrived = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{offPeak}/{night}"));

        Assert.Contains(
            $"{offPeak}'s bar for {night} was not in the night's day file and arrived late, at 2026-09-09T04:05:00Z, in the off-peak run. " +
            "Its readings and levels were worked then, and the night's picks, lists, trades and ledger stand as the night wrote them. " +
            "It would have qualified under Breakout and Earnings drift had its bar arrived before the night.",
            arrived,
            StringComparison.Ordinal);

        var early = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{atFetch}/{night}"));

        Assert.Contains(
            $"{atFetch}'s bar for {night} was not in the night's day file and arrived late, at 2026-09-09T00:00:41Z, asked with a request of its own at the end of the night's fetch, before the night's rules read it.",
            early,
            StringComparison.Ordinal);
        Assert.DoesNotContain("would have qualified", early, StringComparison.Ordinal);

        // Still missing, the stock's page says nothing of an arrival.
        Assert.DoesNotContain("class=\"late-bar\"", await client.GetStringAsync($"/screens/name/{missing}/{night}"), StringComparison.Ordinal);

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{night}"));

        Assert.Contains("Members missing from the night's day file", run, StringComparison.Ordinal);
        Assert.Contains($">{offPeak}</a>, asked at the end of the fetch at 2026-09-09T00:00:40Z: still missing, the provider's answer ended on 2026-09-04, before {night}.</li>", run, StringComparison.Ordinal);
        Assert.Contains($">{offPeak}</a>, asked in the off-peak run at 2026-09-09T04:05:00Z: arrived, the provider held the bar for {night}. It would have qualified under Breakout and Earnings drift had its bar arrived before the night.</li>", run, StringComparison.Ordinal);
        Assert.Contains($">{atFetch}</a>, asked at the end of the fetch at 2026-09-09T00:00:41Z: arrived, the provider held the bar for {night}.</li>", run, StringComparison.Ordinal);
        Assert.Contains($">{missing}</a>, asked at the end of the fetch at 2026-09-09T00:00:42Z: still missing, the provider's answer held nothing to {night}.</li>", run, StringComparison.Ordinal);
        Assert.Contains(">ZZZ</a>, not asked at the end of the fetch: past the limit, past the 20 asks a night makes.</li>", run, StringComparison.Ordinal);
        Assert.Contains("data-asks=\"4\"", run, StringComparison.Ordinal);

        // A night with no ask draws no such list.
        store.Execute("DELETE FROM late_bar_ask;");
        Assert.DoesNotContain("class=\"late-bars\"", await client.GetStringAsync($"/screens/run/{night}"), StringComparison.Ordinal);
    }
}
