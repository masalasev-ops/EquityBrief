using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.5: the monthly loop over every family. A family's proposals on an index are judged by one
// step-down whichever engine brought them, so a proposal of the dip's menu or grid and one of a book's menu or of the
// engines over its setups each count in their own family's correction and in no other's, worked over constructed folds;
// the month's report names the families in Tonight's order of their cards; and no source a rule's version pins is one
// 21.5 writes outside its comments, so no rule's version moves.
// see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
public partial class FixtureExpectations
{
    [Fact]
    public void EachFamilysProposalsAreJudgedByOneStepDownWhicheverEngineBroughtThem()
    {
        // Weekdays from 2019 to October 2026, each month's first session a month's start, and the tester's folds over them.
        var calendar = Weekdays(new DateOnly(2019, 1, 2), 2035);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), calendar, 0, LoopFolds.Of(calendar, calendar[^1]));
        int[] starts = [.. Enumerable.Range(0, calendar.Length).Where(at => at == 0 || calendar[at].Month != calendar[at - 1].Month)];

        // The block of 63 sessions a session falls in from the test span's first, each unit's edge set by its block.
        int Block(int session) => Math.Max(0, session - read.Opens) / 63;

        IReadOnlyList<(int Opens, double? Edge)> Months(Func<int, double> edge) => [.. starts.SkipLast(1).Select(start => (start, (double?)edge(Block(start))))];

        IReadOnlyList<(int Entry, double? Edge)> Trades(Func<int, double> edge) => [.. Enumerable.Range(0, calendar.Length / 5).Select(at => (at * 5, (double?)edge(Block(at * 5))))];

        LoopProposalRead Book(string name, IReadOnlyList<(int Opens, double? Edge)> months, IReadOnlyList<(int Opens, double? Edge)> own) =>
            new(QualityRule.Name, name, "a change", "the quality rule today", LoopProcedures.Points, BookMonths.LongestMonth, [.. read.Folds.Select(fold => (fold, (string?)"a change"))], read.Folds.Count,
                LoopProcedures.BookEvidence(read.Folds, read.Calendar, [.. read.Folds.Select(_ => months)], own));

        LoopProposalRead Swing(string name, IReadOnlyList<(int Entry, double? Edge)> trades, IReadOnlyList<(int Entry, double? Edge)> own) =>
            new(DipRule.Name, name, "a change", "the dip rule today", LoopProcedures.Risks, 10, [.. read.Folds.Select(fold => (fold, (string?)"a change"))], read.Folds.Count,
                LoopProcedures.Evidence(read.Folds, read.Calendar, [.. read.Folds.Select(_ => trades)], own));

        // Quality at a fair price: the rule's own book at a tenth of a point a month; the menu's sale ahead in every block,
        // by nine tenths a month in every third block and a tenth in the rest; the score's order two points ahead in even
        // blocks and two behind in odd ones; and a condition a twentieth or three twentieths ahead. The dip: the rule's trades
        // at a tenth of a risk; an exit of its menu half a risk ahead in every third block and a twentieth in the rest, and
        // its grid's choice eight tenths ahead in even blocks and seven behind in odd ones.
        var ownBook = Months(_ => 0.001);
        var ownTrades = Trades(_ => 0.1);
        LoopProposalRead[] quality =
        [
            Book("the autopsy's exit, ranked 1", Months(block => block % 3 == 0 ? 0.01 : 0.002), ownBook),
            Book("the learned score, ordering the list", Months(block => block % 2 == 0 ? 0.021 : -0.019), ownBook),
            Book("winners against losers, ranked 1", Months(block => block % 2 == 0 ? 0.0015 : 0.0025), ownBook),
        ];
        LoopProposalRead[] dip =
        [
            Swing("the autopsy's exit, ranked 1", Trades(block => block % 3 == 0 ? 0.6 : 0.15), ownTrades),
            Swing("the grid", Trades(block => block % 2 == 0 ? 0.9 : -0.6), ownTrades),
        ];

        var judged = WalkForwardTester.Judge(read, [dip[0], quality[0], dip[1], quality[1], quality[2]]);
        var qualityTogether = LoopJudge.Judge([.. quality.Select(one => one.Evidence)], read.Opens, read.Newest, BookMonths.LongestMonth);
        var dipTogether = LoopJudge.Judge([.. dip.Select(one => one.Evidence)], read.Opens, read.Newest, 10);
        var menuAlone = LoopJudge.Judge([quality[0].Evidence], read.Opens, read.Newest, BookMonths.LongestMonth);

        // Each family's proposals are judged together, in the order the engines brought them, and the other family's
        // apart: the quality menu's adjusted p is its family's step-down over all three of its proposals, and reads higher
        // than the menu's alone, the score's alternating blocks, flipped to all ahead, reaching it under arrangements the
        // menu's own never do.
        Assert.Equal(
            [dipTogether[0].Adjusted, qualityTogether[0].Adjusted, dipTogether[1].Adjusted, qualityTogether[1].Adjusted, qualityTogether[2].Adjusted],
            judged.Select(one => one.Verdict.Adjusted));
        Assert.All(qualityTogether, verdict => Assert.NotNull(verdict.Adjusted));
        Assert.True(qualityTogether[0].Adjusted > menuAlone[0].Adjusted, $"together {qualityTogether[0].Adjusted}, alone {menuAlone[0].Adjusted}");
    }

    [Fact]
    public async Task TheMonthsReportNamesTheFamiliesInTonightsOrderOfTheirCards()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = new CallClock(new DateTimeOffset(2026, 11, 7, 14, 0, 0, TimeSpan.Zero));
        const string run = "loop-test-SML-20261107T120000Z";

        // One S&P 600 run proposing for the seven families, written in the order of their names, none passing.
        LoopRun(store, run, "SML", "2026-11");

        foreach (var family in LoopDecisions.LoopFamilies.Order(StringComparer.Ordinal))
        {
            LoopProposed(store, run, "SML", family, "the autopsy's exit, ranked 1", false, ExitChange(7), adjusted: 0.2);
        }

        await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, []).ReportAsync("2026-11");

        var report = File.ReadAllText(Path.Combine(store.Root, MonthlyRun.Folder, "2026-11", MonthlyRun.ReportFile));

        Assert.Equal(
            LoopDecisions.LoopFamilies,
            Regex.Matches(report, "^  ([a-z]+): none of its 1 proposals passed\\.", RegexOptions.Multiline).Select(match => match.Groups[1].Value));
    }

    // The shipped sources 21.5 writes outside their comments. The citations it repoints in other sources are comment lines,
    // which no pin reads.
    // see: A comment or a blank line moves no pin, and every other change to a pinned source does
    static readonly string[] LoopFamiliesSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Approval.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Families.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Indices.cs",
        "src/EquityBrief.Core/Loop/LoopDecisions.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Loop.cs",
        "src/EquityBrief.Worker/Candidates/CandidateRegistrar.cs",
        "src/EquityBrief.Worker/Loop/BookProcedures.cs",
        "src/EquityBrief.Worker/Loop/ConditionProcedures.cs",
        "src/EquityBrief.Worker/Loop/EngineRule.cs",
        "src/EquityBrief.Worker/Loop/ExitProcedures.cs",
        "src/EquityBrief.Worker/Loop/IdeaProcedures.cs",
        "src/EquityBrief.Worker/Loop/LoopApply.cs",
        "src/EquityBrief.Worker/Loop/LoopProcedures.cs",
        "src/EquityBrief.Worker/Loop/LoopRegistrations.cs",
        "src/EquityBrief.Worker/Loop/MonthlyRun.cs",
        "src/EquityBrief.Worker/Loop/OwnProcedures.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/RuleWalk.cs",
        "src/EquityBrief.Worker/Loop/ScoreProcedures.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Sweep/ImprovingSweep.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Dip.cs",
        "src/EquityBrief.Worker/Sweep/QualitySweep.cs",
    ];

    // No source a rule's version pins is one 21.5 writes outside its comments, so no rule's version moves, the reader shown
    // to find a source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheLoopOverEveryFamilyWrites()
    {
        var pinned = Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(LoopFamiliesSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(LoopFamiliesSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs", "src/EquityBrief.Core/Loop/RuleHooks.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }
}
