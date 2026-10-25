using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Quality;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.9: Quality at a fair price's card on Tonight, read back off the rendered page over a constructed store:
// its tile among the setups, its card after the sector heavyweights' with its sentence, its three tiles, a row a holding with
// its score, its price against its own past, its change since bought and tonight's line, what the last reshuffle chose, the
// next in line and its rule in words; a night its part failed saying so and nothing else; and a night before its first
// saying it has read none.
// see: Quality at a fair price holds the companies the scorecard rates strongest that are not dear against their own history, reshuffled on each month's first session
// see: A quality holding's edge is its return less an equal-weighted hold of its index's members over the same sessions, after its round trip
public partial class ReadSurface
{
    // The rows 22.9 adds that this check reaches: the card on Tonight.
    internal static string[] QualityPageRows =>
    [
        CheckReach.Key("15.7 Tonight", "Quality at a fair price's card"),
    ];

    // The family's two nights on the universes' store's S&P 400. On 2026-10-01, the month's first session, it reshuffled:
    // M1 first at 8 of 9 and cheaper than in 9 of its last 12 quarters, M2 second at 7 of 9 and at its median, both chosen,
    // M3 under the floor at 6 of 9. On 2026-10-02 both were bought at that day's open, M1 2.5% over its close and M2 at it;
    // M1 still qualifies with its growth at 1.12, M2 fell to 6 of 9 with its growth at 0.94, and M3 rose to 9 of 9 and
    // cheaper than in 3 of its quarters, qualifying unheld with its growth at 1.15.
    static void QualityNightsOn(TemporaryStore store, string? fault = null)
    {
        var settings = QualityFamily.Settings(QualityRule.Starting).Replace("'", "''", StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO quality_night (rule, index_code, session_date, members, scored, qualified, held, reshuffle, settings, fault) VALUES " +
            $"('provisional', 'MID', '2026-10-01', 3, 3, 2, 2, 1, '{settings}', NULL), " +
            $"('provisional', 'MID', '{IndexNight}', 3, 3, 2, 2, 0, '{settings}', {(fault is null ? "NULL" : $"'{fault}'")});");
        store.Execute(
            "INSERT INTO quality_result (rule, index_code, session_date, ticker, passed, place, passed_checks, applied, value_place, reason, carried) VALUES " +
            "('provisional', 'MID', '2026-10-01', 'M1', 1, 1, 8, 9, 0.75, NULL, 1.0), " +
            "('provisional', 'MID', '2026-10-01', 'M2', 1, 2, 7, 9, 0.5, NULL, 1.0), " +
            $"('provisional', 'MID', '2026-10-01', 'M3', 0, NULL, 6, 9, 0.25, '{QualityRule.UnderTheFloor}', 1.0), " +
            $"('provisional', 'MID', '{IndexNight}', 'M3', 1, 1, 9, 9, 0.25, NULL, 1.15), " +
            $"('provisional', 'MID', '{IndexNight}', 'M1', 1, 2, 8, 9, 0.75, NULL, 1.12), " +
            $"('provisional', 'MID', '{IndexNight}', 'M2', 0, NULL, 6, 9, 0.5, '{QualityRule.UnderTheFloor}', 0.94);");
        store.Execute(
            "INSERT INTO quality_holding (rule, index_code, ticker, chosen_on, buy_close, bought_on, fill, gap, bench_gap, members, growth, through, ended_on, sale, end_reason, result, benchmark, cost, edge) VALUES " +
            $"('provisional', 'MID', 'M1', '2026-10-01', '48', '{IndexNight}', '49.2', 0.025, 0.0, 3, 1.12, '{IndexNight}', NULL, NULL, 'open', NULL, NULL, NULL, NULL), " +
            $"('provisional', 'MID', 'M2', '2026-10-01', '50', '{IndexNight}', '50', 0.0, 0.0, 3, 0.94, '{IndexNight}', NULL, NULL, 'open', NULL, NULL, NULL, NULL);");
    }

    [Fact]
    public async Task QualityAtAFairPricesCardReadsBackOffTonightAfterTheSectorHeavyweights()
    {
        using var store = UniversesStore();

        QualityNightsOn(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-quality\".*?</section></div></section>"));

        // After the sector heavyweights' card and before the page's words, with the setups card counting its tile among
        // the store's five in words.
        Assert.Contains("data-card=\"heavyweights\"", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("id=\"setup-quality\"", StringComparison.Ordinal) > page.IndexOf("data-card=\"heavyweights\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("id=\"setup-quality\"", StringComparison.Ordinal) < page.IndexOf("id=\"words\"", StringComparison.Ordinal));
        Assert.Equal("The six setups", Regex.Match(page, "The [a-z0-9]+ setups").Value);
        Assert.Equal(6, Regex.Matches(Assert.Single(Blocks(page, "<section[^>]*id=\"setups\".*?</section>")), "<span class=\"fam-tag\"").Count);

