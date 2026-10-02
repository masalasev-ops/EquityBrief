using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.4: the shape command over constructed stores. The operator's ruled settings open
// the first version and restart nothing, a rejection writes its decision and nothing else, and the first
// acceptance while a live filter candidate stands retires it and registers the accepted settings in the
// same write as the version, stating no count.
public partial class FixtureExpectations
{
    static readonly DateTimeOffset RuledAt = new(2025, 1, 1, 22, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset LiveRegisteredAt = new(2025, 1, 2, 22, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset AcceptedAt = new(2025, 3, 3, 22, 0, 0, TimeSpan.Zero);

    internal static async Task<(int Code, string Said)> ShapeVerb(TemporaryStore store, DateTimeOffset at, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await ShapeCommand.RunAsync(["shape", .. args], FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile, output, error);

        return (code, output.ToString() + error.ToString());
    }

    // Each of the three tables a shape decision can touch, whole, so a comparison says nothing changed
    // rather than that the columns a test thought of did not.
    internal static string ShapeTables(TemporaryStore store) =>
        Text(store, "SELECT COALESCE(json_group_array(json_array(version, settings, opened_at, closed_at, evidence)), '[]') FROM (SELECT * FROM filter_version ORDER BY version);") +
        Text(store, "SELECT COALESCE(json_group_array(json_array(id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence)), '[]') FROM (SELECT * FROM candidate_register ORDER BY id);") +
        Text(store, "SELECT COALESCE(json_group_array(json_array(id, proposed_at, session_date, version, ordinary, current_settings, settings, levers, list_now, list_proposed, findings, decision, decided_at, reason, opened)), '[]') FROM (SELECT * FROM shape_proposal ORDER BY id);");

    internal static void StoreProposal(TemporaryStore store, string version, FilterSettings settings)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText =
            "INSERT INTO shape_proposal (proposed_at, session_date, version, ordinary, current_settings, settings, levers, list_now, list_proposed, findings) " +
            "VALUES ('2025-12-01T22:00:00Z', '2025-12-01', $version, 60, $current, $settings, '[]', 2, 12, '[]');";
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$current", FilterSettings.Proposed.Write());
        command.Parameters.AddWithValue("$settings", settings.Write());
        command.ExecuteNonQuery();
    }

    // The live filter's first candidate, registered on the first evaluator the code carries with every
    // parameter it reads at 1, as the family's registration would stand for the purpose of the bound.
    internal static async Task RegisterLive(TemporaryStore store, DateTimeOffset at)
    {
        var evaluator = CandidateEvaluators.All[0];

        var outcome = await new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile).RegisterAsync(
            SwingFamily.LiveCandidate("1"),
            "the live swing filter's settings",
            "the candidates' sign-flip test over blocks",
            evaluator.Name,
            evaluator.Parameters.ToDictionary(name => name, _ => 1.0, StringComparer.Ordinal),
            "register-live");

        Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
    }

    [Fact]
    public async Task TheRuledSettingsOpenTheFirstVersionAndRestartNothing()
    {
        using var store = new TemporaryStore().Migrated();

        var (code, said) = await ShapeVerb(store, RuledAt, "--settings", "strengthFloor=0.6,rewardToRiskFloor=1.5", "--trade", "swing", "--evidence", "the operator's ruling on the counts");

        Assert.Equal(0, code);
        Assert.Equal(
            "shape: filter version 1 opened, on the evidence: the operator's ruling on the counts; no live filter candidate is registered, so it restarts nothing" + Environment.NewLine,
            said);

        // Every setting not named stands at section 17's proposed value, and the trade gate reads the swing trade at the nearest bands.
        Assert.Equal(
            FilterSettings.Proposed with { StrengthFloor = 0.6, RewardToRiskFloor = 1.5, Trade = TradeInput.Swing },
            FilterSettings.Read(Text(store, "SELECT settings FROM filter_version WHERE version = '1' AND closed_at IS NULL;")));
        Assert.Equal("2025-01-01T22:00:00Z", Text(store, "SELECT opened_at FROM filter_version WHERE version = '1';"));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // The same settings again, a setting the filter does not hold, and a trade input it does not know
        // are each refused with nothing changed, and each attempt is a row on the run log.
        var before = ShapeTables(store);

        Assert.Equal(1, (await ShapeVerb(store, RuledAt.AddMinutes(1), "--settings", "strengthFloor=0.6,rewardToRiskFloor=1.5", "--trade", "swing", "--evidence", "again")).Code);
        Assert.Equal(1, (await ShapeVerb(store, RuledAt.AddMinutes(2), "--settings", "strengthFlor=0.7", "--evidence", "a misspelling")).Code);
        Assert.Equal(1, (await ShapeVerb(store, RuledAt.AddMinutes(3), "--settings", "strengthFloor=0.7", "--trade", "ladders", "--evidence", "a misspelling")).Code);
        Assert.Equal(1, (await ShapeVerb(store, RuledAt.AddMinutes(4), "--settings", "depthLow=6", "--evidence", "a range upside down")).Code);
        Assert.Equal(before, ShapeTables(store));
        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'shape' AND outcome = 'refused';"));
    }

