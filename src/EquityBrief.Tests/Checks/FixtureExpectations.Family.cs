using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.5: the swing family. Each of the nine evaluated over constructed input on both
// sides of the settings it holds, the seventh leaving off a member whose reported quarters read
// deteriorating, the eighth, from 14.6, firing only where the member's analysts raised their estimate, in the
// place of the sector leaders' rule, which read leadership in place of the trend and strength gate from 13.9
// and is read here still by its own registration, and the ninth, from the operator's ruling of 2026-10-02,
// firing where the live filter does, the listings stage leaving them to the filter's stage, the filter's stage
// storing their shadow on every member's row with the state and the estimate it handed each, and the one
// command writing the nine registrations and phase 10's three retirements at one instant or none.
public partial class FixtureExpectations
{
    // The operator's ruled settings as the live filter's candidate states them, the trade gate reading
    // section 10's plan for the swing trade.
    static readonly FilterSettings Ruled = FilterSettings.Proposed with
    {
        StrengthFloor = 0.5,
        DepthLow = 1,
        DepthHigh = 5,
        DryUpCeiling = 1.5,
        StopLow = 0.5,
        StopHigh = 4,
        RewardToRiskFloor = 1.5,
        ArrivalSessions = 3,
        Trade = TradeInput.Clear,
    };

    static IReadOnlyDictionary<string, bool> Fires(GateInputs inputs) =>
        TheSwingFamily.For("1", Ruled).ToDictionary(
            one => one.Candidate,
            one => new SwingFilterRule().EvaluateGates(inputs, one.Parameters).Fired,
            StringComparer.Ordinal);

    static readonly string Live = SwingFamily.LiveCandidate("1");

    // A variant's name as the family defined against version 1 writes it.
    static string Variant(string name) => TheSwingFamily.Variant(name, "1");

    static readonly string NearestBands = Variant(TheSwingFamily.NearestBandsName);

    // The member that passes every gate on both swing plans. Entered at 102 on a typical move of 4, the
    // setup band 100 to 104 half a move below: the plan at the nearest bands stops at 100 and section 10's
    // at the next support band's low edge, 95, 1.75 moves below; both target the band at 120, the nearest
    // above the close and 4.5 moves up. It leads at the edge of the top sectors and of its own sector's
    // top quarter, second of the eight holding a return in the third sector ranked, and its analysts raised
    // their estimate for its year by a cent over the thirty days before.
    static GateInputs FamilyPassing() =>
        Passing() with
        {
            Bands = [new FilterBand(95m, 96m, "support", 3, true), .. Passing().Bands],
            Leadership = Standing(3, 2),
            Estimates = Raised,
        };

    static readonly DateOnly YearEnd = new(2026, 12, 31);

    static readonly EstimateReading Raised = new(YearEnd, 5.01m, 5.00m);

    // A member's standing on the night: its sector's rank, none where the sector is too small to rank, and
    // its place among the sector's members holding a return, the cut the top quarter of them rounded up.
    static Core.Families.LeaderStanding Standing(int? rank, int? place, int counted = 8) =>
        new(new Core.Families.SectorStanding("Alpha", counted, 0.45, rank), place, Core.Families.LeaderRule.Cut(counted, Core.Families.LeaderRule.QuarterOf));

    // The same member with its target band moved to set each plan's reward to risk: (target - 102) / 7 on
    // section 10's plan wherever the target is two typical moves or more above the entry, 110, and
    // (target - 102) / 2 on the plan at the nearest bands.
    static GateInputs AtRewardToRisk(decimal target) =>
        Passing() with { Bands = [new FilterBand(95m, 96m, "support", 3, true), new FilterBand(100m, 104m, "support", 10, true), new FilterBand(target, target + 1, "resistance", 5, true)] };

