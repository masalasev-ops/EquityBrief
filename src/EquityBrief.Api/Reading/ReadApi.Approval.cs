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
// see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
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

        var put = held.Passed && held.Change is not null ? await PutAsync(connection, transaction, run, index, family) : null;
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

        if (LoopDecisions.Refusal(index, family) is { } refused)
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

        return new RequestWritten(true, "Approved. The change is applied before the next night, on this index alone, and the family reads it from that night on.");
    }

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

    // The one proposal of a family the run puts to the operator, none where none is left.
    static async Task<string?> PutAsync(SqliteConnection connection, SqliteTransaction transaction, string run, string index, string family)
    {
        var proposals = new List<(string, double?, bool, bool, bool)>();

        await using var command = Command(connection, transaction, FamilyOnTheRun, [("$run_id", run), ("$index", index), ("$family", family)]);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var declined = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);

            proposals.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.GetInt64(2) == 1,
                reader.GetInt64(3) == 1,
                declined is { } blocks && !LoopDecisions.PutAgain(blocks, reader.GetInt32(4), passedNow: true)));
        }

        return LoopDecisions.PutOf(proposals);
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
