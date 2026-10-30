using System.Globalization;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// The Loop page's decisions: the operator's approve and decline of a proposal of an index's newest run, and approve of a
// restore of a family the live alarm flagged, each one row of the decisions table, the fourth table the read surface
// writes, under the page's own header; refused where the store does not hold what it names or where no apply could apply
// it, with nothing written. And what the page draws of them: every decision on the index with what the apply step did,
// the settings approvals stored, and the live alarm's periods, read and never worked out here.
// see: An approved change is applied by the night's own build on the index it was approved on alone
// see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
// see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
public sealed partial class ReadApi
{
    const string LoopDecisionsOf = @"
        SELECT d.run_id, d.family, d.proposal, d.decision, d.reason, d.decided_at, a.outcome, a.words, a.applied_at, p.blocks, p.change
        FROM loop_decision d
        LEFT JOIN loop_applied a ON a.run_id = d.run_id AND a.index_code = d.index_code AND a.family = d.family AND a.proposal = d.proposal
        LEFT JOIN loop_proposal p ON p.run_id = d.run_id AND p.index_code = d.index_code AND p.family = d.family AND p.proposal = d.proposal
        WHERE d.index_code = $index
        ORDER BY d.decided_at DESC, d.rowid DESC;";

    const string LoopSettingsOf = "SELECT id, family, words, set_at, run_id, proposal FROM provisional_setting WHERE index_code = $index ORDER BY id DESC;";

    const string LoopAlarmsOf = @"
        SELECT family, period, trades, edge, edge_floor, counted, under, streak, flagged, reference FROM loop_alarm
        WHERE index_code = $index ORDER BY family, period;";

    // A proposal a decision names, whether it passed, its change and its blocks, beside the index's newest run.
    const string ProposalToDecide = @"
        SELECT p.passed, p.change, p.blocks, (SELECT r.run_id FROM loop_run r WHERE r.index_code = p.index_code ORDER BY r.rowid DESC LIMIT 1)
        FROM loop_proposal p WHERE p.run_id = $run_id AND p.index_code = $index AND p.family = $family AND p.proposal = $proposal;";

    const string DecidedOf = "SELECT COUNT(*) FROM loop_decision WHERE run_id = $run_id AND index_code = $index AND family = $family AND proposal = $proposal;";

    const string ApprovedInRun = "SELECT COUNT(*) FROM loop_decision WHERE run_id = $run_id AND index_code = $index AND family = $family AND decision = 'approved';";

    // The blocks the same change of a family was last declined over on an earlier run of the index.
    const string DeclinedBefore = @"
        SELECT MAX(q.blocks) FROM loop_decision e
        JOIN loop_proposal q ON q.run_id = e.run_id AND q.index_code = e.index_code AND q.family = e.family AND q.proposal = e.proposal
        WHERE e.decision = 'declined' AND e.index_code = $index AND e.family = $family AND e.proposal = $proposal AND e.run_id <> $run_id
            AND COALESCE(q.change, '') = COALESCE($change, '');";

    // A family's proposals on a run with the blocks the same change was last declined over on an earlier run, which say
    // which one the run puts to the operator.
    const string FamilyOnTheRun = @"
        SELECT p.proposal, p.adjusted, p.passed, p.change IS NOT NULL, p.blocks,
            (SELECT MAX(q.blocks) FROM loop_decision e
                JOIN loop_proposal q ON q.run_id = e.run_id AND q.index_code = e.index_code AND q.family = e.family AND q.proposal = e.proposal
                WHERE e.decision = 'declined' AND e.index_code = p.index_code AND e.family = p.family AND e.proposal = p.proposal AND e.run_id <> p.run_id
                    AND COALESCE(q.change, '') = COALESCE(p.change, ''))
        FROM loop_proposal p WHERE p.run_id = $run_id AND p.index_code = $index AND p.family = $family;";

    const string WordsOfProposal = "SELECT words FROM loop_proposal WHERE run_id = $run_id AND index_code = $index AND family = $family AND proposal = $proposal;";

    const string YearsOfProposal = "SELECT year, proposed_units, proposed_total, current_total FROM loop_test WHERE run_id = $run_id AND index_code = $index AND family = $family AND proposal = $proposal ORDER BY year;";

    const string NewestSettingOf = "SELECT id FROM provisional_setting WHERE index_code = $index AND family = $family ORDER BY id DESC LIMIT 1;";

    const string NewestPeriodOf = "SELECT flagged FROM loop_alarm WHERE index_code = $index AND family = $family ORDER BY period DESC LIMIT 1;";

    public async Task<IReadOnlyList<LoopDecisionRow>> LoopDecisionsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopDecisionsOf;
        command.Parameters.AddWithValue("$index", index);

