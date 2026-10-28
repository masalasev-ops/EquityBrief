using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// What a comparison of the news labels came to: the paid profile it read, the agreement, the model and the runtime that
// answered, why it stopped where it did not reach every article, and the file it wrote, relative to the data root.
public sealed record LabelComparisonOutcome(string PaidProfile, LabelAgreement Agreement, string? Model, string? Runtime, string? Stopped, string File);

// What a comparison of the reviews came to: the reviews stored, those asked again and each not asked with why, how many
// outcomes agreed, the drafts' sentences both reviews kept, both changed or treated apart, the model and the runtime that
// answered, why it stopped where it did not reach every review, and the file it wrote, relative to the data root.
public sealed record ReviewComparisonOutcome(
    int Stored,
    int Asked,
    IReadOnlyList<string> NotAsked,
    int OutcomesAgreeing,
    int Sentences,
    int BothKept,
    int BothChanged,
    int Differ,
    string? Model,
    string? Runtime,
    string? Stopped,
    string File);

// The local model set against paid work already stored, run by hand. From 20.5 every article the news job's paid profile
// labelled under the instruction's version that the store still holds is labelled again by the local profile's model
// under the labeller's own instruction, at temperature zero with the readings' seed and the news job's answer budget, an
// answer that cannot be read asked once more as the labeller asks it. From 20.7 every review the run log holds is asked
// again of the local model as the review asked it, over the pass's version, its night's facts file and the documents it
// cites, each round checked in memory by the claim checker's own rules. Each form holds the local lock so one local
// process calls the model at a time, loads the model where it is not loaded and unloads it at its end only where it
// loaded it, opens the store read-only, and writes what it found to a file of its own under the data root, so it writes
// no store row and switches nothing.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
public sealed class LocalComparison(
    IReadingModelFeed feed,
    LocalModelSettings settings,
    IClock clock,
    string databaseFile,
    string dataRoot,
    Func<TimeSpan, CancellationToken, Task>? wait = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.NewsArticle, Touch.Read),
            new StoreTouch(Store.NewsLabel, Touch.Read),
            new StoreTouch(Store.ResearchSection, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read),
        ],
        Feeds: [Feed.LocalModel]);

    // The folder under the data root a comparison writes its files into.
    public const string Folder = "comparisons";

    // The answer budget a review is asked at again: a section's draft and room past it.
    public const int ReviewAnswerTokens = 4096;

    // Why a stored review is not asked again.
    public const string VersionNotHeld = "the version it was handed is not stored";
    public const string FactsNotHeld = "its night's facts file is no longer held whole";
    public const string ClaimsNotHeld = "the claims it was written from are not stored";

    // Each article a profile labelled under the paid labeller's instruction's version that the store still holds, with
    // the company's name the labeller sends, the oldest label first: the paid labels stand under the version before the
    // keys, each kind read as its key one to one.
    const string PaidLabels = @"
        SELECT b.ticker, b.article_id, b.kind, b.direction, b.reason, a.title, a.text,
               COALESCE((SELECT m.name FROM membership m WHERE m.ticker = b.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1), b.ticker)
        FROM news_label b
        JOIN news_article a ON a.ticker = b.ticker AND a.article_id = b.article_id
        WHERE b.profile = $profile AND b.instruction_version = $version AND b.outcome = $labelled
        ORDER BY b.labelled_at, b.ticker, b.article_id;
    ";

    // The reviews the run log holds, oldest first, each with the instant it began.
    const string StoredReviews = @"
        SELECT started_at, detail FROM run_log
        WHERE substr(stage, 1, length($stage)) = $stage
        ORDER BY started_at, run_id, stage;
    ";

    // The facts file a review read: the newest row on or before the night it was asked over, with its payload as stored,
    // so a night the retention has emptied since is found empty, and neither a later night's row nor an older night's is
    // read in its place.
    const string NightFacts = @"
        SELECT session_date, payload FROM facts
        WHERE ticker = $ticker AND session_date <= $through
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    // The session of the newest night whose facts step began before an instant, read off that night's own fetch row,
    // which names it: a review reads the newest facts file standing when it is asked, and a facts file is written by its
    // night.
    const string NightBefore = @"
        SELECT json_extract(f.detail, '$.session')
        FROM run_log n
        JOIN run_log f ON f.run_id = n.run_id AND f.stage = $fetch
        WHERE substr(n.run_id, 1, length($night)) = $night AND n.stage = $facts AND n.started_at < $before
          AND json_valid(f.detail)
        ORDER BY n.started_at DESC
        LIMIT 1;
    ";

    // The run a night writes its rows under, named by its prefix.
    const string NightRuns = "night-";

    sealed record PaidArticle(string Ticker, string ArticleId, string Title, string Text, string Company, NewsLabel Label);

    sealed record StoredReview(string Ticker, string Section, int Version, string Outcome, string? Final, string Started);

    sealed record ReadyReview(StoredReview Stored, SectionTrial.PassVersion Version, IReadOnlyList<Fact> Facts, DateOnly Night, IReadOnlyList<StoredDocument> Admitted, IReadOnlyList<Claim>? Claims);

    readonly Func<TimeSpan, CancellationToken, Task> pause = wait ?? Task.Delay;

    public async Task<LabelComparisonOutcome> LabelsAsync(string paidProfile, int answerTokens, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var articles = await PaidArticlesAsync(paidProfile, cancellation);
        var pairs = new List<LabelAgreement.Pair>();
        var rows = new List<object>();
        string? model = null;
        string? runtime = null;
        string? stopped = null;

        if (articles.Count > 0)
        {
            await using var locked = await LockAsync("compare-labels", output, cancellation);

            var loaded = false;

            try
            {
                loaded = await feed.PrepareAsync(cancellation);

                foreach (var article in articles)
                {
                    var answers = new List<string>();
                    NewsLabel? label = null;
                    string? cause = null;

                    for (var ask = 0; ask <= ReadingSettings.AskedAgain && label is null; ask++)
                    {
                        try
                        {
                            var reply = await feed.AskAsync(
                                new ReadingRequest(settings.Model, NewsInstruction.System, NewsInstruction.Prompt(article.Company, article.Ticker, article.Title, article.Text), ReadingSettings.Seed, answerTokens, NewsInstruction.Schema()),
                                cancellation);

                            model ??= reply.Model;
                            runtime ??= RuntimeOf(reply);
                            answers.Add(reply.Content ?? string.Empty);

                            (label, cause) = string.Equals(reply.FinishReason, ReleaseAnswers.BudgetReached, StringComparison.Ordinal)
                                ? (null, NewsInstruction.CutOff)
                                : NewsInstruction.Read(reply.Content ?? string.Empty);
                        }
                        catch (ReadingRefused refused)
                        {
                            (label, cause) = (null, refused.Cause);
                        }
                    }

                    pairs.Add(new LabelAgreement.Pair(article.Label, label, label is null ? cause : null, answers));
                    rows.Add(new
                    {
                        ticker = article.Ticker,
                        articleId = article.ArticleId,
                        title = article.Title,
                        paid = new { kind = article.Label.Kind, direction = article.Label.Direction, reason = article.Label.Reason },
                        local = label is null ? null : new { kind = label.Kind, direction = label.Direction, reason = label.Reason },
                        cause = label is null ? cause : null,
                        answers,
                    });
                }

                await feed.ReleaseAsync(loaded, cancellation);
            }
            catch (ReadingRuntimeUnavailable gone)
            {
                stopped = gone.Message;
            }
        }

        var agreement = LabelAgreement.Of(pairs);

        // The stories the two labels disagree on by kind, each with both keys, which the file lists and the run prints.
        var disagreeing = articles
            .Zip(pairs, (article, pair) => (article, pair))
            .Where(both => both.pair.Local is { } local && !string.Equals(local.Kind, both.article.Label.Kind, StringComparison.Ordinal))
            .Select(both => new { ticker = both.article.Ticker, articleId = both.article.ArticleId, title = both.article.Title, paid = both.article.Label.Kind, local = both.pair.Local!.Kind })
            .ToArray();
        var file = await WriteAsync("labels", started, new
        {
            comparedAt = Instant(started),
            paidProfile,
            localProfile = settings.Profile,
            model,
            runtime,
            instructionVersion = NewsInstruction.Version,
            stopped,
            held = articles.Count,
            compared = agreement.Compared,
            labelled = agreement.Labelled,
            unreadable = agreement.Unreadable,
            kind = agreement.Kind,
            direction = agreement.Direction,
            both = agreement.Both,
            reasons = agreement.Reasons,
            reasonsWithoutDigit = agreement.ReasonsWithoutDigit,
            kindDisagreements = disagreeing,
            articles = rows,
        }, cancellation);

        output.WriteLine(Said(paidProfile, articles.Count, agreement, model ?? settings.Model, runtime, stopped, file));

        foreach (var story in disagreeing)
        {
            output.WriteLine($"  {story.ticker} {story.articleId}: {paidProfile} {story.paid}, local {story.local}: {story.title}");
        }

        return new LabelComparisonOutcome(paidProfile, agreement, model, runtime, stopped, file);
    }

    // The comparison's line: what it compared, what the local model labelled and could not, the agreement, the reasons
    // holding no digit, and where the file is.
    public static string Said(string paidProfile, int held, LabelAgreement agreement, string model, string? runtime, string? stopped, string file)
    {
        var causes = agreement.Unreadable.Count == 0
            ? string.Empty
            : " (" + string.Join(", ", agreement.Unreadable.OrderBy(cause => cause.Key, StringComparer.Ordinal).Select(cause => FormattableString.Invariant($"{cause.Key} {cause.Value}"))) + ")";

        return FormattableString.Invariant(
            $"compare-labels: {held} articles {paidProfile} labelled are still stored; {model}{(runtime is null ? string.Empty : " on " + runtime)} labelled {agreement.Labelled} of the {agreement.Compared} it was asked and could not label {agreement.Compared - agreement.Labelled}{causes}; ")
            + FormattableString.Invariant(
                $"of its {agreement.Labelled} labels, the kind agreed on {agreement.Kind}, the direction on {agreement.Direction} and both on {agreement.Both}; {agreement.ReasonsWithoutDigit} of the {agreement.Reasons} reasons its answers stated held no digit; ")
            + (stopped is null ? string.Empty : $"stopped before the end: {stopped}; ")
            + $"written to {file} under the data root, and nothing to the store.";
    }

    public async Task<ReviewComparisonOutcome> ReviewsAsync(TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var ready = new List<ReadyReview>();
        var notAsked = new List<string>();
        int stored;

        await using (var connection = await ReadOnlyAsync(cancellation))
        {
            var reviews = await StoredReviewsAsync(connection, cancellation);

            stored = reviews.Count;

            foreach (var review in reviews)
            {
                if (await ReadyAsync(connection, review, cancellation) is { } prepared)
                {
                    ready.Add(prepared);
                }
                else
                {
                    notAsked.Add($"{review.Ticker}, {review.Section}, version {review.Version.ToString(CultureInfo.InvariantCulture)}: {await WhyNotAsync(connection, review, cancellation)}");
                }
            }
        }

        var rows = new List<object>();
        var (agreeing, sentences, bothKept, bothChanged, differ) = (0, 0, 0, 0, 0);
        string? model = null;
        string? runtime = null;
        string? stopped = null;

        if (ready.Count > 0)
        {
            await using var locked = await LockAsync("compare-reviews", output, cancellation);

            var loaded = false;

            try
            {
                loaded = await feed.PrepareAsync(cancellation);

                foreach (var review in ready)
                {
                    var (outcome, rounds, final, answered) = await AskAgainAsync(review, cancellation);

                    model ??= answered?.Model;
                    runtime ??= answered is null ? null : RuntimeOf(answered);

                    var agreement = ReviewAgreement.Of(review.Version.Prose, review.Stored.Final, final);
                    var same = string.Equals(outcome, review.Stored.Outcome, StringComparison.Ordinal);

                    agreeing += same ? 1 : 0;

                    if (agreement is not null)
                    {
                        sentences += agreement.Sentences;
                        bothKept += agreement.BothKept;
                        bothChanged += agreement.BothChanged;
                        differ += agreement.Differ;
                    }

                    rows.Add(new
                    {
                        ticker = review.Stored.Ticker,
                        section = review.Stored.Section,
                        version = review.Stored.Version,
                        night = review.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        stored = review.Stored.Outcome,
                        local = outcome,
                        sentences = agreement?.Sentences,
                        bothKept = agreement?.BothKept,
                        bothChanged = agreement?.BothChanged,
                        differ = agreement?.Differ,
                        rounds,
                    });
                }

                await feed.ReleaseAsync(loaded, cancellation);
            }
            catch (ReadingRuntimeUnavailable gone)
            {
                stopped = gone.Message;
            }
        }

        var file = await WriteAsync("reviews", started, new
        {
            comparedAt = Instant(started),
            localProfile = settings.Profile,
            model,
            runtime,
            stopped,
            stored,
            asked = rows.Count,
            notAsked,
            outcomesAgreeing = agreeing,
            sentences,
            bothKept,
            bothChanged,
            differ,
            reviews = rows,
        }, cancellation);

        var outcomeOf = new ReviewComparisonOutcome(stored, rows.Count, notAsked, agreeing, sentences, bothKept, bothChanged, differ, model, runtime, stopped, file);

        output.WriteLine(Said(outcomeOf, model ?? settings.Model));

        return outcomeOf;
    }

    // The reviews' line: how many are stored, asked again and not with why, the outcomes agreeing, the sentences, and where
    // the file is.
    public static string Said(ReviewComparisonOutcome outcome, string model) =>
        FormattableString.Invariant($"compare-reviews: {outcome.Stored} reviews are stored; ")
        + FormattableString.Invariant($"{model}{(outcome.Runtime is null ? string.Empty : " on " + outcome.Runtime)} was asked {outcome.Asked} again")
        + (outcome.NotAsked.Count == 0 ? string.Empty : FormattableString.Invariant($" and not {outcome.NotAsked.Count}, ") + string.Join("; ", outcome.NotAsked))
        + FormattableString.Invariant($"; the outcome agreed on {outcome.OutcomesAgreeing} of {outcome.Asked}; ")
        + FormattableString.Invariant($"of the {outcome.Sentences} sentences the drafts handed held, both kept {outcome.BothKept}, both changed {outcome.BothChanged} and {outcome.Differ} one kept and the other changed; ")
        + (outcome.Stopped is null ? string.Empty : $"stopped before the end: {outcome.Stopped}; ")
        + $"written to {outcome.File} under the data root, and nothing to the store.";

    // A stored review asked again, round by round as the review asked it: its first round handed the pass's draft and the
    // section's checks, a refused answer told what was refused once, each answer checked in memory, and none where the
    // model gave no answer or stopped at its budget.
    async Task<(string Outcome, IReadOnlyList<object> Rounds, string? Final, ReadingReply? Answered)> AskAgainAsync(ReadyReview review, CancellationToken cancellation)
    {
        var (ticker, section) = (review.Stored.Ticker, review.Stored.Section);
        var documents = review.Admitted.Select(document => PromptDocument.From(document)).ToArray();
        var rounds = new List<object>();
        var retry = ReviewBrief.For(section, review.Version.Prose);
        var outcome = SectionTrial.NotAnswered;
        string? final = null;
        ReadingReply? answered = null;

        for (var round = 1; round <= 2; round++)
        {
            var request = review.Claims is { } claims
                ? SectionPrompt.ClaimsRequest(settings.Model, ticker, section, claims, review.Facts, retry)
                : SectionPrompt.PaidRequest(settings.Model, ticker, section, review.Facts, documents, retry, review.Night);
            ReadingReply reply;

            try
            {
                reply = await feed.AskAsync(new ReadingRequest(settings.Model, request.System, request.Prompt, ReadingSettings.Seed, ReviewAnswerTokens), cancellation);
            }
            catch (ReadingRefused refused)
            {
                rounds.Add(new { round, prose = (string?)null, verdict = refused.Message });
                outcome = round == 1 ? SectionTrial.NotAnswered : SectionTrial.LeftOut;

                break;
            }

            answered ??= reply;

            if (string.Equals(reply.FinishReason, ReleaseAnswers.BudgetReached, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(reply.Content))
            {
                rounds.Add(new { round, prose = (string?)null, verdict = "no answer within its budget" });
                outcome = round == 1 ? SectionTrial.NotAnswered : SectionTrial.LeftOut;

                break;
            }

            var (prose, parts) = RiskFields.IsRisks(section)
                ? RiskFields.FromAnswer(reply.Content)
                : (reply.Content, review.Claims is null ? null : AcceptedClaims.Stored(review.Claims));
            var verdict = ClaimRules.Check(section, prose, review.Facts, [.. review.Admitted.Cast<StoredDocument?>()], review.Night, parts);

            rounds.Add(new { round, prose, verdict = verdict.Passes ? "accepted" : verdict.Reason });
            final = prose;

            if (verdict.Passes)
            {
                outcome = round == 1 ? SectionTrial.FirstTime : SectionTrial.OnRetry;

                break;
            }

            outcome = SectionTrial.LeftOut;
            retry = RetryBrief.For(verdict.Findings);
        }

        return (outcome, rounds, final, answered);
    }

    // A stored review made ready to ask again: the pass's version it was handed, its night's facts file held whole, the
    // admitted documents the version cites and, for the two written last, the claims it was written from; none where any
    // of them is not held.
    static async Task<ReadyReview?> ReadyAsync(SqliteConnection connection, StoredReview review, CancellationToken cancellation)
    {
        if (await SectionTrial.VersionAsync(connection, review.Ticker, review.Section, review.Version, cancellation) is not { } version
            || await FactsOfAsync(connection, review, version.AsOf, cancellation) is not { } night)
        {
            return null;
        }

        var claims = ClaimRules.IsWrittenLast(review.Section) ? AcceptedClaims.Read(version.Parts) : null;

        if (ClaimRules.IsWrittenLast(review.Section) && claims is null)
        {
            return null;
        }

        var sources = await SectionTrial.SourcesAsync(connection, version.SourceIds, cancellation);

        return new ReadyReview(review, version, night.Facts, night.Session, [.. sources.OfType<StoredDocument>().Where(document => document.Admitted)], claims);
    }

    // Why a stored review is not asked again, in the order its parts are read.
    static async Task<string> WhyNotAsync(SqliteConnection connection, StoredReview review, CancellationToken cancellation) =>
        await SectionTrial.VersionAsync(connection, review.Ticker, review.Section, review.Version, cancellation) is not { } version
            ? VersionNotHeld
            : await FactsOfAsync(connection, review, version.AsOf, cancellation) is null ? FactsNotHeld : ClaimsNotHeld;

    // The facts a review read, through the night it was asked over: the newest night whose facts step began before it,
    // or the version's own day where the run log holds no such night or names a later one.
    static async Task<(DateOnly Session, IReadOnlyList<Fact> Facts)?> FactsOfAsync(SqliteConnection connection, StoredReview review, DateOnly asOf, CancellationToken cancellation)
    {
        var through = asOf;

        await using (var night = connection.CreateCommand())
        {
            night.CommandText = NightBefore;
            night.Parameters.AddWithValue("$fetch", Bars.BarFetcher.Stage);
            night.Parameters.AddWithValue("$facts", Facts.FactsAssembler.Stage);
            night.Parameters.AddWithValue("$night", NightRuns);
            night.Parameters.AddWithValue("$before", review.Started);

            if (await night.ExecuteScalarAsync(cancellation) is string session
                && DateOnly.TryParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var read)
                && read < through)
            {
                through = read;
            }
        }

        await using var command = connection.CreateCommand();

        command.CommandText = NightFacts;
        command.Parameters.AddWithValue("$ticker", review.Ticker);
        command.Parameters.AddWithValue("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) && reader.GetString(1).Length > 0
            ? (DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), FactsFile.Read(reader.GetString(1)))
            : null;
    }

    static async Task<IReadOnlyList<StoredReview>> StoredReviewsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = StoredReviews;
        command.Parameters.AddWithValue("$stage", SectionTrial.ReviewStage + ":");

        var reviews = new List<StoredReview>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            using var detail = JsonDocument.Parse(reader.GetString(1));
            var root = detail.RootElement;
            var final = root.GetProperty("rounds").EnumerateArray()
                .Select(round => round.TryGetProperty("prose", out var prose) && prose.ValueKind == JsonValueKind.String ? prose.GetString() : null)
                .LastOrDefault(prose => prose is not null);

            reviews.Add(new StoredReview(
                root.GetProperty("ticker").GetString()!,
                root.GetProperty("section").GetString()!,
                root.GetProperty("compared").GetProperty("version").GetInt32(),
                root.GetProperty("outcome").GetString()!,
                final,
                reader.GetString(0)));
        }

        return reviews;
    }

    // The local lock, waited for and said so where another local process holds it.
    async Task<FileStream> LockAsync(string verb, TextWriter output, CancellationToken cancellation)
    {
        var held = await LocalLocks.TakeAsync(dataRoot, clock.UtcNow, clock, pause, cancellation);

        if (held is null)
        {
            output.WriteLine($"{verb}: another local process is calling the local model, so this waits for it to end.");

            held = await LocalLocks.TakeAsync(dataRoot, null, clock, pause, cancellation);
        }

        return held!;
    }

    // A comparison's file under the data root's folder of comparisons, named by its kind and its instant, and its path
    // relative to the data root.
    async Task<string> WriteAsync(string kind, DateTimeOffset started, object figures, CancellationToken cancellation)
    {
        var folder = Path.Combine(dataRoot, Folder);
        var name = kind + "-" + started.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".json";

        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, name), JsonSerializer.Serialize(figures, new JsonSerializerOptions { WriteIndented = true }), cancellation);

        return Folder + "/" + name;
    }

    async Task<SqliteConnection> ReadOnlyAsync(CancellationToken cancellation)
    {
        var builder = StoreConnection.Builder(databaseFile);

        builder.Mode = SqliteOpenMode.ReadOnly;

        var connection = new SqliteConnection(builder.ConnectionString);

        await connection.OpenAsync(cancellation);

        return connection;
    }

    async Task<IReadOnlyList<PaidArticle>> PaidArticlesAsync(string paidProfile, CancellationToken cancellation)
    {
        await using var connection = await ReadOnlyAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = PaidLabels;
        command.Parameters.AddWithValue("$profile", paidProfile);
        command.Parameters.AddWithValue("$version", NewsInstruction.PaidVersion);
        command.Parameters.AddWithValue("$labelled", NewsLabelling.Labelled);

        var articles = new List<PaidArticle>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            articles.Add(new PaidArticle(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                new NewsLabel(NewsInstruction.KeyOf(reader.GetString(2)), reader.GetString(3), reader.GetString(4))));
        }

        return articles;
    }

    static string? RuntimeOf(ReadingReply reply) =>
        reply.RuntimeName is null ? null : $"{reply.RuntimeName} {reply.RuntimeVersion}".Trim();

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
