using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.4: the shape proposer's arithmetic worked by hand over constructed nights, its
// trigger at sixty ordinary nights and not one short or with event nights among them, and that it never
// applies what it proposes, over a run as well as over its source.
public partial class FixtureExpectations
{
    static readonly DateOnly ProposalFirst = new(2026, 1, 5);

    static readonly DateTimeOffset ProposalEvening = new(2026, 6, 1, 22, 0, 0, TimeSpan.Zero);

    // Five hundred members, the same on every night. Member i is in an uptrend at strength i/500. The
    // hundred from 400 hold a pullback three moves deep inside an anchored support band on a dry-up of
    // (i - 400)/100; the first twenty of those have their trigger's event tonight and not on the session
    // before; the first three of those carry a ladder plan at a reward to risk of 3 with its stop 1.5
    // moves below, and the other seventeen a stop 3 moves below. No member is excluded.
    internal static StoredNight KnownNight(int day) =>
        new(
            ProposalFirst.AddDays(day),
            [
                .. Enumerable.Range(0, 500).Select(i =>
                {
                    var pulled = i >= 400;
                    var arrived = i is >= 400 and < 420;

                    return new StoredMember(
                        "T" + i.ToString("000", CultureInfo.InvariantCulture),
                        SwingGates.Uptrend,
                        i / 500.0,
                        pulled ? 3 : null,
                        pulled ? (i - 400) / 100.0 : null,
                        null,
                        null,
                        pulled,
                        false,
                        arrived,
                        arrived ? false : null,
                        arrived ? 3 : null,
                        arrived ? (i < 403 ? 1.5 : 3) : null,
                        null,
                        null,
                        false);
                }),
            ]);

    [Fact]
    public void TheProposerMovesEachGatesSettingToTheNearestValueInsideItsBandAndNamesAGateNoValueReaches()
    {
        var proposal = ShapeProposals.Propose([.. Enumerable.Range(0, 60).Select(KnownNight)], FilterSettings.Proposed);

        // Worked by hand. Trend and strength at the held floor of 2/3 passes i >= 334, 166 members, above
        // its band of 50 to 100. Tried nearest the held value first, every floor below 0.80 passes more
        // than 100: at 0.79 it passes i >= 395, 105; at 0.80 it passes i >= 400, exactly 100.
        var trend = proposal.Levers[0];

        Assert.Equal((SwingGates.Trend, ShapeProposals.StrengthFloor), (trend.Gate, trend.Setting));
        Assert.Equal(FilterSettings.ProposedStrengthFloor, trend.Current);
        Assert.Equal(0.80, trend.Proposed);
        Assert.Equal((166.0, 100.0), (trend.MedianNow, trend.MedianProposed));

        // Through the setup, under the floor just proposed: the hundred from 400 all pass at the held
        // ceiling of 1, dry-ups 0.00 to 0.99, above the band of 20 to 60. A ceiling c passes the dry-ups
        // strictly below it, 100c of them, so 0.60 passes 60, the value nearest 1 that reaches the band.
        var setup = proposal.Levers[1];

        Assert.Equal(0.60, setup.Proposed);
        Assert.Equal((100.0, 60.0), (setup.MedianNow, setup.MedianProposed));

        // The trigger has no threshold and passes its twenty, inside 8 to 40, so it is drawn and nothing
        // is said about it.
        var trigger = proposal.Levers[2];

        Assert.Null(trigger.Setting);
        Assert.Equal((20.0, 20.0), (trigger.MedianNow, trigger.MedianProposed));

        // The trade passes the three whose stop sits inside 1 to 2.5 moves, at any floor up to 3, and no
        // floor passes more, so none brings it to 5: a finding, and its setting is left where it is.
        var trade = proposal.Levers[3];

        Assert.Equal(FilterSettings.ProposedRewardToRiskFloor, trade.Current);
        Assert.Null(trade.Proposed);
        Assert.Equal((3.0, 3.0), (trade.MedianNow, trade.MedianProposed));
        Assert.Equal(
            "no rewardToRiskFloor between 1.00 and 4.00 brings the trade's median inside 5 to 35, and it stays 3",
            Assert.Single(proposal.Findings));

        Assert.Equal(FilterSettings.Proposed with { StrengthFloor = 0.80, DryUpCeiling = 0.60 }, proposal.Settings);
        Assert.Equal((3.0, 3.0), (proposal.ListNow, proposal.ListProposed));
    }

