using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.13: the Fundamentals family's card on Tonight in the fundamentals-first family's place, read back off
// the rendered page over a constructed store, its tile in that family's slot, and the fundamentals-first family's card and
// tile drawn nowhere.
// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
// see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
public partial class ReadSurface
{
    // The rows 22.13 adds that this check reaches: the card on Tonight.
    internal static string[] ImprovingPageRows =>
    [
        CheckReach.Key("15.7 Tonight", "The Fundamentals family's card"),
    ];

    static string ScoredChecks(params int[] passed) =>
        "[" + string.Join(",", Enumerable.Range(1, 9).Select(number => FormattableString.Invariant($"{{\"number\":{number},\"answer\":\"{(passed.Contains(number) ? "passed" : "failed")}\"}}"))) + "]";

    // The family's night on the universes' store's S&P 400. M1's filing, read for the first time, lifted it from 5 of 9 to 8
    // of 9 with its sales growing faster, confirming its release before it, so it is bought; M3's lifted it from 5 of 9 to 7
    // of 9, short of the bar; M2's results release since its filing reads better with its outlook raised, so it is flagged.
    // M3 holds a trade chosen the night before and bought at the night's open, its growth 1.05 against the members' 1.10,
    // 0.97 and 1.05. The quality book holds M1 and calls M2 dearer than its own usual.
    static void ImprovingNightOn(TemporaryStore store, string? fault = null)
    {
        var settings = EquityBrief.Worker.Improving.ImprovingFamily.Settings(ImprovingRule.Starting).Replace("'", "''", StringComparison.Ordinal);
        var readings = Enumerable.Repeat<double?>(null, LedgerReadings.Count).ToArray();

        store.Execute(
            "INSERT INTO improving_night (rule, index_code, session_date, members, scored, first_read, improved, bought, flagged, held, settings, fault) VALUES " +
            $"('provisional', 'MID', '2026-10-01', 3, 3, 1, 1, 1, 0, 1, '{settings}', NULL), " +
            $"('provisional', 'MID', '{IndexNight}', 3, 3, 2, 2, 1, 1, 2, '{settings}', {(fault is null ? "NULL" : $"'{fault}'")});");
        store.Execute(
            "INSERT INTO improving_result (rule, index_code, session_date, ticker, passed_checks, applied, filed, first_read, before_passed, before_applied, before_checks, growth, growth_change, released, outlook, flagged, status, reason, carried) VALUES " +
            "('provisional', 'MID', '2026-10-01', 'M1', 5, 9, '2026-08-05', 0, NULL, NULL, NULL, 0.1, 0.01, NULL, NULL, 0, NULL, NULL, 1.0), " +
            "('provisional', 'MID', '2026-10-01', 'M2', 6, 9, '2026-08-05', 0, NULL, NULL, NULL, 0.1, 0.01, NULL, NULL, 0, NULL, NULL, 1.0), " +
            "('provisional', 'MID', '2026-10-01', 'M3', 8, 9, '2026-09-30', 1, 5, 9, NULL, 0.1, 0.01, NULL, NULL, 0, 'bought', NULL, 1.0), " +
            $"('provisional', 'MID', '{IndexNight}', 'M1', 8, 9, '2026-10-01', 1, 5, 9, '{ScoredChecks(1, 2, 5, 7, 9)}', 0.12, 0.02, '2026-09-29', NULL, 0, 'bought', NULL, 1.10), " +
            $"('provisional', 'MID', '{IndexNight}', 'M2', 6, 9, '2026-08-05', 0, NULL, NULL, NULL, 0.08, -0.01, '2026-09-30', 'raised', 1, 'flagged', NULL, 0.97), " +
            $"('provisional', 'MID', '{IndexNight}', 'M3', 7, 9, '2026-10-01', 1, 5, 9, '{ScoredChecks(1, 2, 3, 4, 5)}', 0.1, 0.01, NULL, NULL, 0, 'improved', '{ImprovingRule.UnderTheBar}', 1.05);");
        store.Execute(
            "INSERT INTO improving_trade (rule, index_code, ticker, chosen_on, filed, released, passed_checks, applied, before_passed, before_applied, buy_close, bought_on, fill, gap, bench_gap, members, growth, through, sessions, end_reason) VALUES " +
            "('provisional', 'MID', 'M3', '2026-10-01', '2026-09-30', NULL, 8, 9, 5, 9, '48', '2026-10-02', '48', 0.0, 0.0, 3, 1.05, '2026-10-02', 1, 'open'), " +
            $"('provisional', 'MID', 'M1', '{IndexNight}', '2026-10-01', '2026-09-29', 8, 9, 5, 9, '50', NULL, NULL, NULL, NULL, NULL, 1.0, '{IndexNight}', 0, 'open');");
        store.Execute(
            "INSERT INTO fundamental_score (index_code, session_date, ticker, quarter, financial, passed, applied, checks, gross_profitability) VALUES " +
            $"('MID', '{IndexNight}', 'M1', '2026-08-31', 0, 8, 9, '{ScoredChecks(1, 2, 3, 4, 5, 6, 7, 8)}', 0.4), " +
            $"('MID', '{IndexNight}', 'M2', '2026-06-30', 0, 6, 9, '{ScoredChecks(1, 2, 3, 4, 5, 6)}', 0.2), " +
            $"('MID', '{IndexNight}', 'M3', '2026-08-31', 0, 7, 9, '{ScoredChecks(1, 2, 3, 4, 6, 7, 8)}', 0.3);");

        readings[LedgerReadings.IndexOf("revenue_growth")] = 0.12;
        readings[LedgerReadings.IndexOf("growth_change")] = 0.02;
        readings[LedgerReadings.IndexOf("value_place")] = 0.75;
        store.Execute($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('MID', '{IndexNight}', 'M1', '{System.Text.Json.JsonSerializer.Serialize(readings)}', 'test');");

        var quality = EquityBrief.Worker.Quality.QualityFamily.Settings(QualityRule.Starting).Replace("'", "''", StringComparison.Ordinal);

        store.Execute($"INSERT INTO quality_night (rule, index_code, session_date, members, scored, qualified, held, reshuffle, settings, fault) VALUES ('provisional', 'MID', '{IndexNight}', 3, 3, 1, 1, 1, '{quality}', NULL);");
        store.Execute(
            "INSERT INTO quality_result (rule, index_code, session_date, ticker, passed, place, passed_checks, applied, value_place, reason, carried) VALUES " +
            $"('provisional', 'MID', '{IndexNight}', 'M1', 1, 1, 8, 9, 0.75, NULL, 1.0), " +
            $"('provisional', 'MID', '{IndexNight}', 'M2', 0, NULL, 7, 9, 0.25, '{QualityRule.TooDear}', 1.0);");
        store.Execute($"INSERT INTO quality_holding (rule, index_code, ticker, chosen_on, buy_close, growth, through, end_reason) VALUES ('provisional', 'MID', 'M1', '{IndexNight}', '50', 1.0, '{IndexNight}', 'open');");
    }

    [Fact]
    public async Task TheFundamentalsFamilysCardReadsBackOffTonightInTheFundamentalsFirstFamilysPlace()
    {
        using var store = UniversesStore();

        ImprovingNightOn(store);

        // M1 reports on the second trading day after the night, inside the five; M2 on the sixth, outside them; and M3 goes
        // ex-dividend on the first, which is no report.
        store.Execute(
            "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES " +
            "('M1', '2026-10-06', 'earnings', 'before', '{}', '2026-10-02T21:00:00Z'), " +
            "('M2', '2026-10-12', 'earnings', 'before', '{}', '2026-10-02T21:00:00Z'), " +
            "('M3', '2026-10-05', 'ex-dividend', 'before', '{}', '2026-10-02T21:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-improving\".*?</section></div></section>"));
        var setups = Assert.Single(Blocks(page, "<section[^>]*id=\"setups\".*?</section>"));

        // The fundamentals-first family's card and tile are drawn nowhere; the Fundamentals family's tile stands after the
        // drift's, its line counting tonight's pick and the stock flagged and waiting for its filing.
        Assert.DoesNotContain("id=\"setup-fundamentals\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-family=\"fundamentals\"", setups, StringComparison.Ordinal);
        Assert.True(setups.IndexOf("data-family=\"improving\"", StringComparison.Ordinal) > setups.IndexOf("data-family=\"drift\"", StringComparison.Ordinal));
        Assert.Contains("1 pick tonight. 1 flagged, waiting for the filing.", setups, StringComparison.Ordinal);
        Assert.Contains(ImprovingRule.Looks, setups, StringComparison.Ordinal);

        // Its header as a setup card's, not yet tested, and its sentence from tonight's figures.
        Assert.Contains("Fundamentals</span> <span class=\"status-pill provisional\"", section, StringComparison.Ordinal);
        Assert.Contains(ImprovingRule.Description, section, StringComparison.Ordinal);
        Assert.Contains("<p class=\"card-summary\" data-summary>Tonight 3 companies' businesses improved on a new report. 1 is bought at tomorrow's open; 1 more is flagged from its results release and waits for its filing.</p>", section, StringComparison.Ordinal);

        // The four tiles. M3, held since its buy, rose 5.0%; the index's three stocks over the same nights by the mean of
        // 10%, -3% and 5%, 4.0%.
        var tiles = Assert.Single(Blocks(section, "<div class=\"hw-tiles improving-tiles\".*?</div></div>"));

        Assert.Contains("<span class=\"tile-label\">Bought at tomorrow's open</span><b>1</b>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Flagged, waiting for the filing</span><b>1</b>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Holding 1</span><b><span class=\"gain\">+5.0%</span></b><span>since bought; the index's stocks <span class=\"gain\">+4.0%</span></span>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Reporting this week</span><b>1</b><span>in the next 5 trading days</span>", tiles, StringComparison.Ordinal);

        // Improving fastest, open first: M1 rose by three checks in nine and M3 by two; M2 flagged after them.
        Assert.Contains("id=\"improving-tab-fastest\" value=\"fastest\" checked", section, StringComparison.Ordinal);
        Assert.Equal(["M1", "M3", "M2"], Regex.Matches(Assert.Single(Blocks(section, "<div class=\"improving-panel\" data-tab=\"fastest\".*?</div>")), "<li class=\"improving-row\" data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        var m1 = Assert.Single(Blocks(section, "<li class=\"improving-row\" data-ticker=\"M1\" data-status.*?</li>"));
        var m2 = Assert.Single(Blocks(section, "<li class=\"improving-row\" data-ticker=\"M2\" data-status.*?</li>"));
        var m3 = Assert.Single(Blocks(section, "<li class=\"improving-row\" data-ticker=\"M3\" data-status.*?</li>"));

        Assert.Contains("href=\"#/chart/M1\"", m1, StringComparison.Ordinal);
        Assert.Contains("<span class=\"improving-score\">8 of 9, up from 5</span>", m1, StringComparison.Ordinal);
        Assert.Contains("Its new filing shows a higher return on assets, cash from operations above net income, more current assets against current liabilities and a wider gross margin, which the one before did not; it no longer shows more sales against its assets.", m1, StringComparison.Ordinal);
        Assert.Contains("<b>Bought at tomorrow's open.</b> Its filing of 1 Oct confirmed the release of 29 Sep.", m1, StringComparison.Ordinal);
        Assert.Contains("<span class=\"improving-score\">7 of 9, up from 5</span>", m3, StringComparison.Ordinal);
        Assert.Contains("<b>Improved, short of the bar.</b> It needs 8 of 9 and misses less long-term debt against its assets and more sales against its assets.", m3, StringComparison.Ordinal);
        Assert.Contains("<span class=\"improving-score\">6 of 9</span>", m2, StringComparison.Ordinal);
        Assert.Contains("Its results release of 30 Sep reads results better than expected, its outlook raised.", m2, StringComparison.Ordinal);
        Assert.Contains("<b>Flagged, waiting for its filing.</b> Bought only if its filing lifts it to 8 of 9.", m2, StringComparison.Ordinal);

        // Strongest businesses: the index's three scores, highest first, with what the quality book says of each.
        var strongest = Assert.Single(Blocks(section, "<div class=\"improving-panel\" data-tab=\"strongest\".*?</div>"));

        Assert.Equal(["M1", "M3", "M2"], Regex.Matches(strongest, "data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("Sales grew 12.0% on a year, faster than in the quarter before; gross profit was 40.0% of its assets. Its price is cheaper than in 9 of its own last 12 quarters.", strongest, StringComparison.Ordinal);
        Assert.Contains(EquityBrief.Web.Marks.MarkRenderer.InTheQualityBook + ".", strongest, StringComparison.Ordinal);
        Assert.Contains(EquityBrief.Web.Marks.MarkRenderer.AboveItsUsualPrice + ".", strongest, StringComparison.Ordinal);

        // How it picks, a click away.
        Assert.Contains("<summary>How it picks, and how it will be tested</summary>", section, StringComparison.Ordinal);
        Assert.Contains(MarkRendererWorkedBy, section, StringComparison.Ordinal);
    }

    const string MarkRendererWorkedBy = EquityBrief.Web.Marks.MarkRenderer.WorkedBy;

    // The stock page and the chart page name the Fundamentals family where it bought a stock on the night, keeps a trade in
    // it from an earlier night or flagged it: M1 bought, M3 kept since its buy and M2 flagged.
    [Fact]
    public async Task TheStockAndChartPagesNameTheFundamentalsFamilyWhereItBoughtKeepsOrFlaggedAStock()
    {
        using var store = UniversesStore();

        ImprovingNightOn(store);

        // M1 picked by this family alone and M2 in no book, so each summary names this family.
        store.Execute("DELETE FROM index_family_pick WHERE index_code = 'MID' AND ticker = 'M1';");
        store.Execute("DELETE FROM index_heavyweight_holding WHERE index_code = 'MID' AND ticker = 'M2';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Name(string ticker) => WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}/{IndexNight}"));
        async Task<string> Brief(string ticker) => Regex.Match(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}")), "<div class=\"brief-read\".*?<p class=\"brief-foot\">", RegexOptions.Singleline).Value;

        // The stock page's line beneath the contents, worked from the rows above: M1's filing of 1 Oct lifting it from 5 of 9
        // to 8 of 9, M3 bought on 2 Oct and kept at 7 of 9, and M2's release of 30 Sep waiting for its filing.
        Assert.Contains("<p class=\"listed-under improving-held\">Fundamentals buys it at the next open: its filing of 2026-10-01 lifted its score to 8 of 9, from 5 of 9.</p>", await Name("M1"), StringComparison.Ordinal);
        Assert.Contains("<p class=\"listed-under improving-held\">Fundamentals bought it on 2026-10-02 and keeps it while its score stays at 7 of 9 or more.</p>", await Name("M3"), StringComparison.Ordinal);
        Assert.Contains("<p class=\"listed-under improving-held\">Fundamentals flagged it: its results release of 2026-09-30 reads results better than expected. It is bought only if its filing lifts it to 8 of 9.</p>", await Name("M2"), StringComparison.Ordinal);

        // The chart page's chip in the fundamentals-first family's place, its summary and, for the stock it bought, its way in
        // with no stop.
        var m1 = await Brief("M1");
        var m2 = await Brief("M2");
        var m3 = await Brief("M3");

        Assert.Contains("data-family=\"improving\" data-state=\"bought\"><span class=\"fam-swatch\" data-family=\"improving\"></span>Fundamentals: bought at tomorrow's open</span>", m1, StringComparison.Ordinal);
        Assert.Contains("data-family=\"improving\" data-state=\"flagged\"><span class=\"fam-swatch\" data-family=\"improving\"></span>Fundamentals: flagged, waiting for its filing</span>", m2, StringComparison.Ordinal);
        Assert.Contains("data-family=\"improving\" data-state=\"held\"><span class=\"fam-swatch\" data-family=\"improving\"></span>Fundamentals: in its holdings</span>", m3, StringComparison.Ordinal);
        Assert.All(new[] { m1, m2, m3 }, brief => Assert.DoesNotContain("data-family=\"fundamentals\"", brief, StringComparison.Ordinal));
        Assert.Contains("Tonight Fundamentals picked it.", m1, StringComparison.Ordinal);
        Assert.Contains("Tonight Fundamentals flagged it from its results release, waiting for its filing.", m2, StringComparison.Ordinal);
        Assert.Contains("Tonight it is in the Fundamentals setup's holdings.", m3, StringComparison.Ordinal);
        Assert.Contains("Picked tonight by Fundamentals: buy at the next open.", m1, StringComparison.Ordinal);
        Assert.Contains("It sets no stop: it sells at the close when a new filing drops its score under 7 of 9.", m1, StringComparison.Ordinal);
        Assert.Contains("Otherwise at the close of its 126th trading day, or of its last day in the index.", m1, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFundamentalsFamilysCardSaysItsPartFailedAndBeforeItsFirstNightThatItHasReadNone()
    {
        using var failed = UniversesStore();

        ImprovingNightOn(failed, "the store was busy");

        using var host = new Host(failed.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-improving\".*?</section></div></section>"));

        Assert.Contains("Its part of the night was not computed, so nothing was bought, sold or flagged tonight.", section, StringComparison.Ordinal);
        Assert.DoesNotContain("improving-tiles", section, StringComparison.Ordinal);
        Assert.DoesNotContain("improving-panel", section, StringComparison.Ordinal);

        using var empty = UniversesStore();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        var before = WebUtility.HtmlDecode(await emptyClient.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Contains("It has read no night yet: it reads its first on the first night after it was built.", before, StringComparison.Ordinal);
        Assert.Contains("Its first night has not run yet.", before, StringComparison.Ordinal);
    }
}
