using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 23.10's second part: the S&P 500's swing filter and its heavyweight book taught the hooks. A swing filter rule states a plan
// and no other hook; the live swing filter is given the plan its long test recommends, registered again under its own name,
// today's plan walked unregistered beside the nine its family keeps; its plan is the S&P 500 pullback's traded plan; the
// heavyweights rule states a book's hooks and no other, its conditions keeping and its score ordering each sector's leaders;
// and the per-rule test's lines and tests for the walks beside the pullback's live rule. Each worked by hand.
// see: The S&P 500's swing filter reads the hooks its night keeps a pick under, and its heavyweight book a book's
public partial class FixtureExpectations
{
    // The S&P 500 pullback's plan its long test recommends, as the restart's table of 2026-11-10 names it.
    const string PullbackPlan = "1.13 typical days under the close within 20 sessions, 4 typical days under the buy, the first band above";

    static readonly DateTimeOffset SwingAt = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    static PlanCombination PullbackCombination => PlanPrices.Named(PullbackPlan) ?? throw new InvalidOperationException("the pullback's plan names no plan the test walks");

    [Fact]
    public void ASwingFilterRuleStatesAPlanAloneAndEveryOtherHookIsRefusedAtRegistration()
    {
        var planned = new Dictionary<string, double>(SwingFilterRule.ParametersOf(VersionFive), StringComparer.Ordinal);

        foreach (var (name, value) in PlanHook.ParametersOf(PullbackCombination))
        {
            planned[name] = value;
        }

        // A plan alone is read, and none at all.
        Assert.Null(CandidateRegistrar.SwingHooksUnread(RuleHooks.Of(planned)));
        Assert.Null(CandidateRegistrar.SwingHooksUnread(RuleHooks.Of(SwingFilterRule.ParametersOf(VersionFive))));
        Assert.Null(CandidateRegistrar.Refusal([], "the swing filter on the plan its test recommends", SwingFilterRule.EvaluatorName, planned, SwingAt));

        // Each other form is refused, the exit, a condition, the exposure, a ranking and the expected-value order alike.
        foreach (var (hook, value) in new (string, double)[] { ("exit", 31), ("also_close_over_long_above", 1), ("exposure", 0.45), ("ranking", 1), ("order_expected", 1) })
        {
            var stated = new Dictionary<string, double>(SwingFilterRule.ParametersOf(VersionFive), StringComparer.Ordinal) { [hook] = value };
            var refused = CandidateRegistrar.Refusal([], "the swing filter at " + hook, SwingFilterRule.EvaluatorName, stated, SwingAt);

            Assert.NotNull(refused);
            Assert.Contains("a swing filter rule states a plan alone", refused, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheLiveSwingFilterIsGivenItsPlanAndTodaysPlanIsWalkedUnregisteredBesideTheNineItsFamilyKeeps()
    {
        using var store = new TemporaryStore().Migrated();
        var registered = await new CandidateRegistrar(FixedClock.At(SwingAt, SessionZones.UnitedStates), store.DatabaseFile)
            .RegisterTogetherAsync([.. TheSwingFamily.For("1", VersionFive).Take(9)], "register-the-nine");

        Assert.Equal(CandidateRegistrar.Registered, registered.Outcome);

        var live = SwingFamily.LiveCandidate("1");
        var outcome = await new CandidateRegistrar(FixedClock.At(SwingAt.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile)
            .RegisterPlannedAsync(live, PullbackCombination, traded: true, "23.10's second part: the operator's order of 2026-11-13", "register-the-plan");

        Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
        Assert.Contains($"'{live}' registered again bought and sold by {PullbackPlan}", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains($"'{CandidateRegistrar.VariantOf(live, CandidateRegistrar.TodaysPlan)}' walked unregistered, the family keeping 9", outcome.Detail, StringComparison.Ordinal);

        // The live rule retired and registered again at its own parameters with the plan's, nothing else written, and the
        // plan the S&P 500 pullback trades read off it.
        var since = SwingAt.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("retired,registered", Text(store, $"SELECT group_concat(event, ',') FROM (SELECT event FROM candidate_register WHERE (candidate = '{live}' OR retires = '{live}') AND registered_at > '{since}' ORDER BY id);"));
        Assert.Equal(11, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        var rows = await new CandidateRegistrar(FixedClock.At(SwingAt.AddHours(2), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var parameters = CandidateEvaluator.Read(CandidateFamily.Standing(rows, SwingAt.AddHours(2)).Last(row => row.Candidate == live).Parameters);

        Assert.Equal(SwingFilterRule.ParametersOf(VersionFive).OrderBy(pair => pair.Key), RuleHooks.Without(parameters).OrderBy(pair => pair.Key));
        Assert.Equal(PullbackCombination, RuleHooks.Of(parameters).Plan);
        Assert.Equal(PullbackCombination, PlanPointTest.LivePlans(rows, SwingAt.AddHours(2), "GSPC")[SetupFamilies.Pullback]);
        Assert.DoesNotContain(SetupFamilies.Pullback, PlanPointTest.LivePlans(rows, SwingAt.AddHours(2), "MID").Keys);
    }

    [Fact]
    public void TheHeavyweightsRuleStatesABooksHooksAndNoOther()
    {
        var settings = SectorHeavyweightCandidate.ParametersOf(HeavyweightRule.Live);

        Assert.Null(CandidateRegistrar.Refusal([], "the heavyweights with a condition", SectorHeavyweightCandidate.EvaluatorName, new Dictionary<string, double>(settings, StringComparer.Ordinal) { ["also_close_over_long_above"] = 1 }, SwingAt));
        Assert.Null(CandidateRegistrar.Refusal([], "the heavyweights with a score", SectorHeavyweightCandidate.EvaluatorName, new Dictionary<string, double>(settings, StringComparer.Ordinal) { ["score_close_over_long"] = 1 }, SwingAt));
        Assert.Contains(
            "hooks a book does not read",
            CandidateRegistrar.Refusal([], "the heavyweights at an exit", SectorHeavyweightCandidate.EvaluatorName, new Dictionary<string, double>(settings, StringComparer.Ordinal) { ["exit"] = 31 }, SwingAt),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AHeavyweightBooksConditionsKeepAndItsScoreOrdersEachSectorsLeaders()
    {
        // One sector of four companies, each leading it, each above its averages, by lead D, C, B, A; the rule takes two leaders.
        HeavyweightMember Member(string ticker, decimal value, double ownReturn) =>
            new(ticker, ticker + " Inc", "Energy", value, 1_000_000m, ownReturn, 100.0, 90.0, 80.0, 1.2);

        HeavyweightMember[] members = [Member("A", 400m, 0.11), Member("B", 300m, 0.12), Member("C", 200m, 0.13), Member("D", 100m, 0.14), Member("E", 50m, 0.0)];
        var settings = HeavyweightRule.Live with { Largest = HeavyweightRule.EveryCompany, Leaders = 2, HighBeta = false };

        Assert.Equal(["D", "C"], HeavyweightRule.Read(members, settings).Single().Leaders);

        // A condition leaving D off takes C and B; a score ordering A first takes A and the next it scores, the rule's own
        // order breaking no tie; and an order keeping none takes none.
        Assert.Equal(["C", "B"], HeavyweightRule.Read(members, settings, order: eligible => [.. eligible.Where(ticker => ticker != "D")]).Single().Leaders);
        Assert.Equal(["A", "B"], HeavyweightRule.Read(members, settings, order: eligible => [.. eligible.OrderBy(ticker => ticker, StringComparer.Ordinal)]).Single().Leaders);
        Assert.Empty(HeavyweightRule.Read(members, settings, order: _ => []).Single().Leaders);

        // The rule's hooks as the book applies them: kept where the reading holds and ordered by the score, highest first.
        var hooks = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["score_close_over_long"] = 1, ["also_close_over_long_above"] = 0.5 });
        var at = EquityBrief.Core.Ledger.LedgerReadings.IndexOf("close_over_long");
        var readings = new Dictionary<string, double?[]>(StringComparer.Ordinal)
        {
            ["A"] = CatalogueReading(at, 0.6),
            ["B"] = CatalogueReading(at, 0.9),
            ["C"] = CatalogueReading(at, 0.4),
            ["D"] = CatalogueReading(at, 0.7),
        };

        Assert.Equal(["B", "D"], HeavyweightRule.Read(members, settings, order: eligible => hooks.Order(eligible, ticker => readings.GetValueOrDefault(ticker))).Single().Leaders);
    }

    static double?[] CatalogueReading(int at, double value)
    {
        var all = new double?[EquityBrief.Core.Ledger.LedgerReadings.All.Count];

        all[at] = value;

        return all;
    }

    [Fact]
    public void TheWalksBesideAPullbacksLiveRuleArePrintedEachAgainstItsOwnAndTheTightStopIsUnderOneTypicalMove()
    {
        var rule = new RuleUnderTest(SetupFamilies.Pullback, "the live swing filter, version 5", "live", "the swing filter", LoopProcedures.Risks, 20, [(1, 0.2), (2, -0.4), (3, null)])
        {
            Beside = [(PullbackBesideWords.TodaysPlan, [(1, 0.1), (2, null)]), (PullbackBesideWords.StrengthWidened(RuleTests.PullbackWidenedStrength), [])],
        };

        Assert.Equal(
            [
                "pullback, the live swing filter, version 5: today's plan, bought at the next open at its own stop and sale, walked unregistered and written nowhere, 2 trade(s) at +0.100 risks over the test years, against its own 3 trade(s) at -0.100 risks",
                "pullback, the live swing filter, version 5: the uptrend's strength floor at 0.40, widened, walked unregistered and written nowhere, 0 trade(s), none ended over the test years, against its own 3 trade(s) at -0.100 risks",
            ],
            RuleTests.BesideLines(rule, _ => true));
        Assert.DoesNotContain(RuleTests.BesideLines(rule, _ => false), line => line.Contains("risks over", StringComparison.Ordinal));
        Assert.Empty(RuleTests.BesideLines(new RuleUnderTest(SetupFamilies.Pullback, "a variant", "variant", "words", LoopProcedures.Risks, 20, []), _ => true));

        Assert.True(RuleTests.TightStop(0.99));
        Assert.False(RuleTests.TightStop(1.0));
        Assert.Equal((0.40, 5), (RuleTests.PullbackWidenedStrength, RuleTests.PullbackWidenedArrival));
    }

    // On the operator's ruling of 2026-11-14, item 4: a book's stop and take-profit chosen on its months from its family's first
    // test year on the index to 2018 and judged from 2019, by the calendar year of each month's opening session. The S&P 400's
    // heavyweights are judged from 2015, so the choice doing well in 2014 alone is not chosen; the S&P 500's from 2023, so
    // nothing is chosen and the months from 2019 are still judged. Worked by hand in points a month, an edge times 100.
    [Fact]
    public void ABooksStopAndTakeProfitAreChosenFromItsFirstTestYearTo2018AndJudgedFrom2019ByCalendarYear()
    {
        DateOnly[] calendar = [new(2012, 6, 1), new(2014, 6, 2), new(2015, 6, 1), new(2016, 6, 1), new(2018, 6, 1), new(2019, 6, 3), new(2021, 6, 1)];
        var book = new RuleUnderTest(HeavyweightRule.Name, "the live rule", "live", "words", LoopProcedures.Points, 21, Months: [(1, 0.05), (2, 0.01), (3, 0.03), (5, 0.02), (6, -0.04)])
        {
            BookBeside = [("A", [(1, 0.50), (2, -0.01), (3, -0.01), (5, 0.10), (6, 0.10)]), ("B", [(1, -0.50), (2, 0.02), (3, 0.02), (5, -0.10), (6, 0.00)])],
        };

        Assert.Equal(
            "book choice: heavyweight, the live rule: its own +2.00 from 2015 to 2018 over 2 month(s), -1.00 from 2019 over 2; A -1.00 before, +10.00 from; B +2.00 before, -5.00 from; chosen from 2015 to 2018: B, -5.00 points a month from 2019 against its own -1.00, 4.00 points a month short of its own exit from 2019",
            RuleTests.BookChoiceLine(book, "MID", calendar, _ => true));
        Assert.Equal(
            "book choice: heavyweight, the live rule: its own none before 2019 over 0 month(s), -1.00 from 2019 over 2; A none before, +10.00 from; B none before, -5.00 from; no choice holds a month before 2019, its family's first test year on the index being 2023",
            RuleTests.BookChoiceLine(book, "GSPC", calendar, _ => true));
        Assert.Contains("no choice holds a month from 2015 to 2018", RuleTests.BookChoiceLine(book, "MID", calendar, opens => opens >= 5), StringComparison.Ordinal);
    }
}
