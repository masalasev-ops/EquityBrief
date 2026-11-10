using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.8: the search every third test year with its setting carried between worked by hand across two
// searches, the floors in proportion worked by hand, a fold's choice reading nothing ending in its test year, and a
// constructed search's verdict against the tester's over the same trades; one combination of each entry, stop and sale
// worked by hand over a constructed path, a pick its entry never fills counted as offered and not taken, the tally against
// the figures over a list and the combinations counted; nothing registered; and no source a rule's version pins among those
// 23.8 writes.
// see: A search over the long history re-runs every third test year with its setting carried between
// see: Every price family's traded plan's buy and sell points are tested over the long history and one combination is recommended a family an index
public partial class FixtureExpectations
{
    internal static string[] LongSearchClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The search over the long history"),
        CheckReach.Key(Scope.LimitsTable, "The buy and sell points"),
    ];

    internal static string[] LongSearchRows => [.. ComponentAccess.LongSearchRows, .. SchemaColumns.LongSearchStoreRows, .. LongSearchClaims, .. Reading.ReadSurface.LongSearchPageRows];

    // From 2008, searched at 2008, 2011 and 2014 and carried through the years between; a list opening after the first test
    // year searches at its first year all the same.
    [Fact]
    public void TheCarriedSettingIsWorkedByHandAcrossTwoSearches()
    {
        var asked = new List<int>();

        string Search(int year)
        {
            asked.Add(year);

            return "chosen in " + year;
        }

        var folds = LongSearchRules.Folds([2008, 2009, 2010, 2011, 2012, 2013, 2014], 2008, Search);

        Assert.Equal([2008, 2011, 2014], asked);
        Assert.Equal(
            ["chosen in 2008", "chosen in 2008", "chosen in 2008", "chosen in 2011", "chosen in 2011", "chosen in 2011", "chosen in 2014"],
            folds.Select(fold => fold.Chosen));
        Assert.Equal([true, false, false, true, false, false, true], folds.Select(fold => fold.Searched));

        asked.Clear();

        Assert.Equal(["chosen in 2009", "chosen in 2009", "chosen in 2011"], LongSearchRules.Folds([2009, 2010, 2011], 2008, Search).Select(fold => fold.Chosen));
        Assert.Equal([2009, 2011], asked);
        Assert.Equal(3, LongSearchRules.SearchEvery);
    }

    // A floor of 300 trades and 6 years over 22 scored years: three learning years read 41 and 1, ten read 137 and 3, and
    // the whole span reads them as stated.
    [Fact]
    public void TheFloorsAreReadInProportionToTheLearningYears()
    {
        Assert.Equal((41, 1), LongSearchRules.Floors(300, 6, 3, 22));
        Assert.Equal((137, 3), LongSearchRules.Floors(300, 6, 10, 22));
        Assert.Equal((300, 6), LongSearchRules.Floors(300, 6, 22, 22));
    }

    // Setting A's one large trade ends inside the test year, so the fold reads only its loss and chooses B; read whole, A
    // would win.
    [Fact]
    public void AFoldsChoiceReadsNothingEndingInItsTestYear()
    {
        var fold = new LoopFold(2010, new DateOnly(2010, 1, 4), new DateOnly(2010, 12, 31), true);
        (DateOnly Entered, DateOnly? Ended, double Edge)[] a = [(new DateOnly(2009, 3, 2), new DateOnly(2009, 4, 1), -1.0), (new DateOnly(2009, 12, 1), new DateOnly(2010, 2, 1), 5.0)];
        (DateOnly Entered, DateOnly? Ended, double Edge)[] b = [(new DateOnly(2009, 5, 1), new DateOnly(2009, 6, 1), 0.5)];

        LongFigures Learned(string key, IEnumerable<(DateOnly Entered, DateOnly? Ended, double Edge)> trades) =>
            LongSearchRules.FiguresOf(key, [.. new FoldView<(DateOnly Entered, DateOnly? Ended, double Edge)>(fold, trades, one => one.Entered, one => one.Ended).Learning.Select(one => (one.Entered.Year, one.Edge))]);

        Assert.Equal(new LongFigures("a", 1, -1.0, 0), Learned("a", a));
        Assert.Equal("b", LongSearchRules.Choose([("a", Learned("a", a), 0), ("b", Learned("b", b), 1)], (1, 0)));
        Assert.Equal("a", LongSearchRules.Choose([("a", LongSearchRules.FiguresOf("a", [.. a.Select(one => (one.Entered.Year, one.Edge))]), 0), ("b", Learned("b", b), 1)], (1, 0)));
        Assert.Null(LongSearchRules.Choose([("b", Learned("b", b), 1)], (2, 0)));
    }

    // The same trades judged by the long search and by the tester's own judge read the same verdict.
    [Fact]
    public void AConstructedSearchsVerdictEqualsTheTestersOverTheSameTrades()
    {
        var calendar = Enumerable.Range(0, 5 * 365).Select(day => new DateOnly(2008, 1, 1).AddDays(day)).Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).ToArray();
        var folds = LoopFolds.Of(calendar, calendar[^1], 2009);
        IReadOnlyList<(int Entry, double? Edge)> current = [.. Enumerable.Range(0, 400).Select(at => (at * 3, (double?)(at % 3 == 0 ? 1.0 : -0.4)))];
        IReadOnlyList<(int Entry, double? Edge)> chosen = [.. Enumerable.Range(0, 400).Select(at => (at * 3, (double?)(at % 3 == 0 ? 1.3 : -0.4)))];
        var evidence = LoopProcedures.Evidence(folds, calendar, [.. folds.Select(_ => chosen)], current);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), calendar, 0, folds, Long: true);
        var proposal = new LoopProposalRead("breakout", LoopProcedures.Grid, "a setting", "the rule today", LoopProcedures.Risks, 63, [], 0, evidence);

        Assert.Equal(WalkForwardTester.Judge(read, [proposal])[0].Verdict, LongSearch.Verdict(evidence, calendar, folds, 63));
    }

    // One pick over a constructed path, its close 10 and the rule's stop 9: the next open buys at 10 and the zone at 9.5 on the
    // second session after it, the low trading through it; the close of 11.2 at or over the target of 11 sells at the next
    // open, 11.3. The next open's result is 1.3 risks of its own, the zone's 3.6 of its own over its 3 sessions held; a level
    // of 8 no session reaches in its window is offered and not taken.
    [Fact]
    public void OneCombinationIsWorkedByHandOverAConstructedPath()
    {
        double[] opens = [10.0, 10.0, 9.7, 10.2, 11.0, 11.3, 11.4, 11.4];
        double[] lows = [9.9, 9.8, 9.4, 10.0, 10.9, 11.2, 11.3, 11.3];
        double[] closes = [10.0, 9.9, 9.6, 10.4, 11.2, 11.3, 11.3, 11.3];

        var nextOpen = PlanPoints.FillOf(opens, lows, 0, null, 0);
        var zone = PlanPoints.FillOf(opens, lows, 0, 9.5, 5);

        Assert.Equal((1, 10.0), (nextOpen!.Value.Bar, nextOpen.Value.Price));
        Assert.Equal((2, 9.5), (zone!.Value.Bar, zone.Value.Price));
        Assert.Null(PlanPoints.FillOf(opens, lows, 0, 8.0, 5));

        var sale = new PlanSale(11.0, null, []);
        var bought = PlanPoints.Walk(closes, opens, 1, 10.0, 9.0, sale, 63)!;
        var waited = PlanPoints.Walk(closes, opens, 2, 9.5, 9.0, sale, 63)!;

        Assert.Equal((11.3, 4, PlanPoints.Reached), (bought.Sold, bought.Sessions, bought.End));
        Assert.Equal(1.3, bought.Result, 9);
        Assert.Equal((11.3, 3, PlanPoints.Reached), (waited.Sold, waited.Sessions, waited.End));
        Assert.Equal(3.6, waited.Result, 9);

        // The stop: a close of 9.6 under a stop of 9.7 sells at the next open, 10.2.
        var stopped = PlanPoints.Walk(closes, opens, 1, 10.0, 9.7, sale, 63)!;

        Assert.Equal((10.2, 2, PlanPoints.Stopped), (stopped.Sold, stopped.Sessions, stopped.End));

        // The staged sales at 10.4 and 11: a third at the open after the close of 10.4, a third after the close of 11.2, and
        // the last third at the time limit's last session's next open.
        var staged = PlanPoints.Walk(closes, opens, 1, 10.0, 9.0, new PlanSale(null, null, [10.4, 11.0]), 5)!;

        Assert.Equal(((11.0 + 11.3 + 11.4) / 3, 5, PlanPoints.Reached), (staged.Sold, staged.Sessions, staged.End));

        // The time limit alone sells at the open after its last session's close.
        var timed = PlanPoints.Walk(closes, opens, 1, 10.0, 9.0, new PlanSale(null, null, []), 3)!;

        Assert.Equal((11.0, 3, PlanPoints.Timed), (timed.Sold, timed.Sessions, timed.End));
        Assert.True(PlanPoints.HitOf(PlanPoints.TimeLimit, timed.End));
        Assert.False(PlanPoints.HitOf(PlanPoints.RuleSale, timed.End));

        // A path ending before its sale is no outcome.
        Assert.Null(PlanPoints.Walk(closes, opens, 1, 10.0, 9.0, new PlanSale(20.0, null, []), 63));
    }

    // Three picks offered, two filled at 1.3 and -1.0 and ended in that order, one not filled: 2 of 3 filled, 0.1 a pick
    // offered, 0.15 a fill, and the worst fall of 1.0 from the 1.3 the first reached.
    [Fact]
    public void APickItsEntryNeverFillsIsCountedAsOfferedAndNotTaken()
    {
        PlanPick[] picks =
        [
            new(2006, "2005 to 2009", PlanPoints.Above, true, 1.3, 1.1, 4, PlanPoints.Reached, 5, 0.33) { Hit = true },
            new(2006, "2005 to 2009", PlanPoints.Above, true, -1.0, -1.2, 2, PlanPoints.Stopped, 7, 0.33),
            new(2006, "2005 to 2009", PlanPoints.Below, false, null, null, 0, null, 0, null),
        ];

        var figures = PlanPoints.FiguresOf(picks);
        var tally = new PlanTally(drawdown: true);

        foreach (var pick in picks)
        {
            tally.Add(pick);
        }

        Assert.Equal((3, 2), (figures.Picks, figures.Fills));
        Assert.Equal(2.0 / 3, figures.FillRate!.Value, 12);
        Assert.Equal(0.1, figures.ResultPerPick!.Value, 12);
        Assert.Equal(0.15, figures.ResultPerFill!.Value, 12);
        Assert.Equal(-0.1 / 3, figures.EdgePerPick!.Value, 12);
        Assert.Equal(0.5, figures.HitRate!.Value, 12);
        Assert.Equal(1.0, figures.WorstDrawdown!.Value, 12);
        Assert.Equal(3.0, figures.DaysHeld!.Value, 12);
        Assert.Equal(figures, tally.Figures());
    }

    // The combinations: the next open with the rule's stop and four typical-days stops, each zone within each window with the
    // zone's own stop beside them, and the average within each window, each under four sales.
    [Fact]
    public void TheCombinationsAreCounted()
    {
        Assert.Equal((1 * 5 + 3 * 3 * 6 + 3 * 5) * 4, PlanPoints.Combinations.Count);
        Assert.Equal(296, PlanPoints.Combinations.Count);
        Assert.Contains(PlanPoints.ZoneStop, PlanPoints.StopsFor(PlanPoints.SecondZone));
        Assert.DoesNotContain(PlanPoints.ZoneStop, PlanPoints.StopsFor(PlanPoints.NextOpen));
        Assert.Equal(PlanPoints.Combinations.Count, PlanPoints.Combinations.Distinct().Count());

        // A recommendation rests on 100 fills, the fewer changes from the traded plan first on a tie.
        var traded = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale);
        var zone = new PlanCombination(PlanPoints.SecondZone, 20, PlanPoints.RuleStop, PlanPoints.RuleSale);
        PlanFigures Read(int fills, double edge) => new(fills, fills, 1, null, null, edge, edge, edge, edge, 1, 0);

        Assert.Equal(traded, PlanPoints.Recommended([(zone, Read(100, 0.2)), (traded, Read(100, 0.2))]));
        Assert.Equal(zone, PlanPoints.Recommended([(zone, Read(100, 0.3)), (traded, Read(100, 0.2))]));
        Assert.Null(PlanPoints.Recommended([(zone, Read(99, 0.3))]));
    }

    // Nothing registered: the two components touch only their own tables and the run log, and neither names the register or a
    // rule's versions.
    [Fact]
    public void TheLongSearchAndThePlanPointsRegisterNothing()
    {
        Assert.Equal([Core.Components.Store.LongSearch, Core.Components.Store.RunLog], LongSearch.Access.Stores.Select(touch => touch.Store));
        Assert.Equal(
            [
                Core.Components.Store.LongSetup, Core.Components.Store.Setup, Core.Components.Store.PlanPoint,
                Core.Components.Store.LongFiling, Core.Components.Store.Company, Core.Components.Store.PulledCompany,
                Core.Components.Store.LongFact, Core.Components.Store.FiledFact, Core.Components.Store.PulledRevenue,
                Core.Components.Store.RunLog,
            ],
            PlanPointTest.Access.Stores.Select(touch => touch.Store));
        Assert.All(PlanPointTest.Access.Stores.Where(touch => touch.Store != Core.Components.Store.PlanPoint && touch.Store != Core.Components.Store.RunLog), touch => Assert.Equal(Core.Components.Touch.Read, touch.Touch));
        Assert.Empty(LongSearch.Access.Feeds);
        Assert.Empty(PlanPointTest.Access.Feeds);

        foreach (var file in new[] { "LongSearch.cs", "PlanPointTest.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", file));

            Assert.DoesNotMatch(@"\b(candidate_register|rule_version|loop_applied|loop_decision)\b", source);
        }
    }

    // Every shipped source 23.8 writes or changes, none of them one a rule's version pins, so no rule's code version moves.
    // From 23.10 the test's own words and walk are pinned by every family rule, which trades its plan through them, so that
    // file has left this list (see: At phase 23's restart each price family trades the plan its long test recommends where it beats today's plan from 2019 above the ruled benchmark, today's plan kept beside it as a variant).
    internal static readonly string[] LongSearchSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.LongSearch.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Core/History/LongProfit.cs",
        "src/EquityBrief.Core/Loop/FundLine.cs",
        "src/EquityBrief.Core/Loop/LongSearchRules.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/SinglePageApp.LongSearch.cs",
        "src/EquityBrief.Worker/Loop/LongSearch.cs",
        "src/EquityBrief.Worker/Loop/LoopProcedures.cs",
        "src/EquityBrief.Worker/Loop/PlanPointTest.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.Long.cs",
        "src/EquityBrief.Worker/History/LongHistoryReader.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.cs",
        "src/EquityBrief.Worker/Program.cs",
    ];

    [Fact]
    public void NoSourceTheLongSearchAndThePlanPointsWriteIsOneARulesVersionPins()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(LongSearchSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(LongSearchSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Worker/Sweep/FamilySweep.cs", pinned);
    }
}
