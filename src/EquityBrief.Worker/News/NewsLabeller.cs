using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Local;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.News;

// What one run of the labeller came to: the names it reached, the labels written, the answers kept as
// unreadable by cause, the stories the runtime did not answer, which the next run takes up, the articles not sent for
// their admissibility, the ones labelled already, what the run cost and what the month has, nothing on the local
// model, what stopped it, and how long it waited for the local model's lock.
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
    int LabelsDropped,
    int NotAnswered = 0,
    TimeSpan Waited = default)
{
    public int UnreadableCount => Unreadable.Values.Sum();
}

// A version the labeller labels under, as its pin's row holds it: the model, its file by its path under LM Studio's
// models folder with the file's SHA-256, size and last-modified instant, the runtime that answered the probe kept as a
// record, the instruction's version, the seed, the answer's budget and the window the model is loaded at.
public sealed record LabelPin(
    long Id,
    string Model,
    string File,
    string Sha256,
    long Size,
    DateTimeOffset Modified,
    string RuntimeName,
    string RuntimeVersion,
    int InstructionVersion,
    int Seed,
    int AnswerTokens,
    int Context,
    DateTimeOffset WrittenAt);

// The news labeller. A process of its own the off-peak run starts, and run by hand: it reads tonight's list in the order
// it is drawn and then each name a report is written for, for each name its own stored, admitted articles of the thirty
// days before the night, newest first and every one of them, not yet labelled by the local profile under the
// instruction's version, and asks the local model for a label one article at a time under the labeller's own pin, the
// answer held by the runtime's structured output to the instruction's keys and to a reason with no digit. No paid
// model is asked and nothing is spent. Every call sits on this run and never on the night's.
//
// It labels nothing where no pin is written or the model, its file, the instruction or the window differs from the
// newest pin, and the row says which part. It takes the local model's lock, so it never overlaps the local run, its
// history read or another local process, and waits for it where another holds it, to its time limit and no further, so
// the drain the off-peak run starts after it is never held up for longer. Before each call it stops where its time
// limit, counted from its start with the wait inside it, has passed, where the local run's next slot has come, and where
// the month's limit is already reached, which on the local model reads nothing spent; what is left waits for the next
// run, and the row says which stop and how many names it reached. A batch over every stored story stops at the instant
// it is given rather than at the time limit. An answer cut off at its budget or empty is asked once more and an answer
// code cannot read is kept as unreadable with its cause, so it is not asked again under that profile and version; a
// story the runtime does not answer is left with no row for the next run, and a runtime gone stops the run there. A
// label is never overwritten: the insert ignores a conflict, and after a switch the new profile writes rows of its own,
// the paid profile's labels standing beside them.
// see: The news labeller is a process of its own the off-peak run starts, and labels on the local model under a pin of its own
// see: The news labeller waits for the local model's lock and stops before the local run's next slot, its time limit counted from its start
// see: A label's reason is one sentence holding no digit, and an answer code cannot read is asked once more and then kept as unreadable with its cause
// see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
public sealed class NewsLabeller : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.FamilyNight, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Read),
            new StoreTouch(Store.NewsArticle, Touch.Read),
            new StoreTouch(Store.NewsLabel, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.ResearchRequest, Touch.Read),
            new StoreTouch(Store.LabelPin, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    // The labeller's own run, the stage the night records starting it under, the outcomes a label row carries
    // and the stops, each the word the Core holds for the read surface to draw by.
    public const string RunPrefix = NewsLabelling.RunPrefix;
    public const string Stage = NewsLabelling.Stage;
    public const string NightStage = NewsLabelling.NightStage;
    public const string Labelled = NewsLabelling.Labelled;
    public const string Unreadable = NewsLabelling.Unreadable;
    public const string Finished = NewsLabelling.Finished;
    public const string MonthLimitReached = NewsLabelling.MonthLimitReached;
    public const string TimeLimitPassed = NewsLabelling.TimeLimitPassed;
    public const string PeakWindowOpened = NewsLabelling.PeakWindowOpened;
    public const string CapPaused = NewsLabelling.CapPaused;
    public const string ModelUnreachable = NewsLabelling.ModelUnreachable;
    public const string NoPin = NewsLabelling.NoPin;
    public const string PinDiffers = NewsLabelling.PinDiffers;
    public const string BatchEnded = NewsLabelling.BatchEnded;
    public const string LockHeld = NewsLabelling.LockHeld;
    public const string NextLocalSlot = NewsLabelling.NextLocalSlot;

    // What a pinning run comes to where it writes no version.
    public const string PinUnchanged = "the pin holds this model, its file, the instruction and the window already";
    public const string PinRefused = "the probe was not answered by the profile's own model on a runtime that names itself";
    public const string ModelFileNotSet = "the local profile names no model file";

    // The window read.
    public const int WindowDays = NewsLabelling.WindowDays;

    public static string RunIdFor(DateTimeOffset startedAt) =>
        RunPrefix + startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // Tonight's list in the order it is drawn, with each member's name: the stocks the page lists, in the
    // page's order, on a night the families drew it; and on a night before them the members whose live
    // result passed, improving businesses first where the night stored its readings and the filter's own
    // order within a state. A replayed result is none of the night's. The S&P 400's and 600's lists are none of it.
    // see: The news labeller labels the S&P 500's list and every name a report is written for, each name's own stories in one run
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    static readonly string ListedOnNight = @"
        SELECT g.ticker, COALESCE((SELECT m.name FROM membership m WHERE m.ticker = g.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1), g.ticker)
        FROM gate_result g
        JOIN listing l ON l.ticker = g.ticker AND l.session_date = g.session_date
        LEFT JOIN fundamental_reading f ON f.ticker = g.ticker AND f.session_date = g.session_date
        WHERE g.session_date = $session AND g.version <> '" + EquityBrief.Core.Filter.ReplayedResults.Version + @"'
          AND " + FamilyList.OnTheList("g.ticker", "g.session_date", "g.passed = 1") + @"
        ORDER BY IFNULL(" + FamilyList.PlaceOn("g.ticker", "g.session_date") + ", 0), " + FundamentalState.PlaceIn("f.state") + @", g.rank, g.ticker;
    ";

    // Each name a report is written for, on any index, in the order it was asked: a request asked since the night's
    // session began, written or still to be written, so the night's own requests are labelled at the off-peak run
    // before the drain writes them and a press at the run after it. A request refused or withdrawn writes no report.
    // see: The news labeller labels the S&P 500's list and every name a report is written for, each name's own stories in one run
    const string ReportsAsked = @"
        SELECT r.ticker, COALESCE((SELECT m.name FROM membership m WHERE m.ticker = r.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1), r.ticker)
        FROM research_request r
        WHERE r.asked_at >= $since AND r.state IN ('outstanding', 'writing', 'written')
        GROUP BY r.ticker
        ORDER BY MIN(r.asked_at), r.ticker;
    ";

    // A member's admitted articles of the window not yet labelled under the local profile and version, newest first,
    // which the run reads the member's own stories from.
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
        ORDER BY a.published_at DESC;
    ";

    // Every admitted article the store holds not yet labelled under the local profile and version, whatever labelled it
    // before, newest first, with its member's name, which a batch labelling them all reads.
    const string EveryArticleToLabel = @"
        SELECT a.ticker, a.article_id, a.title, a.text,
               COALESCE((SELECT m.name FROM membership m WHERE m.ticker = a.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1), a.ticker)
        FROM news_article a
        WHERE a.admissibility = $accepted
          AND NOT EXISTS (
              SELECT 1 FROM news_label b
              WHERE b.ticker = a.ticker AND b.article_id = a.article_id AND b.profile = $profile AND b.instruction_version = $version)
        ORDER BY a.published_at DESC, a.ticker, a.article_id;
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

    const string NewestPin = @"
        SELECT id, model, model_file, sha256, size, modified, runtime_name, runtime_version, instruction_version,
               seed, answer_tokens, context, written_at
        FROM label_pin ORDER BY id DESC LIMIT 1;
    ";

    const string InsertPin = @"
        INSERT INTO label_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, instruction_version,
                               temperature, seed, answer_tokens, context, written_at, run_id)
        VALUES ($model, $model_file, $sha256, $size, $modified, $runtime_name, $runtime_version, $instruction_version,
                $temperature, $seed, $answer_tokens, $context, $written_at, $run_id);
    ";

    readonly IReadingModelFeed feed;
    readonly LocalModelSettings settings;
    readonly NewsLimits limits;
    readonly IClock clock;
    readonly string databaseFile;
    readonly string dataRoot;
    readonly Func<TimeSpan, CancellationToken, Task> wait;

    public NewsLabeller(IReadingModelFeed feed, LocalModelSettings settings, NewsLimits limits, IClock clock, string databaseFile, string dataRoot, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        this.feed = feed;
        this.settings = settings;
        this.limits = limits;
        this.clock = clock;
        this.databaseFile = databaseFile;
        this.dataRoot = dataRoot;
        this.wait = wait ?? Task.Delay;
    }

    // One night's labels: the list and the names a report is written for, each name's own stories of the window.
    public async Task<NewsLabelOutcome> RunAsync(DateOnly session, string runId, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var listed = await ListedAsync(connection, session, cancellation);
        var names = listed.Concat((await ReportedAsync(connection, session, cancellation)).Where(reported => !listed.Any(name => name.Ticker == reported.Ticker))).ToList();
        var from = session.AddDays(-WindowDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = session.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var slot = NewsLabelling.NextLocalSlotStart(clock.UtcNow);

        return await LabelAsync(
            connection,
            runId,
            session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            names.Count,
            async (ask, counting) =>
            {
                foreach (var (ticker, company) in names)
                {
                    counting.Reached++;
                    counting.Refused += await CountAsync(connection, ArticlesRefused, ticker, from, to, cancellation);
                    counting.Already += await CountAsync(connection, ArticlesLabelled, ticker, from, to, cancellation);

                    // The member's own stories alone, newest first and every one of them in the run: a story the feed
                    // tagged with another company's listing of the same ticker is never sent.
                    // see: A story is a member's where its title or its opening names the company by its name or its American listing, and no other story is drawn or counted
                    // see: The news labeller labels the S&P 500's list and every name a report is written for, each name's own stories in one run
                    var own = (await ArticlesAsync(connection, ticker, from, to, cancellation))
                        .Where(article => StoryMatch.IsAbout(article.Title, article.Text, ticker, company))
                        .ToList();

                    foreach (var (articleId, title, text) in own)
                    {
                        if (!await ask(ticker, company, articleId, title, text))
                        {
                            return;
                        }
                    }
                }
            },
            (slot, NextLocalSlot),
            limits.TimeLimit,
            cancellation);
    }

    // Every admitted story the store holds labelled on the local model once, the member's own alone, newest first, until
    // the instant given, before which the batch stops for the next writer's slot.
    // see: The news labeller is a process of its own the off-peak run starts, and labels on the local model under a pin of its own
    public async Task<NewsLabelOutcome> EveryStoryAsync(DateTimeOffset until, string runId, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var stories = new List<(string Ticker, string ArticleId, string Title, string Text, string Company)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EveryArticleToLabel;
            command.Parameters.AddWithValue("$accepted", Admissibility.Accepted);
            command.Parameters.AddWithValue("$profile", settings.Profile);
            command.Parameters.AddWithValue("$version", NewsInstruction.Version);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                stories.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            }
        }

        var own = stories.Where(story => StoryMatch.IsAbout(story.Title, story.Text, story.Ticker, story.Company)).ToList();
        var names = own.Select(story => story.Ticker).Distinct(StringComparer.Ordinal).Count();

        return await LabelAsync(
            connection,
            runId,
            NewsLabelling.EveryStory,
            names,
            async (ask, counting) =>
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var (ticker, articleId, title, text, company) in own)
                {
                    if (seen.Add(ticker))
                    {
                        counting.Reached++;
                    }

                    if (!await ask(ticker, company, articleId, title, text))
                    {
                        return;
                    }
                }
            },
            (until, BatchEnded),
            null,
            cancellation);
    }

    // What a pass over the stories tallies as it goes.
    sealed class Counting
    {
        public int Reached;
        public int Refused;
        public int Already;
    }

    // The pass the night's run and a batch share: the pin held, the lock taken, the model made ready, each story asked
    // through the one asking routine the pass hands its stories to, and the run's row written whatever came of it. The
    // pass stops at the instant it is given, with the stop that names it, and at the time limit where it has one.
    async Task<NewsLabelOutcome> LabelAsync(
        SqliteConnection connection,
        string runId,
        string session,
        int nameCount,
        Func<Func<string, string, string, string, string, Task<bool>>, Counting, Task> pass,
        (DateTimeOffset At, string Stop) until,
        TimeSpan? limit,
        CancellationToken cancellation)
    {
        var startedAt = clock.UtcNow;
        var monthBefore = await SpentThisMonthAsync(connection, startedAt, cancellation);
        var unreadable = new Dictionary<string, int>(StringComparer.Ordinal);
        var counting = new Counting();
        var labelled = 0;
        var notAnswered = 0;
        var calls = 0;
        string? model = null;
        var stop = Finished;
        string? why = null;
        var waited = TimeSpan.Zero;
        var pin = await NewestPinAsync(connection, cancellation);

        if (pin is null)
        {
            (stop, why) = (NoPin, "no label pin is written yet; write one with label-news --pin");
        }
        else if (await DiffersAsync(pin, cancellation) is { } part)
        {
            (stop, why) = (PinDiffers, $"{part} differs from label pin {pin.Id.ToString(CultureInfo.InvariantCulture)}, so nothing is labelled until a pin is written for it");
        }
        else if (monthBefore >= limits.MonthLimit)
        {
            stop = MonthLimitReached;
        }

        // The local model's lock, which the local run, its history read and every other local process hold while they
        // ask: the labeller waits for it rather than overlapping them, and no longer than its time limit or the instant
        // it stops at, whichever comes first.
        // see: The news labeller waits for the local model's lock and stops before the local run's next slot, its time limit counted from its start
        var asked = clock.UtcNow;
        var lockBy = limit is { } span && startedAt + span < until.At ? startedAt + span : until.At;

        await using var held = stop == Finished ? await LocalLocks.TakeAsync(dataRoot, lockBy, clock, wait, cancellation) : null;

        waited = clock.UtcNow - asked;

        if (stop == Finished && held is null)
        {
            (stop, why) = (LockHeld, "another local process held the local model's lock until the run's end, so the next run takes these stories up");
        }

        if (stop == Finished)
        {
            var loaded = false;

            try
            {
                loaded = await feed.PrepareAsync(cancellation);

                async Task<bool> AskAsync(string ticker, string company, string articleId, string title, string text)
                {
                    var now = clock.UtcNow;

                    if (limit is { } most && now - startedAt >= most)
                    {
                        stop = TimeLimitPassed;

                        return false;
                    }

                    if (now >= until.At)
                    {
                        stop = until.Stop;

                        return false;
                    }

                    var request = new ReadingRequest(settings.Model, NewsInstruction.System, NewsInstruction.Prompt(company, ticker, title, text), pin!.Seed, pin.AnswerTokens, NewsInstruction.Schema());
                    NewsLabel? label = null;
                    string? cause = null;
                    var answered = false;

                    // An answer cut off at its budget or empty is asked once more; one that arrived whole is read once.
                    for (var ask = 0; ask <= ReadingSettings.AskedAgain && label is null; ask++)
                    {
                        ReadingReply reply;

                        try
                        {
                            reply = await feed.AskAsync(request, cancellation);
                        }
                        catch (ReadingRefused)
                        {
                            calls++;
                            continue;
                        }

                        calls++;
                        answered = true;
                        model ??= reply.Model;

                        var cut = string.Equals(reply.FinishReason, ReleaseAnswers.BudgetReached, StringComparison.Ordinal);

                        (label, cause) = cut ? (null, NewsInstruction.CutOff) : NewsInstruction.Read(reply.Content ?? string.Empty);

                        if (label is null && !cut && !string.IsNullOrWhiteSpace(reply.Content))
                        {
                            break;
                        }
                    }

                    if (!answered)
                    {
                        // The runtime did not answer this story: no row, so the next run takes it up.
                        notAnswered++;

                        return true;
                    }

                    if (label is not null)
                    {
                        labelled += await WriteAsync(connection, ticker, articleId, runId, model, Labelled, null, label, cancellation);
                    }
                    else
                    {
                        unreadable[cause!] = unreadable.GetValueOrDefault(cause!) + 1;
                        await WriteAsync(connection, ticker, articleId, runId, model, Unreadable, cause, null, cancellation);
                    }

                    return true;
                }

                await pass(AskAsync, counting);
                await feed.ReleaseAsync(loaded, cancellation);
            }
            catch (ReadingRuntimeUnavailable gone)
            {
                (stop, why) = (ModelUnreachable, gone.Message);
            }
        }

        var dropped = await DropOrphansAsync(connection, cancellation);
        var outcome = new NewsLabelOutcome(nameCount, counting.Reached, labelled, unreadable, counting.Refused, counting.Already, 0m, monthBefore, stop, dropped, notAnswered, waited);

        await RecordAsync(connection, runId, session, startedAt, calls, model, pin, outcome, why, cancellation);

        return outcome;
    }

    // The first part the run differs from the newest pin in before it asks anything, or none: the model the profile
    // names, the model's file by its size and its modified instant and by its hash where either differs, the
    // instruction's version, and the window the profile loads the model at.
    async Task<string?> DiffersAsync(LabelPin pin, CancellationToken cancellation)
    {
        if (!string.Equals(settings.Model, pin.Model, StringComparison.Ordinal))
        {
            return ReadingPins.ModelPart;
        }

        if (NewsInstruction.Version != pin.InstructionVersion)
        {
            return ReadingPins.PromptPart;
        }

        if (settings.ContextTokens != pin.Context)
        {
            return ReadingPins.WindowPart;
        }

        var folder = LocalModelFile.FolderOf(settings);

        if (settings.ModelFile is not { } relative || !string.Equals(relative, pin.File, StringComparison.Ordinal) || LocalModelFile.Facts(folder, relative) is not { } facts)
        {
            return ReadingPins.FilePart;
        }

        if (facts.Size == pin.Size && facts.Modified.UtcTicks == pin.Modified.UtcTicks)
        {
            return null;
        }

        return string.Equals(await LocalModelFile.HashAsync(folder, relative, cancellation), pin.Sha256, StringComparison.Ordinal) ? null : ReadingPins.FilePart;
    }

    // A new version the labeller labels under: the model's file hashed whole, the runtime read off a probe's answer and
    // kept as a record, the instruction's version, the seed, the answer's budget and the window, written where any part
    // but the runtime differs from the newest pin and refused where none does.
    public async Task<string> PinAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation);

        if (settings.ModelFile is not { } relative)
        {
            return $"label-news: {ModelFileNotSet}, {settings.Profile}, so no version is written.";
        }

        var folder = LocalModelFile.FolderOf(settings);

        if (LocalModelFile.Facts(folder, relative) is not { } facts)
        {
            return $"label-news: no file {relative} stands under LM Studio's models folder, so no version is written.";
        }

        var sha = await LocalModelFile.HashAsync(folder, relative, cancellation);
        ReadingReply probe;
        bool loaded;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
            probe = await feed.AskAsync(new ReadingRequest(settings.Model, LocalRun.ProbeInstruction, LocalRun.ProbeText, ReadingSettings.Seed, LocalRun.ProbeTokens), cancellation);
        }
        catch (Exception refused) when (refused is ReadingRefused or ReadingRuntimeUnavailable)
        {
            return $"label-news: the local model did not answer the probe, so no version is written: {refused.Message}";
        }

        await feed.ReleaseAsync(loaded, cancellation);

        if (!string.Equals(probe.Model, settings.Model, StringComparison.Ordinal) || probe.RuntimeName is not { } runtime || probe.RuntimeVersion is not { } version)
        {
            return $"label-news: {PinRefused}: it was answered by {probe.Model ?? "no model"} on {probe.RuntimeName ?? "no runtime"}, so no version is written.";
        }

        var candidate = new LabelPin(0, settings.Model, relative, sha, facts.Size, facts.Modified, runtime, version, NewsInstruction.Version, ReadingSettings.Seed, NewsLabelling.AnswerTokens, settings.ContextTokens, started);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await NewestPinAsync(connection, cancellation) is { } newest
            && newest.Model == candidate.Model && newest.File == candidate.File && newest.Sha256 == candidate.Sha256
            && newest.InstructionVersion == candidate.InstructionVersion && newest.Seed == candidate.Seed
            && newest.AnswerTokens == candidate.AnswerTokens && newest.Context == candidate.Context)
        {
            return FormattableString.Invariant($"label-news: label pin {newest.Id}: {PinUnchanged}, so no version is written.");
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = InsertPin;
            insert.Parameters.AddWithValue("$model", candidate.Model);
            insert.Parameters.AddWithValue("$model_file", candidate.File);
            insert.Parameters.AddWithValue("$sha256", candidate.Sha256);
            insert.Parameters.AddWithValue("$size", candidate.Size);
            insert.Parameters.AddWithValue("$modified", candidate.Modified.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$runtime_name", candidate.RuntimeName);
            insert.Parameters.AddWithValue("$runtime_version", candidate.RuntimeVersion);
            insert.Parameters.AddWithValue("$instruction_version", candidate.InstructionVersion);
            insert.Parameters.AddWithValue("$temperature", ReadingSettings.Temperature);
            insert.Parameters.AddWithValue("$seed", candidate.Seed);
            insert.Parameters.AddWithValue("$answer_tokens", candidate.AnswerTokens);
            insert.Parameters.AddWithValue("$context", candidate.Context);
            insert.Parameters.AddWithValue("$written_at", started.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$run_id", runId);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var written = await NewestPinAsync(connection, cancellation);

        return FormattableString.Invariant($"label-news: label pin {written!.Id} written for {candidate.Model}, its file {candidate.File} of {candidate.Size} bytes, instruction version {candidate.InstructionVersion}, seed {candidate.Seed}, an answer's budget of {candidate.AnswerTokens} tokens and a window of {candidate.Context}, answered on {candidate.RuntimeName} {candidate.RuntimeVersion}.");
    }

    public static async Task<LabelPin?> NewestPinAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestPin;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new LabelPin(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal))
            : null;
    }

    // A run refused before it asked anything, its profile unreadable or a limit that is not a number, written as its
    // own row where the store is there to hold it, so the run page says why.
    public static async Task RefusedAsync(string databaseFile, string runId, DateTimeOffset at, string message, DateOnly? session = null)
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
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { refused = message, session = session?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));

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

    static async Task<IReadOnlyList<(string Ticker, string Company)>> ReportedAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReportsAsked;
        command.Parameters.AddWithValue("$since", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z");

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

    async Task<int> WriteAsync(SqliteConnection connection, string ticker, string articleId, string runId, string? model, string outcome, string? cause, NewsLabel? label, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = InsertLabel;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$article_id", articleId);
        command.Parameters.AddWithValue("$profile", settings.Profile);
        command.Parameters.AddWithValue("$version", NewsInstruction.Version);
        command.Parameters.AddWithValue("$model", model ?? settings.Model);
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

    // What the labeller's own calls have cost this UTC month, summed in code from the money each row carries: a local
    // run's row carries nothing, so the sum is the paid runs' alone.
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

    async Task RecordAsync(SqliteConnection connection, string runId, string session, DateTimeOffset startedAt, int calls, string? model, LabelPin? pin, NewsLabelOutcome outcome, string? why, CancellationToken cancellation)
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
        command.Parameters.AddWithValue("$network_requests", 0);
        command.Parameters.AddWithValue("$spend", "0");
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new
        {
            session,
            profile = settings.Profile,
            model = model ?? settings.Model,
            local = true,
            pin = pin?.Id,
            instructionVersion = NewsInstruction.Version,
            monthLimit = limits.MonthLimit.ToString(CultureInfo.InvariantCulture),
            timeLimitMinutes = (int)limits.TimeLimit.TotalMinutes,
            names = outcome.Names,
            reached = outcome.NamesReached,
            labelled = outcome.Labelled,
            unreadable = outcome.Unreadable,
            notAnswered = outcome.NotAnswered,
            refusedByAdmissibility = outcome.RefusedByAdmissibility,
            alreadyLabelled = outcome.AlreadyLabelled,
            stop = outcome.Stop,
            why,
            cost = "0",
            monthCost = outcome.MonthCost.ToString(CultureInfo.InvariantCulture),
            labelsDropped = outcome.LabelsDropped,
            waitedMinutes = (int)Math.Round(outcome.Waited.TotalMinutes),
        }));

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
