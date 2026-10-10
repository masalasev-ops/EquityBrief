using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Filter;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.7: "Close to a buy point", the second list on Tonight, read back off the pages against
// constructed stores: a night of members each missing a different gate, one missing two and one excluded, the
// cap of twenty across both lists, a night the market gate closed, an earlier night, the name page's why,
// and the stores left as they were.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
public partial class ReadSurface
{
    // The claims 12.7's correction adds: the region's eleven parts and section 18's row for both lists empty.
    internal static readonly string[] CloseToABuyPointClaims =
    [
        CheckReach.Key("15.7 Tonight", "Close to a buy point, one row per member whose stored result missed exactly one of the five gates and carries no exclusion"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, nearest to qualifying first with a tie in the list's own order"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, drawing the places the list leaves of the twenty"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, a line above them stating how many are drawn of how many are one gate short"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, what a row of the list carries"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, the gate it missed in place of the gates"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, what it had against the bar it needed in plain words"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, how far short that is as a share of the bar"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, the trade its plan states"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, one line above the rows stated once saying they would qualify if the market turned"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point, a key saying it recommends nothing"),
        CheckReach.Key(Scope.FailureTable, "Both lists on tonight's page empty on a night the swing filter listed"),
    ];

    // The version the constructed nights store their results under, ruled here so every bar a distance is
    // read against is a figure this file states: breadth 45%, strength 0.50, a pullback of 1 to 5 typical
    // moves, volume while it came down 1.5 times its average or less, reward to risk 1.5, a stop 0.5 to 4
    // typical moves below the entry, and a trigger first fired within the last 3 sessions.
    static readonly FilterSettings CloseVersion = new(0.45, 0.5, 1, 5, 1.5, 1.5, 0.5, 4, 15, 3, TradeInput.Clear);

    // A member's stored gates with the values its one missed gate read, every other gate's as the switch
    // night's rows carry them.
    static string GatesMissing(Member member, string gate, Dictionary<string, string> values)
    {
        using var document = JsonDocument.Parse(GatesJson(member));

        var gates = document.RootElement.GetProperty("gates").EnumerateArray()
            .Select(one => new
            {
                gate = one.GetProperty("gate").GetString(),
                passed = one.GetProperty("passed").GetBoolean(),
                reason = one.GetProperty("reason").GetString(),
                values = one.GetProperty("gate").GetString() == gate
                    ? values
                    : one.GetProperty("values").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString() ?? string.Empty),
            })
            .ToArray();

        return JsonSerializer.Serialize(new { gates, notes = Array.Empty<string>() });
    }

    static void Missing(TemporaryStore store, string session, Member member, string gate, Dictionary<string, string> values) =>
        store.Execute($"UPDATE gate_result SET gates = '{GatesMissing(member, gate, values)}' WHERE ticker = '{member.Ticker}' AND session_date = '{session}';");

    static void OpenCloseVersion(TemporaryStore store) =>
        store.Execute($"INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{CloseVersion.Write()}', '2026-09-01T22:00:00Z', NULL, 'constructed');");

    // The members one gate short on the switch night, each with the values its missed gate read and the
    // distance worked by hand against the version above:
    //   ST, strength 0.45 against 0.50: (0.50 - 0.45) / 0.50 = 0.10
    //   TD, reward 1.2 times the risk against 1.5: (1.5 - 1.2) / 1.5 = 0.20
    //   SE, a pullback 0.7 typical moves deep against 1 to 5: (1 - 0.7) / 1 = 0.30
    //   TR2 and TR, a range with strength over its bar: half a bar, 0.50, TR2's trade 3 times the risk and
    //     TR's 2, so TR2 first
    //   TG, its trigger first fired 4 sessions before tonight against a window of 3: (4 + 1 - 3) / 3 = 0.67
    // and three that are not: TWO missing two gates, EX missing one with an earnings date inside the holding
    // window, and P1, which passed and is on the list itself.
    static readonly string[] CloseOrder = ["ST", "TD", "SE", "TR2", "TR", "TG"];

