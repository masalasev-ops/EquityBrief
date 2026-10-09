using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 15.1's second half: every page reads one index at a time chosen under Universe, its heading and every
// figure naming that index, and each S&P 400 or 600 card's rule written from the settings its night stored.
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public partial class ReadSurface
{
    const string IndexNight = "2026-10-02";

    // Two S&P 500 members, three S&P 400 members and two S&P 600 members on the night, each with a close. The S&P 400's
    // night read all three at a breadth of 52 per cent over the floor of 50: the breakout passed M1, listed first; the
    // pullback passed M3, held back by its S&P 500 trade from 2026-09-30; the drift passed none, M2 under the price
    // floor and the others with no setup; its sector heavyweights bought M2 at the night's rebalance. The S&P 600's
    // families read no night.
    static TemporaryStore UniversesStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector, name) VALUES " +
            "('GSPC', 'AAA', NULL, NULL, '2026-10-02T21:00:00Z', 'Energy', 'Triple A'), ('GSPC', 'BBB', NULL, NULL, '2026-10-02T21:00:00Z', 'Energy', 'Double B'), " +
            "('MID', 'M1', NULL, NULL, '2026-10-02T21:00:00Z', 'Energy', 'Mid One'), ('MID', 'M2', NULL, NULL, '2026-10-02T21:00:00Z', 'Energy', 'Mid Two'), " +
            "('MID', 'M3', NULL, NULL, '2026-10-02T21:00:00Z', 'Energy', 'Mid Three'), " +
            "('SML', 'S1', NULL, NULL, '2026-10-02T21:00:00Z', 'Energy', 'Small One'), ('SML', 'S2', NULL, NULL, '2026-10-02T21:00:00Z', 'Energy', 'Small Two');");
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "SELECT ticker, '2026-10-02', '50', '51', '49', '50', 1000000, 'test', '2026-10-02T21:00:00Z', '50' FROM membership;");
        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES " +
            "('AAA', '2026-10-02', '[]', 0, '[]', '[]'), ('BBB', '2026-10-02', '[]', 0, '[]', '[]');");

        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('MID', '{IndexNight}', 3, 0.52, 1, '{IndexFamilies.Settings("MID")}', 1);");
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('MID', '{IndexNight}', 'M1', 'pullback', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup'), " +
            $"('MID', '{IndexNight}', 'M2', 'pullback', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup'), " +
            $"('MID', '{IndexNight}', 'M3', 'pullback', 1, 1, '30', '28', '34', NULL, 20, 2.0, NULL), " +
            $"('MID', '{IndexNight}', 'M1', 'breakout', 1, 1, '50', '47', NULL, '3', 63, 2.4, NULL), " +
            $"('MID', '{IndexNight}', 'M2', 'breakout', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup'), " +
            $"('MID', '{IndexNight}', 'M3', 'breakout', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup'), " +
            $"('MID', '{IndexNight}', 'M1', 'drift', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup'), " +
            $"('MID', '{IndexNight}', 'M2', 'drift', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'the price under $5'), " +
            $"('MID', '{IndexNight}', 'M3', 'drift', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup');");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('MID', '{IndexNight}', 'M1', 'breakout', 'listed', 1, '[]', NULL, NULL, NULL), " +
            $"('MID', '{IndexNight}', 'M3', 'pullback', 'open trade', NULL, '[]', 'GSPC', 'pullback', '2026-09-30');");
        store.Execute(
            "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, ended_on, result, cost) VALUES " +
            $"('MID', 'breakout', 'M1', '{IndexNight}', 1, '50', '47', NULL, '3', 63, NULL, NULL, NULL), " +
            "('MID', 'pullback', 'M3', '2026-09-28', 1, '30', '28', '34', NULL, 20, '2026-10-01', 2.0, 0.02);");
        store.Execute(
            "INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES " +
            $"('MID', 'M2', '{IndexNight}', 'Energy', '50', 1.0, '[]', '{IndexNight}');");

        // One researched name in each of the S&P 500 and the S&P 400.
        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES " +
            "('AAA', 'thesis', 1, '2026-10-02', 'a writer', 'accepted', 'A sentence.', '[]', NULL), ('M1', 'thesis', 1, '2026-10-02', 'a writer', 'accepted', 'A sentence.', '[]', NULL);");

        return store;
    }

    // The S&P 400's breakout rule in words at the settings the night stores, worked out here from the rule's own
    // numbers: a 126-session high, 1.5 times the volume, ranges at 0.85 of the 20 sessions before, a stop 1.5 typical
    // moves under, $5 and $10,000,000 a day over 50 sessions, net income over 4 quarters, and the S&P 500's market
    // floor of 45 per cent read on the index's own breadth.
    const string MidBreakoutWords =
        "A stock closes above its highest price of the 126 sessions before on 1.5 times its average volume, after its daily ranges narrowed to at most 0.85 of those over the 20 sessions before them. " +
        "Stop 1.5 typical moves below, raised as the price climbs and never lowered; no target. " +
        "Unlike the S&P 500's rules, a stock is read only at $5 or more and trading at least $10,000,000 a day over 50 sessions, and only where its net income over its last 4 quarters sums above zero; " +
        "it is ranked among the S&P 400's own members, and the lists close while the S&P 400's own breadth is under 45%; each trade's result is read after the published spread, half paid at each end.";

    [Fact]
    public async Task TonightOnTheSAndP400NamesItsIndexInEveryFigureAndDrawsItsCardsFromItsOwnNight()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        // The heading and the selector: the three indices each with its members on the night, the S&P 400 chosen.
        Assert.Contains("<div class=\"screen-mast\" data-title=\"Tonight: S&P 400\"><span class=\"m-screen\">Tonight: S&P 400</span>", page, StringComparison.Ordinal);
        Assert.Equal(
            [("500", "2", "S&P 500 · 2 members", false), ("400", "3", "S&P 400 · 3 members", true), ("600", "2", "S&P 600 · 2 members", false)],
            Regex.Matches(Assert.Single(Blocks(page, "<label class=\"universe-pick\" data-universe=\"400\">.*?</label>")), "<option value=\"([^\"]+)\" data-members=\"([^\"]+)\"( selected)?>([^<]+)</option>")
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[4].Value, match.Groups[3].Success)));
        Assert.Contains($"<section class=\"tonight\" data-night=\"{IndexNight}\" data-universe=\"400\" data-index-code=\"MID\"", page, StringComparison.Ordinal);

        // The line: the S&P 400's own breadth against the floor, the members a setup passed, M1 and M3, of the three,
        // the one buy point across the four setups, the fundamentals-first family's among them from 17.8, and no open
        // trade before the night, the trades' link keeping the index.
        var line = Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>"));

        Assert.StartsWith("<p class=\"market-line\" data-index=\"S&P 400\" data-open=\"true\" data-breadth=\"0.52\" data-floor=\"0.45\" data-passed=\"2\" data-members=\"3\" data-buy-points=\"1\" data-setups-listing=\"1\" data-setups=\"4\" data-close=\"none\" data-open-trades=\"0\">", line, StringComparison.Ordinal);
        Assert.Contains("<b class=\"market-open\">The lists are open</b>: the S&P 400's breadth: 52.0% of its members closed above their 200-day average, at or above its floor of 45%.", line, StringComparison.Ordinal);
        Assert.Contains("2 of 3 S&P 400 members passed a setup · 1 buy point tonight across 1 of 4 setups · <a href=\"#/picks?status=open&universe=400\">0 open trades</a>", line, StringComparison.Ordinal);
        Assert.DoesNotContain("S&P 500 members", page, StringComparison.Ordinal);

        // Every card provisional, the fundamentals-first family's among them from 17.8, each rule written from the
        // settings the night stored, the breakout's listing M1.
        Assert.Equal(5, Regex.Matches(page, "<b class=\"provisional\">Provisional: not yet frozen</b>").Count);

        var breakout = FamilyCardOf(page, "breakout");

        Assert.Contains($"<p class=\"lede\">{MidBreakoutWords}</p>", page, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"M1\" data-family=\"breakout\"", breakout, StringComparison.Ordinal);
        Assert.Contains("<span class=\"co\">Mid One</span>", breakout, StringComparison.Ordinal);

        // The pullback passed M3 and holds it back for its trade on the S&P 500's list, naming that list.
        var pullback = FamilyCardOf(page, "pullback");

        Assert.Contains("Every stock this setup passed tonight is held back, as the notes beneath say.", pullback, StringComparison.Ordinal);
        Assert.Contains("<p class=\"family-note\">M3 qualified again tonight but its trade from 2026-09-30 on the S&P 500's list is still open, so it is not listed. One stock, one trade.</p>", pullback, StringComparison.Ordinal);

        // The drift carries its line of evidence and says how far the S&P 400's members got.
        var drift = FamilyCardOf(page, "drift");

        Assert.Contains("<p class=\"family-note\">Published evidence finds this drift gone outside microcaps (Martineau 2022), and the 14.8 test measured -0.222 on the 1,500.</p>", drift, StringComparison.Ordinal);
        Assert.Contains("No stock passed this setup tonight: of the 3 S&P 400 members read, 2 no setup, 1 the price under $5.", drift, StringComparison.Ordinal);

        // The heavyweights' card names its design and the sector comparison its index reads, against the 400's fund.
        Assert.Contains(
            "<p class=\"lede\">On each month's first session, or the first after it whose stored year holds the closes its readings need, among each sector's 10 largest companies of the S&P 400 by value, the 2 whose 251-session returns beat their sector's members' mean in the S&P 400 by the most, where they beat it at all, " +
            "their close above their 50-day average and that above their 200-day, their beta against IJH at least one, bought at that close. Held while it leads: sold at the close of a later month's rebalance where the rule would no longer buy it. " +
            "Design (a): a sector's return is its members' mean within the S&P 400, since no sector fund is read at the index's level. Unlike the S&P 500's rules,",
            page,
            StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"M2\" data-sector=\"Energy\" data-held-since=\"2026-10-02\"", page, StringComparison.Ordinal);
        Assert.Contains("The rebalance of 2026-10-02 bought M2.", page, StringComparison.Ordinal);
        Assert.Contains(
            "Each row is a stock the S&P 400's sector heavyweights hold at tonight's close, bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, as one of the leaders of its sector's largest members of the index. It has no stop and no target: it is held while it leads, and sold at the close of a later month's rebalance where the rule would no longer buy it, or at its last close as a member of the index.",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("month's first close", page, StringComparison.Ordinal);

        // The S&P 600's families read no night, and its page says so under its own heading.
        var small = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=600"));

        Assert.Contains("data-title=\"Tonight: S&P 600\"", small, StringComparison.Ordinal);
        Assert.Contains($"The S&P 600's families read nothing for {IndexNight}: no night of theirs is stored for it.", small, StringComparison.Ordinal);
        Assert.Contains("<option value=\"600\" data-members=\"2\" selected>S&P 600 · 2 members</option>", small, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnSAndP400HeavyweightsCardDrawsEachHoldingsLeadOverItsSectorsMembersMeanAsItsBookStoredIt()
    {
        // M2's lead stored at the rebalance that bought it on the night, 4.12 points, and M3 held since a month before,
        // bought before its book stored a lead.
        using var store = UniversesStore();

        store.Execute("UPDATE index_heavyweight_holding SET lead = 0.0412 WHERE index_code = 'MID' AND ticker = 'M2';");
        store.Execute("INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES ('MID', 'M3', '2026-09-01', 'Energy', '45', 1.1, '[]', '2026-10-01');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")));

        Assert.Contains("<tr data-ticker=\"M2\" data-sector=\"Energy\" data-held-since=\"2026-10-02\" data-lead=\"0.0412\"", card, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num heavyweight-lead\">+4.1 points over 251 sessions, at 2026-10-02</td>", card, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"M3\" data-sector=\"Energy\" data-held-since=\"2026-09-01\" data-lead=\"none\"", card, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num heavyweight-lead\"><span class=\"degraded\">bought before its book stored a lead</span></td>", card, StringComparison.Ordinal);
        Assert.DoesNotContain("not read", card, StringComparison.Ordinal);

        // The column's key names the comparison design (a) reads on the index, its sector's members' mean, and no fund.
        Assert.Contains(
            "<span class=\"head-tip\" role=\"tooltip\">Its return over the look-back less its sector's members' mean in the S&P 400 over the same sessions, in percentage points, at the rebalance that bought it.</span>",
            card,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fund's", card, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnSAndP400PicksRowDrawsTheBusinessStateItsNightStoredAndSaysNotReadOnlyWhereNoneWasStored()
    {
        // M1, the S&P 400's breakout pick, with no reading stored for the night: its row says not read.
        using (var store = UniversesStore())
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

            Assert.Contains("<td class=\"business-cell\"><span class=\"degraded\" data-state=\"none\">not read</span></td>", FamilyRowOf(FamilyCardOf(page, "breakout"), "M1"), StringComparison.Ordinal);
        }

        // Its reading stored for the night, improving: its row draws the state as the S&P 500's rows draw theirs.
        using (var store = UniversesStore())
        {
            StoreReading(store, IndexNight, "M1", Improving());

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

            Assert.Contains("<td class=\"business-cell\"> <span class=\"business\" tabindex=\"0\" data-state=\"improving\">improving", FamilyRowOf(FamilyCardOf(page, "breakout"), "M1"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnSAndP400PicksRowSaysARewardToRiskItsNightDidNotStoreIsNotStoredRatherThanNone()
    {
        // M2 listed by the S&P 400's drift with a stop and a target, the night storing no reward to risk for the drift's
        // rows: its row says the ratio is not stored, where the breakout's M1, trailing with no target, reads open.
        using var store = UniversesStore();

        store.Execute($"UPDATE index_family_result SET passed = 1, place = 1, entry = '40', stop = '37.85', target = '44.05', cap = 60, reason = NULL WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND ticker = 'M2' AND family = 'drift';");
        store.Execute($"INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES ('MID', '{IndexNight}', 'M2', 'drift', 'listed', 1, '[]', NULL, NULL, NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\"><span class=\"degraded\">not stored</span></td>", FamilyRowOf(FamilyCardOf(page, "drift"), "M2"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">open</td>", FamilyRowOf(FamilyCardOf(page, "breakout"), "M1"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnSAndP400RuleWhoseSettingChangesHasItsCardDescriptionChangeWithIt()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var before = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Contains($"<p class=\"lede\">{MidBreakoutWords}</p>", before, StringComparison.Ordinal);

        // The breakout's high widened to 251 sessions on twice the volume with no range ceiling, and the S&P 400's
        // dollar volume floor raised to $20,000,000, as a night run under those settings stores them.
        store.Execute(
            "UPDATE index_family_night SET settings = json_set(settings, '$.breakout', 'high=251|volume=2|ceiling=off|stop=1.5', '$.floors.dollarVolume', 20000000) " +
            $"WHERE index_code = 'MID' AND session_date = '{IndexNight}';");

        var after = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.DoesNotContain(MidBreakoutWords, after, StringComparison.Ordinal);
        Assert.Contains(
            "<p class=\"lede\">A stock closes above its highest price of the 251 sessions before on 2 times its average volume. Stop 1.5 typical moves below, raised as the price climbs and never lowered; no target. " +
            "Unlike the S&P 500's rules, a stock is read only at $5 or more and trading at least $20,000,000 a day over 50 sessions,",
            after,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PastPicksRunUniverseAndResearchedOpenWithTheSelectorAndReadTheIndexItChose()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Past picks: the S&P 400's two trades newest first, each named with its index, the ended one's result before
        // and after its cost.
        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400"));

        Assert.Contains("data-title=\"Past picks: S&P 400\"", picks, StringComparison.Ordinal);
        Assert.Contains("<label class=\"universe-pick\" data-universe=\"400\">", picks, StringComparison.Ordinal);
        Assert.Equal(
            [("M1", "breakout", "open", "none", "none"), ("M3", "pullback", "2026-10-01", "2", "1.98")],
            Regex.Matches(picks, "<tr data-ticker=\"([^\"]+)\" data-index=\"S&P 400\" data-family=\"([^\"]+)\" data-listed=\"[^\"]+\" data-ended=\"([^\"]+)\" data-result=\"([^\"]+)\" data-cost=\"[^\"]+\" data-after-cost=\"([^\"]+)\">")
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value, match.Groups[5].Value)));
        Assert.Contains("<td class=\"setup\" data-setup=\"pullback\">Pullback · S&P 400</td>", picks, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">+2.00</td><td class=\"r num\">+0.02</td><td class=\"r num\">+1.98</td>", picks, StringComparison.Ordinal);
        Assert.Contains("Showing 2 of 2 S&P 400 trades, 1 open", picks, StringComparison.Ordinal);
        Assert.Contains(
            "A holding is bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, and sold at the close of a later month's rebalance where it no longer leads its sector among the S&P 400's members, or at its last close as a member.",
            picks,
            StringComparison.Ordinal);
        Assert.DoesNotContain("month's first close", picks, StringComparison.Ordinal);

        // Run: the S&P 400's own night read off its rows.
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400"));

        Assert.Contains("data-title=\"Run evidence: S&P 400\"", run, StringComparison.Ordinal);
        Assert.Contains($"<li>3 S&P 400 members read on {IndexNight}</li>", run, StringComparison.Ordinal);
        Assert.Contains("<li>2 of 3 S&P 400 members passed a setup, 1 listed and 1 held back by a trade still open on any index's list</li>", run, StringComparison.Ordinal);
        Assert.Contains("<li>1 S&P 400 trade(s) kept tonight and 0 ended</li>", run, StringComparison.Ordinal);
        Assert.Contains("<li>the S&P 400's sector heavyweights rebalanced tonight and hold 1</li>", run, StringComparison.Ordinal);

        // Universe: the S&P 600's two members and no other.
        var universe = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe?universe=600"));

        Assert.Contains("data-title=\"The universe: S&P 600\"", universe, StringComparison.Ordinal);
        Assert.Contains("<section class=\"universe\" data-universe=\"600\" data-names=\"2\"", universe, StringComparison.Ordinal);

        // Researched: the S&P 400's researched member alone, and the S&P 500's alone where it is chosen.
        var mid = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/researched?universe=400"));
        var large = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/researched"));

        Assert.Contains("data-title=\"Researched: S&P 400\"", mid, StringComparison.Ordinal);
        Assert.Equal(["M1"], Regex.Matches(mid, "<tr data-ticker=\"([^\"]+)\" data-written").Select(match => match.Groups[1].Value));
        Assert.Contains("data-title=\"Researched: S&P 500\"", large, StringComparison.Ordinal);
        Assert.Equal(["AAA"], Regex.Matches(large, "<tr data-ticker=\"([^\"]+)\" data-written").Select(match => match.Groups[1].Value));
    }

    [Fact]
    public async Task TheSAndP500sCardNamesTheIndexWhoseOpenTradeHoldsAStockBack()
    {
        using var store = await FamilyNightStore();

        // HELD's trade from the night before is still open, and the night's row names the S&P 400's list as its holder.
        store.Execute("UPDATE family_pick SET held_index = 'MID' WHERE ticker = 'HELD' AND state = 'open trade';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        Assert.Contains("on the S&P 400's list is still open, so it is not listed. One stock, one trade.", FamilyCardOf(page, "pullback"), StringComparison.Ordinal);
        Assert.Contains("<div class=\"screen-mast\" data-title=\"Tonight: S&P 500\">", page, StringComparison.Ordinal);
        Assert.Contains("<option value=\"500\" data-members=\"", page, StringComparison.Ordinal);
    }

    // An S&P 600 night whose part failed: the night row names the failure and holds no answer, and the book holds S1 from
    // a month before. Tonight opens on the words in place of the market line, every swing card and the heavyweights' card
    // say the same and list nothing, and the Run page names the cause.
    // see: A failure in the S&P 400's or 600's part of the night is caught and named, and the S&P 500's night is built regardless
    [Fact]
    public async Task AnSAndP600NightThatFailedSaysNotComputedTonightOnEveryCardAndItsRunPageNamesTheCause()
    {
        using var store = UniversesStore();

        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced, fault) VALUES " +
            $"('SML', '{IndexNight}', 0, NULL, 0, '{IndexFamilies.Settings("SML")}', 0, 'FormatException: The input string was not in a correct format.');");
        store.Execute(
            "INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES " +
            "('SML', 'S1', '2026-09-01', 'Energy', '50', 1.0, '[]', '2026-10-01');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=600"));
        const string Words = "Not computed tonight: the S&P 600's part of the night failed, and the Run page names its cause.";

        Assert.Contains($"<p class=\"degraded\" data-index-night=\"not-computed\">{Words}</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("breadth", page[..page.IndexOf(Words, StringComparison.Ordinal)], StringComparison.Ordinal);

        foreach (var family in new[] { "pullback", "breakout", "drift", "fundamentals" })
        {
            Assert.Contains(Words, FamilyCardOf(page, family), StringComparison.Ordinal);
        }

        // The paragraph, the four swing cards, the fundamentals-first family's among them from 17.8, and the heavyweights'
        // card, which lists none of the book's holdings.
        Assert.Equal(6, Regex.Matches(page, Regex.Escape(Words)).Count);
        Assert.DoesNotContain("data-ticker=\"S1\"", page, StringComparison.Ordinal);

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=600"));

        Assert.Contains(
            $"<p class=\"degraded\" data-index-night=\"not-computed\">Not computed tonight: the S&P 600's part of the night of {IndexNight} failed on FormatException: The input string was not in a correct format., and the S&P 500's night was built regardless.</p>",
            run,
            StringComparison.Ordinal);

        // The S&P 400's night, computed, reads as before.
        Assert.DoesNotContain("not-computed", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")), StringComparison.Ordinal);
    }
}