    // Two hundred members a night under filter version 1, each in an uptrend at strength i/250, so the 33
    // from 167 reach the held floor of 2/3 and pass trend and strength, 16.5% of the index on every night;
    // no swing readings, so no setup and nothing after it. A night in the events list trades at twice its
    // volume, which marks it an event.
    static TemporaryStore ProposalStore(int nights, IReadOnlySet<int> events)
    {
        var store = new TemporaryStore().Migrated();

        store.Execute($"INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{FilterSettings.Proposed.Write()}', '2026-01-02T22:00:00Z', NULL, 'constructed');");
        AppendProposalNights(store, 0, nights, events);

        return store;
    }

    static void AppendProposalNights(TemporaryStore store, int from, int to, IReadOnlySet<int> events)
    {
        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();

        using var row = connection.CreateCommand();
        row.Transaction = transaction;
        row.CommandText =
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, gates) " +
            "VALUES ($ticker, $session, '1', 'test', 1, $trend, 0, NULL, 0, 0, 0, '[]', 0, $gates);";
        var ticker = row.Parameters.Add("$ticker", Microsoft.Data.Sqlite.SqliteType.Text);
        var session = row.Parameters.Add("$session", Microsoft.Data.Sqlite.SqliteType.Text);
        var gates = row.Parameters.Add("$gates", Microsoft.Data.Sqlite.SqliteType.Text);
        var trend = row.Parameters.Add("$trend", Microsoft.Data.Sqlite.SqliteType.Integer);

        using var market = connection.CreateCommand();
        market.Transaction = transaction;
        market.CommandText =
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            "VALUES ($session, 200, 200, 120, 0.6, 200, 100, 0.5, 200, $ratio);";
        var marketSession = market.Parameters.Add("$session", Microsoft.Data.Sqlite.SqliteType.Text);
        var ratio = market.Parameters.Add("$ratio", Microsoft.Data.Sqlite.SqliteType.Real);

