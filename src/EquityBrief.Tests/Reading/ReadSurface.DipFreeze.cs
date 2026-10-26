using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Dips;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.3: the short-term dip frozen on the S&P 400 of the universes' store, read back off the rendered pages:
// its card on Tonight drawn by its live rule, live since its freeze with its versions counted, its pick's why naming that
// rule, its exit in its own words and the line every frozen card draws from the answer its freeze followed; a version's
// tab drawing that version's own picks; and paper trading keeping an account for the live rule and one for each version,
// the family's tile and its versions' and its rule scorecard's pickers listing it.
// see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
// see: A dip rule is registered as the family on its index and kept by the dip's own step alone
public partial class ReadSurface
{
    // The S&P 400 dip's sweep of 22.2 as its run recorded it: the setting it would run, 546 trades at 0.024 after costs,
    // above nothing in 6 of 8 years, 0.033 without its five largest, 8 of 1,728 settings meeting the floors where luck
    // passes 249.8, and its tester's adjusted p of 0.0397, not passing.
    static readonly AnswerFigures MidDip = new(
        "drop=-3|trend=50|exit=65|limit=5|stop=3|market=5",
        546,
        0.024,
        YearsOf(0.01, 0.013, 0.02, 0.009, 0.03, 0.04, 0.013, -0.05),
        0.033,
        1728,
        8,
        249.8,
        "20261024T060946Z",
        0.0397,
        false);

    const string MidDipLive = "drop=-3,trend=50,exit=65,limit=5,stop=3,market=5,volume=0";

    // A query's first column, a row a value.
    static IReadOnlyList<string> Query(TemporaryStore store, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    static readonly string[] MidDipVersions =
    [
        "drop=-3,trend=200,exit=65,limit=5,stop=3,market=5,volume=0",
        "drop=-3,trend=50,exit=65,limit=5,stop=4,market=5,volume=0",
        "drop=-3,trend=50,exit=65,limit=5,stop=3,market=0,volume=0",
    ];

    // The universes' store with the S&P 400 dip's answer recorded and its family frozen the day before its night, or at
    // the instant given, the night drawn by its live rule: M2 listed at its close of 50 with its stop at 44, kept by the
    // live rule and by the version stopped at 4 typical moves.
    static async Task<(TemporaryStore Store, string[] Rules)> FrozenDipStore(DateTimeOffset? frozenAt = null)
    {
        var store = UniversesStore();

        RecordFigured(store, "20261024T060843Z", DipRule.Name, null, SweepAnswer.PassedWord, "2026-09-30T11:00:00Z", MidDip);

        var (code, said) = await FixtureExpectations.RegisterVerbAt(store, frozenAt ?? new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), RegisterVerb.IndexFamily, DipRule.Name, "--index", "MID", "--parameters", MidDipLive, "--variants", string.Join(";", MidDipVersions));

        Assert.True(code == 0, said);

        var rules = Query(store, "SELECT candidate FROM candidate_register WHERE evaluator = 'dip-400' ORDER BY id;").ToArray();
        var setting = new DipCandidate("MID").SettingOf(EquityBrief.Core.Candidates.CandidateEvaluator.Read(Query(store, "SELECT parameters FROM candidate_register WHERE evaluator = 'dip-400' ORDER BY id LIMIT 1;").Single()));

        store.Execute(
            "INSERT INTO dip_night (index_code, session_date, members, market, market_open, settings, passed, listed, fault) VALUES " +
            $"('MID', '{IndexNight}', 3, 'S&P 500 0.45', 1, '{DipFamily.Settings(setting, rules[0], "MID").Replace("'", "''", StringComparison.Ordinal)}', 1, 1, NULL);");
        store.Execute(
            "INSERT INTO dip_result (index_code, session_date, ticker, passed, place, entry, stop, time_limit, rsi, reason, gates) VALUES " +
            $"('MID', '{IndexNight}', 'M2', 1, 1, '50', '44', 5, 3.5, NULL, '{DipGatesPassed()}');");
        store.Execute(
            "INSERT INTO dip_pick (index_code, session_date, ticker, state, place, listed_under, held_index, held_family, held_night) VALUES " +
            $"('MID', '{IndexNight}', 'M2', 'listed', 1, NULL, NULL, NULL, NULL);");

        foreach (var (rule, stop) in new[] { (rules[0], "44"), (rules[2], "42") })
        {
            store.Execute(
                "INSERT INTO dip_trade (rule, index_code, ticker, session_date, place, entry, stop, time_limit, exit_average, risk_moves, end_reason) VALUES " +
                $"('{rule.Replace("'", "''", StringComparison.Ordinal)}', 'MID', 'M2', '{IndexNight}', 1, '50', '{stop}', 5, 65, 3, 'open');");
        }

        return (store, rules);
    }

