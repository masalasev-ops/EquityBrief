using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.35: the grammar taught the live pullback rule's buy, a distance under the close waiting up to a
// window, and its sale at the first band above falling back to a multiple of the risk, planned as the rule's own registration
// plans them; the swing filter's dials as a line's filter, reading constructed setups at two settings by hand; the search's
// two stages, its count and its choice on 2005 to 2018 alone worked by hand; its list, count and time printed and nothing
// walked without the word, on both sides; and a survivor of the search registering its dials as the filter's settings.
// see: The pullbacks' search walks the swing filter's dials as grammar lines, chosen on 2005 to 2018 and judged together from 2019
public partial class FixtureExpectations
{
    internal static readonly string[] PullbackSearchClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The pullbacks' search"),
    ];

    internal static string[] PullbackSearchRows =>
        [.. ComponentAccess.PullbackSearchRows, .. PullbackSearchClaims, .. Reading.ReadSurface.PullbackSearchRows];

    const string LivePullbackPlan = "buy 1.13 typical days under the close within 20 sessions · stop 4 typical days · take profit first band else 2 risks · sell after 63 sessions";

    const string LiveFilterPart = "filter strength 0.5, breadth 0.45, depth 1 to 5, dry-up 1.5, arrival 3, reward 2, stop 0.5 to 4, band 0";

    // Registration 1974's own plan: a distance of 1.13 typical moves under the close within 20 sessions, a stop 4 typical days
    // under the buy, the first band's sale and a take-profit at 2 times the risk alone.
    static IReadOnlyDictionary<string, double> LivePlanParameters { get; } = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [PlanHook.EntryParameter] = PlanHook.Distance,
        [PlanHook.DistanceParameter] = 1.13,
        [PlanHook.WindowParameter] = 20,
        [PlanHook.StopParameter] = 4,
        [PlanHook.SaleParameter] = 1,
        [PlanHook.TakeProfitParameter] = 2,
        [PlanHook.TakeProfitShapeParameter] = PlanHook.AtTheRisk,
    };

    [Fact]
    public void TheGrammarReadsTheLivePullbackRulesBuyAndSaleAndPlansThemAsItsRegistrationDoes()
    {
        var (line, refused) = IdeaGrammar.Parse("event pullback(500) loose · " + LiveFilterPart + " · " + LivePullbackPlan);

        Assert.Null(refused);
        Assert.Equal(PlanHook.Of(LivePlanParameters), Survivors.PlanOf(line!));
        Assert.Equal(PlanHook.Of(LivePlanParameters), IdeaTestRun.CombinationOf(line!));
        Assert.Equal(PlanPoints.DistanceEntry(1.13), line!.Entry);
        Assert.Equal(("first band", (double?)2), (line.Shape, line.Multiple));
        Assert.Equal(line.Canonical, IdeaGrammar.Parse(line.Canonical).Line!.Canonical);
        Assert.Contains("Bought 1.13 typical daily moves under the close, waiting up to 20 sessions", line.Words, StringComparison.Ordinal);
        Assert.Contains("or at 2 times the risk where no band stands above", line.Words, StringComparison.Ordinal);

        // A distance or a multiple off the grammar's lists, and a filter on another family, are refused.
        Assert.Contains("under the close", IdeaGrammar.Parse("event pullback(500) · buy 3.5 typical days under the close within 20 sessions · stop 4 typical days · take profit none · sell after 63 sessions").Refused, StringComparison.Ordinal);
        Assert.Contains("first band", IdeaGrammar.Parse("event pullback(500) · buy next open · stop 4 typical days · take profit first band else 4 risks · sell after 63 sessions").Refused, StringComparison.Ordinal);
        Assert.Contains("a filter reads", IdeaGrammar.Parse("event breakout(500) loose · " + LiveFilterPart + " · buy next open · stop 4 typical days · take profit none · sell after 63 sessions").Refused, StringComparison.Ordinal);
        Assert.Contains("the filter names", IdeaGrammar.Parse("event pullback(500) loose · filter strength 0.45, breadth 0.45, depth 1 to 5, dry-up 1.5, arrival 3, reward 2, stop 0.5 to 4, band 0 · buy next open · stop 4 typical days · take profit none · sell after 63 sessions").Refused, StringComparison.Ordinal);
    }

    // Three setups, each passing every dial at strength 0.4: the first at strength 0.45, the second at 0.55, the third at 0.55
    // with a report 10 sessions off. At a floor of 0.4 the first two are kept and the third is in the earnings window; at 0.5
    // the second alone. A stop of 5 typical moves passes no stop band, and a setup holding no band strength passes nothing.
    [Fact]
    public void TheFilterReadsConstructedSetupsAtTwoSettingsByHand()
    {
        static IdeaSetup Setup(string ticker, double strength, double earnings, double stopMoves = 2) => new(ticker, new DateOnly(2010, 3, 1), new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["strength"] = strength,
            ["breadth"] = 0.6,
            ["depth"] = 2,
            ["dry_up"] = 1.0,
            ["freshness"] = 2,
            ["reward_to_risk"] = 3,
            ["band_strength"] = 1,
            ["earnings_sessions"] = earnings,
            [IdeaFilter.StopMoves] = stopMoves,
        });

        IdeaSetup[] setups = [Setup("AAA", 0.45, 40), Setup("BBB", 0.55, 40), Setup("CCC", 0.55, 10), Setup("DDD", 0.55, 40, stopMoves: 5)];
        var at40 = IdeaGrammar.Parse("event pullback(500) loose · filter strength 0.4, breadth 0.45, depth 1 to 5, dry-up 1.5, arrival 3, reward 2, stop 0.5 to 4, band 0 · " + LivePullbackPlan).Line!;
        var at50 = IdeaGrammar.Parse("event pullback(500) loose · " + LiveFilterPart + " · " + LivePullbackPlan).Line!;

        Assert.Equal(["AAA", "BBB"], IdeaTester.Selected(at40, setups, new Dictionary<string, double[]>()).Select(setup => setup.Ticker));
        Assert.Equal(["BBB"], IdeaTester.Selected(at50, setups, new Dictionary<string, double[]>()).Select(setup => setup.Ticker));
        Assert.False(at50.Filter!.Passes(new Dictionary<string, double>(Setup("EEE", 0.55, 40).Readings.Where(pair => pair.Key != "band_strength"))));
    }

    // The first stage crosses four dials at three levels, 81 lines; from a setting chosen, the second walks the other four
    // dials, 4, 4, 3 and 3 levels besides the chosen's own, 14 lines. The choice reads the early years alone: the line best
    // there is chosen though another is far better from 2019.
    [Fact]
    public void TheSearchsStagesTheirCountAndTheChoiceOnTheEarlyYearsAreWorkedByHand()
    {
        var live = IdeaFilter.Parse(LiveFilterPart)!;
        var first = PullbackSearch.FirstStage("GSPC", live, LivePullbackPlan);

        Assert.Equal(81, first.Count);
        Assert.Equal(81, first.Distinct(StringComparer.Ordinal).Count());
        Assert.All(first, line => Assert.NotNull(IdeaGrammar.Parse(line).Line?.Filter));
        Assert.Contains("event pullback(500) loose · " + LiveFilterPart + " · " + LivePullbackPlan, first);

        var second = PullbackSearch.SecondStage("GSPC", live, LivePullbackPlan);

        Assert.Equal((14, 14), (second.Count, PullbackSearch.SecondStageCount));
        Assert.DoesNotContain(second, line => first.Contains(line));

        var lines = first.Take(2).Select(text => IdeaGrammar.Parse(text).Line!).ToArray();
        IReadOnlyList<(IdeaLine Line, IdeaFigures Figures)> walked =
        [
            (lines[0], new IdeaFigures(100, 0.05, 80, 0.90, [])),
            (lines[1], new IdeaFigures(100, 0.06, 80, -0.10, [])),
        ];

        Assert.Equal(lines[1].Filter, PullbackSearch.Chosen(walked));
    }

    // Over a constructed store holding the live swing filter: without the word the lines, their count and the time are printed
    // and nothing is walked; with it the 81 lines are walked and the second stage named from them. The plan and the filter are
    // read off the registration.
    [Fact]
    public async Task TheSearchPrintsItsListCountAndTimeAndWalksNothingWithoutTheWord()
    {
        using var store = new TemporaryStore().Migrated();
        var evaluator = CandidateEvaluators.Find("swing-filter")!;
        var parameters = "{\"arrivalSessions\": 3, \"bestOf\": 0, \"breadthFloor\": 0.45, \"depthHigh\": 5, \"depthLow\": 1, \"dryUpCeiling\": 1.5, \"earningsWindowSessions\": 15, \"filedState\": 0, \"leaderSectors\": 0, \"leaderShareOf\": 0, \"marketGate\": 1, \"plan_distance\": 1.13, \"plan_entry\": 5, \"plan_sale\": 1, \"plan_stop\": 4, \"plan_take_profit\": 2, \"plan_take_profit_shape\": 1, \"plan_window\": 20, \"raisedEstimates\": 0, \"rewardToRiskFloor\": 2, \"skipDeteriorating\": 0, \"stopHigh\": 4, \"stopLow\": 0.5, \"strengthFloor\": 0.5, \"trade\": 2}";

        store.Execute("INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES "
            + $"(1, '{FamilyRecords.LivePrefix}swing filter, version 5', 'r', 't', 'swing-filter', '{parameters}', '{evaluator.Version}', 'registered', NULL, '2026-11-01T00:00:00Z', NULL);");

        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var walked = new List<IReadOnlyList<string>>();
        PullbackSearch Search(StringWriter said) => new(clock, store.DatabaseFile, store.Root, said)
        {
            Measure = (_, _) => Task.FromResult((TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(12), 4000, 900)),
            Walk = (lines, then, _) =>
            {
                walked.Add(lines);
                walked.Add(then([.. lines.Take(1).Select(text => (IdeaGrammar.Parse(text).Line!, new IdeaFigures(10, 0.1, 10, 0.1, [])))]));

                return Task.FromResult(0);
            },
        };

        var said = new StringWriter();

        Assert.Equal(2, await Search(said).RunAsync("GSPC", word: false));
        Assert.Contains("pullback-search: the live rule, " + FamilyRecords.LivePrefix + "swing filter, version 5: event pullback(500) loose · " + LiveFilterPart + " · " + LivePullbackPlan, said.ToString(), StringComparison.Ordinal);
        Assert.Contains("pullback-search: 81 line(s) in the first stage, then 14 walked one dial at a time from the setting it chooses on 2005 to 2018: 95 in all", said.ToString(), StringComparison.Ordinal);
        Assert.Contains("the walker read in 90 s and the line walked in 12.0 s, 4000 setup(s) offered and 900 bought; about 21 minute(s) for the search", said.ToString(), StringComparison.Ordinal);
        Assert.Contains("the search walks only on the operator's word", said.ToString(), StringComparison.Ordinal);
        Assert.Empty(walked);
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM notebook;"));

        Assert.Equal(0, await Search(new StringWriter()).RunAsync("GSPC", word: true));
        Assert.Equal((81, 14), (walked[0].Count, walked[1].Count));
        Assert.Equal(2, await Search(new StringWriter()).RunAsync("MID", word: true));
    }

    // A survivor of the search registers as the swing filter's variant, its dials the filter's settings; one reading a band
    // strength, a dry-up left off or another index queues, its reason named.
    [Fact]
    public void ASurvivorOfTheSearchRegistersItsDialsAsTheSwingFiltersSettings()
    {
        var line = IdeaGrammar.Parse("event pullback(500) loose · filter strength 0.6, breadth 0.4, depth 1.5 to 5, dry-up 1.25, arrival 5, reward 2.5, stop 1 to 4, band 0 · " + LivePullbackPlan).Line!;
        var settings = Survivors.Parameters(line, new Dictionary<string, double>(StringComparer.Ordinal) { ["strengthFloor"] = 0.5, ["trade"] = 2 }, new Dictionary<string, double[]>());

        Assert.Null(Survivors.CannotRunYet(line));
        Assert.Equal((0.6, 0.4, 1.5, 5.0, 1.25, 5.0, 2.5, 1.0, 4.0, 2.0), (settings["strengthFloor"], settings["breadthFloor"], settings["depthLow"], settings["depthHigh"], settings["dryUpCeiling"], settings["arrivalSessions"], settings["rewardToRiskFloor"], settings["stopLow"], settings["stopHigh"], settings["trade"]));
        Assert.Equal((1.13, 1.0, 2.0, PlanHook.AtTheRisk), (settings[PlanHook.DistanceParameter], settings[PlanHook.SaleParameter], settings[PlanHook.TakeProfitParameter], (int)settings[PlanHook.TakeProfitShapeParameter]));

        Assert.Contains("band strength", Survivors.CannotRunYet(IdeaGrammar.Parse(line.Canonical.Replace("band 0", "band 2", StringComparison.Ordinal)).Line!));
        Assert.Contains("dry-up", Survivors.CannotRunYet(IdeaGrammar.Parse(line.Canonical.Replace("dry-up 1.25", "dry-up off", StringComparison.Ordinal)).Line!));
        Assert.Contains("other gates", Survivors.CannotRunYet(IdeaGrammar.Parse(line.Canonical.Replace("pullback(500)", "pullback(400)", StringComparison.Ordinal)).Line!));
    }
}