    static TemporaryStore CloseStore(string session = TheSwitch, int passers = 1, int extra = 0)
    {
        var store = new TemporaryStore().Migrated();

        OpenCloseVersion(store);

        var st = new Member("ST", Trend: false);
        var td = new Member("TD", Trade: false, RewardToRisk: 1.2);
        var se = new Member("SE", Setup: false);
        var tr2 = new Member("TR2", Trend: false, RewardToRisk: 3);
        var tr = new Member("TR", Trend: false, RewardToRisk: 2);
        var tg = new Member("TG", Trigger: false);

        Evening(store, session, filter: true,
        [
            .. Enumerable.Range(1, passers).Select(at => new Member($"P{at}", Passed: true, Rank: at)),
            st, td, se, tr2, tr, tg,
            new Member("TWO", Trend: false, Setup: false),
            new Member("EX", Trade: false, Exclusions: ["an earnings date inside the holding window"]),
            // Each (1.5 - 0.1) / 1.5 = 0.93 short, after TG, and tied among themselves to the ticker.
            .. Enumerable.Range(1, extra).Select(at => new Member($"X{at:00}", Trade: false, RewardToRisk: 0.1)),
        ]);

        // The clear plan the version's trade gate reads, as the rows stored it: entered at 100, stopped at 96
        // and won at 110.
        store.Execute($"UPDATE gate_result SET clear_stop = '96', clear_target = '110' WHERE session_date = '{session}';");

        Missing(store, session, st, "trend and strength", new() { ["trend state"] = "uptrend", ["strength"] = "0.45", ["floor"] = "0.5" });
        Missing(store, session, se, "setup", new() { ["family"] = "none", ["depth"] = "0.7", ["dry-up"] = "1.2", [SwingGates.PullbackBandValue] = "yes" });
        Missing(store, session, tr2, "trend and strength", new() { ["trend state"] = "range", ["strength"] = "0.6", ["floor"] = "0.5" });
        Missing(store, session, tr, "trend and strength", new() { ["trend state"] = "range", ["strength"] = "0.6", ["floor"] = "0.5" });
        Missing(store, session, tg, "trigger", new() { ["arrived"] = "none", ["arrival window"] = "3" });

        // TG's trigger event tonight and on the six sessions before it, newest first: it fired four sessions
        // before tonight and on none after, the session before that firing holding none.
        var on = DateOnly.ParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute($"UPDATE gate_result SET trigger_event = 0 WHERE ticker = 'TG' AND session_date = '{session}';");

        foreach (var back in Enumerable.Range(1, 6))
        {
            var earlier = on.AddDays(-back).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            GateRow(store, earlier, tg);
            store.Execute($"UPDATE gate_result SET trigger_event = {(back == 4 ? 1 : 0)} WHERE ticker = 'TG' AND session_date = '{earlier}';");
        }

        return store;
    }

    static string CloseList(string page) =>
        Assert.Single(Blocks(page, "<section class=\"tonight-list close-list\".*?</section>"));

