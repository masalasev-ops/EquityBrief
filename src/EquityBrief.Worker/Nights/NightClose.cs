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
    //
    // Members on tonight's session, being joined by it where the join date is
    // known and not left by it, which is the population the list was written
    // over.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string NamesStale = @"
        SELECT COUNT(*)
        FROM membership m
        WHERE m.index_code = $index
          AND (m.joined IS NULL OR m.joined <= $session)
          AND (m.""left"" IS NULL OR m.""left"" > $session)
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

        var computed = await CountAsync(connection, NamesComputed, null, null, cancellation);
        var listed = await CountAsync(connection, OnTheList, null, null, cancellation);
        var reasons = await CountAsync(connection, ReasonsFired, null, null, cancellation);
        var stale = await CountAsync(connection, NamesStale, indexCode, clock.SessionDateAt(startedAt), cancellation);
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

    // The outcomes a night that did not finish writes. A closed vocabulary
    // beside `ok`, for the reason `ok` is one: the run page decides what to draw
    // in its stale-and-failed region by reading this column.
    public const string Failed = "failed";
    public const string Stopped = "stopped";

    // The third, for a night that ran on a day the exchange did not trade. Not a
    // failure and not `ok`: nothing was computed, and a row reading `ok` under
    // this stage would say the night's counts were recorded.
    // see: A night on a day the exchange did not trade fetches nothing and exits clean
    public const string NoSession = "no session";

    // What that row says, in one place so the night prints what the log holds.
    public static string NotASession(DateOnly day) =>
        day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " is a " +
        day.DayOfWeek.ToString() + " the exchange did not trade, so there is no session to fetch and " +
        "nothing was fetched or computed.";

    // The same insert, for a stage that never reached its own. On conflict it
    // writes nothing, so a composite step whose first component already wrote
    // its row is recorded under the next candidate rather than over the row
    // that succeeded.
    const string AppendFailure = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, 0, $network_requests, '0', $detail)
        ON CONFLICT (run_id, stage) DO NOTHING;
    ";

    // How a night that stopped closes: one row for the stage it stopped on,
    // naming the step, its outcome and why.
    //
    // It is here rather than in the night because recording what the night did
    // is this component's work, and on a night that failed this is the whole of
    // what it did. Before the phase 5 sign-off a failed step wrote nothing to
    // the run log at all: its name went to stderr, which a scheduled task
    // discards, so the first scheduled night showed eleven clean stages and an
    // empty stale-and-failed region for a night the scheduler recorded as
    // exiting 1.
    //
    // `stages` are the run log stages the step would have written, in order,
    // and the row goes under the first of them the run does not hold yet. The
    // facts step writes two, and a failure in its second must not be recorded
    // over the first's success.
    //
    // The instants come from the caller's clock rather than being read here, so
    // this path reads no clock of its own.
    //
    // `networkRequests` is what the step asked the feeds for before it stopped,
    // read off the feeds by the caller. The row carried 0 until the phase 5
    // sign-off, so a step that made three attempts at a feed and then failed
    // recorded none, and the stopped night's requests were missing from the one
    // figure the cost rule is read against.
    public static async Task<string?> RecordStopAsync(
        string databaseFile,
        string dataRoot,
        string runId,
        IReadOnlyList<string> stages,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string outcome,
        string detail,
        int networkRequests = 0)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync();

        foreach (var stage in stages)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = AppendFailure;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", stage);
            command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$ended_at", endedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$outcome", outcome);
            command.Parameters.AddWithValue("$detail", Portable(detail, dataRoot));
            command.Parameters.AddWithValue("$network_requests", networkRequests);

            if (await command.ExecuteNonQueryAsync() == 1)
            {
                return stage;
            }
        }

        return null;
    }

    // A message made safe for a store row that is copied between machines.
    //
    // Exception text carries absolute paths mid-string, which is why the
    // portability matcher reads a whole value rather than its first character,
    // and a stored path is a hard rule broken rather than a detail leaked. The
    // data root is named first, since it is the path a store's own failures
    // carry, and then any other token that roots a path is replaced whole. A
    // token is rooted where it begins with a separator followed by something, or
    // with a drive letter, a colon and a separator; in the middle of a token a
    // slash is a date, a ratio or a relative path and is left alone.
    // see: The whole system is a checkout and one database file
    public static string Portable(string text, string dataRoot)
    {
        var named = string.IsNullOrEmpty(dataRoot)
            ? text
            : text.Replace(dataRoot, "<data root>", StringComparison.OrdinalIgnoreCase);

        return System.Text.RegularExpressions.Regex.Replace(
            named,
            @"(?<=^|[\s'""(\[<])(?:[A-Za-z]:[\\/]|[\\/]+(?=[^\s\\/]))[^\s'""\)\]>]*",
            "<path>");
    }

    static async Task<int> CountAsync(
        SqliteConnection connection,
        string sql,
        string? indexCode,
        DateOnly? session,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        if (indexCode is not null)
        {
            command.Parameters.AddWithValue("$index", indexCode);
        }

        if (session is { } on)
        {
            command.Parameters.AddWithValue("$session", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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
