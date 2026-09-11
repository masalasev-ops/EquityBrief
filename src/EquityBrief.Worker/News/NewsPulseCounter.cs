using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.News;

public sealed record NewsPulseOutcome(int Articles, int NamesCounted, int RowsWritten, int Requests, int RowsDropped);

// The news pulse counter. Counts today's articles per name from one dated query,
// so the staleness judge can work without spending anything.
//
// The fan-out is done in code rather than by the provider: one dated query
// covers the whole market and every name in it is reached by reading the
// attribution the payload already carries. A request per ticker would put five
// hundred calls on the nightly path for the same articles.
// see: News is one dated query, paged to cover the day, and attributed to names locally
//
// It reads membership because a count is kept for the names the index holds
// rather than for every symbol the market wrote about: one request already
// reached 3,232 distinct symbols against an index of 503, and a row per symbol
// would be a table about the market rather than about the universe.
public sealed class NewsPulseCounter : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.NewsPulse, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.News]);

    public const string Stage = "news-pulse";

    // One year retained, which is enough to hold a ninety-day baseline.
    public const int RetentionDays = 365;

    const string CurrentMembers = @"
        SELECT ticker
        FROM membership
        WHERE index_code = $index AND ""left"" IS NULL
        ORDER BY ticker;
    ";

    // Insert only, with the conflict ignored rather than replacing the row.
    // SCHEMA gives this table an inserter and no updater, so replacing would be
    // an update by another name, and a night run twice writes the same count for
    // the same session.
    const string Insert = @"
        INSERT INTO news_pulse (ticker, session_date, article_count)
        VALUES ($ticker, $session_date, $article_count)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string DropOlderThan = @"
        DELETE FROM news_pulse WHERE session_date < $oldest;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly INewsFeed feed;
    readonly IClock clock;
    readonly string databaseFile;

    public NewsPulseCounter(INewsFeed feed, IClock clock, string databaseFile)
    {
        this.feed = feed;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<NewsPulseOutcome> RunAsync(
        string indexCode,
        DateOnly sessionDate,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var before = feed.Requests;

        // One dated query for the session, paged by the feed until the day is
        // covered. The count of requests is read off the feed rather than stated
        // here, because a caller that wrote the figure would be recording its
        // own intention.
        var articles = await feed.ArticlesAsync(sessionDate, sessionDate, cancellation);
        var byName = NewsAttribution.ByName(articles);

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, cancellation);
        var written = 0;
        var counted = 0;

        // One transaction around the whole loop rather than one per row.
        //
        // Without it every row commits on its own and each commit is a disk
        // sync, which costs about the same whatever the row holds. Over the
        // four names of the committed fixture that is invisible; over 503 it
        // was measured at 110 seconds for 503 rows. The stages that had this were the ones
        // phase 5 wrote, because a fixture of four names cannot show it.
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var ticker in members)
        {
            // A member the day wrote nothing about gets a row carrying zero.
            // That is a measurement rather than an absence: the pulse is
            // compared against its own baseline, and a night missing from the
            // series would read as a night nobody counted.
            var count = byName.TryGetValue(ticker, out var found) ? found.Count : 0;

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session_date", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$article_count", count);

            written += await command.ExecuteNonQueryAsync(cancellation);

            if (count > 0)
            {
                counted++;
            }
        }

        await transaction.CommitAsync(cancellation);

        var dropped = await DroppedAsync(connection, sessionDate.AddDays(-RetentionDays), cancellation);

        await RecordAsync(
            connection, runId, startedAt, articles.Count, members.Count, written,
            feed.Requests - before, counted, dropped, cancellation);

        return new NewsPulseOutcome(articles.Count, counted, written, feed.Requests - before, dropped);
    }

    static async Task<IReadOnlyList<string>> MembersAsync(
        SqliteConnection connection,
        string indexCode,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<int> DroppedAsync(SqliteConnection connection, DateOnly oldest, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = DropOlderThan;
        command.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int articles,
        int members,
        int written,
        int requests,
        int counted,
        int dropped,
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

        // The request count is the page count, and it is on the row a person
        // reads because it is the figure the cost rule is about.
        command.Parameters.AddWithValue("$network_requests", requests);
        command.Parameters.AddWithValue(
            "$detail",
            $"{articles} article(s) over {requests} page(s), {members} member(s), {counted} with news, {dropped} dropped");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