    [Fact]
    public async Task ARejectionWritesItsDecisionAndReasonAndNothingElse()
    {
        using var store = new TemporaryStore().Migrated();

        Assert.Equal(0, (await ShapeVerb(store, RuledAt, "--settings", "strengthFloor=0.6", "--evidence", "the ruling")).Code);
        await RegisterLive(store, LiveRegisteredAt);
        StoreProposal(store, "1", FilterSettings.Proposed with { StrengthFloor = 0.7 });

        var versions = Text(store, "SELECT json_group_array(json_array(version, settings, opened_at, closed_at, evidence)) FROM filter_version;");
        var register = Text(store, "SELECT json_group_array(json_array(id, candidate, event, registered_at)) FROM candidate_register;");
        var proposal = Text(store, "SELECT json_array(proposed_at, session_date, version, ordinary, current_settings, settings, levers, list_now, list_proposed, findings) FROM shape_proposal WHERE id = 1;");

        var (code, said) = await ShapeVerb(store, AcceptedAt, "--reject", "1", "--reason", "the list would move under an expiry week");

        Assert.Equal(0, code);
        Assert.Equal("shape: shape proposal 1 rejected, and no setting, version or registration changed: the list would move under an expiry week" + Environment.NewLine, said);

        // The decision, when and why, and nothing else: the proposal's own columns, the versions and the
        // register stand as they were, and no version is named as opened.
        Assert.Equal(
            "[\"rejected\",\"2025-03-03T22:00:00Z\",\"the list would move under an expiry week\",null]",
            Text(store, "SELECT json_array(decision, decided_at, reason, opened) FROM shape_proposal WHERE id = 1;"));
        Assert.Equal(proposal, Text(store, "SELECT json_array(proposed_at, session_date, version, ordinary, current_settings, settings, levers, list_now, list_proposed, findings) FROM shape_proposal WHERE id = 1;"));
        Assert.Equal(versions, Text(store, "SELECT json_group_array(json_array(version, settings, opened_at, closed_at, evidence)) FROM filter_version;"));
        Assert.Equal(register, Text(store, "SELECT json_group_array(json_array(id, candidate, event, registered_at)) FROM candidate_register;"));

        // A decision is never written twice, and a rejection is never bounded by the restarts.
        var decided = ShapeTables(store);

        Assert.Equal(1, (await ShapeVerb(store, AcceptedAt.AddMinutes(1), "--reject", "1", "--reason", "again")).Code);
        Assert.Equal(1, (await ShapeVerb(store, AcceptedAt.AddMinutes(2), "--accept", "1")).Code);
        Assert.Equal(decided, ShapeTables(store));
    }

