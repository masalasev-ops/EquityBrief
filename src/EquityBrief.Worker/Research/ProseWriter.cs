using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// One section the writer wrote.
public sealed record WrittenSection(string Section, int Version, string Model, bool Retry);

// One section the writer was asked for and did not write, and why.
//
// Held in two lists. `NotWritten` is a section this pass left absent, which is what
// a reader is told about with its reason. `Skipped` is a section already written
// today, left out today, or waiting on the checker, each of which a reader is shown
// through its own stored row, so a line saying it was not written would contradict
// the page it sits on.
public sealed record UnwrittenSection(string Section, string Reason);

public sealed record ProseOutcome(
    string Ticker,
    DateOnly AsOf,
    IReadOnlyList<WrittenSection> Written,
    IReadOnlyList<UnwrittenSection> NotWritten,
    IReadOnlyList<UnwrittenSection> Skipped,
    int ModelCalls,
    bool Unavailable);

// The prose writer. Whatever sections the local lane holds, for one name, on the
// operator's own model, at no cost.
//
// It names no section of its own: the lane is a list in configuration and the
// writer takes the list whole, so moving a section between lanes changes a value
// and touches no code. It writes each section as pending and the claim checker is
// what moves it, like anything else that writes a section.
// see: The local lane is a configured list of section names, and the prose writer writes whatever the list holds
// see: The model never fetches; components fetch and hand it documents
//
// The documents a researched section rests on are handed in by the caller rather
// than read here, because the component that fetched them is the component that
// tested and stored them, and a writer that went looking for its own evidence
// would be deciding what the section may rest on.
public sealed class ProseWriter(
    ILocalModelFeed model,
    LocalModelSettings settings,
    IReadOnlyList<string> lane,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.ResearchSection, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Stage = "prose";

    // This machine's lane, which is the list's value and not a fact about the
    // component: the short-prompt work figure 12.2 puts on the left.
    public static readonly string[] DefaultLane =
    [
        "The cause of each large move",
        "What the company sells",
        "The segment commentary",
        "The key under each figure",
    ];

    // The reasons a section in the lane is not written, stated once so the run log
    // and the page say them the same way.
    public const string CannotHold = "the machine cannot hold it";
    public const string Unavailable = "the local model is unavailable";
    public const string NothingHanded = "no document was handed to the writer for it";
    public const string NoFactsFile = "no facts file is stored for the name on or before today";
    public const string NoDocumentInsideAMove = "no document handed to the writer was published inside any stored move";

    // And the three that are not absences: the section has a row a reader is shown
    // or will be, so the page says nothing of the skip.
    public const string AwaitingTheChecker = "an earlier draft is still waiting on the claim checker";
    public const string WrittenToday = "it was already written today";
    public const string LeftOutToday = "it was left out today and a new pass is what writes it again";

    const string FactsFor = @"
        SELECT payload FROM facts
        WHERE ticker = $ticker AND session_date <= $as_of AND payload != ''
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    const string NewestVersion = @"
        SELECT version, as_of, status, reject_reason
        FROM research_section
        WHERE ticker = $ticker AND section = $section
        ORDER BY version DESC
        LIMIT 1;
    ";

    const string Insert = @"
        INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason)
        VALUES ($ticker, $section, $version, $as_of, $model, 'pending', $prose, $source_ids, NULL);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, $model_calls, 0, '0', $detail);
    ";

    // A lane as the writer takes it: every name one figure 12.2 gives a prompt for,
    // and none twice. Checked before anything is asked, because a name nobody can
    // write refused halfway through a pass is a pass that spent the sections before
    // it and then stopped.
    public static IReadOnlyList<string> Checked(IReadOnlyList<string> lane)
    {
        var unknown = lane.Where(section => !SectionPrompt.Asks.ContainsKey(section)).ToArray();

        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"The local lane names {string.Join(", ", unknown.Select(section => $"'{section}'"))}, which figure 12.2 " +
                "names no section as. A lane is written in the figure's own names, because the section column holds " +
                "them and a name spelled differently is a section nothing would ever read back.");
        }

        var repeated = lane.GroupBy(section => section, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);

        if (repeated is not null)
        {
            throw new InvalidOperationException(
                $"The local lane names '{repeated.Key}' {repeated.Count()} times. One pass writes a section once.");
        }

        return lane;
    }

    public async Task<ProseOutcome> WriteAsync(
        string ticker,
        IReadOnlyDictionary<string, IReadOnlyList<StoredDocument>> documents,
        string runId,
        CancellationToken cancellation = default)
    {
        Checked(lane);

        var startedAt = clock.UtcNow;
        var asOf = clock.SessionDateAt(startedAt);

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var written = new List<WrittenSection>();
        var notWritten = new List<UnwrittenSection>();
        var skipped = new List<UnwrittenSection>();
        var calls = 0;

        var before = await SectionsHeldAsync(connection, ticker, cancellation);
        var facts = await FactsAsync(connection, ticker, asOf, cancellation);

        // Every call is built and measured before the first is made, so a section
        // the machine cannot hold is refused before the pass starts rather than
        // attempted after the others have spent their time.
        var planned = new List<(string Section, ModelRequest Request, IReadOnlyList<string> Ids, bool Retry, int Version)>();

        foreach (var section in lane)
        {
            if (facts is null)
            {
                notWritten.Add(new UnwrittenSection(section, NoFactsFile));

                continue;
            }

            var newest = await NewestAsync(connection, ticker, section, cancellation);
            var today = newest is { } found && found.AsOf == asOf;

            if (newest is { Status: "pending" })
            {
                skipped.Add(new UnwrittenSection(section, AwaitingTheChecker));

                continue;
            }

            if (today && newest!.Status == "accepted")
            {
                skipped.Add(new UnwrittenSection(section, WrittenToday));

                continue;
            }

            if (today && newest!.Status == "fallback")
            {
                skipped.Add(new UnwrittenSection(section, LeftOutToday));

                continue;
            }

            // The one retry: the newest version was refused today, so this draft is
            // told why.
            var retry = today && newest!.Status == "rejected";

            var handed = documents.TryGetValue(section, out var given) ? given : [];

            if (ClaimRules.IsResearched(section) && handed.Count == 0)
            {
                notWritten.Add(new UnwrittenSection(section, NothingHanded));

                continue;
            }

            // Only what admissibility admitted reaches the model. A section handed
            // nothing admitted is still inserted, empty and citing what it was
            // handed, so the checker leaves it out with the line that says no
            // admissible source was found rather than the writer deciding that.
            var admitted = handed.Where(document => document.Admitted).ToArray();

            var request = SectionPrompt.Request(
                settings.Model,
                ticker,
                section,
                facts,
                Prompted(admitted),
                retry ? newest!.Reason : null);

            if (ClaimRules.IsResearched(section) && admitted.Length == 0)
            {
                planned.Add((section, request with { Prompt = string.Empty }, [.. handed.Select(document => document.Id)], retry, (newest?.Version ?? 0) + 1));

                continue;
            }

            // A cause with no document inside any move has nothing a sentence could
            // rest on, which code knows before a call is made, so none is. Asked
            // anyway, the model wrote a paragraph saying why it could not write one,
            // and that paragraph cost a call and the section's one retry.
            // see: A cause of a move rests only on a document published inside that move
            if (string.Equals(section, ClaimRules.CauseSection, StringComparison.Ordinal)
                && SectionPrompt.MovesWithDocuments(facts, Prompted(admitted)).Count == 0)
            {
                notWritten.Add(new UnwrittenSection(section, NoDocumentInsideAMove));

                continue;
            }

            if (!SectionPrompt.Fits(request, settings.ContextTokens))
            {
                notWritten.Add(new UnwrittenSection(
                    section,
                    FormattableString.Invariant($"{CannotHold}: an estimated {SectionPrompt.EstimatedTokens(request)} prompt tokens and {OpenAiCompatibleModelFeed.AnswerTokens} for the answer against a context of {settings.ContextTokens}")));

                continue;
            }

            planned.Add((section, request, request.DocumentIds, retry, (newest?.Version ?? 0) + 1));
        }

        var unavailable = false;

        foreach (var (section, request, ids, retry, version) in planned)
        {
            if (unavailable)
            {
                notWritten.Add(new UnwrittenSection(section, Unavailable));

                continue;
            }

            string modelName;
            string prose;

            if (request.Prompt.Length == 0)
            {
                modelName = settings.Model;
                prose = string.Empty;
            }
            else
            {
                try
                {
                    calls++;

                    var answer = await model.CompleteAsync(request, cancellation).ConfigureAwait(false);

                    modelName = answer.Model;
                    prose = answer.Text;
                }
                catch (LocalModelUnavailable gone)
                {
                    // The runtime is not answering, so no later call will either, and
                    // waiting out a timeout per section would spend the evening on it.
                    unavailable = true;
                    notWritten.Add(new UnwrittenSection(section, $"{Unavailable}: {gone.Message}"));

                    continue;
                }
                catch (ProviderRefusal refused)
                {
                    notWritten.Add(new UnwrittenSection(section, refused.Message));

                    continue;
                }
            }

            await using var insert = connection.CreateCommand();

            insert.CommandText = Insert;
            insert.Parameters.AddWithValue("$ticker", ticker);
            insert.Parameters.AddWithValue("$section", section);
            insert.Parameters.AddWithValue("$version", version);
            insert.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$model", modelName);
            insert.Parameters.AddWithValue("$prose", prose);
            insert.Parameters.AddWithValue("$source_ids", JsonSerializer.Serialize(ids));

            await insert.ExecuteNonQueryAsync(cancellation);

            written.Add(new WrittenSection(section, version, modelName, retry));
        }

        var outcome = new ProseOutcome(ticker, asOf, written, notWritten, skipped, calls, unavailable);

        await using var record = connection.CreateCommand();

        record.CommandText = AppendRun;
        record.Parameters.AddWithValue("$run_id", runId);
        record.Parameters.AddWithValue("$stage", Stage);
        record.Parameters.AddWithValue("$started_at", Instant(startedAt));
        record.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        record.Parameters.AddWithValue("$outcome", unavailable ? "unavailable" : "ok");
        // Measured from the store rather than counted here, which is what SCHEMA says
        // the column holds: a stage's own count of what it wrote is its opinion.
        record.Parameters.AddWithValue("$rows_written", await SectionsHeldAsync(connection, ticker, cancellation) - before);
        record.Parameters.AddWithValue("$model_calls", calls);
        record.Parameters.AddWithValue("$detail", Detail(outcome));

        await record.ExecuteNonQueryAsync(cancellation);

        return outcome;
    }

    // The run log's detail, as JSON, which is what SCHEMA declares the column holds
    // and what lets the name page read back, for one name, which of its local lane's
    // sections were not written and why.
    public static string Detail(ProseOutcome outcome) =>
        JsonSerializer.Serialize(new
        {
            ticker = outcome.Ticker,
            asOf = outcome.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            written = outcome.Written.Select(section => new { section = section.Section, version = section.Version, model = section.Model, retry = section.Retry }),
            notWritten = outcome.NotWritten.Select(section => new { section = section.Section, reason = section.Reason }),
            skipped = outcome.Skipped.Select(section => new { section = section.Section, reason = section.Reason }),
            modelCalls = outcome.ModelCalls,
        });

    static async Task<long> SectionsHeldAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM research_section WHERE ticker = $ticker;";
        command.Parameters.AddWithValue("$ticker", ticker);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static PromptDocument[] Prompted(IEnumerable<StoredDocument> admitted) =>
        [.. admitted.Select(document => new PromptDocument(document.Id, document.Title, document.PublishedOn, document.Body!))];

    sealed record Newest(int Version, DateOnly AsOf, string Status, string? Reason);

    static async Task<Newest?> NewestAsync(SqliteConnection connection, string ticker, string section, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestVersion;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$section", section);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new Newest(
                reader.GetInt32(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3))
            : null;
    }

    static async Task<IReadOnlyList<Fact>?> FactsAsync(SqliteConnection connection, string ticker, DateOnly asOf, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = FactsFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync(cancellation) is string payload ? FactsFile.Read(payload) : null;
    }

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
