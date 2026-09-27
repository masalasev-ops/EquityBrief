using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.5: the swing family. Each of the six evaluated over constructed input on both
// sides of the settings it holds, the listings stage leaving them to the filter's stage, the filter's
// stage storing their shadow on every member's row, and the one command writing the six registrations
// and phase 10's three retirements at one instant or none.
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
    // above the close and 4.5 moves up.
    static GateInputs FamilyPassing() =>
        Passing() with { Bands = [new FilterBand(95m, 96m, "support", 3, true), .. Passing().Bands] };

    // The same member with its target band moved to set each plan's reward to risk: (target - 102) / 7 on
    // section 10's plan wherever the target is two typical moves or more above the entry, 110, and
    // (target - 102) / 2 on the plan at the nearest bands.
    static GateInputs AtRewardToRisk(decimal target) =>
        Passing() with { Bands = [new FilterBand(95m, 96m, "support", 3, true), new FilterBand(100m, 104m, "support", 10, true), new FilterBand(target, target + 1, "resistance", 5, true)] };

    [Fact]
    public void EachOfTheSixFiresOnItsOwnSideOfEverySettingItMovesAndNotAStepPastIt()
    {
        // Worked by hand. The passing member: breadth 0.75, an uptrend at strength 0.8, a pullback 3 moves
        // deep on a dry-up of 0.8 inside the band 100 to 104, its event tonight and none on the session
        // before; section 10's plan from 102 to 120 over a stop at 95, 18 / 7 = 2.57 with the stop 7 / 4 =
        // 1.75 typical moves below, and the plan at the nearest bands over a stop at 100, 18 / 2 = 9 with
        // the stop 2 / 4 = 0.5 below. Every one of the six fires.
        Assert.All(Fires(FamilyPassing()), fire => Assert.True(fire.Value, fire.Key));
        Assert.Equal(6, Fires(FamilyPassing()).Count);

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

        // The dry-up, which every one of the six holds at the live 1.5.
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

        // An exclusion leaves every one of the six unfired.
        Assert.All(Fires(FamilyPassing() with { Suspect = true }), fire => Assert.False(fire.Value, fire.Key));

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
    public async Task TheFamilysCommandWritesSixRegistrationsAndThreeRetirementsAtOneInstantOrNone()
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

        // One of the three already retired refuses all nine, since a retirement names a candidate that stands.
        using (var one = await FamilyStore(threeAt))
        {
            Assert.Equal(0, (await RegisterVerbAt(one, threeAt.AddHours(1), "--retire", TheThreeCandidates.CrossedByAMarginName, "--evidence", "an earlier retirement")).Code);

            var (code, said) = await RegisterVerbAt(one, familyAt, RegisterVerb.TheFamily);

            Assert.Equal(1, code);
            Assert.Contains($"'{TheThreeCandidates.CrossedByAMarginName}' was refused, so none of the nine was written", said, StringComparison.Ordinal);
            Assert.Equal(4, Scalar(one, "SELECT COUNT(*) FROM candidate_register;"));
        }

        using var store = await FamilyStore(threeAt);

        var (written, told) = await RegisterVerbAt(store, familyAt, RegisterVerb.TheFamily);

        Assert.Equal(0, written);
        Assert.Contains("retired 3 and registered 6 at one instant, the live filter at filter version 1, family of 6 of 8", told, StringComparison.Ordinal);

        // Nine rows after the three, all at one instant.
        Assert.Equal(12, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(DISTINCT registered_at) FROM candidate_register WHERE id > 3;"));
        Assert.Equal("2026-09-07T22:00:00Z", Text(store, "SELECT MIN(registered_at) FROM candidate_register WHERE id > 3;"));

        // Each retirement carries the words saying no result of the candidate it retires was read.
        Assert.Equal(
            [.. TheThreeCandidates.All.Select(one => one.Candidate).Order(StringComparer.Ordinal)],
            [.. TextRows(store, "SELECT retires FROM candidate_register WHERE event = 'retired' ORDER BY retires;")]);
        Assert.All(TextRows(store, "SELECT evidence FROM candidate_register WHERE event = 'retired';"), evidence => Assert.Contains(TheSwingFamily.NothingRead, evidence, StringComparison.Ordinal));

        // The six are the family, each on the one evaluator at its version, the live filter stating the open
        // version's settings and each variant those with one thing moved, named for version 1.
        Assert.Equal(
            [Live, NearestBands, Variant(TheSwingFamily.DepthName), Variant(TheSwingFamily.MarketOffName), Variant(TheSwingFamily.StrengthName), Variant(TheSwingFamily.ArrivalName)],
            TextRows(store, "SELECT candidate FROM candidate_register WHERE event = 'registered' AND id > 3 ORDER BY id;"));
        Assert.All(TextRows(store, "SELECT evaluator || '|' || evaluator_version FROM candidate_register WHERE event = 'registered' AND id > 3;"), row => Assert.Equal(SwingFilterRule.EvaluatorName + "|" + new SwingFilterRule().Version, row));
        Assert.Equal(Ruled, SwingFilterRule.SettingsOf(CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Live}';"))));
        Assert.Equal(
            Ruled with { Trade = TradeInput.Swing },
            SwingFilterRule.SettingsOf(CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{NearestBands}';"))));
        Assert.Equal(0.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Variant(TheSwingFamily.MarketOffName)}';"))[SwingFilterRule.MarketGateParameter]);

        // A second run is refused whole, the three no longer standing.
        Assert.Equal(1, (await RegisterVerbAt(store, familyAt.AddHours(1), RegisterVerb.TheFamily)).Code);
        Assert.Equal(12, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }

    [Fact]
    public async Task TheFiltersStageStoresEachFamilyCandidatesVerdictOnEveryMembersRow()
    {
        using var store = await FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, (await RegisterVerbAt(store, new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero), RegisterVerb.TheFamily)).Code);

        // ZZB's strength set to 0.6, above the live floor of 0.5 and below the variant's two thirds.
        store.Execute("UPDATE swing_reading SET strength = 0.6, place_short = 0.6, place_long = 0.6 WHERE ticker = 'ZZB';");

        // Worked by hand under the ruled settings: ZZA, ZZB and ZZC pass every gate, section 10's plan from
        // 102 over a stop at 95 to 120 or 126 and the plan at the nearest bands over a stop at 100, and their
        // events tonight with none on the session before; ZZD is in a range. So ZZA and ZZC fire for all
        // six, ZZB for all but the strength variant, and ZZD for none.
        var family = FamilyShadow.For(
            await new CandidateRegistrar(FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(),
            FilterEvening.AddMinutes(-30));
        var outcome = await new SwingFilter(FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "filter-family", family);

        Assert.Equal((24, 0), (outcome.Evaluated, outcome.Faults!.Count));

        IReadOnlyDictionary<string, bool> FiredOn(string ticker)
        {
            using var document = JsonDocument.Parse(Text(store, $"SELECT shadow FROM gate_result WHERE ticker = '{ticker}' AND session_date = '{FilterNight}';"));

            return document.RootElement.GetProperty("candidates").EnumerateArray().ToDictionary(
                one => one.GetProperty("candidate").GetString()!,
                one => one.GetProperty("fired").GetBoolean(),
                StringComparer.Ordinal);
        }

        Assert.All(FiredOn("ZZA"), fire => Assert.True(fire.Value, fire.Key));
        Assert.All(FiredOn("ZZC"), fire => Assert.True(fire.Value, fire.Key));
        Assert.All(FiredOn("ZZD"), fire => Assert.False(fire.Value, fire.Key));
        Assert.Equal(
            [Variant(TheSwingFamily.StrengthName)],
            FiredOn("ZZB").Where(fire => !fire.Value).Select(fire => fire.Key));
        Assert.Equal(6, FiredOn("ZZB").Count);

        Assert.EndsWith(
            "; 6 swing family candidate(s) registered, 24 shadow evaluation(s) written, 0 skipped on a member without the readings: 0 stale, 0 gapped",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'filter-family' AND stage = 'swing-filter';"),
            StringComparison.Ordinal);
        Assert.Equal("ok", Text(store, "SELECT outcome FROM run_log WHERE run_id = 'filter-family' AND stage = 'swing-filter';"));
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