    [Fact]
    public void EachOfTheNineFiresOnItsOwnSideOfEverySettingItMovesAndNotAStepPastIt()
    {
        // Worked by hand. The passing member: breadth 0.75, an uptrend at strength 0.8, a pullback 3 moves
        // deep on a dry-up of 0.8 inside the band 100 to 104, its event tonight and none on the session
        // before; section 10's plan from 102 to 120 over a stop at 95, 18 / 7 = 2.57 with the stop 7 / 4 =
        // 1.75 typical moves below, and the plan at the nearest bands over a stop at 100, 18 / 2 = 9 with
        // the stop 2 / 4 = 0.5 below; second of eight in the third sector. Every one of the nine fires, the
        // best three where the live filter does, since its count is read where its record is read.
        Assert.All(Fires(FamilyPassing()), fire => Assert.True(fire.Value, fire.Key));
        Assert.Equal(9, Fires(FamilyPassing()).Count);

        // The plan: the live filter reads section 10's, the variant the nearest bands'. The live floor of 1.5
        // at a target of 112.5, 10.5 / 7, and not at 112.49, 1.4986. A target at 105 is 0.75 typical moves
        // up, inside the noise for section 10's plan, which then has no target, and 3 / 2 = 1.5 on the
        // nearest bands', which fires; at 104.98, 1.49, it does not.
        Assert.True(Fires(AtRewardToRisk(112.5m))[Live]);
        Assert.False(Fires(AtRewardToRisk(112.49m))[Live]);
        Assert.Equal((false, true), (Fires(AtRewardToRisk(105m))[Live], Fires(AtRewardToRisk(105m))[NearestBands]));
        Assert.False(Fires(AtRewardToRisk(104.98m))[NearestBands]);

        // A band 0.625 typical moves above the entry, at 104.5: the nearest bands' plan targets it at 2.5 / 2
        // = 1.25 and does not fire, and section 10's reaches past it to 120 and does.
        var nearBand = FamilyPassing() with { Bands = [.. FamilyPassing().Bands, new FilterBand(104.5m, 104.6m, "resistance", 2, true)] };

        Assert.Equal((true, false), (Fires(nearBand)[Live], Fires(nearBand)[NearestBands]));

        // The pullback's depth: live 1 to 5, the variant 1 to 3.
        GateInputs Deep(double depth) => FamilyPassing() with { Reading = FamilyPassing().Reading! with { Depth = depth } };

        Assert.Equal((true, false), (Fires(Deep(5.0))[Live], Fires(Deep(5.01))[Live]));
        Assert.Equal((true, false), (Fires(Deep(1.0))[Live], Fires(Deep(0.99))[Live]));
        Assert.Equal((true, false), (Fires(Deep(3.0))[Variant(TheSwingFamily.DepthName)], Fires(Deep(3.01))[Variant(TheSwingFamily.DepthName)]));
        Assert.True(Fires(Deep(3.01))[Live]);

        // The dry-up, which every one of the nine holds at the live 1.5.
        GateInputs Dry(double dryUp) => FamilyPassing() with { Reading = FamilyPassing().Reading! with { DryUp = dryUp } };

        Assert.All(Fires(Dry(1.49)), fire => Assert.True(fire.Value, fire.Key));
        Assert.All(Fires(Dry(1.5)), fire => Assert.False(fire.Value, fire.Key));

        // Strength: live 0.5, the variant two thirds.
        Assert.Equal((true, false), (Fires(FamilyPassing() with { Strength = 0.5 })[Live], Fires(FamilyPassing() with { Strength = 0.49 })[Live]));
        Assert.Equal(
            (true, false, true),
            (Fires(FamilyPassing() with { Strength = 2.0 / 3.0 })[Variant(TheSwingFamily.StrengthName)], Fires(FamilyPassing() with { Strength = 0.66 })[Variant(TheSwingFamily.StrengthName)], Fires(FamilyPassing() with { Strength = 0.66 })[Live]));

        // The stop's distance, 0.5 to 4 typical moves. Section 10's plan on a typical move of 0.5 stops at the
        // setup band, 2 / 0.5 = 4 moves below, and fires, and on 0.49, 4.08 moves, does not; the nearest
        // bands' plan on 4 stops 2 / 4 = 0.5 below and fires, and on 4.01 does not.
        Assert.Equal((true, false), (Fires(FamilyPassing() with { TypicalMove = 0.5 })[Live], Fires(FamilyPassing() with { TypicalMove = 0.49 })[Live]));
        Assert.Equal((true, false), (Fires(FamilyPassing() with { TypicalMove = 4 })[NearestBands], Fires(FamilyPassing() with { TypicalMove = 4.01 })[NearestBands]));

        // The market: the live floor 0.5 at a breadth of 0.5 and not at 0.49, which the variant that reads
        // no market fires on, as it does on a night whose breadth is not available.
        GateInputs AtBreadth(double? share) => FamilyPassing() with { Breadth = new Breadth(100, 100, 50, share) };

        Assert.Equal((true, false), (Fires(AtBreadth(0.5))[Live], Fires(AtBreadth(0.49))[Live]));
        Assert.True(Fires(AtBreadth(0.49))[Variant(TheSwingFamily.MarketOffName)]);
        Assert.True(Fires(FamilyPassing() with { Breadth = null })[Variant(TheSwingFamily.MarketOffName)]);
        Assert.False(Fires(FamilyPassing() with { Breadth = null })[Live]);

        // Arrival: tonight's close at the previous high of 101 is no event, and section 10's stop at 95, 6 / 2
        // = 3 moves below. Fired on 09-04 and not 09-03 arrives one session back, inside the live window of
        // three and outside the variant's one; fired back to 09-02 and not 09-01 arrives two back, inside
        // the live window; fired on every session back to 09-01, it did not arrive in three.
        GateInputs Arrived(bool on03, bool on02, bool on01) => FamilyPassing() with
        {
            Close = 101m,
            TypicalMove = 2,
            TriggerFiredTheSessionBefore = true,
            Earlier = [new SessionEvent(new DateOnly(2026, 9, 3), on03), new SessionEvent(new DateOnly(2026, 9, 2), on02), new SessionEvent(new DateOnly(2026, 9, 1), on01)],
        };

        Assert.Equal((true, false), (Fires(Arrived(false, false, false))[Live], Fires(Arrived(false, false, false))[Variant(TheSwingFamily.ArrivalName)]));
        Assert.True(Fires(Arrived(true, false, false))[Live]);
        Assert.False(Fires(Arrived(true, true, true))[Live]);
        Assert.True(Fires(FamilyPassing())[Variant(TheSwingFamily.ArrivalName)]);

        // An exclusion leaves every one of the nine unfired.
        Assert.All(Fires(FamilyPassing() with { Suspect = true }), fire => Assert.False(fire.Value, fire.Key));

        // The analysts' revisions, the eighth, firing where the base fires and the current fiscal year's estimate
        // stands above its level thirty days before: raised by a cent fires, level and lowered do not, and an
        // estimate the night did not read, or read as not filed, does not; the other eight fire whatever the
        // estimate. A member the base does not pass does not fire it however its estimate moved, and is the one
        // the night does not ask the provider about.
        // see: The pullback's sector leaders' rule is retired and the analysts' revisions variant registered in its place
        // see: A member's estimates are raised where its current fiscal year's consensus earnings estimate stands above its level 30 days before
        var revisions = Variant(TheSwingFamily.RevisionsName);

        GateInputs Estimated(EstimateReading? reading) => FamilyPassing() with { Estimates = reading };

        Assert.Equal(
            (true, false, false),
            (Fires(Estimated(new(YearEnd, 5.01m, 5.00m)))[revisions], Fires(Estimated(new(YearEnd, 5.00m, 5.00m)))[revisions], Fires(Estimated(new(YearEnd, 4.99m, 5.00m)))[revisions]));
        Assert.All(
            new EstimateReading?[] { null, EstimateReading.Unread("the provider did not serve it"), new(YearEnd, 5.01m, null) },
            reading => Assert.False(Fires(Estimated(reading))[revisions]));
        Assert.Equal(8, Fires(Estimated(null)).Count(fire => fire.Value));
        Assert.True(Fires(Estimated(null))[Live]);
        Assert.False(Fires(Estimated(Raised) with { Strength = 0.49 })[revisions]);

        var reading = TheSwingFamily.For("1", Ruled).Single(one => one.Candidate == revisions).Parameters;

        Assert.Equal(1.0, reading[SwingFilterRule.RaisedEstimatesParameter]);
        Assert.All(TheSwingFamily.For("1", Ruled).Where(one => one.Candidate != revisions), one => Assert.Equal(0.0, one.Parameters[SwingFilterRule.RaisedEstimatesParameter]));
        Assert.Equal(
            (true, false, false),
            (SwingFilterRule.AsksForEstimates(FamilyPassing() with { Estimates = null }, reading),
             SwingFilterRule.AsksForEstimates(FamilyPassing() with { Estimates = null, Strength = 0.49 }, reading),
             SwingFilterRule.AsksForEstimates(FamilyPassing() with { Estimates = null }, SwingFilterRule.ParametersOf(Ruled))));

        // Its verdict names the estimate it read and why it read none where it read none.
        Assert.Equal(
            ("raised: 5.01 now against 5.00 30 days before, for the year to 2026-12-31", "not read: the night asked for none", "not read: the provider did not serve it"),
            (new SwingFilterRule().EvaluateGates(FamilyPassing(), reading).Values[SwingFilterRule.EstimatesValue],
             new SwingFilterRule().EvaluateGates(Estimated(null), reading).Values[SwingFilterRule.EstimatesValue],
             new SwingFilterRule().EvaluateGates(Estimated(EstimateReading.Unread("the provider did not serve it")), reading).Values[SwingFilterRule.EstimatesValue]));
        Assert.DoesNotContain(SwingFilterRule.EstimatesValue, new SwingFilterRule().EvaluateGates(FamilyPassing(), SwingFilterRule.ParametersOf(Ruled)).Values.Keys);

        // Sector leadership, the rule the revisions replaced, still read by its own registration's parameters at the
        // top 3 sectors and the top quarter of each in place of the trend and strength gate, so the rows it stored are
        // read as it read them. Third sector and second of eight, the cut 8 / 4 = 2, leads, and fourth or third does
        // not; in a sector of nine the cut rounds up to 3, so third leads and fourth does not. A sector holding too few
        // to rank, a member the night ranked in no sector and one it read no standing for lead nothing, and the nine
        // fire whatever the standing.
        // see: The sector leaders are a variant of the pullback's starting point and not a family of their own
        var leadersRule = TheSwingFamily.Leaders("1", Ruled);

        bool Leads(GateInputs inputs) => new SwingFilterRule().EvaluateGates(inputs, leadersRule.Parameters).Fired;

        GateInputs Led(Core.Families.LeaderStanding? standing) => FamilyPassing() with { Leadership = standing };

        Assert.Equal(Variant(TheSwingFamily.LeadersName), leadersRule.Candidate);
        Assert.DoesNotContain(TheSwingFamily.For("1", Ruled), one => one.Candidate == leadersRule.Candidate);
        Assert.Equal((true, false), (Leads(Led(Standing(3, 2))), Leads(Led(Standing(4, 2)))));
        Assert.False(Leads(Led(Standing(3, 3))));
        Assert.Equal((true, false), (Leads(Led(Standing(1, 3, counted: 9))), Leads(Led(Standing(1, 4, counted: 9)))));
        Assert.All(
            new Core.Families.LeaderStanding?[] { Standing(null, 1, counted: 4), new(null, null, 0), null },
            standing => Assert.False(Leads(Led(standing))));
        Assert.Equal(9, Fires(Led(Standing(4, 2))).Count(fire => fire.Value));
        Assert.True(Fires(Led(null))[Live]);

        // The trend and strength gate is not read where leadership is: a leader at strength 0.49 fires the
        // leaders' rule and not the live filter, and one in no uptrend the same.
        Assert.Equal((true, false), (Leads(FamilyPassing() with { Strength = 0.49 }), Fires(FamilyPassing() with { Strength = 0.49 })[Live]));
        Assert.Equal((true, false), (Leads(FamilyPassing() with { TrendState = "range" }), Fires(FamilyPassing() with { TrendState = "range" })[Live]));

        // Its verdict says where the member stood, and its parameters state the settings it reads.
        Assert.Equal(
            ("sector 3 of the top 3, place 2 of 8 against 2, leading", "sector 4 of the top 3, place 2 of 8 against 2, not leading", "not ranked"),
            (new SwingFilterRule().EvaluateGates(FamilyPassing(), SwingFilterRule.ParametersOf(Ruled, leadership: Core.Families.LeaderRule.Live)).Values[SwingFilterRule.LeadershipValue],
             new SwingFilterRule().EvaluateGates(Led(Standing(4, 2)), SwingFilterRule.ParametersOf(Ruled, leadership: Core.Families.LeaderRule.Live)).Values[SwingFilterRule.LeadershipValue],
             new SwingFilterRule().EvaluateGates(Led(null), SwingFilterRule.ParametersOf(Ruled, leadership: Core.Families.LeaderRule.Live)).Values[SwingFilterRule.LeadershipValue]));
        Assert.Equal((3.0, 4.0, 0.0, 0.0), (
            SwingFilterRule.ParametersOf(Ruled, leadership: Core.Families.LeaderRule.Live)[SwingFilterRule.LeaderSectorsParameter],
            SwingFilterRule.ParametersOf(Ruled, leadership: Core.Families.LeaderRule.Live)[SwingFilterRule.LeaderShareOfParameter],
            SwingFilterRule.ParametersOf(Ruled)[SwingFilterRule.LeaderSectorsParameter],
            SwingFilterRule.ParametersOf(Ruled)[SwingFilterRule.LeaderShareOfParameter]));
        Assert.DoesNotContain(SwingFilterRule.LeadershipValue, new SwingFilterRule().EvaluateGates(FamilyPassing(), SwingFilterRule.ParametersOf(Ruled)).Values.Keys);

        // The business state: the seventh leaves off a member whose reported quarters read deteriorating on
        // the night and fires on improving, steady, not enough quarters, no fundamentals yet and a state the
        // night did not store; the live filter and the other seven read no state and fire on deteriorating as
        // on any other.
        // see: The seventh swing family candidate leaves off a member whose reported quarters read deteriorating, and no live rule removes a stock for its state
        var skipping = Variant(TheSwingFamily.DeterioratingName);

        GateInputs InState(string? state) => FamilyPassing() with { FundamentalState = state };

        Assert.False(Fires(InState(FundamentalState.Deteriorating))[skipping]);
        Assert.True(Fires(InState(FundamentalState.Deteriorating))[Live]);
        Assert.Equal(8, Fires(InState(FundamentalState.Deteriorating)).Count(fire => fire.Value));
        Assert.All(
            new[] { FundamentalState.Improving, FundamentalState.Steady, FundamentalState.NotEnoughQuarters, FundamentalState.NoFundamentalsYet, null },
            state => Assert.True(Fires(InState(state))[skipping], state ?? "no state stored"));
        Assert.All(Fires(InState(FundamentalState.Improving)), fire => Assert.True(fire.Value, fire.Key));

        var skipped = new SwingFilterRule().EvaluateGates(InState(FundamentalState.Deteriorating), SwingFilterRule.ParametersOf(Ruled, skipDeteriorating: true));
        var kept = new SwingFilterRule().EvaluateGates(InState(null), SwingFilterRule.ParametersOf(Ruled));

        Assert.Equal((false, FundamentalState.Deteriorating, "yes"), (skipped.Fired, skipped.Values[SwingFilterRule.StateValue], skipped.Values["skips deteriorating"]));
        Assert.Equal((true, SwingFilterRule.StateNotRead, "no"), (kept.Fired, kept.Values[SwingFilterRule.StateValue], kept.Values["skips deteriorating"]));
        Assert.Equal(1.0, SwingFilterRule.ParametersOf(Ruled, skipDeteriorating: true)[SwingFilterRule.SkipDeterioratingParameter]);
        Assert.Equal(0.0, SwingFilterRule.ParametersOf(Ruled)[SwingFilterRule.SkipDeterioratingParameter]);

        // The verdict names each gate's answer, the market read, the exclusions, the session it arrived on
        // and the plan its trade gate read, which is the plan its setup is scored on.
        var verdict = new SwingFilterRule().EvaluateGates(AtBreadth(0.49), SwingFilterRule.ParametersOf(Ruled, marketGate: false));

        Assert.Equal(("failed", "no", "none", "tonight", "clear"), (verdict.Values[SwingGates.Market], verdict.Values["market read"], verdict.Values["exclusions"], verdict.Values["arrived"], verdict.Values[SwingFilterRule.PlanValue]));
        Assert.Equal("swing", new SwingFilterRule().EvaluateGates(FamilyPassing(), SwingFilterRule.ParametersOf(Ruled with { Trade = TradeInput.Swing })).Values[SwingFilterRule.PlanValue]);
        Assert.Equal(Ruled, SwingFilterRule.SettingsOf(SwingFilterRule.ParametersOf(Ruled)));
        Assert.Equal(
            [0.0, 1.0, 2.0],
            [.. new[] { TradeInput.Ladder, TradeInput.Swing, TradeInput.Clear }.Select(input => SwingFilterRule.ParametersOf(Ruled with { Trade = input })[SwingFilterRule.TradeParameter])]);
    }

