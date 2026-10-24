using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.8: the fundamentals-first family's card on every index, read back off the rendered page. On the S&P
// 400 and 600 it stands after the drift's among the index's own cards; on the S&P 500 after the S&P 500's own families,
// drawn from the rows the index families' step writes there, each card numbered in the page's order; each with its rule
// in words, its pick with its plan and why it passed, or why it lists none.
// see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
public partial class ReadSurface
{
    // The family's card on Tonight, on every index.
    internal static readonly string[] FundamentalsPageClaims =
    [
        CheckReach.Key("15.7 Tonight", "The fundamentals-first family's card"),
    ];

    // The family's card from the card holding its heading and its rule to the next card the page draws.
    static string FundamentalsCard(string page)
    {
        var card = SetupCardOf(page, "fundamentals");

        Assert.Contains("<section class=\"family-card\" data-family=\"fundamentals\"", card, StringComparison.Ordinal);

        return card;
    }

    // A card's words as a reader reads them, the marks between its clauses taken out.
    static string Read(string card) => Regex.Replace(Regex.Replace(card, "<[^>]+>", " "), "\\s+", " ");

    [Fact]
    public async Task TheFundamentalsFirstCardIsDrawnOnEveryIndexReadBackOffThePage()
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

        // The S&P 400: the fourth card of four after the drift, its rule in the words the night's settings state, M2 listed
        // with its plan and why it passed.
        var mid = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var midCard = FundamentalsCard(mid);

        Assert.Contains("data-place=\"4\" data-of=\"4\" data-picks=\"1\"", midCard, StringComparison.Ordinal);
        Assert.Contains(SetupFamilies.FundamentalsFirst.Heading, Read(midCard), StringComparison.Ordinal);
        Assert.Contains("revenue up more than 0 per cent on the year-earlier quarter and growing faster than the quarter before", Read(midCard), StringComparison.Ordinal);
        Assert.Equal(["M2"], CardPicksOf(midCard));
        Assert.Contains("<tr data-ticker=\"M2\" data-family=\"fundamentals\"", Assert.Single(Blocks(mid, "<table class=\"list-table buy-points\".*?</table>")), StringComparison.Ordinal);
        Assert.True(mid.IndexOf("id=\"setup-drift\"", StringComparison.Ordinal) < mid.IndexOf("id=\"setup-fundamentals\"", StringComparison.Ordinal));

        // The S&P 600: the card listing none and saying how many members stopped at each part of the rule.
        var small = FundamentalsCard(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=600")));

        Assert.Contains("data-picks=\"0\"", small, StringComparison.Ordinal);
        Assert.Contains(FundamentalsRule.NoTrend, Read(small), StringComparison.Ordinal);

        // From the 19.7 correction of 2026-10-18 each card's chips, read off the provisional setting its night stored, as a
        // family's card draws its rule's.
        const string Chips = "<ul class=\"rule-chips\" data-chips=\"3\"><li class=\"rule-chip\">revenue up more than 0%</li><li class=\"rule-chip\">gross or operating margin up</li><li class=\"rule-chip\">cash at least 1 times income</li></ul>";

        Assert.Contains(Chips, midCard, StringComparison.Ordinal);
        Assert.Contains(Chips, small, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSAndP500DrawsItsFundamentalsFirstCardAfterItsOwnFamiliesFromTheIndexFamiliesRows()
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

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = FundamentalsCard(page);
        var families = Regex.Matches(page, "<section class=\"family-card\" data-family=\"([a-z]+)\" data-place=\"(\\d+)\" data-of=\"(\\d+)\"").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}").ToArray();

        // After the S&P 500's three cards, the fourth of four, its rule read on the S&P 500 by the index families' step.
        Assert.Equal(["pullback|1|4", "breakout|2|4", "drift|3|4", "fundamentals|4|4"], families);

        // The line atop the cards counts every member a setup passed, FF1 under the fundamentals-first family among them:
        // one more than the swing filter's and the S&P 500's own families' passes.
        var ownPasses = int.Parse(
            Text(store, $"SELECT COUNT(*) FROM (SELECT ticker FROM gate_result WHERE session_date = '{TheSwitch}' AND passed = 1 UNION SELECT ticker FROM family_result WHERE session_date = '{TheSwitch}' AND passed = 1);"),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains($"data-passed=\"{ownPasses + 1}\"", Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>")), StringComparison.Ordinal);
        Assert.Equal(["FF1"], CardPicksOf(card));
        Assert.Contains("Read on the S&P 500 by the S&P 400's and 600's step, apart from its own setups.", Read(card), StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"FF1\" data-family=\"fundamentals\"", Assert.Single(Blocks(page, "<table class=\"list-table buy-points\".*?</table>")), StringComparison.Ordinal);

        // From the 19.7 correction of 2026-10-18 its chips on the S&P 500 too, off the setting the S&P 500's night stored.
        Assert.Contains("<ul class=\"rule-chips\" data-chips=\"3\"><li class=\"rule-chip\">revenue up more than 0%</li><li class=\"rule-chip\">gross or operating margin up</li><li class=\"rule-chip\">cash at least 1 times income</li></ul>", card, StringComparison.Ordinal);

        // From the 17.8 correction of 2026-10-18 the family's S&P 500 trade, kept under the S&P 500 by the index families'
        // step: on Past picks in a card of its own after the S&P 500's swing trades, named with its index, and on the Run
        // page as the family's row after the S&P 500's own families, listed tonight, open, and provisional.
        store.Execute(
            "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES " +
            $"('GSPC', 'fundamentals', 'FF1', '{TheSwitch}', 1, '80', '76', '88', NULL, 20);");

        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));
        var trades = Assert.Single(Blocks(picks, "<section class=\"card\" data-card=\"business-first-picks\">.*?</section>"));

        Assert.Contains("1 S&P 500 fundamentals-first trade, 1 open", trades, StringComparison.Ordinal);
        Assert.Contains($"<tr data-ticker=\"FF1\" data-index=\"S&P 500\" data-family=\"fundamentals\" data-listed=\"{TheSwitch}\" data-ended=\"open\"", trades, StringComparison.Ordinal);
        Assert.True(picks.IndexOf("data-card=\"picks", StringComparison.Ordinal) < picks.IndexOf("data-card=\"business-first-picks\"", StringComparison.Ordinal));

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));
        var setups = Regex.Matches(Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>")), "<tr data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value);

        Assert.Equal(["pullback", "breakout", "drift", "fundamentals"], setups);
        Assert.Contains("<tr data-family=\"fundamentals\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-listed=\"1\" data-trades=\"1\" data-open=\"1\" data-finished=\"0\">", run, StringComparison.Ordinal);
    }
}
