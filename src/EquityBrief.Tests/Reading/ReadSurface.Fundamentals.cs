using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.8 and from 22.13: the fundamentals-first family's card on every index drawn no more on Tonight, its
// passes counted on no line, its trades on no list of Past picks, while its rule runs out of sight and its record stays on
// the Run page.
// see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
// see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
public partial class ReadSurface
{
    // The family's card on Tonight, on every index.
    internal static readonly string[] FundamentalsPageClaims =
    [
        CheckReach.Key("15.7 Tonight", "The fundamentals-first family's card"),
    ];

    [Fact]
    public async Task TheFundamentalsFirstCardIsDrawnOnNoIndexWhileItsRuleRunsOutOfSight()
    {
        // The universes' store: the S&P 400's night with the family passing M2 listed first, and the S&P 600's night,
        // written here, passing none, the market open.
        using var store = UniversesStore();

        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('MID', '{IndexNight}', 'M1', 'fundamentals', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{FundamentalsRule.NoRevenue}'), " +
            $"('MID', '{IndexNight}', 'M2', 'fundamentals', 1, 1, '50', '47', '56', NULL, 20, 2.0, NULL), " +
            $"('MID', '{IndexNight}', 'M3', 'fundamentals', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup'), " +
            $"('SML', '{IndexNight}', 'S1', 'fundamentals', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{FundamentalsRule.NoTrend}');");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('MID', '{IndexNight}', 'M2', 'fundamentals', 'listed', 2, '[]', NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('SML', '{IndexNight}', 2, 0.6, 1, '{IndexFamilies.Settings("SML")}', 0);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The S&P 400: no card, no tile and no buy point of the family, the Fundamentals family's card in its place, and the
        // line counting M1 and M3 alone, the breakout's and the pullback's passes.
        var mid = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.DoesNotContain("id=\"setup-fundamentals\"", mid, StringComparison.Ordinal);
        Assert.DoesNotContain("<section class=\"family-card\" data-family=\"fundamentals\"", mid, StringComparison.Ordinal);
        Assert.DoesNotContain("data-family=\"fundamentals\"", Assert.Single(Blocks(mid, "<section[^>]*id=\"setups\".*?</section>")), StringComparison.Ordinal);
        Assert.DoesNotContain("data-family=\"fundamentals\"", Assert.Single(Blocks(mid, "<table class=\"list-table buy-points\".*?</table>")), StringComparison.Ordinal);
        Assert.Contains("id=\"setup-improving\"", mid, StringComparison.Ordinal);
        Assert.Contains("data-passed=\"2\"", Assert.Single(Blocks(mid, "<p class=\"market-line\".*?</p>")), StringComparison.Ordinal);

        // The S&P 600: no card of the family either.
        var small = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=600"));

        Assert.DoesNotContain("id=\"setup-fundamentals\"", small, StringComparison.Ordinal);
        Assert.Contains("id=\"setup-improving\"", small, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSAndP500DrawsNoFundamentalsFirstCardAndItsRecordStaysOnTheRunPage()
    {
        // The S&P 500's families night of the switch, its own breakout and pullback cards listing; the index families'
        // step's night on the S&P 500 passing FF1 under the fundamentals-first family.
        using var store = await FamilyPagesStore();

        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'FF1', NULL, NULL, '2026-10-02T21:00:00Z');");
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('GSPC', '{TheSwitch}', 12, 0.6, 1, '{IndexFamilies.Settings("GSPC")}', 0);");
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('GSPC', '{TheSwitch}', 'FF1', 'fundamentals', 1, 1, '80', '76', '88', NULL, 20, 2.0, NULL);");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('GSPC', '{TheSwitch}', 'FF1', 'fundamentals', 'listed', 1, '[]', NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES " +
            $"('GSPC', 'fundamentals', 'FF1', '{TheSwitch}', 1, '80', '76', '88', NULL, 20);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var families = Regex.Matches(page, "<section class=\"family-card\" data-family=\"([a-z]+)\" data-place=\"(\\d+)\" data-of=\"(\\d+)\"").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}").ToArray();

        // The S&P 500's three cards alone, numbered among themselves.
        Assert.Equal(["pullback|1|3", "breakout|2|3", "drift|3|3"], families);

        // The line atop the cards counts the swing filter's and the S&P 500's own families' passes and not FF1's.
        var ownPasses = int.Parse(
            Text(store, $"SELECT COUNT(*) FROM (SELECT ticker FROM gate_result WHERE session_date = '{TheSwitch}' AND passed = 1 UNION SELECT ticker FROM family_result WHERE session_date = '{TheSwitch}' AND passed = 1);"),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains($"data-passed=\"{ownPasses}\"", Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>")), StringComparison.Ordinal);
        Assert.DoesNotContain("data-family=\"fundamentals\"", Assert.Single(Blocks(page, "<table class=\"list-table buy-points\".*?</table>")), StringComparison.Ordinal);

        // Past picks draws no card of its trades.
        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        Assert.DoesNotContain("data-card=\"business-first-picks\"", picks, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"FF1\"", picks, StringComparison.Ordinal);

        // Its record stays where its rule is read: the Run page's row after the S&P 500's own families, listed tonight,
        // open and provisional.
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));
        var setups = Regex.Matches(Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>")), "<tr data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value);

        Assert.Equal(["pullback", "breakout", "drift", "fundamentals"], setups);
        Assert.Contains("<tr data-family=\"fundamentals\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-listed=\"1\" data-trades=\"1\" data-open=\"1\" data-finished=\"0\">", run, StringComparison.Ordinal);
    }
}