        // Its tile: two holdings kept monthly, the next reshuffle on 2 November in 21 trading days, the 20 of October
        // after the 2nd and the 2nd of November.
        Assert.Contains("2 holdings. Next reshuffle in 21 trading days.", page, StringComparison.Ordinal);

        // Its header as a setup card's, not yet tested, and its sentence from tonight's figures.
        Assert.Contains("Quality at a fair price</span> <span class=\"status-pill provisional\"", section, StringComparison.Ordinal);
        Assert.Contains("Not yet tested", section, StringComparison.Ordinal);
        Assert.Contains("<p class=\"card-summary\" data-summary>It holds 2 stocks of 10, each with strong fundamentals and a price at or below its own usual. It reshuffles next in 21 trading days.</p>", section, StringComparison.Ordinal);

        // The three tiles. Since the reshuffle the holdings rose by the mean of 12% and -6%, 3.0%, and the index's three
        // stocks by the mean of 12%, -6% and 15%, 7.0%.
        var tiles = Assert.Single(Blocks(section, "<div class=\"hw-tiles quality-tiles\".*?</div></div>"));

        Assert.Contains("<span class=\"tile-label\">Holding</span><b>2 of 10</b>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Since the last reshuffle, 1 Oct</span><b><span class=\"gain\">+3.0%</span></b><span>the index's stocks <span class=\"gain\">+7.0%</span></span>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Next reshuffle</span><b>2 Nov</b><span>in 21 trading days</span>", tiles, StringComparison.Ordinal);

        // A row a holding in the order they were chosen. M1 since bought: 12% on its growth less its 2.5% gap at the open,
        // 9.5%; M2 -6% with no gap.
        var m1 = Assert.Single(Blocks(section, "<tr data-ticker=\"M1\".*?</tr>"));
        var m2 = Assert.Single(Blocks(section, "<tr data-ticker=\"M2\".*?</tr>"));

        Assert.Equal(["M1", "M2"], Regex.Matches(section, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("Mid One", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num quality-score\">8 of 9</td>", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"quality-place\">cheaper than in 9 of 12 quarters</td>", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num quality-since\"><span class=\"gain\">+9.5%</span></td>", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"quality-tonight\">Still qualifies.</td>", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num quality-score\">6 of 9</td>", m2, StringComparison.Ordinal);
        Assert.Contains("<td class=\"quality-place\">cheaper than in 6 of 12 quarters</td>", m2, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num quality-since\"><span class=\"loss\">-6.0%</span></td>", m2, StringComparison.Ordinal);
        Assert.Contains("<td class=\"quality-tonight\">Score fell to 6 of 9; sold at the next reshuffle unless it recovers.</td>", m2, StringComparison.Ordinal);

        // What the last reshuffle chose, and M3 next in line at 9 of 9.
        Assert.Contains("The last reshuffle, on 1 Oct 2026, chose M1, M2.", section, StringComparison.Ordinal);
        Assert.Contains("<summary>Next in line (1)</summary>", section, StringComparison.Ordinal);
        Assert.Contains("M3</a> Mid Three: a score of 9 of 9, cheaper than in 3 of its own last 12 quarters.", section, StringComparison.Ordinal);

        // Its rule in words a click away, from the settings it runs on.
        Assert.Contains("<summary>The full rule, and how it will be tested</summary>", section, StringComparison.Ordinal);
        Assert.Contains(char.ToUpperInvariant(QualityRule.Starting.Words[0]) + QualityRule.Starting.Words[1..] + ".", section, StringComparison.Ordinal);
        Assert.DoesNotContain("quality-sold", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QualityAtAFairPricesCardSaysItsPartFailedAndBeforeItsFirstNightThatItHasReadNone()
    {
        using var failed = UniversesStore();

        QualityNightsOn(failed, "the store was busy");

        using var host = new Host(failed.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-quality\".*?</section></div></section>"));

        // Its sentence says the night was not computed, its tile says so, and nothing else is drawn.
        Assert.Contains("Its part of the night was not computed, so nothing was bought or sold tonight.", section, StringComparison.Ordinal);
        Assert.Contains("Not computed tonight.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("quality-table", section, StringComparison.Ordinal);
        Assert.DoesNotContain("quality-tiles", section, StringComparison.Ordinal);

        // A store whose step read no night yet draws the card saying its first holdings come on its first night.
        using var empty = UniversesStore();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        var before = WebUtility.HtmlDecode(await emptyClient.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Contains("It has read no night yet: its first holdings are chosen on the first night it runs.", before, StringComparison.Ordinal);
    }
}
