using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Bars;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 22.6 correction: Tonight names the session its picks are bought at, tomorrow only where the exchange
// trades the day after the night, so a Friday's buys are Monday's and a night before a closure names the session after it.
// see: Tonight names the session its picks are bought at, tomorrow only where the exchange trades the next day
public partial class ReadSurface
{
    [Fact]
    public async Task TonightNamesTheSessionItsPicksAreBoughtAtAndSaysTomorrowOnlyWhereTheExchangeTradesTheNextDay()
    {
        // A Thursday, a Friday, the Wednesday before Thanksgiving's closure, the closure table's last day and no night, which
        // from the operator's order of 2026-11-14 names no tomorrow it cannot read off a night.
        Assert.Equal("tomorrow's", MarkRenderer.OpenDay(new DateOnly(2026, 10, 29)));
        Assert.Equal("Monday's", MarkRenderer.OpenDay(new DateOnly(2026, 10, 30)));
        Assert.Equal("Friday's", MarkRenderer.OpenDay(new DateOnly(2026, 11, 25)));
        Assert.Equal("the next trading day's", MarkRenderer.OpenDay(ExchangeClosures.CoveredThrough));
        Assert.Equal("the next trading day's", MarkRenderer.OpenDay(null));

        Assert.Contains(
            "data-buy-points=\"0\">No stock to buy at Monday's open: no setup picked one tonight.</p>",
            WebUtility.HtmlDecode(new MarkRenderer().TomorrowsBuys([], null, night: new DateOnly(2026, 10, 30))),
            StringComparison.Ordinal);

        // The night of 2026-10-02, a Friday, drawn: the card's title, its sentences and the minute's line name Monday.
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        // From 24.46 the new ideas' tile names the session, and each plan bought at the open says it buys at whatever price
        // it opens.
        Assert.Contains("<span class=\"g-s\">7 new picks to buy at Monday's open", page, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Matches(page, "<p class=\"pp-cond\">Buys at the next open, at whatever price it opens\\.").Count);
        Assert.DoesNotContain("omorrow's buys", page, StringComparison.Ordinal);
        Assert.DoesNotContain("tomorrow's open", page, StringComparison.Ordinal);
    }
}