        var rows = new List<LoopDecisionRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopDecisionRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return rows;
    }

    // The index's newest tester run by the order the runs were written, the one whose proposals are put to the operator.
    public async Task<string?> NewestLoopRunAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT run_id FROM loop_run WHERE index_code = $index ORDER BY rowid DESC LIMIT 1;";
        command.Parameters.AddWithValue("$index", index);

        return await command.ExecuteScalarAsync() as string;
    }

    // The sector heavyweights' six settings of each design as the index's newest tester run judged them, the month it ran
    // for, each design's adjusted p and whether it passed, read for the card of a book registered before its family's
    // freeze and never worked out here.
    // see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
    const string SixSettingsOf = @"
        SELECT r.month, p.proposal, p.adjusted, p.passed FROM loop_proposal p JOIN loop_run r ON r.run_id = p.run_id
        WHERE p.index_code = $index AND p.family = $family AND p.proposal IN ($a, $b)
            AND p.run_id = (SELECT n.run_id FROM loop_run n WHERE n.index_code = $index ORDER BY n.rowid DESC LIMIT 1)
        ORDER BY p.proposal;";

    public async Task<IReadOnlyList<SixSettingsVerdict>> SixSettingsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SixSettingsOf;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", EquityBrief.Core.Families.HeavyweightRule.Name);
        command.Parameters.AddWithValue("$a", SixSettings.DesignA);
        command.Parameters.AddWithValue("$b", SixSettings.DesignB);

        var verdicts = new List<SixSettingsVerdict>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            verdicts.Add(new SixSettingsVerdict(reader.GetString(1), reader.GetString(0), reader.IsDBNull(2) ? null : reader.GetDouble(2), reader.GetInt64(3) == 1));
        }

        return verdicts;
    }

    // Each family on an index whose newest period the live alarm read stands flagged, with that period and the one before
    // it that counted.
    const string FlaggedOf = @"
        SELECT a.family, a.period, a.streak FROM loop_alarm a
        WHERE a.index_code = $index AND a.flagged = 1
            AND a.period = (SELECT MAX(b.period) FROM loop_alarm b WHERE b.index_code = a.index_code AND b.family = a.family)
        ORDER BY a.family;";

    public async Task<IReadOnlyList<(string Family, DateOnly Period, int Streak)>> LoopFlagsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FlaggedOf;
        command.Parameters.AddWithValue("$index", index);

        var rows = new List<(string, DateOnly, int)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetInt32(2)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<LoopSettingRow>> LoopSettingsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopSettingsOf;
        command.Parameters.AddWithValue("$index", index);

        var rows = new List<LoopSettingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopSettingRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<LoopAlarmRow>> LoopAlarmsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopAlarmsOf;
        command.Parameters.AddWithValue("$index", index);

        var rows = new List<LoopAlarmRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopAlarmRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(2),
                Optional(reader, 3),
                Optional(reader, 4),
                reader.GetInt64(5) == 1,
                reader.GetInt64(6) == 1,
                reader.GetInt32(7),
                reader.GetInt64(8) == 1,
                reader.GetString(9)));
        }

        return rows;
    }

    // The operator's word on a proposal of the index's newest run. Refused for a run that is not the index's newest, a
    // proposal the run does not hold, a second word on one proposal, an approval of a proposal that did not pass or states
    // no change, of a change no apply could apply, of a second change to a family in one run, or of a change declined
    // before and not put again, and a decline giving no reason.
    public async Task<RequestWritten> DecideAsync(string index, string run, string family, string proposal, bool approve, string? reason)
    {
        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        (bool Passed, string? Change, int Blocks, string? Newest)? named = null;

        await using (var reading = Command(connection, transaction, ProposalToDecide, [("$run_id", run), ("$index", index), ("$family", family), ("$proposal", proposal)]))
        await using (var reader = await reading.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                named = (reader.GetInt64(0) == 1, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3));
            }
        }

        if (named is not { } held)
        {
            return new RequestWritten(false, $"Nothing was decided: the run {run} holds no proposal \"{proposal}\" for the {family} on this index.");
        }

        if (held.Newest != run)
        {
            return new RequestWritten(false, "Nothing was decided: a newer tester run stands for this index, so its proposals are the ones put to you.");
        }

        if (await CountAsync(connection, transaction, DecidedOf, [("$run_id", run), ("$index", index), ("$family", family), ("$proposal", proposal)]) > 0)
        {
            return new RequestWritten(false, "Nothing was decided: this proposal already holds your word.");
        }

        // From the 20.8 correction of 2026-10-19 a proposal the local model's cutoff holds back is put to no one.
        var cutoff = await CutoffAsync(connection, transaction);

        if (held.Passed && held.Change is not null && await HeldByCutoffAsync(connection, transaction, run, index, family, proposal, cutoff) && cutoff is { } day)
        {
            return new RequestWritten(false, "Nothing was decided. " + LoopDecisions.HeldByCutoffWords(day));
        }

        var put = held.Passed && held.Change is not null ? await PutAsync(connection, transaction, run, index, family, cutoff) : null;
        var notPut = put is not null && put != proposal
            ? $"Nothing was decided: this run puts the family's strongest proposal that passed to you, {put}, and no other."
            : null;

        if (!approve)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return new RequestWritten(false, "Nothing was declined: a decline records its reason, and none was given.");
            }

            if (notPut is not null)
            {
                return new RequestWritten(false, notPut);
            }

            await DecisionAsync(connection, transaction, run, index, family, proposal, LoopDecisions.Declined, reason.Trim());
            await transaction.CommitAsync();

            return new RequestWritten(true, "Declined, with your reason. It changes nothing, and the proposal is put to you again only once a later run reads a new complete block and it passes with that block.");
        }

        if (!held.Passed || held.Change is null)
        {
            return new RequestWritten(false, held.Passed
                ? "Nothing was approved: the proposal states no change in the form an approval applies."
                : "Nothing was approved: the proposal did not pass the tester.");
        }

        var registered = await RegisteredOnAsync(index, family);

        if (LoopDecisions.Refusal(index, family, registered) is { } refused)
        {
            return new RequestWritten(false, $"Nothing was approved: {refused}.");
        }

        if (await CountAsync(connection, transaction, ApprovedInRun, [("$run_id", run), ("$index", index), ("$family", family)]) > 0)
        {
            return new RequestWritten(false, "Nothing was approved: you approved another change to this family in this run, and one change a family a run is applied.");
        }

        if (await ScalarAsync(connection, transaction, DeclinedBefore, [("$run_id", run), ("$index", index), ("$family", family), ("$proposal", proposal), ("$change", held.Change)]) is long declined
            && !LoopDecisions.PutAgain((int)declined, held.Blocks, passedNow: true))
        {
            return new RequestWritten(false, FormattableString.Invariant($"Nothing was approved: you declined this change over {declined} blocks, and this run reads {held.Blocks}, so it is not put to you again until a later run reads a new complete block."));
        }

        if (notPut is not null)
        {
            return new RequestWritten(false, notPut);
        }

        await DecisionAsync(connection, transaction, run, index, family, proposal, LoopDecisions.Approved, null);
        await transaction.CommitAsync();

        // see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
        // see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
        var large = index == EquityBrief.Core.Families.MarketCloses.Index;

        return new RequestWritten(true, large && family == EquityBrief.Core.Families.SetupFamilies.Pullback
            ? LoopChange.Read(held.Change)?.Shape is { Count: > 0 }
                ? "Approved. The move is applied at the start of the next night, on this index alone: the swing filter opens its next version with the move made and the live filter registered again under it, restarting its record."
                : "Approved. The change is applied at the start of the next night, on this index alone, and the swing filter's page reads it from that night on."
            : large || registered
                ? FormattableString.Invariant($"Approved. The change is applied at the start of the next night, on this index alone, and registered as the family's live rule, which the page reads from that night on; the rule it replaces runs beside it as a variant for {LoopDecisions.ReplacedSessions} sessions, or in the background where the family holds {EquityBrief.Core.Candidates.CandidateFamily.Maximum} rules.")
                : "Approved. The change is applied before the next night, on this index alone, and the family reads it from that night on.");
    }

    // From 21.5 whether the family's live rule stands registered on the index, which a family keeping no stored setting is
    // changed through alone, read off the register as it stands.
    // see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
    public async Task<bool> RegisteredOnAsync(string index, string family) =>
        LoopDecisions.RegistersOn(index, family, EquityBrief.Core.Candidates.CandidateFamily.Standing(RegisterRowsOf(await RegisteredCandidatesAsync()), DateTimeOffset.MaxValue));

    // The register's rows as the core's reader of what stands reads them.
    public static IReadOnlyList<EquityBrief.Core.Candidates.RegisterRow> RegisterRowsOf(IEnumerable<CandidateRow> rows) =>
    [
        .. rows.Select(row => new EquityBrief.Core.Candidates.RegisterRow(
            row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator, row.Parameters, row.EvaluatorVersion, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
    ];

    // The operator's approval of a restore of a family the live alarm flagged: the setting standing before the newest
    // change an approval stored, applied as a change is. Refused where the change named is not the family's newest, where
    // the family's newest period is not flagged, and where no apply could apply it.
    public async Task<RequestWritten> RestoreAsync(string index, string family, long setting)
    {
        if (LoopDecisions.Refusal(index, family) is { } refused)
        {
            return new RequestWritten(false, $"Nothing was restored: {refused}.");
        }

        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        if (await ScalarAsync(connection, transaction, NewestSettingOf, [("$index", index), ("$family", family)]) is not long newest || newest != setting)
        {
            return new RequestWritten(false, "Nothing was restored: the change named is not the newest the family stands at.");
        }

        if (await ScalarAsync(connection, transaction, NewestPeriodOf, [("$index", index), ("$family", family)]) is not long flagged || flagged != 1)
        {
            return new RequestWritten(false, "Nothing was restored: the live alarm has not flagged this rule, and a restore waits on its flag.");
        }

        var proposal = LoopDecisions.RestoreProposal(setting);

        if (await CountAsync(connection, transaction, DecidedOf, [("$run_id", LoopDecisions.RestoreRun), ("$index", index), ("$family", family), ("$proposal", proposal)]) > 0)
        {
            return new RequestWritten(false, "Nothing was restored: this restore already holds your word.");
        }

        await DecisionAsync(connection, transaction, LoopDecisions.RestoreRun, index, family, proposal, LoopDecisions.Approved, null);
        await transaction.CommitAsync();

        return new RequestWritten(true, "Approved. The setting before the change is restored before the next night, on this index alone.");
    }

    async Task DecisionAsync(SqliteConnection connection, SqliteTransaction transaction, string run, string index, string family, string proposal, string decision, string? reason)
    {
        await using var command = Command(connection, transaction, InsertDecision,
        [
            ("$run_id", run), ("$index", index), ("$family", family), ("$proposal", proposal), ("$decision", decision),
            ("$reason", (object?)reason ?? DBNull.Value), ("$decided_at", clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture)),
        ]);

        await command.ExecuteNonQueryAsync();
    }

    static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object? Value)> parameters)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    // The one proposal of a family the run puts to the operator, none where none is left: one an earlier decline holds back
    // left out, and from the 20.8 correction of 2026-10-19 one the local model's cutoff holds back.
    static async Task<string?> PutAsync(SqliteConnection connection, SqliteTransaction transaction, string run, string index, string family, DateOnly? cutoff)
    {
        var read = new List<(string Proposal, double? Adjusted, bool Passed, bool Changes, bool Declined)>();

        await using (var command = Command(connection, transaction, FamilyOnTheRun, [("$run_id", run), ("$index", index), ("$family", family)]))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var declined = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);

                read.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetDouble(1),
                    reader.GetInt64(2) == 1,
                    reader.GetInt64(3) == 1,
                    declined is { } blocks && !LoopDecisions.PutAgain(blocks, reader.GetInt32(4), passedNow: true)));
            }
        }

        var proposals = new List<(string, double?, bool, bool, bool)>();

        foreach (var one in read)
        {
            proposals.Add((one.Proposal, one.Adjusted, one.Passed, one.Changes, one.Declined || await HeldByCutoffAsync(connection, transaction, run, index, family, one.Proposal, cutoff)));
        }

        return LoopDecisions.PutOf(proposals);
    }

    // The local model's cutoff the newest reading version whose history read is complete states, none before one is.
    static async Task<DateOnly?> CutoffAsync(SqliteConnection connection, SqliteTransaction transaction) =>
        await ScalarAsync(connection, transaction, NewestCutoff, []) is string day ? DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    // Whether the local model's cutoff holds a proposal of the run back, read over its words and its own test years.
    // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
    static async Task<bool> HeldByCutoffAsync(SqliteConnection connection, SqliteTransaction transaction, string run, string index, string family, string proposal, DateOnly? cutoff)
    {
        if (cutoff is null)
        {
            return false;
        }

        var parameters = new (string Name, object? Value)[] { ("$run_id", run), ("$index", index), ("$family", family), ("$proposal", proposal) };
        var words = await ScalarAsync(connection, transaction, WordsOfProposal, parameters) as string;
        var years = new List<(int, int, double, double)>();

        await using (var command = Command(connection, transaction, YearsOfProposal, parameters))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                years.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetDouble(2), reader.GetDouble(3)));
            }
        }

        return LoopDecisions.HeldByCutoff(words, years, cutoff);
    }

    static async Task<long> CountAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object? Value)> parameters) =>
        await ScalarAsync(connection, transaction, sql, parameters) is long count ? count : 0;

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object? Value)> parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        var scalar = await command.ExecuteScalarAsync();

        return scalar is DBNull ? null : scalar;
    }
}