        for (var day = from; day < to; day++)
        {
            var night = ProposalFirst.AddDays(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            for (var i = 0; i < 200; i++)
            {
                ticker.Value = "T" + i.ToString("000", CultureInfo.InvariantCulture);
                session.Value = night;
                trend.Value = i >= 167 ? 1 : 0;
                gates.Value = SwingFilter.GatesJson(new GateResult(
                    (string)ticker.Value,
                    [
                        new Gate(SwingGates.Trend, i >= 167, "constructed", new Dictionary<string, string> { ["trend state"] = SwingGates.Uptrend, ["strength"] = (i / 250.0).ToString("R", CultureInfo.InvariantCulture) }),
                        new Gate(SwingGates.Setup, false, "constructed", new Dictionary<string, string> { ["depth"] = "none", [SwingGates.PullbackBandValue] = "no", [SwingGates.BreakoutBandValue] = "no" }),
                    ],
                    null,
                    false,
                    new TradeReading(null, null, null, null, null, "none"),
                    new TradeReading(null, null, null, null, null, "none"),
                    [],
                    [],
                    i / 250.0,
                    null));
                row.ExecuteNonQuery();
            }

            marketSession.Value = night;
            ratio.Value = events.Contains(day) ? 2.0 : 1.0;
            market.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    static async Task<ShapeProposalOutcome> ProposeOver(TemporaryStore store, string runId) =>
        await new ShapeProposer(FixedClock.At(ProposalEvening, SessionZones.UnitedStates), store.DatabaseFile).RunAsync(runId);

    static long Scalar(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static string Text(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task TheProposerWritesAtSixtyOrdinaryNightsAndNotOneShortOrWithEventsAmongThem()
    {
        // Fifty-nine ordinary nights: short of the trigger, nothing proposed, and the run log says so.
        using (var short59 = ProposalStore(59, new HashSet<int>()))
        {
            var outcome = await ProposeOver(short59, "shape-59");

            Assert.Equal(("1", 59, false, (long?)null), (outcome.Version, outcome.Ordinary, outcome.Crossed, outcome.Proposal));
            Assert.Equal(0, Scalar(short59, "SELECT COUNT(*) FROM shape_proposal;"));
            Assert.Equal(
                "59 of the 60 ordinary nights under filter version 1, and nothing proposed",
                Text(short59, "SELECT detail FROM run_log WHERE stage = 'shape-proposal';"));
        }

        using var store = ProposalStore(60, new HashSet<int>());

        var crossed = await ProposeOver(store, "shape-60");

        Assert.Equal(("1", 60, true, (long?)1), (crossed.Version, crossed.Ordinary, crossed.Crossed, crossed.Proposal));

        // Worked by hand over the stored rows. Strength i/250 runs 0 to 0.796, so the held floor of 2/3
        // passes i >= 167, 33, below the band of 50, and every floor above it passes fewer. Tried nearest
        // 2/3 first, 0.61 passes i >= 153, 47, and 0.60 passes i >= 150, exactly 50, inside it. No member
        // holds a depth, so no dry-up ceiling brings the setup to 20, the trigger has no threshold, and no
        // floor brings the trade to 5: three findings.
        var settings = FilterSettings.Read(Text(store, "SELECT settings FROM shape_proposal WHERE id = 1;"));
        var levers = JsonSerializer.Deserialize<List<Lever>>(Text(store, "SELECT levers FROM shape_proposal WHERE id = 1;"))!;

        Assert.Equal(FilterSettings.Proposed with { StrengthFloor = 0.60 }, settings);
        Assert.Equal((0.60, 33.0, 50.0), (levers[0].Proposed!.Value, levers[0].MedianNow!.Value, levers[0].MedianProposed!.Value));
        Assert.Equal(3, JsonSerializer.Deserialize<List<string>>(Text(store, "SELECT findings FROM shape_proposal WHERE id = 1;"))!.Count);
        Assert.Equal(60, Scalar(store, "SELECT ordinary FROM shape_proposal WHERE id = 1;"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM shape_proposal WHERE decision IS NULL;"));
        Assert.Equal(
            "shape calibration is due: 60 ordinary nights under filter version 1, and proposal 1 written with 3 finding(s)",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'shape-60' AND stage = 'shape-proposal';"));

        // A night run again stands on the proposal already written for the version rather than writing
        // a second.
        var again = await ProposeOver(store, "shape-60-again");

        Assert.Null(again.Proposal);
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM shape_proposal;"));

        // Rejected, the version stays open and the next proposal for it waits on sixty more ordinary
        // nights: none at sixty, one at a hundred and twenty, read over all of them.
        Assert.Equal(0, (await ShapeVerb(store, ProposalEvening.AddMinutes(1), "--reject", "1", "--reason", "an expiry week inside the window")).Code);

        var rejected = await ProposeOver(store, "shape-rejected");

        Assert.Equal((true, (long?)null), (rejected.Crossed, rejected.Proposal));
        Assert.Equal(
            "60 ordinary nights under filter version 1, 1 proposal(s) for it rejected, and the next is written at 120",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'shape-rejected' AND stage = 'shape-proposal';"));

        AppendProposalNights(store, 60, 119, new HashSet<int>());
        Assert.Null((await ProposeOver(store, "shape-119")).Proposal);

        AppendProposalNights(store, 119, 120, new HashSet<int>());

        var next = await ProposeOver(store, "shape-120");

        Assert.Equal((120, (long?)2), (next.Ordinary, next.Proposal));
        Assert.Equal(120, Scalar(store, "SELECT ordinary FROM shape_proposal WHERE id = 2;"));

        // Sixty nights with ten events among them are fifty ordinary ones and wait; seventy with the same
        // ten cross, and the proposal reads the sixty ordinary nights alone.
        var tenEvents = Enumerable.Range(0, 10).Select(day => day * 6).ToHashSet();

        using (var withEvents = ProposalStore(60, tenEvents))
        {
            var waiting = await ProposeOver(withEvents, "shape-events");

            Assert.Equal((50, false), (waiting.Ordinary, waiting.Crossed));
            Assert.Equal(0, Scalar(withEvents, "SELECT COUNT(*) FROM shape_proposal;"));
        }

        using var seventy = ProposalStore(70, tenEvents);

        var reached = await ProposeOver(seventy, "shape-seventy");

        Assert.Equal((60, true), (reached.Ordinary, reached.Crossed));
        Assert.Equal(60, Scalar(seventy, "SELECT ordinary FROM shape_proposal WHERE id = 1;"));
    }

    [Fact]
    public async Task ANightStoredBeforeTheSetupKeptItsBandAnswersIsLeftOutOfTheRecountAndNamed()
    {
        using var store = ProposalStore(60, new HashSet<int>());

        // The first ten nights' rows lose the setup's two band answers, as a row stored before the setup
        // kept them reads: worked by hand, the fifty others give the same medians and the same floor.
        var tenth = ProposalFirst.AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute(
            "UPDATE gate_result SET gates = json_remove(gates, '$.gates[1].values.\"" + SwingGates.PullbackBandValue + "\"', '$.gates[1].values.\"" + SwingGates.BreakoutBandValue + "\"') " +
            $"WHERE session_date < '{tenth}';");

        var outcome = await ProposeOver(store, "shape-unanswered");

        Assert.Equal((50, true, (long?)1), (outcome.Ordinary, outcome.Crossed, outcome.Proposal));
        Assert.Equal(50, Scalar(store, "SELECT ordinary FROM shape_proposal WHERE id = 1;"));
        Assert.Equal(FilterSettings.Proposed with { StrengthFloor = 0.60 }, FilterSettings.Read(Text(store, "SELECT settings FROM shape_proposal WHERE id = 1;")));

        var findings = JsonSerializer.Deserialize<List<string>>(Text(store, "SELECT findings FROM shape_proposal WHERE id = 1;"))!;

        Assert.Equal(4, findings.Count);
        Assert.Equal("10 ordinary night(s) under filter version 1 were stored before the setup kept its band answers and were not recounted", findings[0]);
    }

    [Fact]
    public async Task TheProposerNeverAppliesWhatItProposesOverARunAndOverItsSource()
    {
        using var store = ProposalStore(60, new HashSet<int>());

        var versions = Text(store, "SELECT json_group_array(json_array(version, settings, opened_at, closed_at, evidence)) FROM filter_version;");
        var gates = Scalar(store, "SELECT COUNT(*) FROM gate_result;");

        var outcome = await ProposeOver(store, "shape-applies");

        Assert.Equal((long?)1, outcome.Proposal);

        // The run wrote its proposal and nothing else: the open version, its settings, the register and
        // the gate results stand as they were.
        Assert.Equal(versions, Text(store, "SELECT json_group_array(json_array(version, settings, opened_at, closed_at, evidence)) FROM filter_version;"));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.Equal(gates, Scalar(store, "SELECT COUNT(*) FROM gate_result;"));

        // Over the source: the proposer declares no write to a version or the register, and no statement
        // in it writes either; the arithmetic it runs holds no statement at all.
        Assert.DoesNotContain(ShapeProposer.Access.Stores, touch =>
            (touch.Store == EquityBrief.Core.Components.Store.FilterVersion && touch.Touch != Touch.Read) || touch.Store == EquityBrief.Core.Components.Store.CandidateRegister);

        var proposer = File.ReadAllText(Path.Combine(Repository.Root, "src/EquityBrief.Worker/Filter/ShapeProposer.cs"));

        Assert.DoesNotMatch(@"(?is)(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+(filter_version|candidate_register|gate_result)\b", proposer);
        Assert.DoesNotContain("CandidateRegistrar", proposer, StringComparison.Ordinal);
        Assert.DoesNotContain("ShapeCommand", proposer, StringComparison.Ordinal);
        var arithmetic = File.ReadAllText(Path.Combine(Repository.Root, "src/EquityBrief.Core/Filter/ShapeProposals.cs"));

        Assert.DoesNotMatch(@"(INSERT\s+INTO|UPDATE\s+\w+\s+SET|DELETE\s+FROM)", arithmetic);
        Assert.DoesNotContain("Sqlite", arithmetic, StringComparison.Ordinal);
    }
}
