using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Membership;

// Membership loader.
//
// Records tonight's index constituents with join and leave dates, so a name
// added last month is not shown as present in an older window, and answers which
// names were members on a past date
// (see: The universe is the S&P 500, and membership is fetched, not maintained).
//
// One feed call for the whole index rather than one per name, which is what the
// nightly path requires (see: The nightly run is arithmetic only).
public sealed class MembershipLoader(
    IIndexMembershipFeed feed,
    IClock clock,
    string databaseFile) : IComponent
{
    // The catalogue row and the matrix row for this component, in its own words.
    // component-access reconciles this against both rows, against SCHEMA's
    // ownership of membership, and against the statements below, in both
    // directions.
    //
    // Read as well as Insert and Update: the past-date query lives here, and a
    // component that reads what it writes with only the write declared is the
    // defect this pass repaired in two rows of the matrix.
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.IndexMembership]);

    // An upsert rather than a delete and reinsert, because membership has no
    // declared deleter and a name that leaves keeps its row and its history
    // (see: Your own listing history is kept forever). A second run on the same
    // night rewrites the same rows with the same values, which is what makes the
    // stage idempotent.
    const string Upsert = @"
        INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at)
        VALUES ($index_code, $ticker, $joined, $left, $observed_at)
        ON CONFLICT (index_code, ticker, joined) DO UPDATE SET
            ""left"" = excluded.""left"",
            observed_at = excluded.observed_at;
    ";

    // Members on a date, which is a different question from members now. A name
    // is a member on that date when it had joined by then and had not left, and
    // a leave date is the day it stopped being one, so the comparison is strict
    // on the left edge and not on the right.
    const string MembersOn = @"
        SELECT ticker
        FROM membership
        WHERE index_code = $index_code
          AND joined <= $on
          AND (""left"" IS NULL OR ""left"" > $on)
        ORDER BY ticker;
    ";

    // The run log row for this stage. Insert only: the run log is appended to
    // and never rewritten, and SCHEMA gives its Insert cell to "every component
    // appends" rather than to a named owner.
    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // Measured from the store rather than counted as the writes went by, which
    // is what SCHEMA requires of this column: a stage's own count of what it
    // wrote is the stage's opinion.
    const string RowsObservedAt = @"
        SELECT COUNT(*) FROM membership WHERE index_code = $index_code AND observed_at = $observed_at;
    ";

    public const string Stage = "membership";

    public async Task<int> LoadAsync(
        string indexCode,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var startedAt = clock.UtcNow;
        var constituents = await feed.ConstituentsAsync(indexCode, cancellationToken);
        var observedAt = startedAt.ToString("O");

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var constituent in constituents)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Upsert;
            command.Parameters.AddWithValue("$index_code", indexCode);
            command.Parameters.AddWithValue("$ticker", constituent.Ticker);
            command.Parameters.AddWithValue("$joined", Text(constituent.Joined));
            command.Parameters.AddWithValue("$left", Text(constituent.Left) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$observed_at", observedAt);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var measure = connection.CreateCommand();
        measure.CommandText = RowsObservedAt;
        measure.Parameters.AddWithValue("$index_code", indexCode);
        measure.Parameters.AddWithValue("$observed_at", observedAt);

        var written = Convert.ToInt32(await measure.ExecuteScalarAsync(cancellationToken));

        await using var log = connection.CreateCommand();
        log.CommandText = AppendRun;
        log.Parameters.AddWithValue("$run_id", runId);
        log.Parameters.AddWithValue("$stage", Stage);
        log.Parameters.AddWithValue("$started_at", observedAt);
        log.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("O"));
        log.Parameters.AddWithValue("$outcome", "ok");
        log.Parameters.AddWithValue("$rows_written", written);

        // One feed call for the whole index, which is the figure the zero-per-name
        // limit is asserted against.
        log.Parameters.AddWithValue("$network_requests", 1);
        log.Parameters.AddWithValue("$detail", DBNull.Value);

        await log.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return written;
    }

    public async Task<IReadOnlyList<string>> MembersOnAsync(
        string indexCode,
        DateOnly on,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = MembersOn;
        command.Parameters.AddWithValue("$index_code", indexCode);
        command.Parameters.AddWithValue("$on", Text(on));

        var members = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = databaseFile,
    }.ToString();

    // A session date is a date and not an instant, which SCHEMA states and which
    // is why the comparisons above are string comparisons: an ISO date sorts
    // lexically in the same order it sorts chronologically.
    static string Text(DateOnly date) => date.ToString("yyyy-MM-dd");

    static string? Text(DateOnly? date) => date is { } value ? Text(value) : null;
}
