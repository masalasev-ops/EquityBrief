using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.9: the Loop page's presses and what the page draws of the operator's word, read off the rendered
// pages over constructed stores. Approve and decline write one decision row each under the page's own header and refuse
// what the store cannot hold or no apply could apply, with nothing written; the page draws the presses on the newest
// run's passing proposals, each decision with what the apply did, the settings approvals stored and the live alarm with
// its restore; and a change approved on the S&P 600 and applied is read on the S&P 600's cards alone.
// see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
// see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
// see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
public partial class ReadSurface
{
    // The Loop page's three regions, Tonight's line and the card's row this check reaches.
    internal static readonly string[] ApprovalPageClaims =
    [
        CheckReach.Key(Scope.LoopPage, Scope.LoopWord),
        CheckReach.Key(Scope.LoopPage, Scope.LoopDecided),
        CheckReach.Key(Scope.LoopPage, Scope.LoopAlarm),
        CheckReach.Key("15.7 Tonight", "The live alarm's line"),
        CheckReach.Key(Scope.CardPage, Scope.CardApproved),
    ];

    const string SmallOctober = "loop-test-SML-20261009T120000Z";

    const string SmallSeptember = "loop-test-SML-20260905T120000Z";

    const string LargeOctober = "loop-test-GSPC-20261009T120000Z";

