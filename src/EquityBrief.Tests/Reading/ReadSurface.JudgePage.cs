using System.Globalization;
using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 24.6's AI judge page: its scoreboard, its four accounts' chart, its cards in the words of the rule on the open, where it
// looked and a replayed night apart, read back off the page over a constructed store; the verdict against random picks and
// the three groups worked by hand; and Home's line of it.
// see: No line states or implies the price a stock will open at, and a buy at the open buys at whatever price it opens
public partial class ReadSurface
{
    internal static string[] JudgePageRows =>
    [
        CheckReach.Key("15.26 The AI judge", "The judge's scoreboard"),
        CheckReach.Key("15.26 The AI judge", "The four accounts' chart"),
        CheckReach.Key("15.26 The AI judge", "The judge's picks tonight"),
        CheckReach.Key("15.26 The AI judge", "Where the AI looked"),
        CheckReach.Key("15.26 The AI judge", "Replayed nights"),
        CheckReach.Key("15.24 Home", "The AI judge's line"),
    ];

    static readonly string[] JudgeSessions = ["2026-11-19", "2026-11-20", "2026-11-23", "2026-11-24", "2026-11-25", "2026-11-26", "2026-11-27"];

    // A night of 19 Nov: AAA picked first, a week with the whole at the target, its close 100 and typical move 2; BBB second,
    // two weeks with half then the give-up price, the same close and move; CCC and DDD offered and not picked; and a replayed
    // night of 12 Nov picking EEE apart from the live record. AAA opens the next session at 101, so a week puts up 2 under it,
    // a stop of 99 and a target of 105; a close of 106 on the 23rd sells at the next open, 107, 3 risks. The others stand at 100.
    static TemporaryStore JudgePageStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail) VALUES "
            + "('judge-pick-1', '2026-11-19', 'v', 1, 20261119, 'picked', '2026-11-20T01:00:00Z', '2026-11-20T01:10:00Z', 4, 2, 2, 0, ''), "
            + "('judge-replay-1', '2026-11-12', 'v', 1, 20261112, 'picked', '2026-11-20T02:00:00Z', '2026-11-20T02:10:00Z', 4, 1, 2, 0, '');");
        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance) VALUES "
            + $"('2026-11-19', 1, 'judge-pick-1', 'GSPC', 'AAA', '1 week', '{JudgeMenu.Whole}', 'Rising estimates and a breakout on heavy volume.', '100', 2.0, '98', '104', NULL), "
            + $"('2026-11-19', 2, 'judge-pick-1', 'SML', 'BBB', '2 weeks', '{JudgeMenu.HalfThenGiveUp}', 'Fell hard on light volume while its business held.', '100', 2.0, '97', '106', '3'), "
            + $"('2026-11-12', 1, 'judge-replay-1', 'GSPC', 'EEE', '1 week', '{JudgeMenu.Whole}', 'A replayed pick.', '50', 1.0, '49', '52', NULL);");
        store.Execute(
            "INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES "
            + "('GSPC', '2026-11-19', 'AAA', 'v', '{\"sector\":\"Industrials\",\"size\":\"large\"}', 'r', 's', '2026-11-20T01:00:00Z', 'f'), "
            + "('SML', '2026-11-19', 'BBB', 'v', '{\"sector\":\"Health Care\",\"size\":\"mid-size\"}', 'r', 's', '2026-11-20T01:00:00Z', 'f'), "
            + "('GSPC', '2026-11-19', 'CCC', 'v', '{}', 'r', 's', '2026-11-20T01:00:00Z', 'f'), "
            + "('GSPC', '2026-11-19', 'DDD', 'v', '{}', 'r', 's', '2026-11-20T01:00:00Z', 'f');");
        store.Execute("INSERT INTO listed_name (ticker, name, read_on, run_id) VALUES ('AAA', 'Alpha Corp', '2026-11-15', 'r'), ('BBB', 'Beta Inc', '2026-11-15', 'r');");

        string[] aaaOpens = ["100", "101", "104", "107", "107", "107", "107"];
        string[] aaaCloses = ["100", "103", "106", "107", "107", "107", "107"];
        var bars = new List<string>();

        for (var at = 0; at < JudgeSessions.Length; at++)
        {
            bars.Add($"('AAA', '{JudgeSessions[at]}', '{aaaOpens[at]}', '{aaaCloses[at]}', '{aaaOpens[at]}', '{aaaCloses[at]}', 1000, 'eod', '{JudgeSessions[at]}T22:00:00Z')");

            foreach (var flat in new[] { "BBB", "CCC", "DDD" })
            {
                bars.Add($"('{flat}', '{JudgeSessions[at]}', '100', '100', '100', '100', 1000, 'eod', '{JudgeSessions[at]}T22:00:00Z')");
            }
        }

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", bars) + ";");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('AAA', '2026-11-19', 'atr14', 2.0, 14), ('BBB', '2026-11-19', 'atr14', 2.0, 14), ('CCC', '2026-11-19', 'atr14', 2.0, 14), ('DDD', '2026-11-19', 'atr14', 2.0, 14);");

        return store;
    }

    [Fact]
    public async Task TheJudgesPageDrawsItsScoreboardChartCardsAndWhereItLookedWithAReplayedNightApart()
    {
        using var store = JudgePageStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge"));

        // The scoreboard, by hand: AAA closed at 3 risks, BBB open; one trade closed of the 300, every closed one a winner,
        // $3.00 per $1 risked; and the same week bought on the four members made 3 on AAA and nothing on the three standing
        // still, a mean of 0.75, so AAA made $2.25 more.
        Assert.Contains("<b>1</b> trades closed<span>1 of 300 needed for a firm verdict</span>", page, StringComparison.Ordinal);
        Assert.Contains("<b>100%</b> were winners", page, StringComparison.Ordinal);
        Assert.Contains("<b>$3.00</b> made per $1 risked", page, StringComparison.Ordinal);
        Assert.Contains("<b>$2.25</b> more per $1 than the same plans bought on every stock those nights", page, StringComparison.Ordinal);
        Assert.Contains("against random picks so far.", page, StringComparison.Ordinal);

        // The chart: the judge's line and random picks' middle, with the band, and the line on a replayed night.
        Assert.Contains("<polygon class=\"judge-band\"", page, StringComparison.Ordinal);
        Assert.Contains("AI judge: ", page, StringComparison.Ordinal);
        Assert.Contains("Random picks: the middle of 1,000 random accounts: ", page, StringComparison.Ordinal);
        Assert.Contains(SinglePageApp.JudgeReplayedLine, page, StringComparison.Ordinal);

        // The cards, in the words of the rule on the open, AAA first.
        Assert.Contains("The AI judge's picks tonight (2)", page, StringComparison.Ordinal);
        Assert.Contains("Alpha Corp <span class=\"tag\">AI judge</span> <span class=\"tag\">Not one of the rules' buys</span>", page, StringComparison.Ordinal);
        Assert.Contains("<b>Why the AI picked it.</b> Rising estimates and a breakout on heavy volume.", page, StringComparison.Ordinal);
        Assert.Contains("It saw: Industrials, a large company. No name, ticker or date. Ranked 1 of 2 tonight.", page, StringComparison.Ordinal);
        Assert.Contains("Buys at the next open, at whatever price it opens. Last close $100.00. The prices below are worked from the last close until it is bought, then from the price it paid.", page, StringComparison.Ordinal);
        Assert.Contains("Sell if it closes below $98.00, 1 typical daily move under its buy price.", page, StringComparison.Ordinal);
        Assert.Contains("Take profit at $104.00, 2 times the risk: $4.00 up for $2.00 down.", page, StringComparison.Ordinal);
        Assert.Contains("Otherwise sell after 1 week.", page, StringComparison.Ordinal);
        Assert.Contains("It saw: Health Care, a mid-size company. No name, ticker or date. Ranked 2 of 2 tonight.", page, StringComparison.Ordinal);
        Assert.Contains("Sell if it closes below $97.00, 1.5 typical daily moves under its buy price.", page, StringComparison.Ordinal);
        Assert.Contains("Take profit on half at $106.00, 2 times the risk: $6.00 up for $3.00 down. Sell the other half on a close under a give-up price that rises behind its highest close, $3.00 under it.", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("Alpha Corp", StringComparison.Ordinal) < page.IndexOf("Beta Inc", StringComparison.Ordinal));

        // No line states the price a stock opens at.
        Assert.DoesNotContain("at about $", page, StringComparison.Ordinal);
        Assert.DoesNotContain("open, about", page, StringComparison.Ordinal);
        Assert.DoesNotContain("near tonight's close", page, StringComparison.Ordinal);

        // Where it looked: the rules picked nothing, so both picks are ones no setup found, one closed.
        Assert.Contains($"<tr data-group=\"{JudgeScore.NoSetupFound}\">", page, StringComparison.Ordinal);
        Assert.Contains($"{JudgeScore.NoSetupFound}</b><br>Stocks the AI chose that no rule picked that night.</td><td>2</td><td>1</td><td>$3.00</td>", page, StringComparison.Ordinal);

        // The replayed night apart, never in the live record: EEE is counted in neither the scoreboard nor the cards.
        Assert.Contains("Replayed, 1 night: no trade closed yet.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("A replayed pick.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVerdictAgainstRandomPicksAndWhereTheAiLookedAreWorkedByHand()
    {
        // Above the band's high reads better, under its low worse, inside it or unread not told apart.
        var band = new JudgeBand(new DateOnly(2026, 11, 27), 100m, 105m, 108m);

        Assert.Equal(JudgeScore.Better, JudgeScore.AgainstRandom(108.01m, band));
        Assert.Equal(JudgeScore.Worse, JudgeScore.AgainstRandom(99.99m, band));
        Assert.Equal(JudgeScore.NotToldApart, JudgeScore.AgainstRandom(108m, band));
        Assert.Equal(JudgeScore.NotToldApart, JudgeScore.AgainstRandom(100m, band));
        Assert.Equal(JudgeScore.NotToldApart, JudgeScore.AgainstRandom(null, band));
        Assert.Equal(JudgeScore.NotToldApart, JudgeScore.AgainstRandom(110m, null));

        // The scoreboard: two closed of three, one a winner, a mean of 1 per $1 risked, 2.25 over the same plan where read.
        Assert.Equal(new JudgeScoreboard(2, 1, 1.0, 2.25, 300), JudgeScore.Of([(3.0, 2.25), (-1.0, null), (null, null)]));

        // Where it looked: AAA shared with a rule, BBB found by no rule, and CCC and DDD the rules' picks it passed over.
        var day = new DateOnly(2026, 11, 20);

        Assert.Equal(
            [new JudgeLooked(JudgeScore.NoSetupFound, 1, 0, null), new JudgeLooked(JudgeScore.Shared, 1, 1, 3.0), new JudgeLooked(JudgeScore.PassedOver, 2, 1, -1.0)],
            JudgeScore.Looked([("AAA", day, 3.0), ("BBB", day, null)], [("AAA", day, 1.0), ("CCC", day, -1.0), ("DDD", day.AddDays(1), null)]));
    }

    [Fact]
    public void HomeDrawsTheJudgesLineWithItsCountAndItsRecordBesideRandomPicks()
    {
        var view = new JudgeView(new DateOnly(2026, 11, 19), "picked", "", [new JudgeCardView(1, 2, "AAA", null, null, null, "r", 100m, 98m, 104m, null, "1 week", JudgeMenu.Whole), new JudgeCardView(2, 2, "BBB", null, null, null, "r", 100m, 97m, 106m, 3m, "2 weeks", JudgeMenu.HalfThenGiveUp)], new JudgeScoreboard(1, 1, 3.0, 2.25, 300), JudgeScore.NotToldApart, 0.4, [], [], [], null, 0);
        var drawn = WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Judge = view }, string.Empty));

        Assert.Contains($"<b>AI judge</b> Picked 2 stocks tonight in its own paper account. Since it started: $3.00 per $1 risked, against $0.40 for random picks. <a href=\"{SinglePageApp.JudgeRoute}\">See the AI judge</a>", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-home=\"judge\"", WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day(), string.Empty)), StringComparison.Ordinal);
    }

    // From 24.13, the operator's word of 2026-11-21: until the judge has a bought trade, the scoreboard and the four accounts
    // say why they are empty, the night read from its first stored picks and the open from the session after it.
    internal static string[] JudgeEmptyRecordRows =>
    [
        CheckReach.Key("15.26 The AI judge", "An empty record"),
    ];

    [Fact]
    public async Task UntilTheJudgeHasABoughtTradeItsScoreboardAndFourAccountsSayWhyTheyAreEmpty()
    {
        // The night of Friday 20 Nov picks AAA and nothing holds an open after it: the session after it is Monday 23 Nov, the
        // weekend between, worked by hand off the exchange's calendar.
        Assert.Equal(new DateOnly(2026, 11, 23), JudgeScreen.OpenAfter(new DateOnly(2026, 11, 20)));
        Assert.Equal(new DateOnly(2026, 11, 27), JudgeScreen.OpenAfter(new DateOnly(2026, 11, 25)));
        Assert.Equal(new DateOnly(2026, 11, 20), JudgeScreen.OpenAfter(new DateOnly(2026, 11, 19)));

        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail) VALUES "
            + "('judge-pick-1', '2026-11-20', 'v', 1, 20261120, 'picked', '2026-11-21T01:00:00Z', '2026-11-21T02:39:00Z', 4, 2, 2, 0, '');");
        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance) VALUES "
            + $"('2026-11-20', 1, 'judge-pick-1', 'GSPC', 'AAA', '1 week', '{JudgeMenu.Whole}', 'Rising estimates.', '100', 2.0, '98', '104', NULL);");
        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('AAA', '2026-11-20', '100', '100', '100', '100', 1000, 'eod', '2026-11-20T22:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge"));
        const string Words = "The judge's first picks, made on the night of 20 Nov, are bought at the open on Mon 23 Nov. The accounts start from then.";

        Assert.Equal(Words, SinglePageApp.EmptyRecordWords(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 23)));
        Assert.Equal(2, Blocks(page, "<p class=\"empty\" data-empty=\"record\" data-summary>" + System.Text.RegularExpressions.Regex.Escape(Words) + "</p>").Count);
        Assert.DoesNotContain("trades closed<span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg class=\"judge-chart\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("No account has a session to draw yet.", page, StringComparison.Ordinal);

        // Once a pick is bought, the store above whose AAA opened on the session after its night, the tiles and the chart draw
        // as before and the words are gone; and its replayed night is drawn apart, labelled replayed.
        using var bought = JudgePageStore();
        using var boughtHost = new Host(bought.Root);
        using var boughtClient = boughtHost.CreateClient();
        var drawn = WebUtility.HtmlDecode(await boughtClient.GetStringAsync("/screens/judge"));

        Assert.DoesNotContain("data-empty=\"record\"", drawn, StringComparison.Ordinal);
        Assert.Contains("trades closed<span>", drawn, StringComparison.Ordinal);
        Assert.Contains("<svg class=\"judge-chart\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<p data-replayed=\"record\" data-summary>Replayed, 1 night:", drawn, StringComparison.Ordinal);

        // Before the judge has picked at all, no words of a first night.
        Assert.Null(SinglePageApp.EmptyRecord(new JudgeView(null, null, null, [], new JudgeScoreboard(0, 0, null, null, 300), JudgeScore.NotToldApart, null, [], [], [], null, 0)));
    }

    // No source a rule's version pins is one 24.13 writes, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheJudgesEmptyRecordWrites()
    {
        string[] written = ["src/EquityBrief.Api/Reading/JudgeScreen.cs", "src/EquityBrief.Web/App/SinglePageApp.Judge.cs"];
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(written, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(written.Intersect(pinned, StringComparer.Ordinal));
    }
}