    [Fact]
    public async Task CloseToABuyPointDrawsEachMemberOneGateShortNearestFirstAndNoneMissingTwoOrExcluded()
    {
        using var store = CloseStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var close = CloseList(page);
        var drawn = DrawnTickers(close);

        // The order worked by hand, and none missing two gates, none excluded and none on the list itself.
        Assert.Equal(CloseOrder, drawn);
        Assert.DoesNotContain("TWO", drawn);
        Assert.DoesNotContain("EX", drawn);
        Assert.DoesNotContain("P1", drawn);
        Assert.Equal(["P1"], DrawnTickers(Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"))));

        // Both directions against the store: every row drawn is a result missing exactly one gate with no
        // exclusion, and every such result is drawn.
        var oneShort = Strings(store,
            $"SELECT ticker FROM gate_result WHERE session_date = '{TheSwitch}' AND passed = 0 AND exclusions = '[]' " +
            "AND (5 - market - trend - setup - trigger_pass - trade) = 1 ORDER BY ticker;");

        Assert.Equal(oneShort.Order(StringComparer.Ordinal), drawn.Order(StringComparer.Ordinal));

        // Each row names the gate it missed, what it had against the bar, and how far short.
        foreach (var (ticker, gate, words) in new (string, string, string)[]
        {
            ("ST", "trend and strength", "strength 0.45, needs 0.50; 10% short"),
            ("TD", "trade", "the reward is 1.20 times the risk, needs 1.5; 20% short"),
            ("SE", "setup", "the pullback is 0.70 typical moves deep, needs 1 to 5; 30% short"),
            ("TR2", "trend and strength", "the chart reads a range, needs an uptrend; 50% short"),
            ("TR", "trend and strength", "the chart reads a range, needs an uptrend; 50% short"),
            ("TG", "trigger", "the buy signal fired 4 sessions before tonight, outside its 3-session window; 67% short"),
        })
        {
            var row = RowOf(close, ticker);

            Assert.Contains($"<td class=\"missed\" data-gate=\"{gate}\"", row, StringComparison.Ordinal);
            Assert.Contains($"<b>{gate}</b>: {words}", row, StringComparison.Ordinal);

            // The trade its plan states, the clear plan the version reads, as the row stored it.
            Assert.Contains("<span class=\"missed-trade\">entry 100.00, stop 96.00, target 110.00</span>", row, StringComparison.Ordinal);
        }

        // Its count stated above its rows, its column named, and its key saying it recommends nothing.
        Assert.StartsWith("Showing all 6 names one gate short.", WordsOf(Regex.Match(close, "<p class=\"list-count\"[^>]*>.*?</p>", RegexOptions.Singleline).Value), StringComparison.Ordinal);
        Assert.Contains("data-heading=\"Gate missed\"", close, StringComparison.Ordinal);
        Assert.DoesNotContain("data-heading=\"Gates\"", close, StringComparison.Ordinal);
        Assert.Contains("Close to a buy point", page, StringComparison.Ordinal);
        Assert.Contains(EquityBrief.Web.App.SinglePageApp.CloseKeyText, page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTwoListsDrawTwentyRowsBetweenThemTheListFirst()
    {
        // Fifteen on the list and six plus eight one gate short: the list draws its fifteen and this one the
        // five places left, of fourteen. Twenty on the list leaves this one none, and it says how many it holds.
        foreach (var (passers, drawnHere, says) in new (int, int, string)[]
        {
            (15, 5, "Showing 5 of the 14 names one gate short."),
            (20, 0, "The list above fills every row the page draws, so none of the 14 names one gate short is drawn."),
        })
        {
            using var store = CloseStore(passers: passers, extra: 8);
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
            var close = CloseList(page);

            Assert.Equal(passers, DrawnTickers(Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"))).Count);
            Assert.Equal(drawnHere, DrawnTickers(close).Count);
            Assert.Equal(CloseOrder.Take(drawnHere), DrawnTickers(close));
            Assert.StartsWith(says, WordsOf(Regex.Match(close, "<p class=\"list-count\"[^>]*>.*?</p>", RegexOptions.Singleline).Value), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OnANightTheMarketGateClosedTheListHoldsTheMembersShortOfTheMarketAloneUnderOneLine()
    {
        using var store = new TemporaryStore().Migrated();

        OpenCloseVersion(store);

        // Every member fails the market; M0 to M3 pass everything else, M4 also fails its trend, and M5 is
        // excluded. So M0 to M3 are close, each the same distance short, (0.45 - 0.42) / 0.45 = 7%, and in the
        // list's own order from there: the trade's reward to risk first, M3's 3.0, then M1's 2.5, then M0 and
        // M2 at 2.0 by strength, M2's 0.6 first.
        Evening(store, TheSwitch, filter: true,
        [
            new Member("M0", Market: false, Breadth: 0.42, Floor: 0.45, RewardToRisk: 2, Strength: 0.5),
            new Member("M1", Market: false, Breadth: 0.42, Floor: 0.45, RewardToRisk: 2.5),
            new Member("M2", Market: false, Breadth: 0.42, Floor: 0.45, RewardToRisk: 2, Strength: 0.6),
            new Member("M3", Market: false, Breadth: 0.42, Floor: 0.45, RewardToRisk: 3),
            new Member("M4", Market: false, Trend: false, Breadth: 0.42, Floor: 0.45),
            new Member("M5", Market: false, Breadth: 0.42, Floor: 0.45, Exclusions: ["a series in doubt"]),
        ]);
        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            $"VALUES ('{TheSwitch}', 6, 6, 2, 0.42, 6, 3, 0.5, 6, 0.9);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var close = CloseList(page);

        Assert.Equal(["M3", "M1", "M2", "M0"], DrawnTickers(close));
        Assert.Contains("The market gate closed tonight, so these would qualify if the market turned: 42.0% of the members closed above their 200-day average, and it needs 45%.", close, StringComparison.Ordinal);

        // The line is stated once, and no row says it again.
        Assert.Single(Regex.Matches(close, "would qualify if the market turned"));
        Assert.All(DrawnTickers(close), ticker => Assert.Contains("<b>market</b>; 7% short", RowOf(close, ticker), StringComparison.Ordinal));
        Assert.DoesNotContain("the market's breadth is", close, StringComparison.Ordinal);

        // The list itself still says the gate closed and lists no name.
        Assert.Empty(DrawnTickers(Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"))));
    }

    [Fact]
    public async Task ANightWhoseListsAreBothEmptySaysSoInEach()
    {
        // Worked by hand: no member passes, and none misses exactly one gate with nothing excluding it: two
        // miss two gates, one misses three, and one misses one with an exclusion.
        using var store = new TemporaryStore().Migrated();

        OpenCloseVersion(store);
        Evening(store, TheSwitch, filter: true,
        [
            new Member("E0", Trend: false, Setup: false),
            new Member("E1", Trigger: false, Trade: false),
            new Member("E2", Setup: false, Trigger: false, Trade: false),
            new Member("E3", Trade: false, Exclusions: ["a series in doubt"]),
        ]);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var list = Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"));
        var close = CloseList(page);

        Assert.Contains("No name passed the swing filter tonight:", list, StringComparison.Ordinal);
        Assert.Empty(DrawnTickers(list));
        Assert.Contains("<p class=\"degraded\" data-close=\"0\">No member missed exactly one gate tonight with nothing excluding it.</p>", close, StringComparison.Ordinal);
        Assert.Empty(DrawnTickers(close));
    }

    [Fact]
    public async Task AnEarlierNightDrawsItsOwnMembersOneGateShort()
    {
        // Two more nights the filter listed, copies of the switch night: on the earlier one only ST is one
        // gate short, every other member passing; on the later one the six are, TG's firing now six sessions
        // before it and still the furthest. Each night's page draws its own.
        using var store = CloseStore();

        const string Earlier = "2026-10-05";
        const string Later = "2026-10-06";

        foreach (var session in new[] { Earlier, Later })
        {
            store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{session}', 'filter');");
            store.Execute(
                "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) " +
                $"SELECT ticker, '{session}', reasons, fired_count, plan_at_listing, shadow_reasons, band_strength FROM listing WHERE session_date = '{TheSwitch}';");
            store.Execute(
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, " +
                "ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, swing_reward_to_risk, swing_stop_moves, exclusions, passed, rank, strength, band_strength, gates, clear_stop, clear_target) " +
                $"SELECT ticker, '{session}', version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, " +
                "ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, swing_reward_to_risk, swing_stop_moves, exclusions, passed, rank, strength, band_strength, gates, clear_stop, clear_target " +
                $"FROM gate_result WHERE session_date = '{TheSwitch}';");
        }

        store.Execute($"UPDATE gate_result SET market = 1, trend = 1, setup = 1, trigger_pass = 1, trade = 1, exclusions = '[]', passed = 1 WHERE session_date = '{Earlier}' AND ticker <> 'ST';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        Assert.Equal(["ST"], DrawnTickers(CloseList(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{Earlier}")))));
        Assert.Equal(CloseOrder, DrawnTickers(CloseList(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{Later}")))));
        Assert.Equal(CloseOrder, DrawnTickers(CloseList(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")))));
    }

    [Fact]
    public async Task ANameOneGateShortSaysOnItsPageWhichGateItMissedAndByHowMuchAndThatItIsNotAPick()
    {
        using var store = CloseStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var st = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/ST/{TheSwitch}"));
        var why = Assert.Single(Blocks(st, "<section class=\"why-it-is-here\".*?</section>"));

        Assert.Contains($"Close to a buy point on {TheSwitch}: one gate short", st, StringComparison.Ordinal);
        Assert.Contains("data-missed=\"trend and strength\"", why, StringComparison.Ordinal);
        Assert.Contains($"ST passed every gate of the swing filter on {TheSwitch} but one, and nothing excluded it, so it is close to a buy point and not on the list.", why, StringComparison.Ordinal);
        Assert.Contains("<b>trend and strength</b>: strength 0.45, needs 0.50; 10% short of its bar.", why, StringComparison.Ordinal);
        Assert.Contains("The trade its plan states: entry 100.00, stop 96.00, target 110.00.", why, StringComparison.Ordinal);
        Assert.Contains("It is not a pick", why, StringComparison.Ordinal);

        // A name missing two gates, and one excluded, say only that they are not on the list.
        foreach (var ticker in new[] { "TWO", "EX" })
        {
            var not = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}/{TheSwitch}")), "<section class=\"why-it-is-here\".*?</section>"));

            Assert.Contains("data-listed=\"false\"", not, StringComparison.Ordinal);
            Assert.DoesNotContain("data-missed", not, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DrawingTheSecondListWritesNothingAndPastPicksAndTheEdgeClockReadNoneOfIt()
    {
        using var store = CloseStore();

        // Every table's row count, and every row of the stores the lists are drawn from.
        string Dump() => string.Join("\n",
            Strings(store, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;")
                .Select(table => table + ":" + string.Join("|", Strings(store, $"SELECT COUNT(*) FROM \"{table}\";")))
                .Concat(Strings(store, "SELECT json_group_array(json_array(ticker, session_date, passed, rank, trigger_event, exclusions, gates)) FROM (SELECT * FROM gate_result ORDER BY ticker, session_date);"))
                .Concat(Strings(store, "SELECT json_group_array(json_array(ticker, session_date, fired_count, shadow_reasons)) FROM (SELECT * FROM listing ORDER BY ticker, session_date);")));

        // Taken once the read surface is up, since it records its own start on the run log.
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        _ = await client.GetStringAsync("/screens/watch");

        var before = Dump();

        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        Assert.Equal(CloseOrder, DrawnTickers(CloseList(tonight)));
        _ = await client.GetStringAsync($"/screens/name/ST/{TheSwitch}");

        // Nothing is stored for the second list, so nothing another page reads can hold it.
        Assert.Equal(before, Dump());

        // Past picks holds the list's own name and none of the second list's.
        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        Assert.Contains("<tr data-ticker=\"P1\"", picks, StringComparison.Ordinal);
        Assert.All(CloseOrder, ticker => Assert.DoesNotContain($"<tr data-ticker=\"{ticker}\"", picks, StringComparison.Ordinal));

        // And the near-miss rule is named only by the readers that draw the list: the edge clock, Past picks
        // and every writer read nothing of it.
        var readers = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}EquityBrief.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Regex.IsMatch(File.ReadAllText(path), @"\b(NearMiss|CloseScreen)\."))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "src/EquityBrief.Api/Program.cs",
                "src/EquityBrief.Api/Reading/CloseScreen.cs",
                "src/EquityBrief.Api/Reading/TonightScreen.cs",
            ],
            readers);
    }
}
