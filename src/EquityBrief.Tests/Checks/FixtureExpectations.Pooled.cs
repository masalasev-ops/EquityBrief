using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.2: the one verdict across the three indices worked by hand over constructed units of three
// indices, their blocks summed before one sign vector flips them, two kinds of change read together by the step-down, the
// count screen on both sides of its count summed, a book's month counted once, a rule reading a release reading judged
// from its cutoff's year alone, the index that carried a pass named, a kind read from a variant's settings, the pooled step
// over a constructed store, the passes luck alone would bring worked from the bar, and no source a rule's version pins
// among those 23.2 writes.
// see: One verdict across the three indices stands beside each index's own, and an approval still changes one index's rule
public partial class FixtureExpectations
{
    internal static string[] PooledClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The verdict across the indices"),
        CheckReach.Key(Scope.FailureTable, "The per-rule tests summed across the indices do not line up"),
    ];

    // Every row 23.2 adds to the document.
    internal static string[] PooledRows => [.. ComponentAccess.PooledTestRows, .. SchemaColumns.PooledStoreRows, .. PooledClaims, .. Reading.ReadSurface.PooledPageRows];

    static readonly DateOnly PooledFirst = new(2022, 1, 3);

    const int PooledCap = 10;

    // A rule on an index with one unit a block, at the block's sixth session, each block's figure given and read against
    // an edge of nothing, its history ending as many sessions after its last block as a block waits.
    static PooledRule Blocked(string index, string rule, bool head, double[] figures, string? kind = null)
    {
        var span = (figures.Length * 63) - 1 + PooledCap;

        return new PooledRule(
            index,
            "pullback",
            rule,
            head,
            false,
            PooledCap,
            span,
            PooledFirst.AddDays(span),
            null,
            [.. figures.Select((figure, block) => new PooledUnit(PooledFirst.AddDays((block * 63) + 5), (block * 63) + 5, figure, 0.0))],
            kind);
    }

    // A rule on an index with a trade every third session from the one given, each at the figure given, over 900 sessions.
    static PooledRule Traded(string index, int from, double figure, int every = 3, int sessions = 900) =>
        new(
            index,
            "pullback",
            index + " rule",
            true,
            false,
            PooledCap,
            sessions + PooledCap - 1,
            PooledFirst.AddDays(sessions + PooledCap - 1),
            null,
            [.. Enumerable.Range(0, sessions).Where(place => place % every == from).Select(place => new PooledUnit(PooledFirst.AddDays(place), place, figure, 0.0))]);

    [Fact]
    public void ThreeIndicesBlocksAreSummedBeforeOneSignVectorFlipsThem()
    {
        var large = Blocked("GSPC", "the S&P 500's", true, [1, 2, -1, 0.5, 1, 1.5, -0.5, 2]);
        var mid = Blocked("MID", "the S&P 400's", true, [0.5, 0, 1, 1, -1, 0.5, 1, 0]);
        var small = Blocked("SML", "the S&P 600's", true, [-0.5, 1, 0, 0.5, 1, 0, 0.5, 1]);

        var verdict = Assert.Single(PooledJudge.Judge([large, mid, small]));

        // Worked by hand: each block's sum across the three, 1, 3, 0, 2, 1, 2, 1 and 3, eight blocks, the eighth read since
        // the history ends a block's cap after it.
        Assert.Equal(
            [1.0, 3.0, 0.0, 2.0, 1.0, 2.0, 1.0, 3.0],
            LoopGate.Blocks(PooledJudge.Evidence([large, mid, small]).Evidence.Units, 0, large.Span, PooledCap).Select(block => block.Sum));
        Assert.Equal(8, verdict.Verdict.Blocks);

        // One sign vector flips the three indices' blocks together, so the statistic follows the summed blocks' signed sum,
        // their squares being 29 whatever the signs: only the arrangements keeping every block's sign, the empty block
        // either way, reach the observed 13, 2 of 256. That is over the bar of 0.05 over 12.
        Assert.Equal(2.0 / 256, verdict.Verdict.Adjusted);
        Assert.False(verdict.Verdict.Gate);
        Assert.Equal(PooledJudge.Own, verdict.Kind);
        Assert.Equal(["GSPC", "MID", "SML"], verdict.Indices);
        Assert.Equal(["the S&P 500's", "the S&P 400's", "the S&P 600's"], verdict.Rules);
    }

    [Fact]
    public void TwoKindsOfChangeAreReadTogetherByTheStepDown()
    {
        // The first kind on the S&P 500 and 400 sums to a risk in each of the first seven blocks and nothing in the eighth,
        // the second on the S&P 400 and 600 to nothing in the first and a risk in each of the seven after.
        PooledRule[] rules =
        [
            Blocked("GSPC", "the S&P 500's lower floor", false, [1, 1, 1, 1, 0, 0, 0, 0], "floor lowered"),
            Blocked("MID", "the S&P 400's lower floor", false, [0, 0, 0, 0, 1, 1, 1, 0], "floor lowered"),
            Blocked("MID", "the S&P 400's price unread", false, [0, 1, 1, 1, 0, 0, 0, 0], "valuation off"),
            Blocked("SML", "the S&P 600's price unread", false, [0, 0, 0, 0, 1, 1, 1, 1], "valuation off"),
        ];

        var verdicts = PooledJudge.Judge(rules);

        // Worked by hand: alone, each kind reaches its observed sum of seven only where its seven blocks keep their sign,
        // its empty block either way, 2 of 256. Read together, the arrangements where either reaches it: the six blocks
        // both share kept and at least one of the first and the eighth, 3 of 256, and the second kind's step no lower.
        Assert.Equal(["floor lowered", "valuation off"], verdicts.Select(verdict => verdict.Kind));
        Assert.All(verdicts, verdict => Assert.Equal(3.0 / 256, verdict.Verdict.Adjusted));
        Assert.Equal(2.0 / 256, PooledJudge.Judge(rules[..2]).Single().Verdict.Adjusted);
        Assert.Equal(["GSPC", "MID"], verdicts[0].Indices);
        Assert.Equal(["MID", "SML"], verdicts[1].Indices);

        // A kind registered on one index alone is that index's own verdict and is not summed.
        Assert.Empty(PooledJudge.Judge([rules[0], rules[2]]));
    }

    [Fact]
    public void TheCountScreenPassesSummedWhereNoIndexAloneMeetsItAndFailsUnderIt()
    {
        // 120 trades on each index, none meeting the 300 alone, 360 summed; 90 on each, 270 summed.
        PooledRule[] meeting = [Traded("GSPC", 0, 1, 7, 840), Traded("MID", 1, 1, 7, 840), Traded("SML", 2, 1, 7, 840)];
        PooledRule[] short_ = [Traded("GSPC", 0, 1, 7, 630), Traded("MID", 1, 1, 7, 630), Traded("SML", 2, 1, 7, 630)];

        Assert.All(meeting, rule => Assert.Equal(120, rule.Units.Count));
        Assert.All(meeting, rule => Assert.False(LoopScreens.Counts(false, rule.Units.Count)));
        Assert.All(short_, rule => Assert.Equal(90, rule.Units.Count));

        var met = PooledJudge.Judge(meeting).Single().Verdict;
        var under = PooledJudge.Judge(short_).Single().Verdict;

        Assert.Equal(360, met.Units);
        Assert.True(met.Counts);
        Assert.Equal(270, under.Units);
        Assert.False(under.Counts);
    }

    [Fact]
    public void ABooksMonthCountsOnceACalendarMonthHoweverManyIndicesHoldIt()
    {
        // Each index's book opens a month on the first or second session of each month of 2022 and 2023, its edge a
        // hundredth, a fiftieth and three hundredths by index.
        PooledRule Book(string index, int day, double figure) => new(
            index,
            "quality",
            index + " book",
            true,
            true,
            23,
            540,
            new DateOnly(2024, 1, 31),
            null,
            [.. Enumerable.Range(0, 24).Select(month => new PooledUnit(new DateOnly(2022 + (month / 12), 1 + (month % 12), day), month * 21, figure, 0.0))]);

        var (evidence, _) = PooledJudge.Evidence([Book("GSPC", 3, 0.01), Book("MID", 4, 0.02), Book("SML", 3, 0.03)]);

        // Worked by hand: 24 calendar months, not 72, each six hundredths summed, 2022 counting its twelve, a year's count
        // for a book.
        Assert.Equal(24, evidence.Units.Count);
        Assert.All(evidence.Units, unit => Assert.Equal(0.06, unit.Proposed!.Value, 10));
        Assert.Equal(12, evidence.Years.Single(year => year.Year == 2022).ProposedUnits);
        Assert.Equal(LoopScreens.Fewest(true), evidence.Years.Single(year => year.Year == 2022).CurrentUnits);
        Assert.Equal(0.72, evidence.Years.Single(year => year.Year == 2022).ProposedTotal, 10);
        Assert.Equal(24, PooledJudge.Judge([Book("GSPC", 3, 0.01), Book("MID", 4, 0.02), Book("SML", 3, 0.03)]).Single().Verdict.Units);
    }

    [Fact]
    public void ARuleReadingAReleaseReadingIsJudgedFromItsCutoffsYearAlone()
    {
        // A trade a week from 2022-01-03 to 2025-12-29 on two indices, the S&P 400's reading a release reading from 2025.
        PooledRule Weekly(string index, int? from) => new(
            index,
            "drift",
            index + " drift",
            true,
            false,
            PooledCap,
            1460,
            new DateOnly(2025, 12, 31),
            from,
            [.. Enumerable.Range(0, 209).Select(week => new PooledUnit(PooledFirst.AddDays(week * 7), week * 5, 1, 0.0))]);

        var (evidence, judged) = PooledJudge.Evidence([Weekly("GSPC", null), Weekly("MID", 2025)]);

        // Worked by hand: 2025's 52 weeks on each index, 104 trades, and no test year before 2025.
        Assert.Equal(2025, judged);
        Assert.Equal(104, evidence.ProposedResults.Count);
        Assert.Equal([2025], evidence.Years.Select(year => year.Year));
        Assert.Equal(2025, PooledJudge.Judge([Weekly("GSPC", null), Weekly("MID", 2025)]).Single().JudgedFrom);
        Assert.Null(PooledJudge.Evidence([Weekly("GSPC", null), Weekly("MID", null)]).From);
    }

    [Fact]
    public void APassOneIndexCarriedSaysSoAndChangesNoVerdict()
    {
        // A trade every third session on each index over 900 sessions, 300 each. The S&P 600's earn a risk each and the
        // others lose a tenth: fourteen blocks each above nothing, so the pass stands, and without the S&P 600 the other two
        // together lose 60 risks.
        var carried = PooledJudge.Judge([Traded("GSPC", 0, -0.1), Traded("MID", 1, -0.1), Traded("SML", 2, 1)]).Single();

        Assert.Equal(14, carried.Verdict.Blocks);
        Assert.Equal(1.0 / 16384, carried.Verdict.Adjusted);
        Assert.True(carried.Passed);
        Assert.Equal("SML", carried.CarriedBy);

        // Each earning a risk, every pair stands above nothing without the third, so none carried it.
        var shared = PooledJudge.Judge([Traded("GSPC", 0, 1), Traded("MID", 1, 1), Traded("SML", 2, 1)]).Single();

        Assert.True(shared.Passed);
        Assert.Null(shared.CarriedBy);

        // Named for a pass alone: the same figures under the count carry no name.
        Assert.Equal("SML", PooledJudge.CarriedBy([Traded("GSPC", 0, -0.1), Traded("MID", 1, -0.1), Traded("SML", 2, 1)], null));
        Assert.Null(PooledJudge.Judge([Traded("GSPC", 0, -0.1, 7, 630), Traded("MID", 1, -0.1, 7, 630), Traded("SML", 2, 1, 7, 630)]).Single().CarriedBy);
    }

    [Fact]
    public void AKindIsEachSettingAVariantMovesFromItsLiveRuleAndWhichWay()
    {
        var live = CandidateEvaluator.Read("{\"book\": 10, \"floor\": 7, \"sale\": 0, \"trend\": 0, \"valuation\": 1}");

        Assert.Equal("floor lowered", PooledJudge.KindOf(live, CandidateEvaluator.Read("{\"book\": 10, \"floor\": 6, \"sale\": 0, \"trend\": 0, \"valuation\": 1}")));
        Assert.Equal("book raised", PooledJudge.KindOf(live, CandidateEvaluator.Read("{\"book\": 20, \"floor\": 7, \"sale\": 0, \"trend\": 0, \"valuation\": 1}")));
        Assert.Equal("valuation off", PooledJudge.KindOf(live, CandidateEvaluator.Read("{\"book\": 10, \"floor\": 7, \"sale\": 0, \"trend\": 0, \"valuation\": 0}")));
        Assert.Equal("sale on, trend on", PooledJudge.KindOf(live, CandidateEvaluator.Read("{\"book\": 10, \"floor\": 7, \"sale\": 1, \"trend\": 1, \"valuation\": 1}")));
        Assert.Equal("readingVersion on", PooledJudge.KindOf(live, CandidateEvaluator.Read("{\"book\": 10, \"floor\": 7, \"readingVersion\": 5, \"sale\": 0, \"trend\": 0, \"valuation\": 1}")));
        Assert.Null(PooledJudge.KindOf(live, live));
    }

    [Fact]
    public void ThePassesLuckAloneWouldBringAreWorkedFromTheBarAndTheSetupsStanding()
    {
        // Worked by hand: 0.05 over 12 a run, twelve runs a year, 0.05 a setup a year; seven setups 0.35, one pass in about
        // 2.86 years, three; one setup one in twenty.
        Assert.Equal(0.35, PooledJudge.FalsePassesAYear(7), 10);
        Assert.Equal(3, PooledJudge.YearsAPass(7));
        Assert.Equal(0.05, PooledJudge.FalsePassesAYear(1), 10);
        Assert.Equal(20, PooledJudge.YearsAPass(1));
        Assert.Null(PooledJudge.YearsAPass(0));

        Assert.Contains("about 0.35 a year across the 7 setups: one pass in about three years may be luck.", MarkRenderer.PooledLuckLine(7), StringComparison.Ordinal);
        Assert.Contains("about 0.05 a year across the 1 setup: one pass in about twenty years may be luck.", MarkRenderer.PooledLuckLine(1), StringComparison.Ordinal);
    }

    static void PooledExecute(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // A per-rule test's rule row on an index for 2026-11, with the units kept, or none for a test written before them.
    static string PooledRuleRow(string run, string index, string rule, string standing, bool kept) =>
        "INSERT INTO loop_rule (run_id, month, index_code, family, rule, standing, words, against, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, why, cap, span, through, judged_from) " +
        $"VALUES ('{run}', '2026-11', '{index}', 'quality', '{rule}', '{standing}', 'words', 'its benchmark', 'risks', 1, 1, NULL, 0, 0, 0, 0, NULL, 0, 0, NULL, {(kept ? "10, 513, '2023-05-31'" : "NULL, NULL, NULL")}, NULL);";

    static string PooledUnitRows(string run, string index, string rule, double[] figures, int shift = 0) =>
        string.Concat(figures.Select((figure, block) => FormattableString.Invariant($"INSERT INTO loop_rule_unit (run_id, index_code, family, rule, unit, session_date, place, figure, compared) VALUES ('{run}', '{index}', 'quality', '{rule}', {block}, '{PooledFirst.AddDays((block * 63) + 5):yyyy-MM-dd}', {(block * 63) + 5 + shift}, {figure}, 0.0);")));

    static string PooledRegisterRow(long id, string candidate, string parameters) =>
        $"INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES ({id}, '{candidate}', 'quality', 'test', 'quality-500', '{parameters}', 'v1', 'registered', NULL, '2026-11-01T00:00:00Z', NULL);";

    [Fact]
    public async Task ThePooledStepSumsTheMonthsTestsAndNamesAnIndexThatKeptNoUnits()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 12, 5, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // The S&P 500's and 600's tests of the month kept their units, each with its quality rule and a lower floor; the S&P
        // 400's was written before units were kept.
        PooledExecute(store, string.Concat(
            PooledRuleRow("loop-rules-GSPC-1", "GSPC", "large live", "live", true),
            PooledRuleRow("loop-rules-GSPC-1", "GSPC", "large lower", "variant", true),
            PooledRuleRow("loop-rules-SML-1", "SML", "small live", "live", true),
            PooledRuleRow("loop-rules-SML-1", "SML", "small lower", "variant", true),
            PooledRuleRow("loop-rules-MID-1", "MID", "mid live", "live", false),
            PooledUnitRows("loop-rules-GSPC-1", "GSPC", "large live", [1, 2, -1, 0.5, 1, 1.5, -0.5, 2]),
            PooledUnitRows("loop-rules-SML-1", "SML", "small live", [-0.5, 1, 0, 0.5, 1, 0, 0.5, 1]),
            PooledUnitRows("loop-rules-GSPC-1", "GSPC", "large lower", [1, 1, 1, 1, 1, 1, 1, 1]),
            PooledUnitRows("loop-rules-SML-1", "SML", "small lower", [1, 1, 1, 1, 1, 1, 1, 1]),
            PooledRegisterRow(1, "large live", "{\"floor\": 7, \"book\": 10}"),
            PooledRegisterRow(2, "large lower", "{\"floor\": 6, \"book\": 10}"),
            PooledRegisterRow(3, "small live", "{\"floor\": 7, \"book\": 10}"),
            PooledRegisterRow(4, "small lower", "{\"floor\": 6, \"book\": 10}")));

        var test = new PooledTest(clock, store.DatabaseFile, store.Root, output, (_, _) => Task.CompletedTask);

        // Printed, nothing is written.
        var (printed, printedWords) = await test.RunAsync("2026-11", print: true);

        Assert.True(printed);
        Assert.EndsWith("printed and not written", printedWords, StringComparison.Ordinal);
        Assert.Empty(Query(store, "SELECT run_id FROM loop_pooled;"));

        var (held, words) = await test.RunAsync("2026-11");

        Assert.True(held);
        Assert.Contains("the S&P 400 adding nothing", words, StringComparison.Ordinal);
        Assert.Contains("the S&P 400 adds nothing: its per-rule test loop-rules-MID-1 kept no units", output.ToString(), StringComparison.Ordinal);

        // Worked by hand: the rules' blocks summed, 0.5, 3, -1, 1, 2, 1.5, 0 and 3, a signed sum of 10 against the 12 every
        // block's size makes. It is reached as observed, with the -1 turned and nothing more, with it turned and the 0.5
        // or the 1 flipped, four ways, each with the empty block either way: 8 of 256. The lower floor sums two risks every
        // block, reached by the observed arrangement alone, 1 of 256, under the bar.
        Assert.Equal(
            ["quality|its rules|GSPC SML|0.03125|0", "quality|floor lowered|GSPC SML|0.00390625|1"],
            Query(store, "SELECT family || '|' || kind || '|' || indices || '|' || adjusted || '|' || gate FROM loop_pooled ORDER BY rowid;"));
        Assert.Equal(
            ["[\"large lower\",\"small lower\"]"],
            Query(store, "SELECT rules FROM loop_pooled WHERE kind = 'floor lowered';"));
        Assert.Equal(["loop-pooled|ok|2"], Query(store, "SELECT stage || '|' || outcome || '|' || rows_written FROM run_log WHERE stage = 'loop-pooled';"));
    }

    [Fact]
    public async Task PerRuleTestsWhoseSessionsDoNotLineUpWriteNothing()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 12, 5, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // The S&P 600's test places each of the same sessions one later than the S&P 500's.
        PooledExecute(store, string.Concat(
            PooledRuleRow("loop-rules-GSPC-1", "GSPC", "large live", "live", true),
            PooledRuleRow("loop-rules-SML-1", "SML", "small live", "live", true),
            PooledUnitRows("loop-rules-GSPC-1", "GSPC", "large live", [1, 1, 1, 1, 1, 1, 1, 1]),
            PooledUnitRows("loop-rules-SML-1", "SML", "small live", [1, 1, 1, 1, 1, 1, 1, 1], shift: 1)));

        var (held, words) = await new PooledTest(clock, store.DatabaseFile, store.Root, output, (_, _) => Task.CompletedTask).RunAsync("2026-11");

        Assert.False(held);
        Assert.Equal("the per-rule tests of the S&P 500 and the S&P 600 place 2022-01-08 at sessions 5 and 6 from the test span's first, so their blocks do not line up on the one exchange calendar and nothing was summed", words);
        Assert.Empty(Query(store, "SELECT run_id FROM loop_pooled;"));

        // A month no index holds a test of writes nothing either.
        var (none, noneWords) = await new PooledTest(clock, store.DatabaseFile, store.Root, output, (_, _) => Task.CompletedTask).RunAsync("2026-10");

        Assert.False(none);
        Assert.Equal("no index holds a per-rule test of 2026-10 that kept its units, so nothing was summed", noneWords);
    }

    [Fact]
    public void TheMonthlyRunSumsTheThreeIndicesAfterTheirPerRuleTestsAndBeforeItsReport()
    {
        var steps = MonthlyRun.StepNames.ToList();

        Assert.Equal(steps.IndexOf("rules-SML") + 1, steps.IndexOf(MonthlyRun.Pooled));
        Assert.Equal(steps.IndexOf(MonthlyRun.Pooled) + 1, steps.IndexOf("report"));
    }

    // The sources 23.2 writes, none of which a rule's version pins.
    static readonly string[] PooledSources =
    [
        "src/EquityBrief.Core/Loop/PooledJudge.cs",
        "src/EquityBrief.Core/Loop/LoopRows.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Worker/Loop/PooledTest.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Loop/MonthlyRun.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Pooled.cs",
        "src/EquityBrief.Api/Reading/PaperScreen.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Pooled.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Paper.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.LoopSummary.cs",
        "src/EquityBrief.Web/Marks/ReaderWords.cs",
        "src/EquityBrief.Web/Marks/ReaderText.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
        "src/EquityBrief.Api/Reading/ReadApi.cs",
        "src/EquityBrief.Api/Program.cs",
    ];

    [Fact]
    public void NoSourceARulesVersionPinsIsOne23Point2Writes()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(PooledSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(PooledSources.Intersect(pinned, StringComparer.Ordinal));

        // Nor any the ledger's readings version pins, whose declaration a rule's pinned catalogue carries.
        string[] readings = [.. LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))];

        Assert.Contains("src/EquityBrief.Core/Ledger/LedgerReleases.cs", readings);
        Assert.Empty(PooledSources.Intersect(readings, StringComparer.Ordinal));
    }
}