    [Fact]
    public void TheListingsStageLeavesTheFamilyToTheFiltersStageAndTheFiltersStageSkipsAMemberItHoldsNoBarFor()
    {
        var evaluator = new SwingFilterRule();
        var at = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        RegisterRow Row(long id, string candidate, string name, string version, IReadOnlyDictionary<string, double> parameters) =>
            new(id, candidate, "a rule", "a test", name, CandidateEvaluator.Write(parameters), version, CandidateFamily.Registered, null, at, null);

        IReadOnlyList<RegisterRow> standing =
        [
            Row(1, Live, SwingFilterRule.EvaluatorName, evaluator.Version, SwingFilterRule.ParametersOf(Ruled)),
            Row(2, "arrived and narrow", ArrivedAndNarrow.EvaluatorName, new ArrivedAndNarrow().Version, new Dictionary<string, double> { [ArrivedAndNarrow.Width] = 1 }),
        ];

        // The listings stage evaluates its own and says nothing of the family's.
        var listings = ShadowColumn.Evaluate(standing, new CandidateNight("ZZA", GateNight, new Dictionary<string, double>()));

        Assert.DoesNotContain(listings.Outcomes, one => one.Candidate == Live);
        Assert.DoesNotContain(listings.Skipped, one => one.Candidate == Live);
        Assert.Contains(listings.Skipped, one => one.Candidate == "arrived and narrow");

        // The filter's stage evaluates the family alone, a member it holds no bar for skipped with the reason,
        // and a moved evaluator a fault.
        Assert.Equal([Live], ShadowColumn.ForTheFilter(standing).Select(one => one.Candidate));

        var filter = ShadowColumn.EvaluateGates(ShadowColumn.ForTheFilter(standing), FamilyPassing());

        Assert.Equal((Live, true), (Assert.Single(filter.Outcomes).Candidate, filter.Outcomes[0].Fired));

        var stale = ShadowColumn.EvaluateGates(ShadowColumn.ForTheFilter(standing), Passing(), new NameWithheld(ShadowSkipCause.Stale, "no bar is stored for ZZA on 2026-09-08"));

        Assert.Empty(stale.Outcomes);
        Assert.Equal(ShadowSkipCause.Stale, Assert.Single(stale.Skipped).Cause);

        var moved = ShadowColumn.EvaluateGates([Row(1, Live, SwingFilterRule.EvaluatorName, "000000000001", SwingFilterRule.ParametersOf(Ruled))], Passing());

        Assert.True(Assert.Single(moved.Skipped).IsFault);
        Assert.Equal(3, ShadowColumn.ArrivalReach(ShadowColumn.ForTheFilter(standing)));

        // The night's shadow as the filter's stage runs it: the family standing at the night's start, a
        // member the night holds no bar for skipped with the reason and counted, and one it does evaluated.
        // A night that started in the second the family registered, or before it, holds none of it, which is
        // what keeps a night run again for an earlier session from scoring a candidate registered after it.
        Assert.Equal(0, FamilyShadow.For(standing, at).Standing);
        Assert.Equal(0, FamilyShadow.For(standing, at.AddDays(-1)).Standing);
        Assert.Equal("; no swing family candidate stands registered, so nothing was evaluated in shadow", FamilyShadow.For(standing, at).Said);

        var night = FamilyShadow.For(standing, at.AddSeconds(1));

        Assert.Equal(1, night.Standing);

        using (var skipped = JsonDocument.Parse(night.Evaluate(Passing(), stale: true, gap: null)))
        {
            Assert.Empty(skipped.RootElement.GetProperty("candidates").EnumerateArray());
            Assert.Equal("no bar is stored for the night's session", Assert.Single(skipped.RootElement.GetProperty("skipped").EnumerateArray()).GetProperty("reason").GetString());
        }

        using (var gapped = JsonDocument.Parse(night.Evaluate(Passing(), stale: false, gap: new DateOnly(2026, 9, 2))))
        {
            Assert.Equal("the stored series has a gap at 2026-09-02, so nothing is computed across it", Assert.Single(gapped.RootElement.GetProperty("skipped").EnumerateArray()).GetProperty("reason").GetString());
        }

        night.Evaluate(Passing(), stale: false, gap: null);

        Assert.Equal((1, 1), (night.Standing, night.Evaluated));
        Assert.Equal("; 1 swing family candidate(s) registered, 1 shadow evaluation(s) written, 2 skipped on a member without the readings: 1 stale, 1 gapped", night.Said);
    }