    [Fact]
    public async Task TheFirstAcceptanceWhileALiveCandidateStandsRetiresItAndRegistersTheSettingsInOneWrite()
    {
        using var store = new TemporaryStore().Migrated();

        Assert.Equal(0, (await ShapeVerb(store, RuledAt, "--settings", "strengthFloor=0.6", "--evidence", "the ruling")).Code);
        await RegisterLive(store, LiveRegisteredAt);
        StoreProposal(store, "1", FilterSettings.Proposed with { StrengthFloor = 0.6, DryUpCeiling = 0.85 });

        // A proposal written for another version proposes a move from settings no longer held.
        StoreProposal(store, "0", FilterSettings.Proposed with { StrengthFloor = 0.9 });

        var before = ShapeTables(store);

        Assert.Equal(1, (await ShapeVerb(store, AcceptedAt, "--accept", "2")).Code);
        Assert.Equal(before, ShapeTables(store));

        // The first acceptance while a live candidate stands states no count: it costs the restart alone.
        var (code, said) = await ShapeVerb(store, AcceptedAt.AddMinutes(1), "--accept", "1");

        Assert.Equal(0, code);
        Assert.StartsWith("shape: filter version 2 opened, closing 1, on the evidence: shape proposal 1, over 60 ordinary nights under filter version 1; retired 'the live swing filter, version 1' as 2 and registered 'the live swing filter, version 2' as 3 at one instant", said, StringComparison.Ordinal);

        // The version, the retirement and the registration landed at one instant, and the proposal names
        // the version it opened.
        Assert.Equal("[[\"1\",\"2025-03-03T22:01:00Z\"],[\"2\",null]]", Text(store, "SELECT json_group_array(json_array(version, closed_at)) FROM (SELECT * FROM filter_version ORDER BY version);"));
        Assert.Equal(
            FilterSettings.Proposed with { StrengthFloor = 0.6, DryUpCeiling = 0.85 },
            FilterSettings.Read(Text(store, "SELECT settings FROM filter_version WHERE version = '2';")));
        Assert.Equal(
            "[[1,\"the live swing filter, version 1\",\"registered\",null],[2,\"the live swing filter, version 1\",\"retired\",\"the live swing filter, version 1\"],[3,\"the live swing filter, version 2\",\"registered\",null]]",
            Text(store, "SELECT json_group_array(json_array(id, candidate, event, retires)) FROM (SELECT * FROM candidate_register ORDER BY id);"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(DISTINCT registered_at) FROM candidate_register WHERE id > 1;"));
        Assert.Equal("[\"accepted\",\"2\"]", Text(store, "SELECT json_array(decision, opened) FROM shape_proposal WHERE id = 1;"));

        // The new candidate reads the parameters its evaluator reads, and the retirement's evidence says
        // what restarted.
        Assert.Equal(
            Text(store, "SELECT parameters FROM candidate_register WHERE id = 1;"),
            Text(store, "SELECT parameters FROM candidate_register WHERE id = 3;"));
        Assert.StartsWith("a shape acceptance opening filter version 2, restarting 0 non-empty block(s)", Text(store, "SELECT evidence FROM candidate_register WHERE id = 2;"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARuleCorrectionOpensTheNextVersionOnThePlanNamedAndRegistersTheFamilyAgainAtOneInstantAndIsNoAcceptance()
    {
        var threeAt = new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
        var familyAt = new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero);
        var correctedAt = new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero);
        var nearest = Ruled with { Trade = TradeInput.Swing };

        string[] Correct(string trade, string restarts, string evidence) => ["--rule-correction", "--trade", trade, "--restarts", restarts, "--evidence", evidence];

        // With no version open there is no rule to correct.
        using (var closed = await FamilyStore(threeAt, versionOpen: false))
        {
            var (refused, why) = await ShapeVerb(closed, correctedAt, Correct("clear", "0", "the ruling"));

            Assert.Equal(1, refused);
            Assert.Contains("no filter version is open, so there is no rule to correct. Nothing was changed.", why, StringComparison.Ordinal);
        }

        // Version 1 open on the plan at the nearest bands, before the family stands: no family to correct.
        using var store = await FamilyStore(threeAt, versionOpen: false);

        store.Execute($"INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{nearest.Write()}', '2026-09-05T22:00:00Z', NULL, 'the ruling');");

        var (early, earlySaid) = await ShapeVerb(store, threeAt.AddHours(1), Correct("clear", "0", "too early"));

        Assert.Equal(1, early);
        Assert.Contains("no live filter candidate stands registered, so there is no family to correct", earlySaid, StringComparison.Ordinal);

        Assert.Equal(0, (await RegisterVerbAt(store, familyAt, RegisterVerb.TheFamily)).Code);

        // The plan the version already reads, a word naming no plan, and a count of blocks the live filter
        // has not run are each refused with nothing changed; the live filter has run none, its first night
        // not yet stored.
        var before = ShapeTables(store);

        Assert.Contains("filter version 1 already reads the swing plan", (await ShapeVerb(store, correctedAt.AddMinutes(-4), Correct("swing", "0", "no change"))).Said, StringComparison.Ordinal);
        Assert.Contains("--trade 'clean' is not ladder, swing or clear", (await ShapeVerb(store, correctedAt.AddMinutes(-3), Correct("clean", "0", "a misspelling"))).Said, StringComparison.Ordinal);
        Assert.Contains("'the live swing filter, version 1' has run 0 non-empty block(s), and --restarts 1 states another count", (await ShapeVerb(store, correctedAt.AddMinutes(-2), Correct("clear", "1", "a wrong count"))).Said, StringComparison.Ordinal);
        Assert.Equal(1, (await ShapeVerb(store, correctedAt.AddMinutes(-1), "--rule-correction", "--trade", "clear", "--evidence", "no count")).Code);
        Assert.Equal(before, ShapeTables(store));
        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'shape' AND outcome = 'refused' AND started_at >= '2026-09-27T21:56:00Z';"));

        var (corrected, said) = await ShapeVerb(store, correctedAt, Correct("clear", "0", "the operator's ruling of 2026-09-26"));

        Assert.Equal(0, corrected);
        Assert.StartsWith(
            "shape: filter version 2 opened, closing 1, its trade gate reading the clear plan and every other setting as version 1 held it; retired 8 and registered 8 at one instant, family of 8 of 8",
            said,
            StringComparison.Ordinal);

        // Version 2 is version 1 with the plan moved, opened as version 1 closes.
        Assert.Equal("[[\"1\",\"2026-09-27T22:00:00Z\"],[\"2\",null]]", Text(store, "SELECT json_group_array(json_array(version, closed_at)) FROM (SELECT * FROM filter_version ORDER BY version);"));
        Assert.Equal(Ruled, FilterSettings.Read(Text(store, "SELECT settings FROM filter_version WHERE version = '2';")));

        // The eight standing retired and the eight the code writes for version 2 registered, all at that instant,
        // each retirement stating what it restarts and that it is a correction.
        const string At = "2026-09-27T22:00:00Z";

        Assert.Equal(
            [.. TheSwingFamily.For("1", nearest).Select(one => one.Candidate).Order(StringComparer.Ordinal)],
            TextRows(store, $"SELECT retires FROM candidate_register WHERE event = 'retired' AND registered_at = '{At}' ORDER BY retires;"));
        Assert.Equal(
            [.. TheSwingFamily.For("2", Ruled).Select(one => one.Candidate)],
            TextRows(store, $"SELECT candidate FROM candidate_register WHERE event = 'registered' AND registered_at = '{At}' ORDER BY id;"));
        Assert.All(
            TextRows(store, $"SELECT evidence FROM candidate_register WHERE event = 'retired' AND registered_at = '{At}';"),
            evidence => Assert.StartsWith("a rule correction opening filter version 2, restarting 0 non-empty block(s): the operator's ruling of 2026-09-26", evidence, StringComparison.Ordinal));
        Assert.Equal(
            (2.0, 1.0),
            (CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{SwingFamily.LiveCandidate("2")}' AND registered_at = '{At}';"))[SwingFilterRule.TradeParameter],
             CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{TheSwingFamily.Variant(TheSwingFamily.NearestBandsName, "2")}' AND registered_at = '{At}';"))[SwingFilterRule.TradeParameter]));

        // It is no acceptance: the bound counts none after it, so the first acceptance still states nothing,
        // and that one is counted.
        async Task<int> Accepted() => SwingFamily.AcceptedWhileLive(await new CandidateRegistrar(FixedClock.At(correctedAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync());

        Assert.Equal(0, await Accepted());
        Assert.Equal(0, (await ShapeVerb(store, correctedAt.AddDays(1), "--settings", "strengthFloor=0.6", "--evidence", "the first live trigger")).Code);
        Assert.Equal(1, await Accepted());
        Assert.Equal(1, (await ShapeVerb(store, correctedAt.AddDays(2), "--settings", "strengthFloor=0.65", "--evidence", "a later trigger")).Code);
    }

    [Fact]
    public async Task ARuleCorrectionNamingNoPlanOpensTheNextVersionWrittenAsTheCodeWritesItAndRegistersTheFamilyAgain()
    {
        // A version opened before the filter read pullbacks alone names the base's tightness and the
        // breakout's volume; a correction naming no plan opens the next with every setting it held, the
        // plan among them, and without the two, registers the family again at one instant, and is refused
        // once the open version is written as the code writes it.
        // see: The swing filter reads pullbacks alone, and a breakout returns only as a registered candidate built from its measured record
        var threeAt = new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
        var familyAt = new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero);
        var correctedAt = new DateTimeOffset(2026, 9, 29, 22, 0, 0, TimeSpan.Zero);
        var stored = Ruled.Write().Replace("\"rewardToRiskFloor\"", "\"tightnessCeiling\":0.7,\"breakoutVolumeMultiple\":1.5,\"rewardToRiskFloor\"", StringComparison.Ordinal);

        Assert.Contains("tightnessCeiling", stored, StringComparison.Ordinal);

        using var store = await FamilyStore(threeAt, versionOpen: false);

        store.Execute($"INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{stored}', '2026-09-05T22:00:00Z', NULL, 'the ruling');");

        Assert.Equal(0, (await RegisterVerbAt(store, familyAt, RegisterVerb.TheFamily)).Code);

        var (corrected, said) = await ShapeVerb(store, correctedAt, "--rule-correction", "--restarts", "0", "--evidence", "the operator's ruling of 2026-09-26: pullbacks alone");

        Assert.Equal(0, corrected);
        Assert.StartsWith(
            $"shape: filter version 2 opened, closing 1, its trade gate reading the {FilterSettings.Word(Ruled.Trade)} plan and every other setting as version 1 held it, written as the code now writes a version's settings; retired 8 and registered 8 at one instant",
            said,
            StringComparison.Ordinal);

        var opened = Text(store, "SELECT settings FROM filter_version WHERE version = '2';");

        Assert.Equal(Ruled.Write(), opened);
        Assert.DoesNotContain("tightnessCeiling", opened, StringComparison.Ordinal);
        Assert.DoesNotContain("breakoutVolumeMultiple", opened, StringComparison.Ordinal);
        Assert.Equal(1, Scalar(store, "SELECT COUNT(DISTINCT registered_at) FROM candidate_register WHERE registered_at = '2026-09-29T22:00:00Z';"));
        Assert.Equal(
            [.. TheSwingFamily.For("2", Ruled).Select(one => one.Candidate)],
            TextRows(store, "SELECT candidate FROM candidate_register WHERE event = 'registered' AND registered_at = '2026-09-29T22:00:00Z' ORDER BY id;"));

        // The next correction naming no plan would change nothing, and is refused with nothing changed.
        var before = ShapeTables(store);
        var (again, againSaid) = await ShapeVerb(store, correctedAt.AddMinutes(1), "--rule-correction", "--restarts", "0", "--evidence", "twice");

        Assert.Equal(1, again);
        Assert.Contains("filter version 2's settings are already written as the code writes them, so the correction would change nothing. Nothing was changed.", againSaid, StringComparison.Ordinal);
        Assert.Equal(before, ShapeTables(store));
    }

    [Fact]
    public void TheBoundCountsTheRetirementsAnAcceptanceWritesAndNoOther()
    {
        var at = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        RegisterRow Retired(long id, string evidence) =>
            new(id, SwingFamily.LiveCandidate("2"), "a rule", "a test", SwingFilterRule.EvaluatorName, "{}", "000000000000", CandidateFamily.Retired, SwingFamily.LiveCandidate("2"), at, evidence);

        // Registered again after a code change moved its evaluator, and retired with its family by a rule
        // correction: neither is an acceptance. Retired by an acceptance: one.
        Assert.Equal(0, SwingFamily.AcceptedWhileLive([Retired(1, "the 3.4 correction's touches and band width moved every evaluator's version")]));
        Assert.Equal(0, SwingFamily.AcceptedWhileLive([Retired(1, ShapeCommand.CorrectionEvidence + " 3, restarting 0 non-empty block(s): the ruling")]));
        Assert.Equal(1, SwingFamily.AcceptedWhileLive([Retired(1, SwingFamily.AcceptanceEvidence + " 2, restarting 0 non-empty block(s): the ruling"), Retired(2, "the 3.4 correction's touches and band width moved every evaluator's version")]));
    }
}
