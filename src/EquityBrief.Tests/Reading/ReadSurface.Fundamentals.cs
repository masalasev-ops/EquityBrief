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
        const string Opens = "<section class=\"card\" data-card=\"family\">";
        var at = page.IndexOf("<section class=\"family-card\" data-family=\"fundamentals\"", StringComparison.Ordinal);

        Assert.True(at >= 0, "The page draws no fundamentals-first card.");

        var start = page.LastIndexOf(Opens, at, StringComparison.Ordinal);
        var end = page.IndexOf("<section class=\"card\"", at, StringComparison.Ordinal);

        return page[start..(end < 0 ? page.Length : end)];
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
        Assert.Contains("M2", midCard, StringComparison.Ordinal);
        Assert.Contains("every part of the fundamentals-first rule passing on the S&P 400's provisional setting", Read(midCard), StringComparison.Ordinal);
        Assert.True(mid.IndexOf("data-family=\"drift\"", StringComparison.Ordinal) < mid.IndexOf("data-family=\"fundamentals\"", StringComparison.Ordinal));

        // The S&P 600: the card listing none and saying how many members stopped at each part of the rule.
        var small = FundamentalsCard(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=600")));

        Assert.Contains("data-picks=\"0\"", small, StringComparison.Ordinal);
        Assert.Contains(FundamentalsRule.NoTrend, Read(small), StringComparison.Ordinal);
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
        Assert.Contains("FF1", card, StringComparison.Ordinal);
        Assert.Contains("Read on the S&P 500 by the S&P 400's and 600's step, apart from its own families.", Read(card), StringComparison.Ordinal);
        Assert.Contains("every part of the fundamentals-first rule passing on the S&P 500's provisional setting", Read(card), StringComparison.Ordinal);
    }
}