    static string Exit(int exit) => LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExitParameter] = exit }).Json;

    static string Condition(string column, double level) => LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [$"also_{column}_above"] = level }).Json;

    static void Run(TemporaryStore store, string run, string index, string month) =>
        store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{run}', '{month}', '{index}', '2026-10-08', '2026-10-09T12:00:00Z', '2026-10-09T12:01:00Z', 5);");

    static void Proposed(TemporaryStore store, string run, string index, string family, string proposal, bool passed, string? change, int blocks = 19) =>
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change) "
            + FormattableString.Invariant($"VALUES ('{run}', '{index}', '{family}', '{Literal(proposal)}', 'the change', 'the {family} rule today', 'risks', 400, {blocks}, 0.001, 1, 1, 4, 3, 1.5, 1, 0.1, 3, {(passed ? 1 : 0)}, NULL, {(change is null ? "NULL" : $"'{change}'")});"));

    static void Decided(TemporaryStore store, string run, string index, string family, string proposal, string decision, string? reason = null) =>
        store.Execute($"INSERT INTO loop_decision (run_id, index_code, family, proposal, decision, reason, decided_at) VALUES ('{run}', '{index}', '{family}', '{Literal(proposal)}', '{decision}', {(reason is null ? "NULL" : $"'{reason}'")}, '2026-10-09T15:00:00Z');");

    // A text inside a quoted SQL literal, its apostrophes doubled.
    static string Literal(string text) => text.Replace("'", "''", StringComparison.Ordinal);

    static string Decide(string index, string run, string family) => $"{SinglePageApp.LoopDecidePostRoute}{index}/{run}/{family}";

    [Fact]
    public async Task TheLoopPagesPressesWriteOneDecisionUnderThePagesHeaderAndRefuseWhatTheStoreCannotHoldOrNoApplyCouldApply()
    {
        using var store = new TemporaryStore().Migrated();

        // The S&P 600's September and October runs, October's the newest, and the S&P 500's October run. September's two
        // pullback conditions, each declined over 19 blocks; October's, the first read over 19 blocks again and the second
        // over 20.
        Run(store, SmallSeptember, "SML", "2026-09");
        Run(store, SmallOctober, "SML", "2026-10");
        Run(store, LargeOctober, "GSPC", "2026-10");
        Proposed(store, SmallSeptember, "SML", "pullback", "winners against losers, ranked 1", true, Condition("rsi", 60));
        Proposed(store, SmallSeptember, "SML", "pullback", "winners against losers, ranked 2", true, Condition("depth", 2));
        Proposed(store, SmallSeptember, "SML", "breakout", "the autopsy's exit, ranked 1", true, Exit(7));
        Decided(store, SmallSeptember, "SML", "pullback", "winners against losers, ranked 1", LoopDecisions.Declined, "too young");
        Decided(store, SmallSeptember, "SML", "pullback", "winners against losers, ranked 2", LoopDecisions.Declined, "too young");
        Proposed(store, SmallOctober, "SML", "breakout", "the autopsy's exit, ranked 1", true, Exit(7));
        Proposed(store, SmallOctober, "SML", "breakout", "the autopsy's exit, ranked 2", true, Exit(8));
        Proposed(store, SmallOctober, "SML", "drift", "the autopsy's exit, ranked 1", false, Exit(7));
        Proposed(store, SmallOctober, "SML", "drift", "the grid", true, null);
        Proposed(store, SmallOctober, "SML", "heavyweight", "the autopsy's exit, ranked 1", true, new LoopChange(null, "exit=break", null, LoopChange.NoHooks).Json);
        Proposed(store, SmallOctober, "SML", "pullback", "winners against losers, ranked 1", true, Condition("rsi", 60), blocks: 19);
        Proposed(store, SmallOctober, "SML", "pullback", "winners against losers, ranked 2", true, Condition("depth", 2), blocks: 20);
        Proposed(store, LargeOctober, "GSPC", "drift", "the autopsy's exit, ranked 1", true, Exit(7));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<(HttpStatusCode Status, string Said)> Press(string index, string run, string family, string proposal, string decision, string? reason = null, bool fromThePage = true) =>
            await CardPress(client, Decide(index, run, family), [("proposal", proposal), ("decision", decision), .. reason is null ? Array.Empty<(string, string)>() : [("reason", reason)]], fromThePage);

        // A press without the page's own header is refused and writes nothing.
        Assert.Equal(HttpStatusCode.Forbidden, (await Press("SML", SmallOctober, "breakout", "the autopsy's exit, ranked 1", "approve", fromThePage: false)).Status);

        // The breakout's first exit, the one the run puts to you of its two that passed at one adjusted p-value, settled
        // by the name, approved and a second word on it refused; its second refused as a second change to the family in
        // the run, a decline of it refused for wanting its reason and, given one, from 17.10 refused as not put to you.
        Assert.Equal(HttpStatusCode.OK, (await Press("SML", SmallOctober, "breakout", "the autopsy's exit, ranked 1", "approve")).Status);
        Assert.Contains("already holds your word", (await Press("SML", SmallOctober, "breakout", "the autopsy's exit, ranked 1", "decline", "a second word")).Said, StringComparison.Ordinal);
        Assert.Contains("one change a family a run is applied", (await Press("SML", SmallOctober, "breakout", "the autopsy's exit, ranked 2", "approve")).Said, StringComparison.Ordinal);
        Assert.Contains("a decline records its reason", (await Press("SML", SmallOctober, "breakout", "the autopsy's exit, ranked 2", "decline")).Said, StringComparison.Ordinal);
        Assert.Contains(
            "this run puts the family's strongest proposal that passed to you, the autopsy's exit, ranked 1, and no other",
            (await Press("SML", SmallOctober, "breakout", "the autopsy's exit, ranked 2", "decline", "the first is enough")).Said,
            StringComparison.Ordinal);

        // Refused: a proposal that did not pass, one stating no change, a book's, an S&P 500 rule's, a run that is not the
        // newest and a proposal the run does not hold.
        Assert.Contains("did not pass the tester", (await Press("SML", SmallOctober, "drift", "the autopsy's exit, ranked 1", "approve")).Said, StringComparison.Ordinal);
        Assert.Contains("states no change", (await Press("SML", SmallOctober, "drift", "the grid", "approve")).Said, StringComparison.Ordinal);
        Assert.Contains(LoopDecisions.BookRefused, (await Press("SML", SmallOctober, "heavyweight", "the autopsy's exit, ranked 1", "approve")).Said, StringComparison.Ordinal);
        Assert.Contains(LoopDecisions.LargeIndexRefused, (await Press("GSPC", LargeOctober, "drift", "the autopsy's exit, ranked 1", "approve")).Said, StringComparison.Ordinal);
        Assert.Contains("a newer tester run stands", (await Press("SML", SmallSeptember, "breakout", "the autopsy's exit, ranked 1", "approve")).Said, StringComparison.Ordinal);
        Assert.Contains("holds no proposal", (await Press("SML", SmallOctober, "drift", "an exit no run proposed", "approve")).Said, StringComparison.Ordinal);

        // The pullback's first condition, declined over 19 blocks and read over 19 again, is not put again; its second, read
        // over 20, is put again, the one the run puts to you though the first comes before it by name, and approved.
        Assert.Contains("declined this change over 19 blocks, and this run reads 19", (await Press("SML", SmallOctober, "pullback", "winners against losers, ranked 1", "approve")).Said, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Press("SML", SmallOctober, "pullback", "winners against losers, ranked 2", "approve")).Status);

        // October's rows are the two words given, each once.
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT family, proposal, decision, IFNULL(reason, 'none') FROM loop_decision WHERE run_id = '{SmallOctober}' ORDER BY family, proposal;";

        using var reader = command.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}");
        }

        Assert.Equal(
            [
                "breakout|the autopsy's exit, ranked 1|approved|none",
                "pullback|winners against losers, ranked 2|approved|none",
            ],
            rows);
    }

    [Fact]
    public async Task TheLoopPageDrawsThePressesEachDecisionWithWhatTheApplyDidTheSettingsStoredAndTheLiveAlarmWithItsRestore()
    {
        using var store = new TemporaryStore().Migrated();

        // October's S&P 600 run: a breakout exit undecided, a drift exit approved and applied as change 1, a pullback
        // condition declined, a heavyweights exit a book cannot take and a failing drift exit; and the S&P 500's run with
        // a passing drift exit.
        Run(store, SmallOctober, "SML", "2026-10");
        Run(store, LargeOctober, "GSPC", "2026-10");
        Proposed(store, SmallOctober, "SML", "breakout", "the autopsy's exit, ranked 1", true, Exit(7));
        Proposed(store, SmallOctober, "SML", "breakout", "the autopsy's exit, ranked 2", true, Exit(10));
        Proposed(store, SmallOctober, "SML", "drift", "the autopsy's exit, ranked 1", true, Exit(8));
        Proposed(store, SmallOctober, "SML", "drift", "the autopsy's exit, ranked 2", false, Exit(9));
        Proposed(store, SmallOctober, "SML", "pullback", "winners against losers, ranked 1", true, Condition("rsi", 60));
        Proposed(store, SmallOctober, "SML", "heavyweight", "the autopsy's exit, ranked 1", true, new LoopChange(null, "exit=break", null, LoopChange.NoHooks).Json);
        Proposed(store, LargeOctober, "GSPC", "drift", "the autopsy's exit, ranked 1", true, Exit(7));
        Decided(store, SmallOctober, "SML", "drift", "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        Decided(store, SmallOctober, "SML", "pullback", "winners against losers, ranked 1", LoopDecisions.Declined, "too young");
        store.Execute($"INSERT INTO loop_applied (run_id, index_code, family, proposal, applied_at, outcome, words) VALUES ('{SmallOctober}', 'SML', 'drift', 'the autopsy''s exit, ranked 1', '2026-10-09T23:35:00Z', 'applied', 'the family stands from the next night at exit: the eighth');");
        store.Execute($"INSERT INTO provisional_setting (id, index_code, family, change, words, set_at, run_id, proposal) VALUES (1, 'SML', 'drift', '{Exit(8)}', 'exit: the eighth', '2026-10-09T23:35:00Z', '{SmallOctober}', 'the autopsy''s exit, ranked 1');");

        // The drift's alarm: July under its low, August under it again and flagged.
        store.Execute(
            "INSERT INTO loop_alarm (index_code, family, period, trades, edge, edge_floor, counted, under, streak, flagged, reference, run_id) VALUES " +
            $"('SML', 'drift', '2026-07-01', 6, -0.4, -0.1, 1, 1, 1, 0, '{SmallOctober}', 'night-1'), ('SML', 'drift', '2026-08-01', 5, -0.3, -0.1, 1, 1, 2, 1, '{SmallOctober}', 'night-2');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600"));

        // The breakout's exit, passing and undecided on the newest run, carries both presses to its own route.
        var breakout = Assert.Single(Blocks(page, "<div class=\"loop-decision\" data-loop-key=\"breakout\\|the autopsy's exit, ranked 1\">.*?</div>"));

        Assert.Contains($"<form class=\"loop-press\" method=\"post\" action=\"{Decide("SML", SmallOctober, "breakout")}\" data-loop-key=\"breakout|the autopsy's exit, ranked 1\"><input type=\"hidden\" name=\"proposal\" value=\"the autopsy's exit, ranked 1\"><input type=\"hidden\" name=\"decision\" value=\"approve\"><button type=\"submit\">Approve</button></form>", breakout, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" name=\"decision\" value=\"decline\"><label>Reason <input name=\"reason\" required></label> <button type=\"submit\">Decline</button>", breakout, StringComparison.Ordinal);

        // From 17.10 its second exit, passing at the same adjusted p-value and after the first by name, is not put to you:
        // it says which is and carries no press.
        var second = Assert.Single(Blocks(page, "<div class=\"loop-decision\" data-loop-key=\"breakout\\|the autopsy's exit, ranked 2\">.*?</div>"));

        Assert.Contains("<p class=\"degraded\" data-decision=\"not-put\">Not put to you: this run puts the family's strongest proposal that passed to you, the autopsy's exit, ranked 1.</p>", second, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"loop-press\"", second, StringComparison.Ordinal);

        // The drift's approval with what the apply did; the pullback's decline with its reason; the book's exit not
        // offered, saying why; the failing exit drawing no word.
        Assert.Contains("<p class=\"loop-decided\" data-decision=\"approved\" data-outcome=\"applied\"><b>Approved</b> on 2026-10-09. Applied on 2026-10-09: the family stands from the next night at exit: the eighth.</p>", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"loop-decided\" data-decision=\"declined\"><b>Declined</b> on 2026-10-09: too young. It changes nothing.</p>", page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"degraded\" data-decision=\"not-offered\">Approve is not offered here: {LoopDecisions.BookRefused}.</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-loop-key=\"drift|the autopsy's exit, ranked 2\"", page, StringComparison.Ordinal);

        // Your decisions, newest first, and the setting the approval stored.
        Assert.Contains("<li data-setting=\"1\" data-family=\"drift\">The drift, change 1 on 2026-10-09: exit: the eighth, from the autopsy's exit, ranked 1 of run loop-test-SML-20261009T120000Z.</li>", page, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(page, "<tr data-run=\"loop-test-SML-20261009T120000Z\" data-family=\"[a-z]+\" data-decision=\"[a-z]+\"").Count);
        Assert.Contains("<td>approved</td><td>applied on 2026-10-09: the family stands from the next night at exit: the eighth</td>", page, StringComparison.Ordinal);

        // The alarm: the drift flagged on August, two counted months running, with the restore of change 1 waiting.
        var alarm = Assert.Single(Blocks(page, "<div class=\"loop-alarm\" data-family=\"drift\".*?</table></div></div>"));

        Assert.Contains("data-flagged=\"1\" data-periods=\"2\"", alarm, StringComparison.Ordinal);
        Assert.Contains("<b>The drift rule is flagged.</b> Its edge after costs stood under its reference's low in 2 counted months running, the newest 2026-08. The flag changes nothing by itself.", alarm, StringComparison.Ordinal);
        Assert.Contains($"<form class=\"loop-press\" method=\"post\" action=\"{SinglePageApp.LoopRestorePostRoute}SML/drift/1\" data-loop-key=\"drift|{LoopDecisions.RestoreProposal(1)}\"><button type=\"submit\">Approve the restore</button></form>", alarm, StringComparison.Ordinal);
        Assert.Contains("<tr data-period=\"2026-08-01\" data-units=\"5\" data-counted=\"1\" data-under=\"1\" data-streak=\"2\" data-reference=\"loop-test-SML-20261009T120000Z\"><td class=\"num\">2026-08</td><td class=\"r num\">5</td><td class=\"r num\">-0.30 risks</td><td class=\"r num\">-0.10 risks</td><td>under the low, 2 running</td></tr>", alarm, StringComparison.Ordinal);

        // A restore pressed: refused without the header, then written once; the page then says it is waiting.
        Assert.Equal(HttpStatusCode.Forbidden, (await CardPress(client, $"{SinglePageApp.LoopRestorePostRoute}SML/drift/1", [], fromThePage: false)).Status);
        Assert.Contains("restored before the next night", (await CardPress(client, $"{SinglePageApp.LoopRestorePostRoute}SML/drift/1", [])).Said, StringComparison.Ordinal);
        Assert.Contains("already holds your word", (await CardPress(client, $"{SinglePageApp.LoopRestorePostRoute}SML/drift/1", [])).Said, StringComparison.Ordinal);
        Assert.Contains("data-restore=\"waiting\"><b>Restore approved</b> on ", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600")), StringComparison.Ordinal);

        // The S&P 500's passing exit offers no press, saying why.
        var large = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500"));

        Assert.Contains($"<p class=\"degraded\" data-decision=\"not-offered\">Approve is not offered here: {LoopDecisions.LargeIndexRefused}.</p>", large, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"loop-press\"", large, StringComparison.Ordinal);

        // A family the alarm flags on the index is named above its Tonight, and none on another index.
        var notice = WebUtility.HtmlDecode(SinglePageApp.LoopAlarmNotice(Universes.Of("600"), [("drift", new DateOnly(2026, 8, 1), 2)]));

        Assert.Contains("<b>The live alarm flags the drift rule on the S&P 600.</b> Its edge after costs stood under its reference's low 2 counted periods running, the newest from 2026-08-01.", notice, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/loop?universe=600\">the Loop page</a>", notice, StringComparison.Ordinal);
        Assert.Empty(SinglePageApp.LoopAlarmNotice(Universes.Of("400"), []));
    }

    [Fact]
    public async Task AChangeApprovedOnTheSAndP600IsReadOnItsCardsAloneAndTheSAndP400sAndTheSAndP500sStandAsTheyWere()
    {
        using var store = UniversesStore();

        Run(store, SmallOctober, "SML", "2026-10");
        Proposed(store, SmallOctober, "SML", "breakout", "the autopsy's exit, ranked 1", true, Exit(7));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var mid = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var large = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}"));

        // The operator approves the S&P 600's breakout exit, and the apply before the next night writes its setting.
        Assert.Equal(HttpStatusCode.OK, (await CardPress(client, Decide("SML", SmallOctober, "breakout"), [("proposal", "the autopsy's exit, ranked 1"), ("decision", "approve")])).Status);

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 9, 23, 35, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Assert.Equal(1, (await new LoopApply(clock, store.DatabaseFile, null).RunAsync("night-apply")).Applied);

        // The S&P 600's night stores its settings with the change, as the index families' step writes them; the S&P 400's
        // as before.
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            var small = await IndexFamilies.StoredSettingsAsync(connection, "SML", CancellationToken.None);

            store.Execute(
                "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
                $"('SML', '{IndexNight}', 2, 0.6, 1, '{IndexFamilies.Settings("SML", null, small).Replace("'", "''", StringComparison.Ordinal)}', 0);");
            Assert.Empty(await IndexFamilies.StoredSettingsAsync(connection, "MID", CancellationToken.None));
        }

        // Read back off the rendered pages: the S&P 600's breakout card states the change; the S&P 400's and the S&P 500's
        // pages are drawn as before it, byte for byte.
        var small600 = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=600"));
        var stated = Assert.Single(Blocks(small600, "<p class=\"lede\">[^<]*approved on the Loop page[^<]*</p>"));

        Assert.StartsWith("<p class=\"lede\">A stock closes above its highest price of the 126 sessions before", stated, StringComparison.Ordinal);
        Assert.Contains("lists close while the S&P 600's own breadth is under 45%; each trade's result is read after the published spread, half paid at each end. It stands at a change approved on the Loop page: exit: ", stated, StringComparison.Ordinal);
        Assert.Equal(mid, WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")));
        Assert.Equal(large, WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}")));
        Assert.Contains($"<p class=\"lede\">{MidBreakoutWords}</p>", mid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALoopPageWhoseEveryProposalFailedDrawsEveryFamilysCardAndOffersNoPress()
    {
        using var store = new TemporaryStore().Migrated();

        Run(store, SmallOctober, "SML", "2026-10");

        foreach (var family in new[] { "pullback", "breakout", "drift", "heavyweight" })
        {
            Proposed(store, SmallOctober, "SML", family, "the autopsy's exit, ranked 1", false, Exit(7));
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600"));

        foreach (var family in SinglePageApp.LoopFamilies)
        {
            Assert.Contains($"data-card=\"loop-{family}\"", page, StringComparison.Ordinal);
            Assert.Contains($"<p class=\"loop-rule\" data-family=\"{family}\" data-rule=\"the {family} rule today\">", page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("class=\"loop-press\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-loop-key=", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ACardWhoseRuleStandsAtAnApprovedChangeSaysItsRecordIsNotThisRulesAndDrawsNone()
    {
        var card = new DecisionCardView("SML", new DateOnly(2026, 10, 2), "breakout", "S1", "The breakout on the S&P 600", 50m, 47m, null, [], null, Approved: "exit: the seventh");
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().DecisionCard(card));

        Assert.Contains($"<section class=\"card-record outline\" data-trades=\"none\" data-approved=\"exit: the seventh\"><h5>The record of the breakout on the S&P 600</h5><p class=\"degraded\">{MarkRenderer.ApprovedRecord("exit: the seventh")}</p></section>", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("Not replayed yet", drawn, StringComparison.Ordinal);
    }
}