    // A store holding phase 10's three standing and filter version 1 open on the ruled settings.
    internal static async Task<TemporaryStore> FamilyStore(DateTimeOffset threeAt, bool versionOpen = true)
    {
        var store = FilterStore();

        if (versionOpen)
        {
            store.Execute($"INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{Ruled.Write()}', '2026-09-05T22:00:00Z', NULL, 'the ruling');");
        }

        var three = await new CandidateRegistrar(FixedClock.At(threeAt, SessionZones.UnitedStates), store.DatabaseFile).RegisterTogetherAsync(TheThreeCandidates.All, "register-three");

        Assert.Equal(CandidateRegistrar.Registered, three.Outcome);

        return store;
    }

    internal static async Task<(int Code, string Said)> RegisterVerbAt(TemporaryStore store, DateTimeOffset at, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await RegisterVerb.RunAsync(["register", .. args], FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile, output, error);

        return (code, output.ToString() + error.ToString());
    }

    [Fact]
    public async Task TheFamilysCommandWritesNineRegistrationsAndThreeRetirementsAtOneInstantOrNone()
    {
        var threeAt = new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
        var familyAt = new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero);

        // With no version open the live filter's candidate has no settings to state, and nothing is written.
        using (var closed = await FamilyStore(threeAt, versionOpen: false))
        {
            var (code, said) = await RegisterVerbAt(closed, familyAt, RegisterVerb.TheFamily);

            Assert.Equal(1, code);
            Assert.Contains("no filter version is open", said, StringComparison.Ordinal);
            Assert.Equal(3, Scalar(closed, "SELECT COUNT(*) FROM candidate_register;"));
        }

