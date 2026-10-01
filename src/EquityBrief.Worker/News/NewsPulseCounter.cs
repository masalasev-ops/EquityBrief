using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.News;

public sealed record NewsPulseOutcome(int Articles, int NamesCounted, int RowsWritten, int Requests, int RowsDropped, int ArticlesStored = 0, int ArticlesDropped = 0);

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
            new StoreTouch(Store.NewsArticle, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.News]);

    public const string Stage = "news-pulse";

    // The stage a fill's rows carry, one a day.
    public const string FillStage = "news-fill";

    // One year retained, which is enough to hold a ninety-day baseline.
    public const int RetentionDays = 365;

    // How long an article is kept for the labeller and the pages, a day past the thirty the labeller reads.
    // see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
    public const int ArticleRetentionDays = 31;

    // Every article naming a member, kept from the same query with its text cut and its admissibility
    // judged as it is stored, once per member per article; a second night that brings the same article
    // back leaves the row as it was.
    const string InsertArticle = @"
        INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date)
        VALUES ($ticker, $article_id, $link, $title, $source, $published_at, $text, $length, $admissibility, $session_date)
        ON CONFLICT (ticker, article_id) DO NOTHING;
    ";

    const string DropArticlesOlderThan = @"
        DELETE FROM news_article WHERE session_date < $oldest;
    ";

    // An article's id: the first sixteen hex characters of a hash of its link, which is what tells one
    // article from another across the two feeds that carry it.
    public static string ArticleId(string link) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(link.Trim())))[..16];

    // The article judged as a document a claim could rest on, over the window the night reads: the session
    // and the day before it, since a provider stamps a publish instant in its own zone.
    // see: A stored source is not automatically an admissible one
    public static string Admissibility(NewsArticle article, DateOnly session) =>
        EquityBrief.Core.Research.Admissibility.Judge(
            new EquityBrief.Core.Research.FetchedDocument(
                EquityBrief.Core.Research.DocumentChannel.NewsFeed,
                article.Url,
                article.Title,
                DateOnly.FromDateTime(article.Published.UtcDateTime),
                article.Text),
            session.AddDays(-1),
            session);

    // The text as it is stored and sent: cut at the instruction's length.
    public static string Cut(string text) =>
        text.Length <= EquityBrief.Core.News.NewsInstruction.TextCharacters ? text : text[..EquityBrief.Core.News.NewsInstruction.TextCharacters];

    // Members on the session counted: joined by it, where the join date is
    // known, and not left by it.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string CurrentMembers = @"
        SELECT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
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

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, sessionDate, cancellation);
        var written = 0;
        var counted = 0;
        var stored = 0;

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

            // The member's articles themselves, for the labeller and the pages, each judged as it is stored.
            foreach (var article in found ?? [])
            {
                await using var keep = connection.CreateCommand();

                keep.Transaction = (SqliteTransaction)transaction;
                keep.CommandText = InsertArticle;
                keep.Parameters.AddWithValue("$ticker", ticker);
                keep.Parameters.AddWithValue("$article_id", ArticleId(article.Url));
                keep.Parameters.AddWithValue("$link", article.Url);
                keep.Parameters.AddWithValue("$title", article.Title);
                keep.Parameters.AddWithValue("$source", article.Channel);
                keep.Parameters.AddWithValue("$published_at", article.Published.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                keep.Parameters.AddWithValue("$text", Cut(article.Text));
                keep.Parameters.AddWithValue("$length", article.Text.Length);
                keep.Parameters.AddWithValue("$admissibility", Admissibility(article, sessionDate));
                keep.Parameters.AddWithValue("$session_date", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                stored += await keep.ExecuteNonQueryAsync(cancellation);
            }
        }

        await transaction.CommitAsync(cancellation);

        var dropped = await DroppedAsync(connection, sessionDate.AddDays(-RetentionDays), cancellation);
        var droppedArticles = await DroppedArticlesAsync(connection, sessionDate.AddDays(-ArticleRetentionDays), cancellation);

        await RecordAsync(
            connection, runId, startedAt, articles.Count, members.Count, written,
            feed.Requests - before, counted, dropped, stored, droppedArticles, cancellation);

        return new NewsPulseOutcome(articles.Count, counted, written, feed.Requests - before, dropped, stored, droppedArticles);
    }

    // The articles of one day stored for the members, as the night stores them, with no pulse row and no drop:
    // the fill run by hand over the days before the labeller's first night. Its row on the run log is its own.
    // see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
    public async Task<NewsPulseOutcome> FillAsync(string indexCode, DateOnly day, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var before = feed.Requests;
        var articles = await feed.ArticlesAsync(day, day, cancellation);
        var byName = NewsAttribution.ByName(articles);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, day, cancellation);
        var stored = 0;

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var ticker in members)
        {
            foreach (var article in byName.TryGetValue(ticker, out var found) ? found : [])
            {
                await using var keep = connection.CreateCommand();

                keep.Transaction = (SqliteTransaction)transaction;
                keep.CommandText = InsertArticle;
                keep.Parameters.AddWithValue("$ticker", ticker);
                keep.Parameters.AddWithValue("$article_id", ArticleId(article.Url));
                keep.Parameters.AddWithValue("$link", article.Url);
                keep.Parameters.AddWithValue("$title", article.Title);
                keep.Parameters.AddWithValue("$source", article.Channel);
                keep.Parameters.AddWithValue("$published_at", article.Published.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                keep.Parameters.AddWithValue("$text", Cut(article.Text));
                keep.Parameters.AddWithValue("$length", article.Text.Length);
                keep.Parameters.AddWithValue("$admissibility", Admissibility(article, day));
                keep.Parameters.AddWithValue("$session_date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                stored += await keep.ExecuteNonQueryAsync(cancellation);
            }
        }

        await transaction.CommitAsync(cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", FillStage + " " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", stored);
        command.Parameters.AddWithValue("$network_requests", feed.Requests - before);
        command.Parameters.AddWithValue("$detail", $"{articles.Count} article(s) over {feed.Requests - before} page(s), {members.Count} member(s), {stored} article(s) stored");

        await command.ExecuteNonQueryAsync(cancellation);

        return new NewsPulseOutcome(articles.Count, 0, 0, feed.Requests - before, 0, stored, 0);
    }

    static async Task<int> DroppedArticlesAsync(SqliteConnection connection, DateOnly oldest, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = DropArticlesOlderThan;
        command.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<IReadOnlyList<string>> MembersAsync(
        SqliteConnection connection,
        string indexCode,
        DateOnly session,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

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
        int stored,
        int droppedArticles,
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
            $"{articles} article(s) over {requests} page(s), {members} member(s), {counted} with news, {dropped} dropped, " +
            $"{stored} article(s) stored for members and {droppedArticles} dropped past {ArticleRetentionDays} days");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
