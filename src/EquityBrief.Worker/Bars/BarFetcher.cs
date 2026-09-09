using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

public sealed record FetchOutcome(
    int RowsWritten,
    int Requests,
    int MembersStored,
    int RowsDropped,
    DateOnly Session,
    DateOnly Oldest);

// The nightly bar fetch. One bulk request, stored for current members only,
// then the sessions that have fallen out of the retention window are dropped.
//
// One request for the night, whatever the universe is. That is the rule the
// nightly path is shaped around, and it is why the fetcher takes a bulk file
// and filters it rather than asking the provider per name.
// see: The nightly run is arithmetic only
//
// The filter reads membership rather than the file, and that distinction is
// load bearing. The provider returns every name the exchange traded, which
// includes names that have left the index and names that were never in it: the
// captured fixture holds two of the first and two of the second, so a fetcher
// that stored what it was sent would store four names the index does not have.
public sealed class BarFetcher : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.BulkPrice]);

    public const string Stage = "fetch";
    public const string Source = "bulk";
    public const string Exchange = "US";

    // One year kept, which is the limits table's figure and the window the
    // level builder reads. Expressed as a year rather than as a session count
    // because the boundary is a date and a session count would drift with
    // holidays.
    public const int RetentionYears = 1;

    readonly IBulkPriceFeed feed;
    readonly IClock clock;
    readonly string databaseFile;

    public BarFetcher(IBulkPriceFeed feed, IClock clock, string databaseFile)
    {
        this.feed = feed;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // Current members, being the names a night stores bars for. A name that has
    // left keeps its rows and its history and is simply not added to.
    // see: Your own listing history is kept forever
    const string CurrentMembers = @"
        SELECT ticker FROM membership WHERE index_code = $index AND left IS NULL;
    ";

    const string StoreBar = @"
        INSERT INTO bar (
            ticker, session_date, open, high, low, close,
            volume, source, observed_at, raw_close)
        VALUES (
            $ticker, $session_date, $open, $high, $low, $close,
            $volume, $source, $observed_at, $raw_close)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    // The retention drop, and one of the two removals SCHEMA sanctions. It
    // takes every session below a date boundary, for every name at once, so
    // what is left is still a contiguous series ending tonight. Nothing here
    // can remove a bar from inside a series it leaves standing, which is the
    // thing the append-only rule is about.
    const string DropOlderThan = @"
        DELETE FROM bar WHERE session_date < $oldest;
    ";

    const string BarCount = "SELECT COUNT(*) FROM bar;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<FetchOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync();

        var members = await MembersAsync(connection, indexCode);
        var before = await CountAsync(connection);

        // One request. Counted by the feed rather than asserted by the caller,
        // because a caller that looped would still report one.
        var rows = await feed.RowsAsync(Exchange, cancellationToken);

        var wanted = rows
            .Where(row => members.Contains(row.Ticker))
            .OrderBy(row => row.Ticker, StringComparer.Ordinal)
            .ToArray();

        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var row in wanted)
        {
            await StoreAsync(connection, row, started);
        }

        var stored = await CountAsync(connection);

        // The retention boundary, taken from the session the file is for rather
        // than from the machine clock, so replaying a fixture drops what that
        // night would have dropped.
        var session = wanted.Length > 0
            ? wanted.Max(row => row.Bar.SessionDate)
            : clock.SessionDateAt(started);

        var oldest = session.AddYears(-RetentionYears);

        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = DropOlderThan;
            drop.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd"));

            await drop.ExecuteNonQueryAsync();
        }

        var after = await CountAsync(connection);

        await transaction.CommitAsync();

        // Both figures measured from the store rather than self-reported, which
        // is what SCHEMA requires of rows_written and what makes the halt
        // condition read a fact rather than a stage's opinion.
        var outcome = new FetchOutcome(
            stored - before,
            feed.Requests,
            wanted.Length,
            stored - after,
            session,
            oldest);

        await AppendAsync(connection, runId, started, outcome);

        return outcome;
    }

    static async Task<HashSet<string>> MembersAsync(SqliteConnection connection, string indexCode)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);

        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<int> CountAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BarCount;

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    static async Task StoreAsync(SqliteConnection connection, BulkBar row, DateTimeOffset observed)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = StoreBar;
        command.Parameters.AddWithValue("$ticker", row.Ticker);
        command.Parameters.AddWithValue("$session_date", row.Bar.SessionDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$open", Money.ToStorage(row.Bar.Open));
        command.Parameters.AddWithValue("$high", Money.ToStorage(row.Bar.High));
        command.Parameters.AddWithValue("$low", Money.ToStorage(row.Bar.Low));
        command.Parameters.AddWithValue("$close", Money.ToStorage(row.Bar.Close));
        command.Parameters.AddWithValue("$volume", row.Bar.Volume);
        command.Parameters.AddWithValue("$source", Source);
        command.Parameters.AddWithValue("$observed_at", observed.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$raw_close", Money.ToStorage(row.Bar.RawClose));

        await command.ExecuteNonQueryAsync();
    }

    async Task AppendAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset started,
        FetchOutcome outcome)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue(
            "$detail",
            $"{{\"members\":{outcome.MembersStored},\"dropped\":{outcome.RowsDropped}," +
            $"\"session\":\"{outcome.Session:yyyy-MM-dd}\",\"oldest\":\"{outcome.Oldest:yyyy-MM-dd}\"}}");

        await command.ExecuteNonQueryAsync();
    }
}
