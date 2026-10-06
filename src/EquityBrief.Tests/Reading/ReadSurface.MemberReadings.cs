using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Readings;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 15.2's second pull request: a member's readings on its page under the index that held it, each read back
// off the rendered page against the row a constructed store holds.
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
public partial class ReadSurface
{
    // The name page's member readings and the run page's two parts, the edge after costs beside each rule's edge and the
    // count analyst coverage waits on.
    internal static readonly string[] MemberReadingClaims =
    [
        CheckReach.Key("15.9 Name", "Member readings"),
        CheckReach.Key("15.10 Run", "What else is waiting on a count, the mid and small cap indices' members holding four dated rating counts against nine in ten of them"),
        CheckReach.Key("15.10 Run", "The setup families, beside each rule's edge its edge after each trade's own round trip at the published table over the trades priced"),
    ];

    const string MemberNight = "2026-10-05";
    const string MemberBefore = "2026-10-02";

    const string NoBarSaid = "not read: it holds no bar on the night, or its stored series holds a hole";

    [Fact]
    public async Task EachMembersReadingsAreDrawnOnItsPageUnderItsIndexAsTheStoreHoldsThem()
    {
        using var store = new TemporaryStore().Migrated();

        // BB an S&P 400 member read whole, with its row on the session before; AA an S&P 500 member whose company files no
        // count, whose quarters were fetched without their interest expense, whose state and volume average the night
        // stored none of and whose industry no S&P 500 member was read over; CC an S&P 600 member holding no bar on the
        // night and filing no industry; and DD, a name the night stored nothing for.
        store.Execute(
            "INSERT INTO member_reading (index_code, session_date, ticker, close, dollar_volume, company_value, cost, cost_double, profit, coverage, state, year_high, nearness, since_high, volume_ratio, industry, industry_month, industry_quarter, peer_surprise) VALUES " +
            $"('MID', '{MemberNight}', 'BB', '42.5', '18250000.5', '3400000000', 0.25, 0.5, 1, 1, 'improving', '47.25', 0.8994708994708995, 12, 1.37, 'Regional Banks', 0.0123, -0.0456, 3.5), " +
            $"('MID', '{MemberBefore}', 'BB', '41.1', '18000000', '3300000000', 0.25, 0.5, 1, 1, 'steady', '47.25', 0.8698, 9, 0.91, 'Regional Banks', 0.01, -0.04, 2.5), " +
            $"('GSPC', '{MemberNight}', 'AA', '210.75', '950000000', NULL, 0.125, 0.25, 0, NULL, NULL, '230', 0.9163, 40, NULL, 'Tobacco', NULL, NULL, NULL), " +
            $"('SML', '{MemberNight}', 'CC', NULL, NULL, NULL, NULL, NULL, 0, 0, 'deteriorating', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);");

        // The companies' fetches: BB's on 2026-10-01 and again after the night, AA's made before the rating counts were
        // stored, and none of CC's.
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry, strong_buy, buy, hold, sell, strong_sell) VALUES " +
            "('BB', '2026-10-01T23:50:00Z', NULL, 'Financials', NULL, 'Regional Banks', NULL, 6, 5, 1, 1, 0), " +
            "('BB', '2026-10-06T23:50:00Z', NULL, 'Financials', NULL, 'Regional Banks', NULL, 7, 5, 1, 1, 0), " +
            "('AA', '2026-09-30T23:50:00Z', NULL, 'Consumer Staples', NULL, 'Tobacco', NULL, NULL, NULL, NULL, NULL, NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> CardOf(string route)
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync(route));

            return Assert.Single(Blocks(page, "<section class=\"card\"[^>]* data-card=\"member\">.*?</section>"));
        }

        static string Cell(string card, string reading) =>
            Regex.Match(card, $"<tr data-reading=\"{reading}\"><td>[^<]*</td>(<td[^>]*>.*?</td>)</tr>", RegexOptions.Singleline) is { Success: true } found
                ? found.Groups[1].Value
                : throw new InvalidOperationException($"no {reading} row on the card");

        static void Reads(string card, string reading, string stored, string said)
        {
            Assert.StartsWith($"<td data-value=\"{stored}\">", Cell(card, reading), StringComparison.Ordinal);
            Assert.Contains(said, Cell(card, reading), StringComparison.Ordinal);
        }

        static string Percent(double figure) => figure.ToString("0.00", CultureInfo.InvariantCulture);

        // BB, read whole under the S&P 400: each figure whole on its element and drawn in its words.
        var bb = await CardOf("/screens/name/BB");

        Assert.Contains($"<div class=\"member-readings\" data-ticker=\"BB\" data-session=\"{MemberNight}\" data-index=\"S&P 400\">", bb, StringComparison.Ordinal);
        Assert.Contains("as a member of the S&P 400", bb, StringComparison.Ordinal);
        Reads(bb, "close", "42.5", "42.50");
        Reads(bb, "dollar-volume", "18250000.5", Figures.Money(18_250_000.5m));
        Reads(bb, "company-value", "3400000000", Figures.Money(3_400_000_000m));
        Reads(bb, "cost", "0.25", $"{Percent(0.25)}% of the price");
        Reads(bb, "cost-double", "0.5", $"{Percent(0.5)}% of the price");
        Reads(bb, "profit", "1", "above nothing: the profit gate passes");
        Reads(bb, "coverage", "1", "at or above it: the coverage passes");
        Reads(bb, "state", "improving", "improving");
        Reads(bb, "year-high", "47.25", "47.25, made 12 session(s) before the night");
        Reads(bb, "nearness", "0.8994708994708995", "89.9% of it");
        Reads(bb, "volume-ratio", "1.37", "1.37");
        Reads(bb, "industry", "Regional Banks", "Regional Banks");
        Reads(bb, "industry-month", "0.0123", "+1.23%");
        Reads(bb, "industry-quarter", "-0.0456", "-4.56%");
        Reads(bb, "peer-surprise", "3.5", "+3.5%");

        // The analysts' counts as context, tonight's page reading the newest fetch with their total.
        Reads(bb, "ratings", "14", "14 in all as the fetch of 2026-10-06 filed them: 7 strong buy, 5 buy, 1 hold, 1 sell and 0 strong sell");

        // Each reading's words carry the window the shared function reads, so a window moved in the code moves the page.
        Assert.Contains($"over the last {MemberReadings.DollarVolumeSessions} sessions", bb, StringComparison.Ordinal);
        Assert.Contains($"Highest high of the {MemberReadings.YearSessions} sessions before the night", bb, StringComparison.Ordinal);
        Assert.Contains($"return over {MemberReadings.IndustryWindows[0]} sessions", bb, StringComparison.Ordinal);
        Assert.Contains($"The same over {MemberReadings.IndustryWindows[1]} sessions", bb, StringComparison.Ordinal);
        Assert.Contains($"over the {MemberReadings.PeerSessions} sessions before the night", bb, StringComparison.Ordinal);

        // The key saying how to read it, and what to take from it.
        Assert.Matches("<div class=\"key\">.*?How to read it\\..*?<p class=\"take\"><b>What to take from it\\.</b> These are facts about its trading and its quarters", bb.Replace("\n", " ", StringComparison.Ordinal));

        // Its page for the session before draws that session's row and not tonight's, and the counts of the fetch it held.
        var before = await CardOf($"/screens/name/BB/{MemberBefore}");

        Assert.Contains($"data-session=\"{MemberBefore}\"", before, StringComparison.Ordinal);
        Reads(before, "state", "steady", "steady");
        Reads(before, "volume-ratio", "0.91", "0.91");
        Reads(before, "ratings", "13", "13 in all as the fetch of 2026-10-01 filed them: 6 strong buy, 5 buy, 1 hold, 1 sell and 0 strong sell");

        // AA, under the S&P 500: what the night stored none of says why, each in its own words.
        var aa = await CardOf("/screens/name/AA");

        Assert.Contains("data-index=\"S&P 500\"", aa, StringComparison.Ordinal);
        Reads(aa, "company-value", "none", "not available: no share count filed before the night");
        Reads(aa, "cost", "0.125", $"{Percent(0.125)}% of the price");
        Reads(aa, "profit", "0", $"at or under nothing, or fewer than {MemberReadings.Quarters} quarters filed with a net income: the profit gate fails");
        Reads(aa, "coverage", "none", "not read: a quarter it reads was fetched before its interest expense was stored");
        Reads(aa, "state", "none", "none stored for the night");
        Reads(aa, "volume-ratio", "none", "not available: no fifty-day average stored for the night");
        Reads(aa, "industry", "Tobacco", "Tobacco");
        Reads(aa, "industry-month", "none", "not available: no S&P 500 member of its industry read over the span");
        Reads(aa, "industry-quarter", "none", "not available: no S&P 500 member of its industry read over the span");
        Reads(aa, "peer-surprise", "none", "none: no S&P 500 member of its industry reported a surprise in them");
        Reads(aa, "ratings", "none", "none filed: the newest fetch, of 2026-09-30, stored no rating counts");

        // CC, under the S&P 600, holding no bar on the night: every reading its bars give says so, and what its quarters
        // and its industry give is drawn as stored.
        var cc = await CardOf("/screens/name/CC");

        Assert.Contains("data-index=\"S&P 600\"", cc, StringComparison.Ordinal);

        foreach (var reading in new[] { "close", "dollar-volume", "company-value", "cost", "cost-double", "year-high", "nearness", "volume-ratio" })
        {
            Reads(cc, reading, "none", NoBarSaid);
        }

        Reads(cc, "coverage", "0", $"under it, or fewer than {MemberReadings.Quarters} quarters filed with an operating income: the coverage fails");
        Reads(cc, "state", "deteriorating", "deteriorating");
        Reads(cc, "industry", "none", "none filed before the night");
        Reads(cc, "industry-month", "none", "not read: no industry filed");
        Reads(cc, "peer-surprise", "none", "not read: no industry filed");
        Reads(cc, "ratings", "none", "no fetch of its company stored");

        // DD, which the night stored nothing for, draws no region and no entry in the contents.
        var dd = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/DD"));

        Assert.DoesNotContain("data-card=\"member\"", dd, StringComparison.Ordinal);
        Assert.DoesNotContain("Its member readings", dd, StringComparison.Ordinal);
    }

    // The run page states each family rule's edge after each trade's own round trip beside its edge, over the trades the
    // recorder priced, the record itself unchanged, and states none for a rule it priced none of.
    // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
    [Fact]
    public async Task TheRunPageStatesEachRulesEdgeAfterEachTradesOwnRoundTripBesideItsEdge()
    {
        using var store = await FamilyPagesStore();

        RegisteredRule(store, 101, EquityBrief.Worker.Candidates.TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 105, EquityBrief.Worker.Candidates.TheSetupFamilies.Drifts[0], "2026-01-02T12:00:00Z");

        // T1 made 1.5 risks against a benchmark of 0.5 and paid 0.05 of a risk a round trip; T2 lost 1.0 against -0.2 and
        // paid 0.03; T3 made 0.5 against 0.1 and was priced at nothing. The edge is (1.0 - 0.8 + 0.4) / 3 = 0.200 over the
        // three decided, and after costs (1.5 - 0.05 - 0.5 - 1.0 - 0.03 + 0.2) / 2 = 0.060 over the two priced.
        Kept(store, LiveBreakout, "breakout", "T1", "2026-01-05", "2026-01-20", 1.5, 0.5);
        Kept(store, LiveBreakout, "breakout", "T2", "2026-01-06", "2026-02-10", -1.0, -0.2);
        Kept(store, LiveBreakout, "breakout", "T3", "2026-01-07", "2026-02-11", 0.5, 0.1);
        store.Execute("UPDATE family_trade SET cost = 0.05 WHERE ticker = 'T1';");
        store.Execute("UPDATE family_trade SET cost = 0.03 WHERE ticker = 'T2';");

        // The drift's one trade, decided and priced at nothing.
        Kept(store, LiveDrift, "drift", "D1", "2026-01-05", "2026-01-20", 1.0, 0.25);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));
        var setups = Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>"));
        var edge = ((1.5 - 0.5) + (-1.0 + 0.2) + (0.5 - 0.1)) / 3;
        var after = ((1.5 - 0.05 - 0.5) + (-1.0 - 0.03 + 0.2)) / 2;

        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"<td class=\"family-record\">3 of 3 trades decided, an edge of {edge:0.000} and {after:0.000} after each trade's own round trip over the 2 priced; 1 whole blocks of the 8 its first look reads</td>"),
            setups,
            StringComparison.Ordinal);
        Assert.Contains("<td class=\"family-record\">1 of 1 trades decided, an edge of 0.750; 1 whole blocks of the 8 its first look reads</td>", setups, StringComparison.Ordinal);

        // The record is the edge before costs: the records table's edge reads as before.
        var records = Assert.Single(Blocks(run, "<table class=\"list-table family-records\".*?</table>"));
        var stored = Regex.Match(records, $"data-rule=\"{Regex.Escape(LiveBreakout)}\" data-live=\"true\" data-trades=\"3\" data-decided=\"3\" data-edge=\"([^\"]+)\"");

        Assert.True(stored.Success, "the live breakout's record row is not drawn with its three decided trades");
        Assert.Equal(edge, double.Parse(stored.Groups[1].Value, CultureInfo.InvariantCulture), 12);
    }
}