        // One of the three already retired refuses all twelve, since a retirement names a candidate that stands.
        using (var one = await FamilyStore(threeAt))
        {
            Assert.Equal(0, (await RegisterVerbAt(one, threeAt.AddHours(1), "--retire", TheThreeCandidates.CrossedByAMarginName, "--evidence", "an earlier retirement")).Code);

            var (code, said) = await RegisterVerbAt(one, familyAt, RegisterVerb.TheFamily);

            Assert.Equal(1, code);
            Assert.Equal(12, TheSwingFamily.RowsAtOnce);
            Assert.Contains($"'{TheThreeCandidates.CrossedByAMarginName}' was refused, so none of the 12 rows was written", said, StringComparison.Ordinal);
            Assert.Equal(4, Scalar(one, "SELECT COUNT(*) FROM candidate_register;"));
        }

        using var store = await FamilyStore(threeAt);

        var (written, told) = await RegisterVerbAt(store, familyAt, RegisterVerb.TheFamily);

        Assert.Equal(0, written);
        Assert.Contains("retired 3 and registered 9 at one instant, the live filter at filter version 1, family of 9 of 9", told, StringComparison.Ordinal);

        // Twelve rows after the three, all at one instant.
        Assert.Equal(15, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(DISTINCT registered_at) FROM candidate_register WHERE id > 3;"));
        Assert.Equal("2026-09-07T22:00:00Z", Text(store, "SELECT MIN(registered_at) FROM candidate_register WHERE id > 3;"));

        // Each retirement carries the words saying no result of the candidate it retires was read.
        Assert.Equal(
            [.. TheThreeCandidates.All.Select(one => one.Candidate).Order(StringComparer.Ordinal)],
            [.. TextRows(store, "SELECT retires FROM candidate_register WHERE event = 'retired' ORDER BY retires;")]);
        Assert.All(TextRows(store, "SELECT evidence FROM candidate_register WHERE event = 'retired';"), evidence => Assert.Contains(TheSwingFamily.NothingRead, evidence, StringComparison.Ordinal));

        // The nine are the family, each on the one evaluator at its version, the live filter stating the open
        // version's settings and each variant those with one thing moved, named for version 1, the seventh
        // leaving a deteriorating business off, the eighth reading analysts' revisions, in the place the sector
        // leaders' rule held until it was retired, and the ninth keeping the night's best three, each stating so in
        // its rule and its parameters.
        var revisions = Variant(TheSwingFamily.RevisionsName);
        var bestThree = Variant(TheSwingFamily.BestThreeName);

        Assert.Equal(
            [Live, NearestBands, Variant(TheSwingFamily.DepthName), Variant(TheSwingFamily.MarketOffName), Variant(TheSwingFamily.StrengthName), Variant(TheSwingFamily.ArrivalName), Variant(TheSwingFamily.DeterioratingName), revisions, bestThree],
            TextRows(store, "SELECT candidate FROM candidate_register WHERE event = 'registered' AND id > 3 ORDER BY id;"));
        Assert.Equal("the swing filter with estimates raised over the last 30 days, from version 1", revisions);
        Assert.Equal(1.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Variant(TheSwingFamily.DeterioratingName)}';"))[SwingFilterRule.SkipDeterioratingParameter]);
        Assert.Equal(0.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Live}';"))[SwingFilterRule.SkipDeterioratingParameter]);
        Assert.Equal(TheSwingFamily.RuleSkippingDeteriorating, Text(store, $"SELECT rule FROM candidate_register WHERE candidate = '{Variant(TheSwingFamily.DeterioratingName)}';"));
        Assert.Equal(TheSwingFamily.RuleReadingEstimates, Text(store, $"SELECT rule FROM candidate_register WHERE candidate = '{revisions}';"));
        Assert.Equal(
            (1.0, 0.0, 0.0),
            (CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{revisions}';"))[SwingFilterRule.RaisedEstimatesParameter],
             CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{revisions}';"))[SwingFilterRule.LeaderSectorsParameter],
             CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Live}';"))[SwingFilterRule.RaisedEstimatesParameter]));
        Assert.Equal(Ruled, SwingFilterRule.SettingsOf(CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{revisions}';"))));
        Assert.Equal(TheSwingFamily.Rule, Text(store, $"SELECT rule FROM candidate_register WHERE candidate = '{Live}';"));
        Assert.All(TextRows(store, "SELECT evaluator || '|' || evaluator_version FROM candidate_register WHERE event = 'registered' AND id > 3;"), row => Assert.Equal(SwingFilterRule.EvaluatorName + "|" + new SwingFilterRule().Version, row));
        Assert.Equal(Ruled, SwingFilterRule.SettingsOf(CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Live}';"))));
        Assert.Equal(
            Ruled with { Trade = TradeInput.Swing },
            SwingFilterRule.SettingsOf(CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{NearestBands}';"))));
        Assert.Equal(0.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Variant(TheSwingFamily.MarketOffName)}';"))[SwingFilterRule.MarketGateParameter]);

        // The best three state the live settings and three a night, and every other rule nought.
        var bestParameters = CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{bestThree.Replace("'", "''", StringComparison.Ordinal)}';"));

        Assert.Equal("the night's best three, in the list's own order, from version 1", bestThree);
        Assert.Equal((Ruled, 3.0), (SwingFilterRule.SettingsOf(bestParameters), bestParameters[SwingFilterRule.BestOfParameter]));
        Assert.Equal(0.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Live}';"))[SwingFilterRule.BestOfParameter]);
        Assert.Equal(TheSwingFamily.RuleKeepingTheBest, Text(store, $"SELECT rule FROM candidate_register WHERE candidate = '{bestThree.Replace("'", "''", StringComparison.Ordinal)}';"));

        // A second run is refused whole, the three no longer standing.
        Assert.Equal(1, (await RegisterVerbAt(store, familyAt.AddHours(1), RegisterVerb.TheFamily)).Code);
        Assert.Equal(15, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }

    [Fact]
    public async Task TheFiltersStageStoresEachFamilyCandidatesVerdictOnEveryMembersRow()
    {
        using var store = await FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, (await RegisterVerbAt(store, new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero), RegisterVerb.TheFamily)).Code);

        // ZZB's strength set to 0.6, above the live floor of 0.5 and below the variant's two thirds.
        store.Execute("UPDATE swing_reading SET strength = 0.6, place_short = 0.6, place_long = 0.6 WHERE ticker = 'ZZB';");

        // The night's fundamental readings: ZZA's reported quarters read deteriorating and ZZC's improving,
        // and ZZB and ZZD have no reading stored.
        store.Execute(
            "INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) VALUES " +
            $"('ZZA', '{FilterNight}', '{FundamentalState.Deteriorating}', NULL, NULL, NULL, '{{}}'), " +
            $"('ZZC', '{FilterNight}', '{FundamentalState.Improving}', NULL, NULL, NULL, '{{}}');");

        // The night's sectors: the four and ZZE in Alpha, the five a sector is ranked on, ZZE holding a long
        // return of 30 and no bar on the night, ZZD 20 and the other three 10 each. Alpha ranks first, alone,
        // and the top quarter of its five rounded up is 2, so ZZE and ZZD lead, and ZZA, ZZB and ZZC, third to
        // fifth on the ticker, do not.
        store.Execute(
            "UPDATE membership SET sector = 'Alpha';" +
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) VALUES ('GSPC', 'ZZE', NULL, NULL, '2026-09-05T21:00:00Z', 'Alpha');" +
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('ZZE', '{FilterBefore}', '100', '101', '99', '100', 1000, 'test', '2026-09-04T21:00:00Z', '100');" +
            $"INSERT INTO swing_reading (ticker, session_date, bars, return_long) VALUES ('ZZE', '{FilterNight}', 252, 30);" +
            "UPDATE swing_reading SET return_long = 20 WHERE ticker = 'ZZD';");

        // Worked by hand under the ruled settings: ZZA, ZZB and ZZC pass every gate, section 10's plan from
        // 102 over a stop at 95 to 120 or 126 and the plan at the nearest bands over a stop at 100, and their
        // events tonight with none on the session before; ZZD is in a range and passes every other gate. So
        // the eighth, reading analysts' revisions, asks for the estimates of ZZA, ZZB and ZZC, and fires on ZZC,
        // whose estimate was raised, alone. ZZC fires for all nine, ZZA for all but the seventh, which leaves a
        // deteriorating business off, and the eighth, its estimate standing level, ZZB for all but the strength
        // variant and the eighth, its state not stored and so not read against and its estimate not served, and
        // ZZD for none; the ninth fires where the live filter does. ZZE, holding no bar on the night, is skipped
        // by all nine and asked about by none.
        var family = FamilyShadow.For(
            await new CandidateRegistrar(FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(),
            FilterEvening.AddMinutes(-30));
        var estimates = new ConstructedEstimates(new Dictionary<string, EstimateReading>(StringComparer.Ordinal)
        {
            ["ZZA"] = new(YearEnd, 5.00m, 5.00m),
            ["ZZB"] = EstimateReading.Unread("the provider did not serve it"),
            ["ZZC"] = Raised,
        });
        var outcome = await new SwingFilter(FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "filter-family", family, estimates: estimates);

        Assert.Equal((36, 0), (outcome.Evaluated, outcome.Faults!.Count));

        IReadOnlyDictionary<string, bool> FiredOn(string ticker)
        {
            using var document = JsonDocument.Parse(Text(store, $"SELECT shadow FROM gate_result WHERE ticker = '{ticker}' AND session_date = '{FilterNight}';"));

            return document.RootElement.GetProperty("candidates").EnumerateArray().ToDictionary(
                one => one.GetProperty("candidate").GetString()!,
                one => one.GetProperty("fired").GetBoolean(),
                StringComparer.Ordinal);
        }

        // The state each candidate read on a member's row, as the filter handed it to the shadow.
        string StateReadOn(string ticker, string candidate)
        {
            using var document = JsonDocument.Parse(Text(store, $"SELECT shadow FROM gate_result WHERE ticker = '{ticker}' AND session_date = '{FilterNight}';"));

            return document.RootElement.GetProperty("candidates").EnumerateArray()
                .Single(one => one.GetProperty("candidate").GetString() == candidate)
                .GetProperty("values").GetProperty(SwingFilterRule.StateValue).GetString()!;
        }

        var skipping = Variant(TheSwingFamily.DeterioratingName);
        var revisions = Variant(TheSwingFamily.RevisionsName);

        // The estimates the source was asked for: the three members the revisions rule passes on everything else, and
        // not ZZD, which fails the trend gate, nor ZZE, which holds no bar on the night. ZZC's were raised, ZZA's stood
        // level and ZZB's the provider did not serve.
        Assert.Equal(["ZZA", "ZZB", "ZZC"], estimates.Asked);

        Assert.DoesNotContain(FiredOn("ZZC"), fire => !fire.Value);
        Assert.DoesNotContain(FiredOn("ZZD"), fire => fire.Value);
        Assert.Equal([skipping, revisions], FiredOn("ZZA").Where(fire => !fire.Value).Select(fire => fire.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            [revisions, Variant(TheSwingFamily.StrengthName)],
            FiredOn("ZZB").Where(fire => !fire.Value).Select(fire => fire.Key).Order(StringComparer.Ordinal));
        Assert.Equal(9, FiredOn("ZZB").Count);
        Assert.Equal(
            (FundamentalState.Deteriorating, FundamentalState.Deteriorating, FundamentalState.Improving, SwingFilterRule.StateNotRead),
            (StateReadOn("ZZA", skipping), StateReadOn("ZZA", Live), StateReadOn("ZZC", skipping), StateReadOn("ZZB", skipping)));

        // The estimate the filter handed each member, as the revisions rule's verdict names it.
        string EstimateOn(string ticker)
        {
            using var document = JsonDocument.Parse(Text(store, $"SELECT shadow FROM gate_result WHERE ticker = '{ticker}' AND session_date = '{FilterNight}';"));

            return document.RootElement.GetProperty("candidates").EnumerateArray()
                .Single(one => one.GetProperty("candidate").GetString() == revisions)
                .GetProperty("values").GetProperty(SwingFilterRule.EstimatesValue).GetString()!;
        }

        Assert.Equal(
            ["raised: 5.01 now against 5.00 30 days before, for the year to 2026-12-31", "not raised: 5.00 now against 5.00 30 days before, for the year to 2026-12-31", "not read: the provider did not serve it", "not read: the night asked for none"],
            [EstimateOn("ZZC"), EstimateOn("ZZA"), EstimateOn("ZZB"), EstimateOn("ZZD")]);

        using (var skippedE = JsonDocument.Parse(Text(store, $"SELECT shadow FROM gate_result WHERE ticker = 'ZZE' AND session_date = '{FilterNight}';")))
        {
            Assert.Empty(skippedE.RootElement.GetProperty("candidates").EnumerateArray());
            Assert.Equal(9, skippedE.RootElement.GetProperty("skipped").GetArrayLength());
        }

        Assert.EndsWith(
            "; 9 swing family candidate(s) registered, 36 shadow evaluation(s) written, 9 skipped on a member without the readings: 9 stale, 0 gapped; estimates read for 3 member(s) a rule reading them passed on everything else, ZZA, ZZB, ZZC: the source's own words",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'filter-family' AND stage = 'swing-filter';"),
            StringComparison.Ordinal);
        Assert.Equal("ok", Text(store, "SELECT outcome FROM run_log WHERE run_id = 'filter-family' AND stage = 'swing-filter';"));
    }

    // An estimates source answering from constructed readings, recording the members it was asked about.
    sealed class ConstructedEstimates(IReadOnlyDictionary<string, EstimateReading> readings) : IEstimateSource
    {
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyDictionary<string, EstimateReading>> ReadAsync(DateOnly night, IReadOnlyList<string> tickers, string runId, CancellationToken cancellation = default)
        {
            Asked.AddRange(tickers);

            return Task.FromResult<IReadOnlyDictionary<string, EstimateReading>>(
                tickers.Where(readings.ContainsKey).ToDictionary(ticker => ticker, ticker => readings[ticker], StringComparer.Ordinal));
        }

        public string Said => ": the source's own words";
    }

    static IReadOnlyList<string> TextRows(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
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
}
