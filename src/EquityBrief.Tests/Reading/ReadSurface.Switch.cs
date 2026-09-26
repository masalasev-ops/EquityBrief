using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.6: tonight's list switched to the swing filter, read back off the pages against
// constructed stores: a night of forty passing, an evening before the switch drawn as it was listed, a
// night the market gate closed and one no name passed, the name page's why and history, the universe's
// last evening, the walk, and the run page's overlap worked by hand.
public partial class ReadSurface
{
    const string BeforeTheSwitch = "2026-10-01";
    const string TheSwitch = "2026-10-02";

    // One member's row on a constructed evening: its listing, fired on the reasons named, and its gate
    // row, each gate as given, with the trade's reward to risk, strength and band strength the filter's
    // order reads.
    sealed record Member(
        string Ticker,
        int Fired = 0,
        string? Plan = null,
        bool Market = true,
        bool Trend = true,
        bool Setup = true,
        bool Trigger = true,
        bool Trade = true,
        bool Passed = false,
        int? Rank = null,
        double RewardToRisk = 2,
        double Strength = 0.5,
        int Band = 0,
        string[]? Exclusions = null,
        double Floor = 0.5,
        double Breadth = 0.6);

    static string ReasonsJson(int fired) =>
        JsonSerializer.Serialize(ShortlistSeries.Reasons.Select((reason, at) => new { name = reason, fired = at < fired, values = new Dictionary<string, string>() }));

    static string GatesJson(Member member)
    {
        object Gate(string name, bool passed, string reason, Dictionary<string, string> values) =>
            new { gate = name, passed, reason, values };

        var ratio = member.RewardToRisk.ToString("R", CultureInfo.InvariantCulture);

        return JsonSerializer.Serialize(new
        {
            gates = new[]
            {
                Gate("market", member.Market, member.Market ? "breadth at or above its floor" : "breadth below its floor", new() { ["breadth"] = member.Breadth.ToString("R", CultureInfo.InvariantCulture), ["floor"] = member.Floor.ToString("R", CultureInfo.InvariantCulture), ["counted"] = "45" }),
                Gate("trend and strength", member.Trend, member.Trend ? "an uptrend at or above its floor" : "not an uptrend", new() { ["trend state"] = "uptrend" }),
                Gate("setup", member.Setup, member.Setup ? "a pullback into an anchored support band" : "no pullback and no breakout", new() { ["family"] = member.Setup ? "pullback" : "none" }),
                Gate("trigger", member.Trigger, member.Trigger ? "the close above the previous session's high" : "no event inside the window", new() { ["arrived"] = member.Trigger ? "tonight" : "none" }),
                Gate("trade", member.Trade, member.Trade ? $"reward to risk {ratio} at or above its floor" : "reward to risk below its floor", new() { ["input"] = "swing", ["reward to risk"] = ratio, ["stop in typical moves"] = "1.2" }),
            },
            notes = Array.Empty<string>(),
        });
    }

    static void Evening(TemporaryStore store, string session, bool filter, IEnumerable<Member> members)
    {
        foreach (var member in members)
        {
            store.Execute(
                "INSERT OR IGNORE INTO membership (index_code, ticker, joined, \"left\", observed_at) " +
                $"VALUES ('GSPC', '{member.Ticker}', '2025-01-02', NULL, '2025-01-02T00:00:00Z');");
            store.Execute(
                "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) " +
                $"VALUES ('{member.Ticker}', '{session}', '{ReasonsJson(member.Fired)}', {member.Fired}, '{member.Plan ?? "{}"}', '{{\"candidates\":[],\"skipped\":[]}}', {member.Band});");
            GateRow(store, session, member);
        }

        if (filter)
        {
            store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{session}', 'filter');");
        }
    }

    // A member's gate row on a session, as the swing filter stores it.
    static void GateRow(TemporaryStore store, string session, Member member)
    {
        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, " +
            "ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, swing_reward_to_risk, swing_stop_moves, exclusions, " +
            "passed, rank, strength, band_strength, gates) " +
            $"VALUES ('{member.Ticker}', '{session}', '1', 'code', {Bit(member.Market)}, {Bit(member.Trend)}, {Bit(member.Setup)}, {(member.Setup ? "'pullback'" : "NULL")}, " +
            $"{Bit(member.Trigger)}, 1, {Bit(member.Trade)}, NULL, NULL, '100', '96', '110', {member.RewardToRisk.ToString("R", CultureInfo.InvariantCulture)}, 1.2, " +
            $"'{JsonSerializer.Serialize(member.Exclusions ?? [])}', {Bit(member.Passed)}, {(member.Rank is { } rank ? rank.ToString(CultureInfo.InvariantCulture) : "NULL")}, " +
            $"{member.Strength.ToString("R", CultureInfo.InvariantCulture)}, {member.Band}, '{GatesJson(member)}');");

