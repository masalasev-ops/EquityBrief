using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Nights;

public sealed record NightCloseOutcome(
    int NamesComputed,
    int NamesOnTheList,
    int ReasonsFired,
    int NamesStale,
    string Duration);

// The night's closing stage. Records what the night did, in the counts section
// 14's last step names: names computed, names on the list, reasons fired, stale
// names and duration.
//
// It computes nothing and it decides nothing. Every figure is counted off the
// store the night has just written, which is what makes the run page a record of
// what happened rather than of what each stage intended: a stage's own count of
// what it wrote is the stage's opinion, and this is the store's.
public sealed class NightClose : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "close";

    const string NamesComputed = @"
        SELECT COUNT(*) FROM ladder
        WHERE as_of = (SELECT MAX(as_of) FROM ladder);
    ";

    const string OnTheList = @"
        SELECT COUNT(*) FROM listing
        WHERE session_date = (SELECT MAX(session_date) FROM listing) AND fired_count > 0;
    ";

    const string ReasonsFired = @"
        SELECT IFNULL(SUM(fired_count), 0) FROM listing
        WHERE session_date = (SELECT MAX(session_date) FROM listing);
    ";

    // A name whose stored series does not end on the newest session anyone has
    // is a name carrying yesterday's bars, which is what the run page's stale
    // region reads. Counted over the index rather than over the names with
    // bars, because a member with none is stale in the way that matters.
    const string NamesStale = @"
        SELECT COUNT(*)
        FROM membership m
        WHERE m.index_code = $index AND m.""left"" IS NULL
          AND IFNULL((SELECT MAX(b.session_date) FROM bar b WHERE b.ticker = m.ticker), '')
              < (SELECT MAX(session_date) FROM bar);
    ";

    // The night's own span, read off the rows its stages wrote rather than
    // measured here. The clock is read once for the row this stage appends and
    // nowhere else, because a duration measured by the component that reports it
    // is that component's opinion of itself.
    const string SpanOfRun = @"
        SELECT MIN(started_at), MAX(ended_at) FROM run_log WHERE run_id = $run_id;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public NightClose(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<NightCloseOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var computed = await CountAsync(connection, NamesComputed, null, cancellation);
        var listed = await CountAsync(connection, OnTheList, null, cancellation);
        var reasons = await CountAsync(connection, ReasonsFired, null, cancellation);
        var stale = await CountAsync(connection, NamesStale, indexCode, cancellation);
        var duration = await DurationAsync(connection, runId, cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue(
            "$detail",
            $"{computed} name(s) computed, {listed} on the list, {reasons} reason(s) fired, " +
            $"{stale} stale, {duration}");

        await command.ExecuteNonQueryAsync(cancellation);

        return new NightCloseOutcome(computed, listed, reasons, stale, duration);
    }

    static async Task<int> CountAsync(
        SqliteConnection connection,
        string sql,
        string? indexCode,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        if (indexCode is not null)
        {
            command.Parameters.AddWithValue("$index", indexCode);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation));
    }

    // A run whose rows carry no span says so rather than reporting nothing,
    // because a duration of zero and a duration nobody recorded read the same
    // on a page and only one of them is true.
    static async Task<string> DurationAsync(
        SqliteConnection connection,
        string runId,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SpanOfRun;
        command.Parameters.AddWithValue("$run_id", runId);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        if (!await reader.ReadAsync(cancellation) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return "no duration recorded";
        }

        var from = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);

        return $"{(to - from).TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} second(s)";
    }
}
