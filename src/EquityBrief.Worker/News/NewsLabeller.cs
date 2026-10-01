using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.News;

// What one run of the labeller came to: the names it reached, the labels written, the answers kept as
// unreadable by cause, the articles not sent for their admissibility, the ones labelled already, what the
// run cost and what the month has, and what stopped it.
public sealed record NewsLabelOutcome(
    int Names,
    int NamesReached,
    int Labelled,
    IReadOnlyDictionary<string, int> Unreadable,
    int RefusedByAdmissibility,
    int AlreadyLabelled,
    decimal Cost,
    decimal MonthCost,
    string Stop,
    int LabelsDropped)
{
    public int UnreadableCount => Unreadable.Values.Sum();
}

// The news labeller. A process of its own the night starts after the close, as it starts the drain, and run
// by hand: it reads tonight's list in the order it is drawn, for each name the stored, admitted articles of
// the thirty days before the night, newest first and at most twenty, not yet labelled by the active profile
// under the instruction's version, and asks the profile's model for a label one article at a time through
// the spend cap. Every call and every dollar sits on this run and never on the night's.
//
// Before it asks anything it stops where the month's limit is already reached, and before each call where
// the call would pass it, where its time limit has passed, where a peak window of the profile has opened,
// or where the day or month cap pauses the call; what is left waits for the next night, and the row says
// which stop and how many names it reached. An answer code cannot read is asked once more and then kept as
// unreadable with its cause, so it is not paid for again under that profile and version. A label is never
// overwritten: the insert ignores a conflict, and after a switch the new profile writes rows of its own.
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
// see: A label's reason is one sentence holding no digit, and an answer code cannot read is asked once more and then kept as unreadable with its cause
// see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
public sealed class NewsLabeller : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Read),
            new StoreTouch(Store.NewsArticle, Touch.Read),
            new StoreTouch(Store.NewsLabel, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.ResearchModel]);

    // The labeller's own run, and the stage the night records starting it under.
    public const string RunPrefix = "label-news-";
    public const string Stage = "news-labels";
    public const string NightStage = "label-news";

    // The outcomes a label row carries.
    public const string Labelled = "labelled";
    public const string Unreadable = "unreadable";

    // The stops, in the words the row and the pages state.
    public const string Finished = "every name reached";
    public const string MonthLimitReached = "the labeller's month limit";
    public const string TimeLimitPassed = "the time limit";
    public const string PeakWindowOpened = "a peak window";
    public const string CapPaused = "the day or month cap";
    public const string ModelUnreachable = "the model could not be reached";

    // The window read, and the most articles a name is sent for.
    public const int WindowDays = 30;
    public const int ArticlesAName = 20;

    public static string RunIdFor(DateTimeOffset startedAt) =>
        RunPrefix + startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // Tonight's list in the order it is drawn: the members whose live result passed, improving businesses
    // first where the night stored its readings and the filter's own order within a state, with each
    // member's name. A replayed result is none of the night's.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    static readonly string ListedOnNight = @"
        SELECT g.ticker, COALESCE((SELECT m.name FROM membership m WHERE m.ticker = g.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1), g.ticker)
        FROM gate_result g
        JOIN listing l ON l.ticker = g.ticker AND l.session_date = g.session_date
        LEFT JOIN fundamental_reading f ON f.ticker = g.ticker AND f.session_date = g.session_date
        WHERE g.session_date = $session AND g.passed = 1 AND g.version <> '" + EquityBrief.Core.Filter.ReplayedResults.Version + @"'
        ORDER BY " + FundamentalState.PlaceIn("f.state") + @", g.rank, g.ticker;
    ";

    // A member's admitted articles of the window not yet labelled under the active profile and version,
    // newest first, at most the count a name is sent for.
    const string ArticlesToLabel = @"
        SELECT a.article_id, a.title, a.text
        FROM news_article a
        WHERE a.ticker = $ticker
          AND a.admissibility = $accepted
          AND a.published_at >= $from
          AND a.published_at < $to
          AND NOT EXISTS (
              SELECT 1 FROM news_label b
              WHERE b.ticker = a.ticker AND b.article_id = a.article_id AND b.profile = $profile AND b.instruction_version = $version)
        ORDER BY a.published_at DESC
        LIMIT $most;
    ";

    const string ArticlesRefused = @"
        SELECT COUNT(*) FROM news_article
        WHERE ticker = $ticker AND admissibility <> $accepted AND published_at >= $from AND published_at < $to;
    ";

    const string ArticlesLabelled = @"
        SELECT COUNT(*) FROM news_article a
        JOIN news_label b ON b.ticker = a.ticker AND b.article_id = a.article_id AND b.profile = $profile AND b.instruction_version = $version
        WHERE a.ticker = $ticker AND a.published_at >= $from AND a.published_at < $to;
    ";

    // A label is inserted and never updated: a conflict leaves the row as it was.
    const string InsertLabel = @"
        INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, cause, kind, direction, reason, labelled_at, run_id)
        VALUES ($ticker, $article_id, $profile, $version, $model, $outcome, $cause, $kind, $direction, $reason, $labelled_at, $run_id)
        ON CONFLICT (ticker, article_id, profile, instruction_version) DO NOTHING;
    ";

    // A label whose article the counter has dropped goes with it.
    const string DropOrphanLabels = @"
        DELETE FROM news_label
        WHERE NOT EXISTS (SELECT 1 FROM news_article a WHERE a.ticker = news_label.ticker AND a.article_id = news_label.article_id);
    ";

    // What the labeller's own runs have spent this UTC month, read as stored and summed in code.
    const string SpentThisMonth = @"
        SELECT spend FROM run_log
        WHERE run_id LIKE $prefix AND started_at >= $since AND stage LIKE 'research call%';
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, $model_calls, $network_requests, $spend, $detail);
    ";

    readonly SpendCap cap;
    readonly ResearchModelSettings settings;
    readonly NewsLimits limits;
    readonly IClock clock;
    readonly string databaseFile;

    public NewsLabeller(SpendCap cap, ResearchModelSettings settings, NewsLimits limits, IClock clock, string databaseFile)
    {
        this.cap = cap;
        this.settings = settings;
        this.limits = limits;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<NewsLabelOutcome> RunAsync(DateOnly session, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var names = await ListedAsync(connection, session, cancellation);
        var monthBefore = await SpentThisMonthAsync(connection, startedAt, cancellation);
        var from = session.AddDays(-WindowDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = session.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var unreadable = new Dictionary<string, int>(StringComparer.Ordinal);
        var labelled = 0;
        var refused = 0;
        var already = 0;
        var reached = 0;
        var calls = 0;
        var cost = 0m;
        var stop = Finished;

        // Nothing is asked where the month's limit is already reached.
        if (monthBefore >= limits.MonthLimit)
        {
            stop = MonthLimitReached;
        }

        foreach (var (ticker, company) in names)
        {
            if (stop != Finished)
            {
                break;
            }

            reached++;
            refused += await CountAsync(connection, ArticlesRefused, ticker, from, to, cancellation);
            already += await CountAsync(connection, ArticlesLabelled, ticker, from, to, cancellation);

            foreach (var (articleId, title, text) in await ArticlesAsync(connection, ticker, from, to, cancellation))
            {
                var now = clock.UtcNow;
                var request = new ModelRequest(NewsInstruction.Lane, NewsInstruction.Section, settings.Model, [articleId], NewsInstruction.System, NewsInstruction.Prompt(company, ticker, title, text));

                if (now - startedAt >= limits.TimeLimit)
                {
                    stop = TimeLimitPassed;
                    break;
                }

                if (settings.Pricing.IsPeak(now))
                {
                    stop = PeakWindowOpened;
                    break;
                }

                if (monthBefore + cost + cap.Ceiling(request) > limits.MonthLimit)
                {
                    stop = MonthLimitReached;
                    break;
                }

                var round = ticker + " " + articleId;
                var first = await cap.AskAsync(request, runId, round, cancellation);

                calls += first.Answered || first.Unusable || first.Failure is not null && !first.Paused ? 1 : 0;
                cost += first.Price;

                if (first.Paused)
                {
                    stop = CapPaused;
                    break;
                }

                var (label, cause) = Read(first);

                if (cause == ModelUnreachable)
                {
                    stop = ModelUnreachable;
                    break;
                }

                if (label is null)
                {
                    // Asked once more, the same request under a round of its own.
                    var second = await cap.AskAsync(request, runId, round + ", retry", cancellation);

                    calls += second.Answered || second.Unusable || second.Failure is not null && !second.Paused ? 1 : 0;
                    cost += second.Price;

                    if (second.Paused)
                    {
                        stop = CapPaused;
                        break;
                    }

                    (label, cause) = Read(second);

                    if (cause == ModelUnreachable)
                    {
                        stop = ModelUnreachable;
                        break;
                    }
                }

                if (label is not null)
                {
                    labelled += await WriteAsync(connection, ticker, articleId, runId, Labelled, null, label, cancellation);
                }
                else
                {
                    unreadable[cause!] = unreadable.GetValueOrDefault(cause!) + 1;
                    await WriteAsync(connection, ticker, articleId, runId, Unreadable, cause, null, cancellation);
                }
            }
        }

        var dropped = await DropOrphansAsync(connection, cancellation);
        var outcome = new NewsLabelOutcome(names.Count, reached, labelled, unreadable, refused, already, cost, monthBefore + cost, stop, dropped);

        await RecordAsync(connection, runId, startedAt, calls, outcome, cancellation);

        return outcome;
    }

    // A call read for its label or the cause it has none: a model that did not answer at all stops the run,
    // an answer cut off at its budget or refused by the provider is a cause, and an answer that arrived is
    // read by the instruction's own checks.
    static (NewsLabel? Label, string? Cause) Read(PaidCall call)
    {
        if (call.Answer is { } answer)
        {
            return NewsInstruction.Read(answer.Text);
        }

        if (call.Unusable)
        {
            return (null, NewsInstruction.CutOff);
        }

        return call.Failure is { } failure && failure.Contains("refus", StringComparison.OrdinalIgnoreCase)
            ? (null, NewsInstruction.Refused)
            : (null, ModelUnreachable);
    }

    // A run refused before it asked anything, its profile's key missing, a limit that is not a number or a model
    // that does not answer, written as its own row where the store is there to hold it, so the run page says why.
    public static async Task RefusedAsync(string databaseFile, string runId, DateTimeOffset at, string message)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "refused");
        command.Parameters.AddWithValue("$rows_written", 0);
        command.Parameters.AddWithValue("$model_calls", 0);
        command.Parameters.AddWithValue("$network_requests", 0);
        command.Parameters.AddWithValue("$spend", "0");
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { refused = message }));

        await command.ExecuteNonQueryAsync();
    }

    // The night's own row for the step that starts the labeller, under the night's run.
    public static async Task RecordTheNightAsync(string databaseFile, string runId, DateTimeOffset startedAt, DateTimeOffset endedAt, string said)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", NightStage);
        command.Parameters.AddWithValue("$started_at", startedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", endedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", 0);
        command.Parameters.AddWithValue("$model_calls", 0);
        command.Parameters.AddWithValue("$network_requests", 0);
        command.Parameters.AddWithValue("$spend", "0");
        command.Parameters.AddWithValue("$detail", said);

        await command.ExecuteNonQueryAsync();
    }

    static async Task<IReadOnlyList<(string Ticker, string Company)>> ListedAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ListedOnNight;
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var names = new List<(string, string)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            names.Add((reader.GetString(0), reader.GetString(1)));
        }

        return names;
    }

    async Task<IReadOnlyList<(string Id, string Title, string Text)>> ArticlesAsync(SqliteConnection connection, string ticker, string from, string to, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ArticlesToLabel;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$accepted", Admissibility.Accepted);
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);
        command.Parameters.AddWithValue("$profile", settings.Profile);
        command.Parameters.AddWithValue("$version", NewsInstruction.Version);
        command.Parameters.AddWithValue("$most", ArticlesAName);

        var articles = new List<(string, string, string)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            articles.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return articles;
    }

    async Task<int> CountAsync(SqliteConnection connection, string sql, string ticker, string from, string to, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$accepted", Admissibility.Accepted);
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);
        command.Parameters.AddWithValue("$profile", settings.Profile);
        command.Parameters.AddWithValue("$version", NewsInstruction.Version);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    async Task<int> WriteAsync(SqliteConnection connection, string ticker, string articleId, string runId, string outcome, string? cause, NewsLabel? label, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = InsertLabel;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$article_id", articleId);
        command.Parameters.AddWithValue("$profile", settings.Profile);
        command.Parameters.AddWithValue("$version", NewsInstruction.Version);
        command.Parameters.AddWithValue("$model", cap.Model);
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$cause", (object?)cause ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", (object?)label?.Kind ?? DBNull.Value);
        command.Parameters.AddWithValue("$direction", (object?)label?.Direction ?? DBNull.Value);
        command.Parameters.AddWithValue("$reason", (object?)label?.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$labelled_at", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$run_id", runId);

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<int> DropOrphansAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = DropOrphanLabels;

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    // What the labeller's own calls have cost this UTC month, summed in code from the money each row carries.
    public static async Task<decimal> SpentThisMonthAsync(SqliteConnection connection, DateTimeOffset now, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SpentThisMonth;
        command.Parameters.AddWithValue("$prefix", RunPrefix + "%");
        command.Parameters.AddWithValue("$since", EquityBrief.Core.Spending.SpendLedger.MonthStart(now).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        var spent = 0m;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            spent += decimal.Parse(reader.GetString(0), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        return spent;
    }

    async Task RecordAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int calls, NewsLabelOutcome outcome, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.Labelled + outcome.UnreadableCount);
        command.Parameters.AddWithValue("$model_calls", calls);
        command.Parameters.AddWithValue("$network_requests", calls);
        command.Parameters.AddWithValue("$spend", outcome.Cost.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new
        {
            profile = settings.Profile,
            model = cap.Model,
            instructionVersion = NewsInstruction.Version,
            names = outcome.Names,
            reached = outcome.NamesReached,
            labelled = outcome.Labelled,
            unreadable = outcome.Unreadable,
            refusedByAdmissibility = outcome.RefusedByAdmissibility,
            alreadyLabelled = outcome.AlreadyLabelled,
            cost = outcome.Cost.ToString(CultureInfo.InvariantCulture),
            monthCost = outcome.MonthCost.ToString(CultureInfo.InvariantCulture),
            stop = outcome.Stop,
            labelsDropped = outcome.LabelsDropped,
        }));

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