        static string Bit(bool value) => value ? "1" : "0";
    }

    // A plan whose first tranche gives the reward to risk named: the zone 100 to 102, its stop at 96,
    // and the target placed so the midpoint's reward over its risk of 5 is the figure.
    static string PlanFor(decimal ratio) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["entryLow"] = "100",
            ["entryHigh"] = "102",
            ["stop"] = "96",
            ["firstTradedTarget"] = (101 + 5 * ratio).ToString(CultureInfo.InvariantCulture),
        });

    // The forty the filter passed on the switch night, with the figures its order reads: reward to risk
    // from 1.5 to 3.25 in steps of a quarter, strength from 0.50 to 0.54 and band strength 0 to 2, so
    // rows tie on the first and on the first two, and the order is worked here by hand from those
    // figures: reward to risk higher first, then strength, then band strength, then the ticker.
    static Member[] Passers()
    {
        var passers = Enumerable.Range(0, 40)
            .Select(at => new Member($"Z{at:00}", Fired: at == 0 ? 1 : 0, RewardToRisk: 1.5 + 0.25 * (at % 8), Strength: 0.5 + 0.01 * (at % 5), Band: at % 3, Passed: true))
            .ToList();

        passers.Sort((a, b) =>
            a.RewardToRisk != b.RewardToRisk ? b.RewardToRisk.CompareTo(a.RewardToRisk)
            : a.Strength != b.Strength ? b.Strength.CompareTo(a.Strength)
            : a.Band != b.Band ? b.Band.CompareTo(a.Band)
            : string.CompareOrdinal(a.Ticker, b.Ticker));

        return [.. passers.Select((member, at) => member with { Rank = at + 1 })];
    }

    // The five that fired three reasons on both evenings and that the filter did not pass: each fails
    // its trade gate on the switch night.
    static Member[] FiredOnly(string session) =>
    [
        .. new (string Ticker, decimal? Ratio)[] { ("Z40", 2m), ("Z41", 3m), ("Z42", null), ("Z43", 3m), ("Z44", 1m) }
            .Select(one => new Member(one.Ticker, Fired: 3, Plan: one.Ratio is { } ratio ? PlanFor(ratio) : "{}", Trade: false)),
    ];

    // The two evenings: before the switch the reasons listed, Z00 firing one and the five three each,
    // while the filter's rows held Z01 passing, which that evening's list does not draw; on the switch
    // night the filter listed its forty and the five fired three each again.
    static TemporaryStore SwitchStore()
    {
        var store = new TemporaryStore().Migrated();

        Evening(store, BeforeTheSwitch, filter: false,
        [
            .. Passers().Select(member => member with { Passed = member.Ticker == "Z01", Rank = member.Ticker == "Z01" ? 1 : null, Fired = member.Ticker == "Z00" ? 1 : 0, Plan = member.Ticker == "Z00" ? PlanFor(2m) : null }),
            .. FiredOnly(BeforeTheSwitch),
        ]);
        Evening(store, TheSwitch, filter: true, [.. Passers(), .. FiredOnly(TheSwitch)]);

        return store;
    }

    static IReadOnlyList<string> DrawnTickers(string page) =>
        [.. Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];

    [Fact]
    public async Task TonightsListDrawsTheNamesTheSwingFilterPassedInItsOrderReadBackAgainstTheStoreInBothDirections()
    {
        using var store = SwitchStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var list = Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"));
        var order = Passers().Select(member => member.Ticker).ToArray();

        // The header's count is the forty the filter passed of the forty-five, the fired count beside it
        // as context.
        Assert.Contains("data-listed=\"40\"", page, StringComparison.Ordinal);
        Assert.Contains($"40 of 45 name(s) passed the swing filter on {TheSwitch}", page, StringComparison.Ordinal);
        Assert.Contains($"6 of 45 name(s) fired on {TheSwitch}, as context", page, StringComparison.Ordinal);

        // The rule, and the line counting the rows against the count listed.
        Assert.Contains("data-rule=\"filter\">This evening was " + ListRules.EveningByFilter + ".</p>", list, StringComparison.Ordinal);
        Assert.StartsWith("Showing 20 of the 40 names the swing filter listed.", WordsOf(Regex.Match(list, "<p class=\"list-count\"[^>]*>.*?</p>", RegexOptions.Singleline).Value), StringComparison.Ordinal);

        // The card names the swing filter's list and the order it is drawn in.
        Assert.Contains("Names at a buy point tonight", page, StringComparison.Ordinal);
        Assert.Contains("in the filter's order, the trade's reward to risk first, then strength, then band strength", page, StringComparison.Ordinal);

        // Both directions: the rows drawn are the twenty first in the order worked by hand, in it, and
        // every one of them passed on the store; no name the filter did not pass is drawn, whatever it
        // fired.
        var drawn = DrawnTickers(list);
        var passed = Strings(store, $"SELECT ticker FROM gate_result WHERE session_date = '{TheSwitch}' AND passed = 1;").ToHashSet(StringComparer.Ordinal);

        Assert.Equal(order.Take(20), drawn);
        Assert.All(drawn, ticker => Assert.Contains(ticker, passed));
        Assert.All(FiredOnly(TheSwitch), member => Assert.DoesNotContain(member.Ticker, drawn));
        Assert.Equal(40, passed.Count);

        // Each row's gates, with the rank it holds, the family, the session its trigger arrived on and the
        // trade the gate read, and the reward to risk drawn being the trade's, its figures to the hundredth
        // as the reward to risk column draws them: the first row's 3.25 and 1.2 worked by hand, and never the
        // figure as the gate stored it.
        Assert.Contains("<th>Gates</th>", list, StringComparison.Ordinal);
        Assert.Contains("pullback, arrived tonight; the swing trade's reward to risk 3.25, its stop 1.20 typical moves below the entry", WordsOf(RowOf(list, drawn[0])), StringComparison.Ordinal);
        Assert.DoesNotContain("trade at ", list, StringComparison.Ordinal);

        foreach (var (ticker, place) in drawn.Select((ticker, at) => (ticker, at + 1)))
        {
            var member = Passers().Single(one => one.Ticker == ticker);
            var row = RowOf(list, ticker);

            Assert.Contains($"data-rank=\"{place}\" data-family=\"pullback\" data-arrived=\"tonight\" data-input=\"swing\"", row, StringComparison.Ordinal);
            Assert.Contains($"data-reward-to-risk=\"{((decimal)member.RewardToRisk).ToString(CultureInfo.InvariantCulture)}\"", row, StringComparison.Ordinal);
            Assert.Contains($"pullback, arrived tonight; the swing trade's reward to risk {member.RewardToRisk.ToString("0.00", CultureInfo.InvariantCulture)}, its stop 1.20 typical moves below the entry", WordsOf(row), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnEveningBeforeTheSwitchIsNotDrawnAndOnAStoreTheFilterNeverListedIsDrawnAsItWasListed()
    {
        using var store = SwitchStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The record starts on the switch, so the evening before it is not drawn, and the line says where
        // the record starts.
        // see: The dated screens open from the swing filter's first night, and an evening before it is not drawn
        Assert.Contains(
            $"<section class=\"before-the-record\" data-night=\"{BeforeTheSwitch}\" data-first=\"{TheSwitch}\">",
            await client.GetStringAsync($"/screens/tonight/{BeforeTheSwitch}"),
            StringComparison.Ordinal);

        // On a store the filter never listed, the same evening is drawn as its reasons listed it.
        store.Execute("DELETE FROM list_rule WHERE rule = 'filter';");

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{BeforeTheSwitch}"));
        var list = Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"));

        // The names that fired, three each first by the plan's reward to risk, Z41 and Z43 tied at 3 and
        // taken by the ticker, Z42's plan computing none and drawn last of its group, then Z00's one; and
        // Z01, which the filter's row passed that evening, is not drawn, since the reasons listed it.
        Assert.Equal(["Z41", "Z43", "Z40", "Z44", "Z42", "Z00"], DrawnTickers(list));
        Assert.Contains("data-rule=\"reasons\">This evening was " + ListRules.ByReasons + ".</p>", list, StringComparison.Ordinal);
        Assert.StartsWith("Showing all 6 names that fired.", WordsOf(Regex.Match(list, "<p class=\"list-count\"[^>]*>.*?</p>", RegexOptions.Singleline).Value), StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Gates</th>", list, StringComparison.Ordinal);
        Assert.Contains("data-listed=\"none\"", page, StringComparison.Ordinal);

        // The card says the order it was drawn in, and says it of this evening's rule.
        Assert.Contains("Names that fired tonight", page, StringComparison.Ordinal);
        Assert.Contains("Most reasons first, then the plan's reward to risk.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightTheMarketGateClosedDrawsNoRowAndOneLineWithTheBreadthAndItsFloor()
    {
        foreach (var (breadth, says) in new (string, string)[]
        {
            ("0.42", "The market gate closed tonight: 42.0% of the members closed above their 200-day average, below its floor of 50%, so no name is listed."),
            ("NULL", "The market gate closed tonight: the night's breadth is not available, so no name is listed."),
        })
        {
            using var store = new TemporaryStore().Migrated();

            Evening(store, TheSwitch, filter: true, [.. Enumerable.Range(0, 6).Select(at => new Member($"M{at}", Fired: at % 2, Market: false))]);
            store.Execute(
                "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
                $"VALUES ('{TheSwitch}', 6, 6, 2, {breadth}, 6, 3, 0.5, 6, 0.9);");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
            var list = Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"));

            Assert.Contains(says, list, StringComparison.Ordinal);
            Assert.Empty(DrawnTickers(list));
            Assert.Contains("data-listed=\"0\"", page, StringComparison.Ordinal);

            // The evening's rule says what puts a name on the list, and on a night that listed none it
            // does not say a gate passed.
            Assert.Contains("data-rule=\"filter\">This evening was " + ListRules.EveningByFilter + ".</p>", list, StringComparison.Ordinal);
            Assert.DoesNotContain(ListRules.ByFilter, list, StringComparison.Ordinal);
        }
    }

    // Every sentence the switch night's pages draw read for the old selection, as the architecture's are:
    // tonight's page with a name selected, that name's own page, its exported report and the universe, each split at its
    // sentences and cells, with the term of each word the name page defines read with its meaning. The
    // evening before the switch, whose reasons did choose its list, is read the same way and its reason
    // totals are found, which shows the reader finds what it looks for on a page.
    [Fact]
    public async Task NoSentenceTheSwitchNightDrawsDescribesTheReasonsChoosingTheList()
    {
        using var store = SwitchStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var tonight = await client.GetStringAsync($"/screens/tonight/{TheSwitch}?name=Z00");
        var name = await client.GetStringAsync($"/screens/name/Z00/{TheSwitch}");
        var universe = await client.GetStringAsync("/screens/universe");
        var export = await client.GetStringAsync("/exports/name/Z00");

        foreach (var page in new[] { tonight, name, export, universe })
        {
            var sentences = SentencesOf(page);

            Assert.True(sentences.Count > 20, $"Read {sentences.Count} sentences, expected more than 20.");
            Assert.DoesNotContain(sentences, ArchitectureConformance.DescribesTheReasonsChoosing);
        }

        // The words each surface of the switch night states in their place, worked from section 15.7's rows.
        var words = WebUtility.HtmlDecode(tonight);

        Assert.Contains("These names appear whether or not they are on the list.", words, StringComparison.Ordinal);
        Assert.Contains("Every name here passed the swing filter's five gates at a price its own chart made significant, and its gates say whether it pulled back to support or broke out; the reasons beside it are context.", words, StringComparison.Ordinal);
        Assert.Contains("Which reasons fired tonight, as context", words, StringComparison.Ordinal);
        Assert.Contains("Most of tonight's fired names carry the same reason, so the evening is one thing happening to many names.", words, StringComparison.Ordinal);
        Assert.Contains("every name that fired tonight is a setup nothing has scored yet", words, StringComparison.Ordinal);

        // The name page's word for a reason means what section 3's row says it means.
        var vocabulary = Assert.Single(ArchitectureTables.In(File.ReadAllText(Repository.Architecture)), table => table.Heading == "3. Vocabulary");
        var reason = Assert.Single(vocabulary.Body, row => row.Count > 1 && row[0] == "Reason")[1];

        Assert.Contains($"<dt>Reason</dt><dd>{reason}</dd>", WebUtility.HtmlDecode(name), StringComparison.Ordinal);
        Assert.Contains($"<dt>Reason</dt><dd>{reason}</dd>", WebUtility.HtmlDecode(export), StringComparison.Ordinal);

        // Shown to find what it looks for: the evening before the switch, listed by the reasons, says so,
        // drawn over the store read as one the filter never listed, since an evening before the filter's
        // first night is not drawn.
        store.Execute("DELETE FROM list_rule WHERE rule = 'filter';");

        var before = await client.GetStringAsync($"/screens/tonight/{BeforeTheSwitch}");

        Assert.Contains(SentencesOf(before), ArchitectureConformance.DescribesTheReasonsChoosing);
        Assert.Contains("Which reasons put tonight's names on the list", WebUtility.HtmlDecode(before), StringComparison.Ordinal);
    }

    // A page's words as sentences, read as section 12.9's scan reads the architecture: scripts, styles and
    // pictures taken out, each cell, paragraph and item its own boundary, and a defined word's term read
    // with its meaning.
    static IReadOnlyList<string> SentencesOf(string page)
    {
        var body = Regex.Replace(page, @"<(script|style|svg)\b.*?</\1>", " ", RegexOptions.Singleline);

        body = Regex.Replace(body, "</(td|th|p|li|dd|figcaption|div|h[1-6]|summary|caption|button|a)>", " |. ");

        var text = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]+>", " "));

        text = Regex.Replace(text, @"\s+", " ");

        return [.. Regex.Split(text, @"(?<=[.;])\s+").Where(sentence => sentence.Trim().Length > 2)];
    }

    [Fact]
    public async Task ANightNoNamePassedDrawsOneLineWithHowManyReachedEachGate()
    {
        using var store = new TemporaryStore().Migrated();

        // Worked by hand: eight members with the market open; six pass trend and strength, four of those
        // the setup, two of those the trigger and one of those the trade, and that one is excluded.
        Evening(store, TheSwitch, filter: true,
        [
            new Member("T0", Exclusions: ["suspect series"]),
            new Member("T1", Trade: false),
            new Member("T2", Trigger: false, Trade: false),
            new Member("T3", Trigger: false),
            new Member("T4", Setup: false),
            new Member("T5", Setup: false, Trigger: false),
            new Member("T6", Trend: false),
            new Member("T7", Trend: false, Setup: false),
        ]);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var list = Assert.Single(Blocks(page, "<section class=\"tonight-list\".*?</section>"));

        Assert.Contains("No name passed the swing filter tonight: 6 passed the trend and strength gate, 4 the setup, 2 the trigger and 1 the trade, and none of those past the exclusions.", list, StringComparison.Ordinal);
        Assert.Empty(DrawnTickers(list));
        Assert.Contains("data-listed=\"0\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachSurfaceReadsAnEveningByTheRuleThatListedIt()
    {
        using var store = SwitchStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var order = Passers().Select(member => member.Ticker).ToArray();

        // The name page on the switch night: Z00 passed, so it says why with the rule, its five gates
        // and the reason it fired as context, and its listing history names the rule of each evening.
        var z00 = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Z00/{TheSwitch}"));
        var why = Assert.Single(Blocks(z00, "<section class=\"why-it-is-here\".*?</section>"));

        Assert.Contains("data-rule=\"filter\" data-gates=\"5\"", why, StringComparison.Ordinal);
        Assert.Contains($"Z00 was {ListRules.ByFilter} on {TheSwitch}.", why, StringComparison.Ordinal);
        Assert.Contains($"As context, the reasons that fired on it that evening: {ShortlistSeries.Reasons[0]}.", why, StringComparison.Ordinal);
        Assert.Contains($"<tr data-evening=\"{TheSwitch}\" data-rule=\"filter\"", z00, StringComparison.Ordinal);
        Assert.Contains($"<tr data-evening=\"{BeforeTheSwitch}\" data-rule=\"reasons\"", z00, StringComparison.Ordinal);

        // Z41 fired three on the switch night and the filter did not pass it, so its page there says it is
        // not on the list, its reasons drawn nowhere as a why; the evening before, the reasons listed it,
        // and its page says why in their words.
        var notListed = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Z41/{TheSwitch}")), "<section class=\"why-it-is-here\".*?</section>"));

        Assert.Contains("data-reasons=\"0\"><p class=\"degraded\" data-listed=\"false\">", notListed, StringComparison.Ordinal);
        Assert.Contains("data-reasons=\"3\"", Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Z41/{BeforeTheSwitch}")), "<section class=\"why-it-is-here\".*?</section>")), StringComparison.Ordinal);

        // The walk on the switch night follows the filter's order.
        var fifth = order[4];
        var walked = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{fifth}/{TheSwitch}"));

        Assert.Contains($"data-previous=\"{order[3]}\" data-next=\"{order[5]}\"", walked, StringComparison.Ordinal);

        // The universe's last evening on the list: Z41 was last listed by the reasons before the switch,
        // and Z01, which passed the filter that evening without being listed, was last listed on the switch
        // night.
        var universe = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe"));

        Assert.Contains($"<td data-last-listed=\"{BeforeTheSwitch}\">", Assert.Single(Blocks(universe, "<tr data-ticker=\"Z41\".*?</tr>")), StringComparison.Ordinal);
        Assert.Contains($"<td data-last-listed=\"{TheSwitch}\">", Assert.Single(Blocks(universe, "<tr data-ticker=\"Z01\".*?</tr>")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageStatesHowManyOfTheNightsNamesWereOnTheListBeforeEachEveningByItsOwnRule()
    {
        using var store = new TemporaryStore().Migrated();

        // The night and the 21 evenings the store holds before it. Worked by hand: A, B and C pass on the
        // night. The evening before, the filter listed A and not B, which fired two; three evenings back
        // the reasons listed B; six back the reasons listed C, which the last five do not reach and the
        // last twenty do; and 21 back the reasons listed A, B and C, which the twenty do not reach. So of
        // the three: 1 the evening before, 2 over the last five, 3 over the last twenty.
        var night = new DateOnly(2026, 11, 30);

        string On(int back) => night.AddDays(-back).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (var back = 21; back >= 0; back--)
        {
            var members = new[] { "A", "B", "C", "D" }.Select(ticker => new Member(ticker)).ToArray();

            members = back switch
            {
                0 => [.. members.Select(member => member.Ticker == "D" ? member : member with { Passed = true, Rank = member.Ticker[0] - 'A' + 1 })],
                1 => [.. members.Select(member => member.Ticker switch { "A" => member with { Passed = true, Rank = 1 }, "B" => member with { Fired = 2 }, _ => member })],
                3 => [.. members.Select(member => member.Ticker == "B" ? member with { Fired = 1 } : member)],
                6 => [.. members.Select(member => member.Ticker == "C" ? member with { Fired = 1 } : member)],
                21 => [.. members.Select(member => member.Ticker == "D" ? member : member with { Fired = 1 })],
                _ => members,
            };

            Evening(store, On(back), filter: back <= 1, members);
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{On(0)}"));
        var overlap = Assert.Single(Blocks(page, "<section class=\"overlap\".*?</section>"));

        Assert.Contains("data-names=\"3\" data-last-night=\"1\" data-five=\"2\" data-twenty=\"3\"", overlap, StringComparison.Ordinal);

        // The funnel says the evening's passes are its list, on an evening the filter listed.
        Assert.Contains("on an evening the swing filter listed, the names passing are the list.", page, StringComparison.Ordinal);
        Assert.Contains("The names passing are this evening's list.", page, StringComparison.Ordinal);
        Assert.Contains($"Of the 3 name(s) on the list on {On(0)}: on the list the evening before, {On(1)}, 1; on it at least once over the last 5 evening(s) before it, 2; over the last 20, 3.", overlap, StringComparison.Ordinal);

        // With fewer than twenty evenings before it, the line says how many the store holds: six back the
        // reasons listed C alone, and the store holds 15 evenings before that one, C listed on the last. A
        // night before the filter's first is not drawn, so the store is read as one the filter never listed.
        Assert.Contains("<section class=\"before-the-record\"", await client.GetStringAsync($"/screens/run/{On(6)}"), StringComparison.Ordinal);

        store.Execute("DELETE FROM list_rule WHERE rule = 'filter';");

        var early = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{On(6)}"));

        Assert.Contains($"Of the 1 name(s) on the list on {On(6)}: on the list the evening before, {On(7)}, 0; on it at least once over the last 5 evening(s) before it, 0; over the last 15, 1, the store holding 15 evening(s) before it.", Assert.Single(Blocks(early, "<section class=\"overlap\".*?</section>")), StringComparison.Ordinal);
    }
}
