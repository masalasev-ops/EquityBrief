using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: an idea's line is parsed into the grammar's parts, refused outside them with the part it could not
// read, and read as one canonical line for two spellings, by hand; an idea is chosen on 2005 to 2018 alone, a later setup's
// figure moving no level; every idea of a run is judged together from 2019 at the weekly bar, worked by hand on both sides
// of it; the notebook takes inserts alone; and its fill counts every source row and writes nothing twice.
// see: An idea is one line of a closed grammar that code parses into one canonical line and tests with no build session
// see: Every idea from any source is tested by the one tester and kept in a notebook nothing is deleted from
public partial class FixtureExpectations
{
    internal static readonly string[] NotebookClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The idea grammar and the one tester"),
    ];

    const string Spelled = "event pullback(600) · if sector_month above tenth 5 · buy next open · stop 1.5 typical days · take profit whole at 2 risks · sell after 20 sessions · cap 5 a night by rsi lowest";

    [Fact]
    public void AnIdeasLineIsParsedRefusedOutsideTheGrammarAndReadAsOneLineForTwoSpellings()
    {
        var (idea, refused) = IdeaGrammar.Parse(Spelled);

        Assert.Null(refused);
        Assert.Equal(("pullback", "SML", false), (idea!.Family, idea.Index, idea.Loose));
        Assert.Equal([new IdeaCondition("sector_month", 6, 10)], idea.Conditions);
        Assert.Equal((PlanPoints.NextOpen, 0, 1.5, IdeaGrammar.Whole, (double?)2, 20, (int?)5, "rsi", false), (idea.Entry, idea.Window, idea.StopDays, idea.Shape, idea.Multiple, idea.Sessions, idea.Cap, idea.OrderBy, idea.Highest));
        Assert.Equal(Spelled, idea.Canonical);

        // The same idea spelled otherwise: its parts out of order, a semicolon, capitals and spaces, 1.50 for 1.5, a band
        // written as its top tenths, and the sessions the same.
        var other = IdeaGrammar.Parse("Sell after 20 sessions;  STOP 1.50 typical days · event pullback(600) · cap 5 a night by rsi lowest · buy next open · take profit whole at 2.0 risks · if sector_month between tenth 6 and 10");

        Assert.Null(other.Refused);
        Assert.Equal(Spelled, other.Line!.Canonical);

        // Two conditions ordered by reading in the canonical line whatever order they came in, a band in its own words.
        var two = IdeaGrammar.Parse("event dip(400) loose · if rsi below tenth 4 · if depth between tenth 4 and 6 · buy at the first zone within 10 sessions · stop 2 typical days · take profit half at 3 risks · sell after 10 sessions");

        Assert.Equal("event dip(400) loose · if depth between tenth 4 and 6 · if rsi below tenth 4 · buy at the first zone within 10 sessions · stop 2 typical days · take profit half at 3 risks · sell after 10 sessions", two.Line!.Canonical);
        Assert.Equal([new IdeaCondition("depth", 4, 6), new IdeaCondition("rsi", 1, 3)], two.Line.Conditions);
        Assert.Equal("Short dips in an uptrend, S&P 400, every setup its loose gates pass, only where depth between its 4th and 6th tenths and rsi in its lowest 3 tenths. Bought at the first zone within 10 sessions, a stop 2 typical days under the buy, half sold at 3 times the risk and the rest held to the time limit, sold after 10 sessions.", two.Line.Words);

        // Refused outside the grammar, each naming the part.
        (string Line, string Says)[] outside =
        [
            ("event pullback(700) · buy next open · stop 1 typical day · take profit none · sell after 20 sessions", "the event"),
            ("event pullback(500) · if sector_month above tenth 10 · buy next open · stop 1 typical day · take profit none · sell after 20 sessions", "a condition"),
            ("event pullback(500) · if no_such_reading above tenth 5 · buy next open · stop 1 typical day · take profit none · sell after 20 sessions", "a condition"),
            ("event pullback(500) · if rsi above tenth 5 · if rsi below tenth 3 · buy next open · stop 1 typical day · take profit none · sell after 20 sessions", "two conditions read rsi"),
            ("event pullback(500) · if rsi above tenth 5 · if depth above tenth 5 · if tightness above tenth 5 · if strength above tenth 5 · buy next open · stop 1 typical day · take profit none · sell after 20 sessions", "4 conditions"),
            ("event pullback(500) · buy next open · stop 1.25 typical days · take profit none · sell after 20 sessions", "the stop"),
            ("event pullback(500) · buy next open · stop 1 typical day · take profit whole at 2.5 risks · sell after 20 sessions", "the take-profit"),
            ("event pullback(500) · buy next open · stop 1 typical day · take profit none · sell after 30 sessions", "the time limit"),
            ("event pullback(500) · buy next open · stop 1 typical day · take profit none · sell after 20 sessions · cap 4 a night by rsi highest", "the cap"),
            ("event pullback(500) · buy next open · stop 1 typical day · take profit none", "names no time limit"),
            ("event pullback(500) · buy next open · stop 1 typical day · take profit none · sell after 20 sessions · hold forever", "no part of the grammar"),
        ];

        foreach (var (line, says) in outside)
        {
            var parsed = IdeaGrammar.Parse(line);

            Assert.Null(parsed.Line);
            Assert.Contains(says, parsed.Refused, StringComparison.Ordinal);
        }

        // The plan the tester walks a line under: its stop in typical days, sold on its time limit, taking profit as it says.
        Assert.Equal(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.TypicalStop(1.5), PlanPoints.TimeLimit).TakingAtRisk(2), IdeaTestRun.CombinationOf(idea));
        Assert.Equal(new PlanCombination(PlanPoints.FirstZone, 10, PlanPoints.TypicalStop(2), PlanPoints.TimeLimit).Halving(3), IdeaTestRun.CombinationOf(two.Line));
        Assert.Contains("FROM long_setup WHERE index_code = $index AND family = $family AND live_pass = 1 UNION ALL", IdeaTestRun.SetupsSql(idea), StringComparison.Ordinal);
        Assert.DoesNotContain("live_pass", IdeaTestRun.SetupsSql(two.Line), StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdeaIsChosenOnTheEarlyYearsAloneAndALaterSetupMovesNoLevel()
    {
        // Twenty setups of 2005 to 2018 reading 1 to 20: the tenths' cut points, by hand, at the 2nd, 4th, ... 18th values.
        IdeaSetup Setup(int year, int day, double value) => new($"S{year}{day}", new DateOnly(year, 3, day), new Dictionary<string, double> { ["rsi"] = value });

        var early = Enumerable.Range(1, 20).Select(value => Setup(2010, value, value)).ToArray();
        var cuts = IdeaTester.CutsOf(early, ["rsi"]);

        Assert.Equal([2.0, 4, 6, 8, 10, 12, 14, 16, 18], cuts["rsi"]);
        Assert.Equal((1, 1, 2, 9, 10), (IdeaGrammar.TenthOf(1, cuts["rsi"]), IdeaGrammar.TenthOf(2, cuts["rsi"]), IdeaGrammar.TenthOf(2.5, cuts["rsi"]), IdeaGrammar.TenthOf(18, cuts["rsi"]), IdeaGrammar.TenthOf(18.5, cuts["rsi"])));

        // Later setups reading far outside the early range, or none at all, leave the levels where they stood.
        IdeaSetup[] late = [Setup(2019, 2, 1000), Setup(2021, 4, -1000), Setup(2024, 6, 3)];

        Assert.Equal(cuts["rsi"], IdeaTester.CutsOf([.. early, .. late], ["rsi"])["rsi"]);

        // An idea above tenth 8 keeps 17 to 20 of the early setups and the later ones above 16, whatever else the later years hold.
        var idea = IdeaGrammar.Parse("event pullback(500) · if rsi above tenth 8 · buy next open · stop 1 typical day · take profit none · sell after 20 sessions").Line!;

        Assert.Equal([17.0, 18, 19, 20, 1000], IdeaTester.Selected(idea, [.. early, .. late], IdeaTester.CutsOf([.. early, .. late], ["rsi"])).Select(one => one.Readings["rsi"]));
        Assert.Equal([17.0, 18, 19, 20], IdeaTester.Selected(idea, early, cuts).Select(one => one.Readings["rsi"]));

        // A night's cap keeps the most of it in its order, a setup without the reading last.
        var capped = IdeaGrammar.Parse("event pullback(500) · buy next open · stop 1 typical day · take profit none · sell after 20 sessions · cap 1 a night by rsi highest").Line!;
        var night = new DateOnly(2012, 5, 1);
        IdeaSetup[] same = [new("AAA", night, new Dictionary<string, double> { ["rsi"] = 30 }), new("BBB", night, new Dictionary<string, double> { ["rsi"] = 70 }), new("CCC", night, new Dictionary<string, double>())];

        Assert.Equal(["BBB"], IdeaTester.Selected(capped, same, cuts).Select(one => one.Ticker));
    }

    [Fact]
    public void EveryIdeaOfARunIsJudgedTogetherFromTwentyNineteenAtTheWeeklyBarWorkedByHandOnBothSides()
    {
        // The weekly bar: the year's 0.05 over its 52 runs, 1 in 1,040.
        Assert.Equal(0.05 / 52, IdeaTester.Bar, 12);

        // Blocks all positive and unequal reach their own statistic in one arrangement of their signs alone: 10 blocks 1 in
        // 1,024, just over the bar, a near miss; 11 blocks 1 in 2,048, under it, passed.
        IdeaFigures Ideas(int blocks, double early = 0.1) => new(30, early, 40, 0.2, [.. Enumerable.Range(0, blocks).Select(block => (block, 1.0 + (block % 2)))]);

        var ten = Assert.Single(IdeaTester.Verdicts([Ideas(10)]));
        var eleven = Assert.Single(IdeaTester.Verdicts([Ideas(11)]));

        Assert.Equal((1.0 / 1024, IdeaTester.NearMiss), (ten.Adjusted!.Value, ten.Verdict));
        Assert.Equal("Reached 1 in 1024, the bar 1 in 1040.", ten.Why);
        Assert.Equal((1.0 / 2048, IdeaTester.Passed), (eleven.Adjusted!.Value, eleven.Verdict));

        // Read together with a second idea in one step-down, neither reads under its own p-value alone.
        var together = IdeaTester.Verdicts([Ideas(11), new IdeaFigures(30, 0.1, 40, 0.01, [.. Enumerable.Range(0, 11).Select(block => (block, block % 3 == 0 ? -1.0 : 1.5))])]);

        Assert.True(together[0].Adjusted >= 1.0 / 2048);

        // Under the floor of blocks no p-value is read, and an idea that made nothing on the early years failed whatever
        // its later blocks hold.
        Assert.Equal((null, IdeaTester.Failed), (IdeaTester.Verdicts([Ideas(7)])[0].Adjusted, IdeaTester.Verdicts([Ideas(7)])[0].Verdict));
        Assert.Equal(IdeaTester.Failed, IdeaTester.Verdicts([Ideas(11, early: -0.05)])[0].Verdict);

        // The figures: picks to 2018 and from 2019 apart, and a block whose sessions after its last do not yet hold the time
        // limit counted in none.
        IdeaPick[] picks = [new(new DateOnly(2018, 6, 1), 0.3), new(new DateOnly(2018, 7, 1), -0.1), new(new DateOnly(2019, 1, 2), 0.5), new(new DateOnly(2019, 2, 1), null), new(new DateOnly(2019, 6, 3), 0.1)];
        int SessionOf(DateOnly day) => day.DayNumber - new DateOnly(2019, 1, 2).DayNumber;
        var figures = IdeaTester.FiguresOf(picks, SessionOf, newest: 63 + 62 + 20, timeLimit: 20);

        Assert.Equal((2, 0.1, 2, 0.3), (figures.EarlyPicks, Math.Round(figures.EarlyEdge!.Value, 12), figures.LatePicks, Math.Round(figures.LateEdge!.Value, 12)));
        Assert.Equal([(0, 0.5)], figures.Blocks);
    }

    [Fact]
    public async Task TheNotebookTakesInsertsAloneAndItsFillCountsEverySourceRowAndWritesNothingTwice()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('loop-1', '2026-11', 'GSPC', '2026-11-06', '2026-11-07T12:00:00Z', '2026-11-07T15:00:00Z', 4);");
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed) VALUES "
            + "('loop-1', 'GSPC', 'pullback', 'the exits menu', 'take profit at 1.5 times the risk', 'the rule today', 'risks', 300, 12, 0.2, 0, 1, 4, 2, 0.1, 1, 0.05, 3, 0), "
            + "('loop-1', 'GSPC', 'breakout', 'one more condition', NULL, 'the rule today', 'risks', 0, 0, NULL, 0, 0, 0, 0, NULL, 0, NULL, 0, 0);");
        store.Execute(
            "INSERT INTO long_search (run_id, index_code, family, through, first_test_year, words, current_words, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, stable_folds, searches, passed, years) VALUES "
            + "('search-1', 'MID', 'breakout', '2026-11-06', 2019, 'the grid at its third setting', 'the provisional setting', 200, 20, 0.001, 1, 1, 6, 5, 0.2, 1, 5, 3, 1, '[]');");
        store.Execute(
            "INSERT INTO plan_point (run_id, index_code, family, through, setups, combination, picks, fills, fill_rate, hit_rate, break_even, result_per_pick, edge_per_pick, edge_per_fill, days_held, worst_drawdown, traded_fills, traded_edge_per_pick, later_fills, later_edge_per_pick, traded_later_edge_per_pick) VALUES "
            + "('plan-1', 'SML', 'dip', '2026-11-06', 900, 'the next open, 1 typical day under the buy, the time limit alone', 800, 790, 0.99, 0.5, 0.4, 0.1, 0.06, 0.06, 6, -12, 790, 0.03, 300, 0.05, 0.02);");

        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 22, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var counts = await new NotebookFill(clock, store.DatabaseFile).FillAsync("fill-1");

        // By hand against the sources: two proposals, one search, one plan test and the operator's two ideas, all written.
        Assert.Equal(
            [new NotebookFillCount(NotebookFill.MonthlyRun, 2, 0, 2), new NotebookFillCount(NotebookFill.LongSearchSource, 1, 0, 1), new NotebookFillCount(NotebookFill.PlanTest, 1, 0, 1), new NotebookFillCount(NotebookFill.Operator, 2, 0, 2)],
            counts);
        Assert.Equal(6L, Scalar(store, "SELECT COUNT(*) FROM notebook;"));
        Assert.Equal(
            ["the monthly run|failed|Chose no change on the years it was chosen on.", "the monthly run|failed|Fell short on its gate.", "a search over the long history|passed|Passed all four parts of its test.", "a plan test|passed|Beat the traded plan from 2019: +0.05 against +0.02 per $1 risked.", "the operator|waiting|" + NotebookFill.WaitingForShape, "the operator|waiting|" + NotebookFill.WaitingForShape],
            Query(store, "SELECT source || '|' || verdict || '|' || why FROM notebook ORDER BY id;"));

        // Run again, it writes nothing it holds.
        Assert.All(await new NotebookFill(clock, store.DatabaseFile).FillAsync("fill-2"), count => Assert.Equal((count.Held, 0), (count.Before, count.Written)));
        Assert.Equal(6L, Scalar(store, "SELECT COUNT(*) FROM notebook;"));

        // An update, a delete and a replace are each refused, and the rows stand as they were.
        foreach (var write in new[]
        {
            "UPDATE notebook SET verdict = 'passed' WHERE id = 1;",
            "DELETE FROM notebook WHERE id = 1;",
            "INSERT OR REPLACE INTO notebook (id, entered_at, source, words, verdict, why, run_id) VALUES (1, 'x', 'the operator', 'w', 'failed', 'y', 'r');",
            "INSERT OR REPLACE INTO notebook (entered_at, source, source_key, words, verdict, why, run_id) VALUES ('x', 'the operator', 'operator|three-parts', 'w', 'failed', 'y', 'r');",
        })
        {
            Assert.Throws<SqliteException>(() => store.Execute(write));
        }

        Assert.Equal(6L, Scalar(store, "SELECT COUNT(*) FROM notebook;"));
        Assert.Equal("failed", Query(store, "SELECT verdict FROM notebook WHERE id = 1;").Single());

        // From the 24.26 correction the table is strict, so a figure written as words is refused.
        Assert.Equal(1L, Scalar(store, "SELECT strict FROM pragma_table_list WHERE name = 'notebook';"));
        Assert.Throws<SqliteException>(() => store.Execute("INSERT INTO notebook (entered_at, source, words, early_picks, verdict, why, run_id) VALUES ('x', 'the operator', 'w', 'many', 'failed', 'y', 'r');"));
    }

    [Fact]
    public async Task TheTesterRefusesALineOutsideTheGrammarAndOneTheNotebookHoldsBeforeItWalksAnything()
    {
        using var store = new TemporaryStore().Migrated();
        var idea = IdeaGrammar.Parse(Spelled).Line!;

        store.Execute($"INSERT INTO notebook (entered_at, source, index_code, family, words, line, verdict, why, tested_on, run_id) VALUES ('2026-11-22T00:00:00Z', 'the operator', 'SML', 'pullback', 'w', '{idea.Canonical}', 'failed', 'y', '2026-11-20', 'r');");

        var said = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 22, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var exit = await new IdeaTestRun(clock, store.DatabaseFile, store.Root, said).RunAsync(["event pullback(700) · buy next open", Spelled.Replace(" · ", "; ", StringComparison.Ordinal)], "the operator");

        Assert.Equal(2, exit);
        Assert.Contains("idea-test: refused, the event is not one of", said.ToString(), StringComparison.Ordinal);
        Assert.Contains($"idea-test: the notebook already holds this idea, tested 2026-11-20: {idea.Canonical}", said.ToString(), StringComparison.Ordinal);
        Assert.Contains("idea-test: no line to test", said.ToString(), StringComparison.Ordinal);
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM notebook;"));
        Assert.Equal(2, await new IdeaTestRun(clock, store.DatabaseFile, store.Root, said).RunAsync([Spelled], "someone else"));
    }

    // No source a rule's version pins is one the notebook, the grammar or the tester writes, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheNotebookOrTheTesterWrites()
    {
        string[] written =
        [
            "src/EquityBrief.Core/Loop/IdeaGrammar.cs", "src/EquityBrief.Core/Loop/IdeaTester.cs", "src/EquityBrief.Worker/Loop/IdeaTestRun.cs",
            "src/EquityBrief.Worker/Loop/NotebookFill.cs", "src/EquityBrief.Worker/Loop/PlanPointTest.cs", "src/EquityBrief.Api/Reading/ReadApi.Notebook.cs",
            "src/EquityBrief.Web/App/SinglePageApp.Notebook.cs",
        ];
        var pinned = Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(written, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(written.Intersect(pinned, StringComparer.Ordinal));
    }
}
