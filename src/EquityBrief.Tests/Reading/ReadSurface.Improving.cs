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

    // M1's scorecard on the night with the figures each check compared: its year against the year before for the ratios, its
    // cash against its profit and its shares against a year before; the first eight passed and the ninth failed.
    const string M1Checks =
        "[{\"number\":1,\"answer\":\"passed\",\"amount\":\"120\"},{\"number\":2,\"answer\":\"passed\",\"amount\":\"150\"}," +
        "{\"number\":3,\"answer\":\"passed\",\"now\":\"0.118\",\"yearBefore\":\"0.104\"},{\"number\":4,\"answer\":\"passed\",\"amount\":\"150\",\"against\":\"120\"}," +
        "{\"number\":5,\"answer\":\"passed\",\"now\":\"0.27\",\"yearBefore\":\"0.29\"},{\"number\":6,\"answer\":\"passed\",\"now\":\"2.05\",\"yearBefore\":\"1.95\"}," +
        "{\"number\":7,\"answer\":\"passed\",\"amount\":\"100\",\"against\":\"101\",\"now\":\"-0.0099\"},{\"number\":8,\"answer\":\"passed\",\"now\":\"0.538\",\"yearBefore\":\"0.526\"}," +
        "{\"number\":9,\"answer\":\"failed\",\"now\":\"0.6\",\"yearBefore\":\"0.62\"}]";

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
            $"('MID', '{IndexNight}', 'M1', '2026-08-31', 0, 8, 9, '{M1Checks}', 0.4), " +
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

    // A member whose scorecard read no check is never ranked or flagged: a stored night flagging one at 0 of 0 draws it
    // nowhere, and a trade held in one is drawn after the ranked rows, its score's place saying no check could be read.
    // see: A Fundamentals flag stands only for a release reporting a quarter newer than the member's newest stored filing, accepted within the last 63 sessions
    [Fact]
    public async Task AMemberWhoseScorecardReadNoCheckIsNeverRankedAndAHeldOnesRowSaysNoCheckCouldBeRead()
    {
        using var store = UniversesStore();

        ImprovingNightOn(store);
        store.Execute(
            "INSERT INTO improving_result (rule, index_code, session_date, ticker, passed_checks, applied, filed, first_read, before_passed, before_applied, before_checks, growth, growth_change, released, outlook, flagged, status, reason, carried) VALUES " +
            $"('provisional', 'MID', '{IndexNight}', 'M4', 0, 0, NULL, 0, NULL, NULL, NULL, NULL, NULL, '2026-09-30', 'kept', 1, 'flagged', NULL, 1.0), " +
            $"('provisional', 'MID', '{IndexNight}', 'M5', 0, 0, NULL, 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, 'held', NULL, 1.0);");
        store.Execute(
            "INSERT INTO improving_trade (rule, index_code, ticker, chosen_on, filed, released, passed_checks, applied, before_passed, before_applied, buy_close, bought_on, fill, gap, bench_gap, members, growth, through, sessions, end_reason) VALUES " +
            "('provisional', 'MID', 'M5', '2026-10-01', '2026-09-30', NULL, 8, 9, 5, 9, '48', '2026-10-02', '48', 0.0, 0.0, 3, 1.0, '2026-10-02', 1, 'open');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-improving\".*?</section></div></section>"));

        Assert.DoesNotContain("data-ticker=\"M4\"", section, StringComparison.Ordinal);
        Assert.Contains($"<td class=\"improving-score\"><b>{EquityBrief.Web.Marks.MarkRenderer.NoChecksRead}</b></td>", section, StringComparison.Ordinal);
        Assert.True(section.IndexOf("data-ticker=\"M5\"", StringComparison.Ordinal) > section.IndexOf("data-ticker=\"M3\"", StringComparison.Ordinal));
        Assert.DoesNotContain("was 0 of 0", section, StringComparison.Ordinal);
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

        // Its header as a setup card's, not yet tested, its picks tonight, how soon it reads, the night it was computed for,
        // and its sentence from tonight's figures.
        Assert.Contains("Fundamentals</span> <span class=\"status-pill provisional\"", section, StringComparison.Ordinal);
        Assert.Contains("<span class=\"fam-count\">1 pick tonight</span>", section, StringComparison.Ordinal);
        Assert.Contains($"{ImprovingRule.Description}<br>{EquityBrief.Web.Marks.MarkRenderer.CheckedNightly}</p>", section, StringComparison.Ordinal);
        Assert.Contains("<span class=\"stamp computed\">Computed for ", section, StringComparison.Ordinal);
        Assert.Contains("<p class=\"card-summary\" data-summary>Tonight 3 companies' businesses improved on a new report. 1 is bought at tomorrow's open; 1 more is flagged from its results release and waits for its filing.</p>", section, StringComparison.Ordinal);

        // The four tiles, each a count and its line. M3, held since its buy, rose 5.0%; the index's three stocks over the
        // same nights by the mean of 10%, -3% and 5%, 4.0%.
        var tiles = Assert.Single(Blocks(section, "<div class=\"hw-tiles improving-tiles\".*?</div></div>"));

        Assert.Contains("<span class=\"tile-label\">Bought at tomorrow's open</span><b data-state=\"bought\">1</b><span>its filing confirmed the improvement</span>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Flagged, waiting for the filing</span><b data-state=\"flagged\">1</b><span>results release read; filing usually within weeks</span>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Holding</span><b>1</b><span><span class=\"gain\">+5.0%</span> since bought, the index's stocks <span class=\"gain\">+4.0%</span></span>", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-label\">Reporting this week</span><b>1</b><span>of the index's stocks, in the next 5 trading days</span>", tiles, StringComparison.Ordinal);

        // The tab bar, Improving fastest open first under its sentence: M1 rose by three checks in nine and M3 by two; M2
        // flagged after them.
        Assert.Contains("id=\"improving-tab-fastest\" value=\"fastest\" checked", section, StringComparison.Ordinal);
        Assert.Contains("<label class=\"improving-tabname\" for=\"improving-tab-fastest\" data-tab=\"fastest\">Improving fastest</label><label class=\"improving-tabname\" for=\"improving-tab-strongest\" data-tab=\"strongest\">Strongest businesses</label>", section, StringComparison.Ordinal);

        var fastest = Assert.Single(Blocks(section, "<div class=\"improving-panel\" data-tab=\"fastest\".*?</table>"));

        Assert.Contains("<p class=\"improving-lead\">Companies whose business got better the most on their newest report, whatever their price. A company is bought when its filing lifts it to at least 8 of the 9 checks with sales growing faster than the quarter before.</p>", fastest, StringComparison.Ordinal);
        Assert.Equal(["Stock", "Business now", "What changed on its new report", "Tonight"], Regex.Matches(fastest, "<th[^>]* data-heading=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(["M1", "M3", "M2"], Regex.Matches(fastest, "<tr class=\"improving-row\" data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        var m1 = Assert.Single(Blocks(section, "<tr class=\"improving-row\" data-ticker=\"M1\" data-status.*?</tr>"));
        var m2 = Assert.Single(Blocks(section, "<tr class=\"improving-row\" data-ticker=\"M2\" data-status.*?</tr>"));
        var m3 = Assert.Single(Blocks(section, "<tr class=\"improving-row\" data-ticker=\"M3\" data-status.*?</tr>"));

        // M1's sentence worked by hand from its rows: its growth of 12% from 12% less its speeding up of 2 points; the four
        // checks its filing passes that the one before did not, each with the figures its own year and the year before
        // stored; and the one it no longer passes.
        Assert.Contains("href=\"#/chart/M1\"", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-score\"><b>8 of 9</b><span class=\"improving-from\">up from 5</span></td>", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-changed\">Sales growth sped up from 10.0% to 12.0% on a year. Return on assets rose from 10.4% to 11.8%. It now brings in more cash than it reports as profit. Current assets now cover current liabilities 2.05 times, up from 1.95. Gross margin widened from 52.6% to 53.8%. It no longer shows more sales against its assets.</td>", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-status\" data-state=\"bought\"><b>Bought at tomorrow's open</b><span>Its filing of 1 Oct confirmed the release of 29 Sep.</span></td>", m1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-score\"><b>7 of 9</b><span class=\"improving-from\">up from 5</span></td>", m3, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-changed\">Sales growth sped up from 9.0% to 10.0% on a year. Current assets cover more of current liabilities. It issued no new shares beyond the scorecard's allowance. Gross margin widened. It no longer shows less long-term debt against its assets.</td>", m3, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-status\" data-state=\"improved\"><b>Improved, short of the bar</b><span>Needs 8 of 9; one check short, missing less long-term debt against its assets and more sales against its assets.</span></td>", m3, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-score\"><b>filing not in yet</b><span class=\"improving-from\">was 6 of 9</span></td>", m2, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-changed\">Its results release of 30 Sep reads results above expectations and a raised outlook.</td>", m2, StringComparison.Ordinal);
        Assert.Contains("<td class=\"improving-status\" data-state=\"flagged\"><b>Flagged, waiting for its filing</b><span>Bought only if its filing lifts it to 8 of 9.</span></td>", m2, StringComparison.Ordinal);

        // Strongest businesses under its own sentence: the index's three scores, highest first, each its business and its
        // price apart, with what the quality book says of each.
        var strongest = Assert.Single(Blocks(section, "<div class=\"improving-panel\" data-tab=\"strongest\".*?</table>"));

        Assert.Contains("<p class=\"improving-lead\">The index's 3 highest scores tonight, whatever their price, ties broken by gross profit against assets.</p>", strongest, StringComparison.Ordinal);
        Assert.Equal(["M1", "M3", "M2"], Regex.Matches(strongest, "data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<td class=\"improving-changed\">Sales grew 12.0% on a year, faster than in the quarter before; gross profit was 40.0% of its assets.</td><td class=\"improving-status\">Its price is cheaper than in 9 of its own last 12 quarters.", strongest, StringComparison.Ordinal);
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
        Assert.Contains("<p class=\"listed-under improving-held\">Fundamentals flagged it: its results release of 2026-09-30 reads results better than expected. The Fundamentals family buys it only if its filing lifts it to 8 of 9, and that buy waits for the filing; no other family's buy waits on it.</p>", await Name("M2"), StringComparison.Ordinal);

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

        // Before its first night the card is stamped with no night it was computed for.
        Assert.DoesNotContain("stamp computed", Assert.Single(Blocks(before, "<section class=\"card setup-card\" id=\"setup-improving\".*?</section></div></section>")), StringComparison.Ordinal);
    }
}