    [Fact]
    public async Task AFrozenDipCardDrawsItsLiveRuleItsVersionsTabsAndTheLineItsRecordedAnswerGivesInTheReadersWords()
    {
        var (store, rules) = await FrozenDipStore();

        using (store)
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
            var dip = FamilyCardOf(page, DipRule.Name);

            // Live since its freeze with its three versions beside it, never not yet tested.
            Assert.Contains("data-state=\"live\" data-live-since=\"2026-09-30\" data-variants=\"3\"", dip, StringComparison.Ordinal);
            Assert.DoesNotContain(SetupFamilies.ProvisionalStatus, dip, StringComparison.Ordinal);

            // The line its recorded answer gives: fixed on the day of its freeze at the best setting its search found, its
            // trades and edge in dollars a hundred risked, and none beating luck or the tester's bar.
            Assert.Contains("data-frozen=\"2026-09-30\" data-run=\"20261024T060843Z\" data-answer=\"passed\"", dip, StringComparison.Ordinal);
            Assert.Contains("Its settings were fixed on 30 Sep 2026 at the best setting its search over settings found. Over the history it was tested on, its 546 trades came to about $2.40 for every $100 risked beyond the same plan on every stock after costs, or about $3.30 without its five largest results, and it came out ahead in 7 of the 8 years", dip, StringComparison.Ordinal);

            // M2's why names the index's live rule, read in the reader's words, its fall and its trend as that rule reads
            // them, and its exit on the RSI where a target would stand.
            var m2 = Assert.Single(Blocks(dip, "<tr data-ticker=\"M2\" data-card-pick=\"true\">.*?</tr>"));

            Assert.Contains("Its 2-day RSI closed at 3.5 after a fall of at least 3 typical days' moves over 3 trading days, above its 200-day average with its 50-day average above that with no report in the drop or before its time limit, every step of the short-term dip passing on the S&P 400's live setup. Bought at the next open, it is sold at the next open after its 2-day RSI closes back above 65 or a close under its stop, or at the close of its 5th trading day where neither came.", m2, StringComparison.Ordinal);
            Assert.Contains("<td class=\"r num plan-target\" data-target=\"none\">its 2-day RSI over 65</td>", m2, StringComparison.Ordinal);

            // The card's choice of rule: the live rule and its three versions, each version named by what it changes in the
            // fold of the versions being tested.
            Assert.Equal(4, Regex.Matches(Assert.Single(Blocks(dip, "<select id=\"rule-dip\".*?</select>")), "<option value=").Count);
            Assert.Contains("<details class=\"fold fam-versions\" data-versions=\"3\">", dip, StringComparison.Ordinal);

            // In the reader's words: no term of section 15.23's list reaches the page.
            Assert.Empty(Checks.PlainWords.FaultsOn(Checks.PlainWords.TonightPage, page, Checks.PlainWords.Terms()));

            // The version stopped at 4 typical moves chosen draws its own pick under the version's band.
            var chosen = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400&dip={RunScreen.Slug(rules[2])}"));
            var version = FamilyCardOf(chosen, DipRule.Name);

            Assert.Contains("A version being tested, not tonight's picks", version, StringComparison.Ordinal);
            Assert.Contains("M2", version, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PaperTradingKeepsAnAccountForTheFrozenDipRuleAndEachVersionAndItsPickersListTheFamily()
    {
        var (store, rules) = await FrozenDipStore();

        using (store)
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400&variants=dip&rule=dip"));

            // The family's tile its live rule's, after the swing families' and before the sector heavyweights'.
            var tiles = Regex.Matches(page, "<div class=\"paper-tile\" data-family=\"([^\"]+)\" data-standing=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)).ToArray();

            Assert.Contains(("dip", "live"), tiles);
            Assert.Equal("heavyweight", tiles[Array.IndexOf(tiles, ("dip", "live")) + 1].Item1);

            // Both pickers list it and choosing it in each chooses it.
            Assert.Contains("<option value=\"dip\" selected>", Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
            Assert.Contains("<option value=\"dip\" selected>", Regex.Match(page, "<select data-merge-key=\"variants\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);

            // An account for the live rule and one for each of its three versions, the version stopped at 4 typical moves
            // carrying its trade.
            var stopped = AccountRowOf(page, RunScreen.Slug(rules[2]));

            Assert.Contains("data-standing=\"live\"", AccountRowOf(page, ReadApi.PageList + "-" + DipRule.Name), StringComparison.Ordinal);
            Assert.Contains("data-standing=\"variant\"", stopped, StringComparison.Ordinal);
            Assert.Equal(3, Regex.Matches(page, "<tr data-account=\"[^\"]*s-p-400-dip-rule-at[^\"]*\"[^>]*data-standing=\"variant\"").Count);

            // From the 22.3 correction of 2026-10-25 an open dip trade's target reads none, since its plan holds no target
            // and its stop never trails, as Past picks reads it, where a trade whose stop trails reads that it trails.
            static string Bought(decimal? target, bool trails) => new MarkRenderer().PaperBought(
                new PaperPageView([], [new PaperBoughtView(ReadApi.PageList + "-" + DipRule.Name, DipRule.Name, "M2", null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), 50m, 44m, target, trails, 5, null, null, null, [], null)], [], [], new PaperNotBought([]), [], null),
                new PaperLink("400"));

            var dipRow = Assert.Single(Blocks(Bought(null, false), "<tr data-ticker=\"M2\".*?</tr>"));

            Assert.Contains("<td class=\"r\"><span class=\"degraded\">none</span></td><td class=\"r num\"><span class=\"degraded\">not stored</span>", dipRow, StringComparison.Ordinal);
            Assert.DoesNotContain("it trails", dipRow, StringComparison.Ordinal);
            Assert.Contains("<td class=\"r\">it trails</td>", Bought(null, true), StringComparison.Ordinal);
        }
    }
}
