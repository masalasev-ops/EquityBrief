using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// What the apply step did with one proposal: its run, index, family and name, whether it was applied, and why in words.
public sealed record LoopApplication(string Run, string Index, string Family, string Proposal, bool Applied, string Words);

public sealed record LoopApplyOutcome(IReadOnlyList<LoopApplication> Applications)
{
    public int Applied => Applications.Count(one => one.Applied);

    public int Refused => Applications.Count(one => !one.Applied);
}

// The apply step. Before the index families read the night, it takes every proposal the operator approved and the step
// has not yet answered, and, only where the adopt setting reads automatic, every proposal of an index's newest run that
// passed and holds no decision, a declined one only where it was put again; and every restore the operator approved. A
// change to an S&P 400 or 600 swing family is written on top of the setting the family stands at as a new row of the
// stored settings, which the family reads from the night on, on that index alone; every other change is refused with
// its reason, an S&P 500 rule's because its page is drawn by its family's own code, and a sector heavyweights' book's
// because it changes only by a freeze. Every answer is one row and none is written twice. It reads no market data,
// makes no request and calls no model.
// see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
// see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
public sealed class LoopApply(IClock clock, string databaseFile, string? adopt = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LoopRun, Touch.Read),
            new StoreTouch(Store.LoopProposal, Touch.Read),
            new StoreTouch(Store.LoopDecision, Touch.Read),
            new StoreTouch(Store.LoopApplied, Touch.Read | Touch.Insert),
            new StoreTouch(Store.ProvisionalSetting, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "loop-apply";

    public const string Verb = "loop-apply";

    // The proposal a restore's decision names, and the word its run carries in place of a tester run's.
    public const string RestoreRun = "restore";

    public static string RestoreProposal(long setting) => FormattableString.Invariant($"restore the setting before change {setting}");

    // Why a change is not applied.
    public const string LargeIndexRefused = "an S&P 500 rule's page is drawn by its family's own code, so an approved change there waits on the operator's ruling of how it reaches the page";

    public const string BookRefused = "a sector heavyweights' book changes its setting only by a freeze, so an approved change to it is not applied";

    public const string NoChangeRefused = "the proposal states no change in the form an approval applies";

    public const string FamilyRefused = "the family has no setting an approval applies";

    // The swing families an approval changes on the S&P 400 and 600.
    public static IReadOnlyList<string> Applies { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name];

    // Every approved decision the step has not answered, oldest first.
    const string Approved = @"
        SELECT d.run_id, d.index_code, d.family, d.proposal, p.change
        FROM loop_decision d
        LEFT JOIN loop_proposal p ON p.run_id = d.run_id AND p.index_code = d.index_code AND p.family = d.family AND p.proposal = d.proposal
        LEFT JOIN loop_applied a ON a.run_id = d.run_id AND a.index_code = d.index_code AND a.family = d.family AND a.proposal = d.proposal
        WHERE d.decision = 'approved' AND a.run_id IS NULL
        ORDER BY d.decided_at, d.rowid;
    ";

    // Each index's newest run's proposals that passed and hold no decision and no answer, with the blocks a decline of the
    // same proposal on an earlier run read, which the automatic setting alone reads.
    const string Undecided = @"
        SELECT p.run_id, p.index_code, p.family, p.proposal, p.change, p.blocks,
            (SELECT MAX(q.blocks) FROM loop_decision e
                JOIN loop_proposal q ON q.run_id = e.run_id AND q.index_code = e.index_code AND q.family = e.family AND q.proposal = e.proposal
                WHERE e.decision = 'declined' AND e.index_code = p.index_code AND e.family = p.family AND e.proposal = p.proposal AND COALESCE(q.change, '') = COALESCE(p.change, ''))
        FROM loop_proposal p
        WHERE p.passed = 1
            AND p.run_id = (SELECT r.run_id FROM loop_run r WHERE r.index_code = p.index_code ORDER BY r.rowid DESC LIMIT 1)
            AND NOT EXISTS (SELECT 1 FROM loop_decision d WHERE d.run_id = p.run_id AND d.index_code = p.index_code AND d.family = p.family AND d.proposal = p.proposal)
            AND NOT EXISTS (SELECT 1 FROM loop_applied a WHERE a.run_id = p.run_id AND a.index_code = p.index_code AND a.family = p.family AND a.proposal = p.proposal)
        ORDER BY p.index_code, p.family, p.proposal;
    ";

    // The settings a family has stood at, oldest first.
    const string Standing = "SELECT id, change FROM provisional_setting WHERE index_code = $index AND family = $family ORDER BY id;";

    const string InsertSetting = @"
        INSERT INTO provisional_setting (index_code, family, change, words, set_at, run_id, proposal)
        VALUES ($index, $family, $change, $words, $set_at, $run_id, $proposal);
    ";

    const string InsertApplied = @"
        INSERT INTO loop_applied (run_id, index_code, family, proposal, applied_at, outcome, words)
        VALUES ($run_id, $index, $family, $proposal, $applied_at, $outcome, $words);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows_written, 0, 0, '0', $detail);";

    public async Task<LoopApplyOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var mode = LoopDecisions.AdoptOf(adopt);
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var waiting = new List<(string Run, string Index, string Family, string Proposal, string? Change)>();

        await foreach (var row in RowsAsync(connection, null, Approved, [], cancellation))
        {
            waiting.Add((row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4)));
        }

        if (mode == LoopDecisions.Automatic)
        {
            await foreach (var row in RowsAsync(connection, null, Undecided, [], cancellation))
            {
                var declinedAt = row.IsDBNull(6) ? (int?)null : row.GetInt32(6);

                if (declinedAt is null || LoopDecisions.PutAgain(declinedAt.Value, row.GetInt32(5), passedNow: true))
                {
                    waiting.Add((row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4)));
                }
            }
        }

        var applications = new List<LoopApplication>();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var (run, index, family, proposal, change) in waiting)
        {
            var standing = new List<(long Id, string Change)>();

            await foreach (var row in RowsAsync(connection, transaction, Standing, [("$index", index), ("$family", family)], cancellation))
            {
                standing.Add((row.GetInt64(0), row.GetString(1)));
            }

            var (applied, words, written) = Answer(run, index, family, proposal, change, standing);

            if (written is { } setting)
            {
                await ExecuteAsync(connection, transaction, InsertSetting,
                [
                    ("$index", index), ("$family", family), ("$change", setting.Json), ("$words", setting.Words()),
                    ("$set_at", Stamp(clock.UtcNow)), ("$run_id", run), ("$proposal", proposal),
                ], cancellation);
            }

            await ExecuteAsync(connection, transaction, InsertApplied,
            [
                ("$run_id", run), ("$index", index), ("$family", family), ("$proposal", proposal), ("$applied_at", Stamp(clock.UtcNow)),
                ("$outcome", applied ? LoopDecisions.Applied : LoopDecisions.Refused), ("$words", words),
            ], cancellation);
            applications.Add(new LoopApplication(run, index, family, proposal, applied, words));
        }

        var outcome = new LoopApplyOutcome(applications);
        var detail = applications.Count == 0
            ? $"no approved change waiting, the adopt setting reading {mode}"
            : FormattableString.Invariant($"{outcome.Applied} applied and {outcome.Refused} refused, the adopt setting reading {mode}: ")
                + string.Join("; ", applications.Select(one => $"{one.Index} {one.Family}, {one.Proposal}, {(one.Applied ? "applied" : "refused")}: {one.Words}"));

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId), ("$stage", Stage), ("$started_at", Stamp(started)), ("$ended_at", Stamp(clock.UtcNow)),
            ("$rows_written", applications.Count + applications.Count(one => one.Applied)), ("$detail", detail),
        ], cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // What the step does with one proposal: a restore writes again the setting standing before the change it names, or
    // the family's code setting where none stood before it; an S&P 400 or 600 swing family's change is written on top of
    // the setting it stands at; every other is refused with its reason.
    public static (bool Applied, string Words, LoopChange? Written) Answer(string run, string index, string family, string proposal, string? change, IReadOnlyList<(long Id, string Change)> standing)
    {
        if (index == WalkForwardTester.LargeIndex)
        {
            return (false, LargeIndexRefused, null);
        }

        if (family == HeavyweightRule.Name)
        {
            return (false, BookRefused, null);
        }

        if (!Applies.Contains(family, StringComparer.Ordinal))
        {
            return (false, FamilyRefused, null);
        }

        var current = standing.Count > 0 ? LoopChange.Read(standing[^1].Change) : null;

        if (run == RestoreRun)
        {
            var undone = standing.Select((row, at) => (row, at)).FirstOrDefault(one => RestoreProposal(one.row.Id) == proposal);

            if (undone.row.Change is null)
            {
                return (false, "the restore names a change the family's settings do not hold", null);
            }

            var before = undone.at > 0 ? LoopChange.Read(standing[undone.at - 1].Change) : null;
            var restored = before ?? new LoopChange(null, null, null, LoopChange.NoHooks);

            return (true, "restored " + (before is null ? "the family's own setting" : restored.Words()), restored);
        }

        if (LoopChange.Read(change) is not { Moves: true } made)
        {
            return (false, NoChangeRefused, null);
        }

        if (family == SetupFamilies.Pullback && made.Places is not null)
        {
            return (false, "the pullback's setting on its grid is not moved by an approval, only its hooks", null);
        }

        var stands = made.OnTopOf(current);

        return (true, "the family stands from the next night at " + stands.Words(), stands);
    }

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            yield return reader;
        }
    }
}
