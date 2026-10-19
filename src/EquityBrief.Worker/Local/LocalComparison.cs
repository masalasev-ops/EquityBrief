using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// What a comparison of the news labels came to: the paid profile it read, the agreement, the model and the runtime that
// answered, why it stopped where it did not reach every article, and the file it wrote, relative to the data root.
public sealed record LabelComparisonOutcome(string PaidProfile, LabelAgreement Agreement, string? Model, string? Runtime, string? Stopped, string File);

// The local model set against paid work already stored, run by hand. From 20.5 every article the news job's paid profile
// labelled under the instruction's version that the store still holds is labelled again by the local profile's model
// under the labeller's own instruction, at temperature zero with the readings' seed and the news job's answer budget, an
// answer that cannot be read asked once more as the labeller asks it. It holds the local lock so one local process calls
// the model at a time, loads the model where it is not loaded and unloads it at its end only where it loaded it, opens the
// store read-only, and writes what it found to a file of its own under the data root, so it writes no store row and
// switches nothing.
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
        ],
        Feeds: [Feed.LocalModel]);

    // The folder under the data root a comparison writes its files into.
    public const string Folder = "comparisons";

    // Each article a profile labelled under the instruction's version that the store still holds, with the company's
    // name the labeller sends, the oldest label first.
    const string PaidLabels = @"
        SELECT b.ticker, b.article_id, b.kind, b.direction, b.reason, a.title, a.text,
               COALESCE((SELECT m.name FROM membership m WHERE m.ticker = b.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1), b.ticker)
        FROM news_label b
        JOIN news_article a ON a.ticker = b.ticker AND a.article_id = b.article_id
        WHERE b.profile = $profile AND b.instruction_version = $version AND b.outcome = $labelled
        ORDER BY b.labelled_at, b.ticker, b.article_id;
    ";

    sealed record PaidArticle(string Ticker, string ArticleId, string Title, string Text, string Company, NewsLabel Label);

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
            var held = await LocalLocks.TakeAsync(dataRoot, clock.UtcNow, clock, pause, cancellation);

            if (held is null)
            {
                output.WriteLine("compare-labels: another local process is calling the local model, so this waits for it to end.");

                held = await LocalLocks.TakeAsync(dataRoot, null, clock, pause, cancellation);
            }

            await using var locked = held;

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
                                new ReadingRequest(settings.Model, NewsInstruction.System, NewsInstruction.Prompt(article.Company, article.Ticker, article.Title, article.Text), ReadingSettings.Seed, answerTokens),
                                cancellation);

                            model ??= reply.Model;
                            runtime ??= reply.RuntimeName is null ? null : $"{reply.RuntimeName} {reply.RuntimeVersion}".Trim();
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
        var folder = Path.Combine(dataRoot, Folder);
        var name = "labels-" + started.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".json";

        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, name), JsonSerializer.Serialize(
            new
            {
                comparedAt = started.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
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
                articles = rows,
            },
            new JsonSerializerOptions { WriteIndented = true }), cancellation);

        var file = Folder + "/" + name;

        output.WriteLine(Said(paidProfile, articles.Count, agreement, model ?? settings.Model, runtime, stopped, file));

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

    async Task<IReadOnlyList<PaidArticle>> PaidArticlesAsync(string paidProfile, CancellationToken cancellation)
    {
        var builder = StoreConnection.Builder(databaseFile);

        builder.Mode = SqliteOpenMode.ReadOnly;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = PaidLabels;
        command.Parameters.AddWithValue("$profile", paidProfile);
        command.Parameters.AddWithValue("$version", NewsInstruction.Version);
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
                new NewsLabel(reader.GetString(2), reader.GetString(3), reader.GetString(4))));
        }

        return articles;
    }
}
