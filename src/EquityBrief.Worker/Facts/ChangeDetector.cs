using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Facts;

// `NotCompared` names each row whose change list is unknown and why, being a
// facts file that is empty or names a fact twice. Its `material_changes` is left
// null, which reads as unknown, where an empty list would read as nothing changed.
public sealed record ChangeOutcome(
    int RowsExamined,
    int RowsWritten,
    int ChangesRecorded,
    int PayloadsEmptied,
    IReadOnlyList<string>? NotCompared = null,
    // Rows whose comparison could not be made tonight and which kept the list
    // an earlier run made, rather than having it replaced with null.
    int KeptEarlierList = 0);

// The change detector. Compares tonight's facts against the last stored ones for
// each name and writes only what changed, on its own column of the same row.
//
// It owns `material_changes` and touches neither `payload` nor `payload_hash`.
// The assembler owns those two, the grain is the same, and the sets are disjoint,
// which is what permits a table with an inserter and a different updater under a
// rule that forbids two owners for one operation. That split is a property
// rather than a coincidence of ordering: a facts re-run must not blank the
// change list, and the assembler's own statement names two columns and no more.
//
// From 5.4 it also empties the payload of a past night the name did not fire on,
// which is the retention section 16 states. That read is of the listings store
// and waits for the checkpoint that creates it, because a component declaring a
// read of a table nothing has created is a declaration with nothing behind it.
public sealed class ChangeDetector : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "changes";

    // Tonight's row and the one before it, per name, in one pass. The previous
    // row is the newest strictly older session rather than yesterday, because a
    // name whose night did not run has no row for yesterday and the comparison
    // is still against the last facts anybody wrote.
    //
    // The list already stored on tonight's row comes with it, so a comparison
    // that cannot be made tonight never replaces one an earlier run made.
    //
    // Tonight's rows only, being the newest session any file holds, rather than
    // each name's newest row. Until the phase 5 sign-off a name with no file
    // tonight had its own last file compared again every night: once the
    // retention had emptied that file, its list was replaced with null beside a
    // whole previous file, which is the keep rule's mirror and not a comparison
    // anybody asked for.
    const string PairsToCompare = @"
        SELECT f.ticker,
               f.session_date,
               f.payload,
               (SELECT p.payload FROM facts p
                WHERE p.ticker = f.ticker AND p.session_date < f.session_date
                ORDER BY p.session_date DESC LIMIT 1),
               f.material_changes
        FROM facts f
        WHERE f.session_date = (SELECT MAX(s.session_date) FROM facts s)
        ORDER BY f.ticker;
    ";

    // This component's own column and no other. Written as an update rather than
    // an upsert, because a facts row it has not seen is a row the assembler has
    // not written and there is nothing to compare.
    const string RecordChanges = @"
        UPDATE facts
        SET material_changes = $material_changes
        WHERE ticker = $ticker AND session_date = $session_date;
    ";

    // The retention section 16 states, which arrived here at 5.4 with the store
    // it reads. A facts row is kept whole for a night the name fired; every
    // other night keeps the hash and the material changes, with the payload
    // emptied.
    //
    // An update rather than a delete, because what the hash is for is answering
    // whether a later night's facts differ without holding the facts they differ
    // from, and a deleted row answers nothing. Tonight's row is never emptied,
    // whatever it fired: it is the row every reader is about to read.
    //
    // "or was opened" is not one of the conditions, because nothing in this
    // store records that a name was read, and a cell promising a behaviour no
    // test can induce is worse than a cell that says less.
    const string EmptyUnlistedPayloads = @"
        UPDATE facts
        SET payload = ''
        WHERE payload != ''
          AND session_date < $tonight
          AND NOT EXISTS (
              SELECT 1 FROM listing l
              WHERE l.ticker = facts.ticker
                AND l.session_date = facts.session_date
                AND l.fired_count > 0);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public ChangeDetector(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<ChangeOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var pairs = new List<(string Ticker, string SessionDate, string Payload, string? Previous, string? Stored)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PairsToCompare;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                pairs.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        var written = 0;
        var changes = 0;
        var kept = 0;

        // One transaction around the whole loop rather than one per row, for the
        // reason `IndicatorEngine` batches its points: every row that commits on
        // its own costs a disk sync, and a sync costs the same whatever the row
        // holds. Over the four names of the committed fixture that is invisible.
        // Over 503 it was measured at 106 seconds for 503 rows before this was here.
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        var notCompared = new List<string>();

        foreach (var pair in pairs)
        {
            // A pair that cannot be compared is that name's own unknown and not
            // the night's failure. Before the phase 5 sign-off one file naming a
            // fact twice stopped this stage for every name, on a framework
            // message about a dictionary key.
            var comparable = FactsFile.TryChanged(pair.Previous, pair.Payload, out var changed, out var reason);

            // A list an earlier run made stands where the file it was made
            // against has since been emptied. The retention empties a past
            // night's payload at the end of every run, so a second run of the
            // same night finds the file before tonight's blank and, until the
            // phase 5 sign-off, replaced a list it had computed an hour earlier
            // with null. The stored list was made against that file while it was
            // whole, and tonight's file is the same one unless the assembler
            // replaced it, in which case the row arrives here with no list at
            // all. Only the emptied case: a previous file that names a fact
            // twice was never comparable, so no list can have been made
            // against it.
            if (!comparable && pair.Previous is { Length: 0 } && pair.Stored is not null)
            {
                kept++;

                continue;
            }

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = RecordChanges;
            command.Parameters.AddWithValue("$ticker", pair.Ticker);
            command.Parameters.AddWithValue("$session_date", pair.SessionDate);

            // An empty list rather than a null where there is nothing to
            // compare against, because a name whose first night this is has no
            // changes rather than an unknown number of them, and the two read
            // differently on a page. A null where the comparison could not be
            // made, for the same reason in the other direction.
            if (comparable)
            {
                command.Parameters.AddWithValue("$material_changes", JsonSerializer.Serialize(changed));
            }
            else
            {
                command.Parameters.AddWithValue("$material_changes", DBNull.Value);
                notCompared.Add($"{pair.Ticker} ({reason})");
            }

            await command.ExecuteNonQueryAsync(cancellation);

            written++;
            changes += changed.Count;
        }

        await transaction.CommitAsync(cancellation);

        var emptied = await EmptyAsync(connection, cancellation);

        await RecordAsync(connection, runId, startedAt, pairs.Count, written, changes, emptied, notCompared, kept, cancellation);

        return new ChangeOutcome(pairs.Count, written, changes, emptied, notCompared, kept);
    }

    // The newest facts session anywhere, read from the store so a component run
    // on its own empties what a night would. Every row before it is a past
    // night; tonight's is not touched.
    static async Task<int> EmptyAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var newest = connection.CreateCommand();
        newest.CommandText = "SELECT MAX(session_date) FROM facts;";

        if (await newest.ExecuteScalarAsync(cancellation) is not string tonight)
        {
            return 0;
        }

        await using var command = connection.CreateCommand();

        command.CommandText = EmptyUnlistedPayloads;
        command.Parameters.AddWithValue("$tonight", tonight);

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int examined,
        int written,
        int changes,
        int emptied,
        IReadOnlyList<string> notCompared,
        int kept,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", written);

        // The names not compared are named, the first few of them, so a rise from
        // one to fifty is a fact on the run page rather than a count nobody reads.
        command.Parameters.AddWithValue(
            "$detail",
            $"{examined} row(s) examined, {written} written, {changes} material change(s), {emptied} payload(s) emptied, " +
            $"{kept} kept the list an earlier run made, {notCompared.Count} not compared" +
            (notCompared.Count == 0 ? string.Empty : ": " + string.Join("; ", notCompared.Take(5))));

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
