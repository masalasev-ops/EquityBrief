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

        // Every setting not named stands at section 17's proposed value, and the trade gate reads the swing trade's own plan.
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
}
