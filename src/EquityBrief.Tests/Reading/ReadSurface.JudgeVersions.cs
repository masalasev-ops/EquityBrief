using System.Net;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 24.14 correction of 2026-11-21: each judge version's picks are bought and walked to their exits in an account of their
// own, an earlier version's drawn on the judge's page under its number with its account apart from the four, the newest
// version's alone the record from its first night, and no pick drawn without an account that buys it; read back off the
// page over a constructed store and worked by hand.
// see: Each picker version's picks are bought and walked in an account of its own, and the record counts the newest version's
public partial class ReadSurface
{
    internal static string[] JudgeVersionRows =>
    [
        CheckReach.Key("15.26 The AI stock picker", "An earlier version's picks"),
    ];

    // Version 1 picks AAA and DDD on the night of 19 Nov under the earlier prompt, AAA a week and DDD two, each with the whole
    // at the target; version 2 picks BBB on the night of 23 Nov under this one, a week. AAA opens at 101 on 20 Nov, its stop
    // 99 and target 105, closes at 106 on the 23rd and sells at the next open, 107: 3 risks. DDD and BBB stand at 100 every
    // session, each still inside its hold on the last.
    static TemporaryStore JudgeVersionStore(bool second)
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO judge_pin (id, reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at) VALUES "
            + $"(1, 1, 'm', 'f', 's', 1, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Earlier[0]}', '2026-11-20T01:00:00Z'), "
            + $"(2, 1, 'm', 'f', 's', 1, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Version}', '2026-11-24T01:00:00Z');");
        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail) VALUES "
            + "('judge-pick-1', '2026-11-19', 'v', 1, 20261119, 'picked', '2026-11-20T01:00:00Z', '2026-11-20T01:10:00Z', 4, 2, 2, 0, '')"
            + (second ? ", ('judge-pick-2', '2026-11-23', 'v', 2, 20261123, 'picked', '2026-11-24T01:00:00Z', '2026-11-24T01:10:00Z', 4, 1, 2, 0, '');" : ";"));
        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance, fields) VALUES "
            + $"('2026-11-19', 1, 'judge-pick-1', 'GSPC', 'AAA', '1 week', '{JudgeMenu.Whole}', 'Version one first.', '100', 2.0, '98', '104', NULL, NULL), "
            + $"('2026-11-19', 2, 'judge-pick-1', 'GSPC', 'DDD', '2 weeks', '{JudgeMenu.Whole}', 'Version one second.', '100', 2.0, '97', '106', NULL, NULL)"
            + (second ? $", ('2026-11-23', 1, 'judge-pick-2', 'GSPC', 'BBB', '1 week', '{JudgeMenu.Whole}', 'Version two.', '100', 2.0, '98', '104', NULL, 'rsi,depth,strength');" : ";"));
        store.Execute(
            "INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES "
            + "('GSPC', '2026-11-19', 'AAA', 'v', '{}', 'r', 's', '2026-11-20T01:00:00Z', 'f'), "
            + "('GSPC', '2026-11-19', 'DDD', 'v', '{}', 'r', 's', '2026-11-20T01:00:00Z', 'f'), "
            + "('GSPC', '2026-11-23', 'BBB', 'v', '{\"rsi\":\"50 of 100\",\"depth\":\"2%\",\"strength\":\"+1%\"}', 'r', 's', '2026-11-24T01:00:00Z', 'f');");

        string[] aaaOpens = ["100", "101", "104", "107", "107", "107", "107"];
        string[] aaaCloses = ["100", "103", "106", "107", "107", "107", "107"];
        var bars = new List<string>();

        for (var at = 0; at < JudgeSessions.Length; at++)
        {
            bars.Add($"('AAA', '{JudgeSessions[at]}', '{aaaOpens[at]}', '{aaaCloses[at]}', '{aaaOpens[at]}', '{aaaCloses[at]}', 1000, 'eod', '{JudgeSessions[at]}T22:00:00Z')");

            foreach (var flat in new[] { "BBB", "DDD" })
            {
                bars.Add($"('{flat}', '{JudgeSessions[at]}', '100', '100', '100', '100', 1000, 'eod', '{JudgeSessions[at]}T22:00:00Z')");
            }
        }

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", bars) + ";");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('AAA', '2026-11-19', 'atr14', 2.0, 14), ('DDD', '2026-11-19', 'atr14', 2.0, 14), ('BBB', '2026-11-23', 'atr14', 2.0, 14);");

        return store;
    }

    [Fact]
    public async Task AVersionsPicksKeepTheirOwnAccountWhenTheVersionChanges()
    {
        // Before version 2 runs, version 1 is the record: AAA's closed trade counts in it and nothing is drawn apart.
        using (var before = JudgeVersionStore(second: false))
        using (var beforeHost = new Host(before.Root))
        using (var beforeClient = beforeHost.CreateClient())
        {
            var page = WebUtility.HtmlDecode(await beforeClient.GetStringAsync("/screens/judge"));

            Assert.Contains("<b>1</b> trades closed<span>1 of 300 needed for a firm verdict</span>", page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-apart=", page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-card=\"judge-version-1\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-version=\"counted\"", page, StringComparison.Ordinal);
        }

        using var store = JudgeVersionStore(second: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var drawn = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge"));

        // Version 1 keeps its account once version 2 runs, by hand: AAA bought on 20 Nov at 101 and sold on 24 Nov at 107, 3
        // risks; DDD bought at 100, its stop 97, still open; one of two closed, a winner, $3.00 per $1 risked.
        var region = Blocks(drawn, "<section class=\"card\" data-card=\"judge-version-1\">.*?</section>").Single();

        Assert.Contains("<h2>Version 1, picked 19 Nov: its own account, apart from the record</h2>", region, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"AAA\"", region, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"DDD\"", region, StringComparison.Ordinal);

        var cards = Blocks(drawn, "<article class=\"home-idea judge-card\" data-ticker=\"[A-Z]+\">.*?</article>");
        var aaa = Assert.Single(cards, card => card.Contains("data-ticker=\"AAA\"", StringComparison.Ordinal));
        var ddd = Assert.Single(cards, card => card.Contains("data-ticker=\"DDD\"", StringComparison.Ordinal));

        Assert.Contains("<p class=\"judge-since\" data-since>Bought on Fri 20 Nov at $101.00 and sold on Tue 24 Nov at $107.00, making $3.00 per $1 risked, in version 1's own account.</p>", aaa, StringComparison.Ordinal);
        Assert.Contains("<span>Bought at</span><b>$101.00</b><span class=\"sale-note judge-buy\">At the open on Fri 20 Nov. Last close before it $100.00.</span>", aaa, StringComparison.Ordinal);
        Assert.Contains("<b class=\"stop\">$99.00</b><span class=\"sale-note judge-stop\">1 typical daily move under the price it paid.</span>", aaa, StringComparison.Ordinal);
        Assert.Contains("<b class=\"aim\">$105.00</b>", aaa, StringComparison.Ordinal);
        Assert.Contains("<b>The AI stock picker's pick, ranked 1 of 2 on the night of 19 Nov.</b>", aaa, StringComparison.Ordinal);
        Assert.Contains("Bought on Fri 20 Nov at $100.00 and still open in version 1's own account.", ddd, StringComparison.Ordinal);

        var summary = SinglePageApp.VersionSummaryWords(new JudgeVersionView(1, new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 19), [], new JudgeScoreboard(1, 1, 3.0, null, 300), []), 2);

        Assert.Equal("Version 1, picked 19 Nov: 0 picks, bought at the open on Fri 20 Nov in this version's own account and walked to their exits by their plans, the same money, sizing and costs as the record's. 0 of 0 bought so far. They are never added to version 2's record. 1 of its trades has closed, 1 of them winners, $3.00 made per $1 risked.", summary);
        Assert.Contains("Version 1, picked 19 Nov: 2 picks, bought at the open on Fri 20 Nov in this version's own account and walked to their exits by their plans, the same money, sizing and costs as the record's. 2 of 2 bought so far. They are never added to version 2's record. 1 of its trades has closed, 1 of them winners, $3.00 made per $1 risked.", drawn, StringComparison.Ordinal);

        // Its account drawn apart in the four accounts' card, labelled, and never added to the four.
        Assert.Contains("<div class=\"judge-apart\" data-apart=\"1\"><p class=\"judge-apart-h\"><b>Version 1, picked 19 Nov, apart from the record.</b>", drawn, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Version 1, picked 19 Nov's own account\"", drawn, StringComparison.Ordinal);
        Assert.Contains("each from version 2's first night.", drawn, StringComparison.Ordinal);

        // The record is version 2's alone from 23 Nov: BBB bought and open, none closed, and the page says so.
        Assert.Contains("<b>0</b> trades closed<span>0 of 300 needed for a firm verdict</span>", drawn, StringComparison.Ordinal);
        Assert.Contains(SinglePageApp.CountedFromWords(new DateOnly(2026, 11, 23), 2), drawn, StringComparison.Ordinal);
        Assert.Equal("Its record counts version 2's picks alone, from the night of 23 Nov 2026. Each earlier version's picks are bought and walked to their exits in an account of their own, drawn apart under its number and never added to the record.", SinglePageApp.CountedFromWords(new DateOnly(2026, 11, 23), 2));
        Assert.Contains("The AI stock picker's picks tonight on the S&P 500 (1)", drawn, StringComparison.Ordinal);

        // No pick is drawn without an account that buys it: each card is tonight's under the record, or an earlier version's
        // saying what its account did, and every pick stored is drawn.
        var since = cards.Where(card => card.Contains("data-since", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, cards.Count);
        Assert.Equal(2, since.Length);
        Assert.All(since, card => Assert.Contains("version 1's own account", card, StringComparison.Ordinal));
        Assert.Single(cards, card => card.Contains("data-ticker=\"BBB\"", StringComparison.Ordinal) && !card.Contains("data-since", StringComparison.Ordinal));
    }

    [Fact]
    public void HomeOnANightAnEarlierVersionPickedNamesThatVersionsAccount()
    {
        var earlier = new JudgeVersionView(1, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), [], new JudgeScoreboard(0, 0, null, null, 300), []);
        var view = new JudgeView(new DateOnly(2026, 11, 20), "picked", "", [new JudgeCardView(1, 1, "AAA", null, null, null, "r", 100m, 98m, 104m, null, "1 week", JudgeMenu.Whole)], new JudgeScoreboard(0, 0, null, null, 300), JudgeScore.NotToldApart, null, [], [], [], null, 0) { PicksFrom = earlier, RecordNumber = 2 };

        Assert.Contains("<b>AI stock picker</b> Picked 1 stock that night in the account of Version 1, picked 20 Nov, kept apart from its record. No trade of it has closed yet.", WebUtility.HtmlDecode(SinglePageApp.JudgeHomeLine(view, past: true)), StringComparison.Ordinal);
        Assert.Contains("Picked 1 stock tonight in its own paper account.", SinglePageApp.JudgeHomeLine(view with { PicksFrom = null }), StringComparison.Ordinal);
        Assert.Equal("Version 1, picked 20 Nov to 27 Nov", SinglePageApp.VersionWords(earlier with { Last = new DateOnly(2026, 11, 27) }));
    }

    // No source a rule's version pins is one the correction writes, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneThePickersVersionAccountsWrite()
    {
        string[] written =
        [
            "src/EquityBrief.Api/Reading/JudgeScreen.cs",
            "src/EquityBrief.Api/Reading/ReadApi.Judge.cs",
            "src/EquityBrief.Web/App/SinglePageApp.Judge.cs",
            "src/EquityBrief.Web/App/Stylesheet.cs",
        ];
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.PlanWalkSources)
            .Concat(EquityBrief.Worker.Rules.RuleVersionScorer.CodeVersionSources)
            .Concat(EquityBrief.Worker.Filter.SwingFilter.CodeVersionSources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(written, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(written.Intersect(pinned, StringComparer.Ordinal));
    }
}
