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
    // The operator's ruled settings, which version 1 opens on and the live filter's candidate states.
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
        Trade = TradeInput.Swing,
    };

    static IReadOnlyDictionary<string, bool> Fires(GateInputs inputs) =>
        TheSwingFamily.For("1", Ruled).ToDictionary(
            one => one.Candidate,
            one => new SwingFilterRule().EvaluateGates(inputs, one.Parameters).Fired,
            StringComparer.Ordinal);

    static readonly string Live = SwingFamily.LiveCandidate("1");

    // The member that passes every gate, its swing trade entered at 102 with its stop at the setup band's
    // low edge of 100 and its target at the resistance band's low edge, its target moved to set the reward
    // to risk: (target - 102) / 2.
    static GateInputs AtRewardToRisk(decimal target) =>
        Passing() with { Bands = [new FilterBand(100m, 104m, "support", 10, true), new FilterBand(target, target + 1, "resistance", 5, true)] };

    [Fact]
    public void EachOfTheSixFiresOnItsOwnSideOfEverySettingItMovesAndNotAStepPastIt()
    {
        // Worked by hand. The passing member: breadth 0.75, an uptrend at strength 0.8, a pullback 3 moves
        // deep on a dry-up of 0.8 inside the band 100 to 104, its event tonight and none on the session
        // before, and its swing trade from 102 to 120 over a stop at 100, a reward to risk of 9 with the
        // stop 2 / 4 = 0.5 typical moves below. Every one of the six fires.
        Assert.All(Fires(Passing()), fire => Assert.True(fire.Value, fire.Key));
        Assert.Equal(6, Fires(Passing()).Count);

        // Reward to risk: the live floor 1.5 at a target of 105 and not at 104.98, 1.49; the variant's 2 at
        // 106 and not at 105.98, 1.99, which the live filter still fires on.
        Assert.True(Fires(AtRewardToRisk(105m))[Live]);
        Assert.False(Fires(AtRewardToRisk(104.98m))[Live]);
        Assert.True(Fires(AtRewardToRisk(106m))[TheSwingFamily.RewardToRiskName]);
        Assert.Equal((true, false), (Fires(AtRewardToRisk(105.98m))[Live], Fires(AtRewardToRisk(105.98m))[TheSwingFamily.RewardToRiskName]));

        // The pullback's depth: live 1 to 5, the variant 1 to 3.
        GateInputs Deep(double depth) => Passing() with { Reading = Passing().Reading! with { Depth = depth } };

        Assert.Equal((true, false), (Fires(Deep(5.0))[Live], Fires(Deep(5.01))[Live]));
        Assert.Equal((true, false), (Fires(Deep(1.0))[Live], Fires(Deep(0.99))[Live]));
        Assert.Equal((true, false), (Fires(Deep(3.0))[TheSwingFamily.DepthName], Fires(Deep(3.01))[TheSwingFamily.DepthName]));
        Assert.True(Fires(Deep(3.01))[Live]);

        // The dry-up, which every one of the six holds at the live 1.5.
        GateInputs Dry(double dryUp) => Passing() with { Reading = Passing().Reading! with { DryUp = dryUp } };

        Assert.All(Fires(Dry(1.49)), fire => Assert.True(fire.Value, fire.Key));
        Assert.All(Fires(Dry(1.5)), fire => Assert.False(fire.Value, fire.Key));

        // Strength: live 0.5, the variant two thirds.
        Assert.Equal((true, false), (Fires(Passing() with { Strength = 0.5 })[Live], Fires(Passing() with { Strength = 0.49 })[Live]));
        Assert.Equal(
            (true, false, true),
            (Fires(Passing() with { Strength = 2.0 / 3.0 })[TheSwingFamily.StrengthName], Fires(Passing() with { Strength = 0.66 })[TheSwingFamily.StrengthName], Fires(Passing() with { Strength = 0.66 })[Live]));

        // The stop's distance, 0.5 to 4 typical moves: 2 / 4 = 0.5 and 2 / 0.5 = 4 fire, 2 / 4.01 and 2 / 0.49 do not.
        Assert.Equal((true, false), (Fires(Passing() with { TypicalMove = 4 })[Live], Fires(Passing() with { TypicalMove = 4.01 })[Live]));
        Assert.Equal((true, false), (Fires(Passing() with { TypicalMove = 0.5 })[Live], Fires(Passing() with { TypicalMove = 0.49 })[Live]));

        // The market: the live floor 0.5 at a breadth of 0.5 and not at 0.49, which the variant that reads
        // no market fires on, as it does on a night whose breadth is not available.
        GateInputs AtBreadth(double? share) => Passing() with { Breadth = new Breadth(100, 100, 50, share) };

        Assert.Equal((true, false), (Fires(AtBreadth(0.5))[Live], Fires(AtBreadth(0.49))[Live]));
        Assert.True(Fires(AtBreadth(0.49))[TheSwingFamily.MarketOffName]);
        Assert.True(Fires(Passing() with { Breadth = null })[TheSwingFamily.MarketOffName]);
        Assert.False(Fires(Passing() with { Breadth = null })[Live]);

        // Arrival: tonight's close at the previous high of 101 is no event, and the stop 1 / 2 = 0.5 moves
        // below. Fired on 09-04 and not 09-03 arrives one session back, inside the live window of three
        // and outside the variant's one; fired back to 09-02 and not 09-01 arrives two back, inside the
        // live window; fired on every session back to 09-01, it did not arrive in three.
        GateInputs Arrived(bool on03, bool on02, bool on01) => Passing() with
        {
            Close = 101m,
            TypicalMove = 2,
            TriggerFiredTheSessionBefore = true,
            Earlier = [new SessionEvent(new DateOnly(2026, 9, 3), on03), new SessionEvent(new DateOnly(2026, 9, 2), on02), new SessionEvent(new DateOnly(2026, 9, 1), on01)],
        };

        Assert.Equal((true, false), (Fires(Arrived(false, false, false))[Live], Fires(Arrived(false, false, false))[TheSwingFamily.ArrivalName]));
        Assert.True(Fires(Arrived(true, false, false))[Live]);
        Assert.False(Fires(Arrived(true, true, true))[Live]);
        Assert.True(Fires(Passing())[TheSwingFamily.ArrivalName]);

        // An exclusion leaves every one of the six unfired.
        Assert.All(Fires(Passing() with { Suspect = true }), fire => Assert.False(fire.Value, fire.Key));

        // The verdict names each gate's answer, the market read, the exclusions and the session it arrived on.
        var verdict = new SwingFilterRule().EvaluateGates(AtBreadth(0.49), SwingFilterRule.ParametersOf(Ruled, marketGate: false));

        Assert.Equal(("failed", "no", "none", "tonight"), (verdict.Values[SwingGates.Market], verdict.Values["market read"], verdict.Values["exclusions"], verdict.Values["arrived"]));
        Assert.Equal(Ruled, SwingFilterRule.SettingsOf(SwingFilterRule.ParametersOf(Ruled)));
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

        var filter = ShadowColumn.EvaluateGates(ShadowColumn.ForTheFilter(standing), Passing());

        Assert.Equal((Live, true), (Assert.Single(filter.Outcomes).Candidate, filter.Outcomes[0].Fired));

        var stale = ShadowColumn.EvaluateGates(ShadowColumn.ForTheFilter(standing), Passing(), new NameWithheld(ShadowSkipCause.Stale, "no bar is stored for ZZA on 2026-09-08"));

        Assert.Empty(stale.Outcomes);
        Assert.Equal(ShadowSkipCause.Stale, Assert.Single(stale.Skipped).Cause);

        var moved = ShadowColumn.EvaluateGates([Row(1, Live, SwingFilterRule.EvaluatorName, "000000000001", SwingFilterRule.ParametersOf(Ruled))], Passing());

        Assert.True(Assert.Single(moved.Skipped).IsFault);
        Assert.Equal(3, ShadowColumn.ArrivalReach(ShadowColumn.ForTheFilter(standing)));

        // The night's shadow as the filter's stage runs it: the family standing at the night's start, a
        // member the night holds no bar for skipped with the reason and counted, and one it does evaluated.
        var night = FamilyShadow.For(standing, at.AddSeconds(1));

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
        // version's settings and each variant those with one setting moved.
        Assert.Equal(
            [Live, TheSwingFamily.RewardToRiskName, TheSwingFamily.DepthName, TheSwingFamily.MarketOffName, TheSwingFamily.StrengthName, TheSwingFamily.ArrivalName],
            TextRows(store, "SELECT candidate FROM candidate_register WHERE event = 'registered' AND id > 3 ORDER BY id;"));
        Assert.All(TextRows(store, "SELECT evaluator || '|' || evaluator_version FROM candidate_register WHERE event = 'registered' AND id > 3;"), row => Assert.Equal(SwingFilterRule.EvaluatorName + "|" + new SwingFilterRule().Version, row));
        Assert.Equal(Ruled, SwingFilterRule.SettingsOf(CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{Live}';"))));
        Assert.Equal(
            Ruled with { RewardToRiskFloor = 2 },
            SwingFilterRule.SettingsOf(CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{TheSwingFamily.RewardToRiskName}';"))));
        Assert.Equal(0.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{TheSwingFamily.MarketOffName}';"))[SwingFilterRule.MarketGateParameter]);

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

        // Worked by hand under the ruled settings: ZZA, ZZB and ZZC pass every gate, their swing trades from
        // 102 over a stop at 100 to 120 or 126, and their events tonight with none on the session before;
        // ZZD is in a range. So ZZA and ZZC fire for all six, ZZB for all but the strength variant, and ZZD
        // for none.
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
            [TheSwingFamily.StrengthName],
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
