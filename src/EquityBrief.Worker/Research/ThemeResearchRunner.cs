using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

public sealed record ThemePassOutcome(
    string Theme,
    DateOnly AsOf,
    string Outcome,
    IReadOnlyList<WrittenSection> Written,
    IReadOnlyList<UnwrittenSection> NotWritten,
    int Fetched,
    int Admitted,
    IReadOnlyList<string> OffList,
    IReadOnlyList<string> Snippets,
    string? Reason)
{
    // The pages the theme's call was handed, each with its site and how densely it names the industry, which
    // the run log keeps so a pass that wrote nothing can be read for what it had to write from.
    public IReadOnlyList<HandedPage> Handed { get; init; } = [];
}

// One page a theme call was handed: its stored id, its site, its title and its mentions of the industry in every
// ten thousand characters.
public sealed record HandedPage(string Id, string Site, string Title, double Mentions);

// The theme research runner. One pass for one theme, which is one industry the index names.
//
// It researches where an industry's own prices are once, and every member the index names
// in that industry reads the one record it writes, so a paid pass is shared rather than
// bought per name. It searches each site on the industry list for the industry within a
// quarter's window, drops a result from a site the list does not carry and a result short of
// a document before anything is stored, tests every page it keeps for admissibility and
// stores it with its verdict as the per-name runner does, and has the spend cap make the one
// paid call over at most ten of the pages it admitted.
// see: Industry research is per theme, not per name
// see: A theme is the industry the index names for a member, and one theme pass serves every member it names
// see: A theme search is scoped by parameter, not by hope
// see: Every paid call is made through the spend cap, which holds each paid job's model
//
// A refresh is paid work serving members nobody opened, so it runs off-peak and never at
// peak, and a pass asked at peak says when the window opens and writes nothing.
// see: A theme refresh runs off-peak, and a name opened at peak is written without one
public sealed class ThemeResearchRunner(
    SpendCap cap,
    ClaimChecker checker,
    ISearchFeed search,
    IReadOnlyList<string> industryList,
    ResearchPricing pricing,
    IClock clock,
    string databaseFile,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? sectorSites = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ThemeSection, Touch.Read | Touch.Insert),
            new StoreTouch(Store.SourceDocument, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.SearchTool]);

    public const string Stage = "theme research";

    // What a theme pass came to, which the run log's outcome column holds.
    public const string Written = "ok";
    public const string NotWarranted = "not warranted";
    public const string AtPeak = "peak";
    public const string Unavailable = "unavailable";
    public const string Paused = "paused";
    public const string AlreadyRunning = "already running";

    // The reasons a theme's cycle is not written, stated once so the run log and the page
    // say them the same way.
    public const string WrittenToday = "a pass for the theme ran today, and a theme is researched once a day at most";
    public const string NothingFound = "the search returned no page from the industry list with its text";
    public const string ShortOfADocument = "short of a document";

    // Why a cycle is not written where every page the searches kept only mentions the industry.
    // see: A theme page is handed to the model only where its text names the industry
    public const string NoPageAboutTheIndustry = "the searches returned no page about the industry";

    // Why a cycle is not written where the model was handed pages about the industry and wrote nothing from them:
    // both answers empty, or a draft citing none of them. What the searches found is named with it, and the
    // sector where the source list carries no site of its own for it, which is what a review adds sites for.
    // see: An industry cycle the model declined is named as declined for lack of industry sources
    public const string Declined = "declined for lack of industry sources";

    public static string DeclinedLine(IReadOnlyList<StoredDocument> about, string industry, string? sector, bool sectorHasSites) =>
        FormattableString.Invariant($"{Declined}: the searches found {about.Count} page(s) about {industry}, from {Listed(about.Select(page => Site(page.Url)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())}, and the model wrote nothing from them")
        + NoSiteOfItsOwn(sector, sectorHasSites);

    public static string NoSiteOfItsOwn(string? sector, bool sectorHasSites) =>
        sector is { Length: > 0 } && !sectorHasSites ? "; the source list carries no site of its own for the " + sector + " sector" : string.Empty;

    // A page's site as the list names sites: its host without the leading www.
    public static string Site(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var address)
            ? address.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? address.Host[4..] : address.Host
            : url;

    static string Listed(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Concat(names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    static readonly System.Text.RegularExpressions.Regex Marker = new(@"\[D\d+\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The sites a theme search reads for a member of a sector: the list's own, and the sector's where it has any.
    // see: A theme search adds its sector's sites, and a site joins the list only where a measurement found industry material on it
    IReadOnlyList<string> SitesFor(string? sector) =>
        sector is not null && sectorSites?.TryGetValue(sector, out var own) == true
            ? [.. industryList.Concat(own).Distinct(StringComparer.OrdinalIgnoreCase)]
            : industryList;

    bool SectorHasSites(string? sector) => sector is not null && sectorSites?.TryGetValue(sector, out var own) == true && own.Count > 0;

    const string NewestCycle = @"
        SELECT version, as_of, status, reject_reason, prose, source_ids
        FROM theme_section
        WHERE theme = $theme AND section = $section
        ORDER BY version DESC
        LIMIT 1;
    ";

    // A pass for this theme that ran to the end on this session, whether or not it found
    // anything to write from. Read off the detail with the store's own JSON function, as the
    // per-name runner reads its own.
    const string RanOnSession = @"
        SELECT COUNT(*) FROM run_log
        WHERE stage = $stage
          AND outcome = $written
          AND CASE WHEN json_valid(detail) THEN json_extract(detail, '$.theme') END = $theme
          AND CASE WHEN json_valid(detail) THEN json_extract(detail, '$.asOf') END = $as_of;
    ";

    const string InsertDocument = @"
        INSERT INTO source_document (id, url, title, published_on, fetched_at, body, admissibility)
        VALUES ($id, $url, $title, $published_on, $fetched_at, $body, $admissibility)
        ON CONFLICT (id) DO NOTHING;
    ";

    const string StoredDocument = @"
        SELECT id, url, title, published_on, fetched_at, body, admissibility
        FROM source_document
        WHERE id = $id;
    ";

    const string InsertSection = @"
        INSERT INTO theme_section (theme, section, version, as_of, model, status, prose, source_ids, reject_reason, industries)
        VALUES ($theme, $section, $version, $as_of, $model, 'pending', $prose, $source_ids, NULL, $industries);
    ";

    const string DocumentsHeld = @"
        SELECT COUNT(*) FROM source_document;
    ";

    const string SectionsHeld = @"
        SELECT COUNT(*) FROM theme_section WHERE theme = $theme;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<ThemePassOutcome> RunAsync(string theme, string runId, CancellationToken cancellation = default, string? sector = null)
    {
        var industrySites = SitesFor(sector);
        var startedAt = clock.UtcNow;
        var asOf = clock.SessionDateAt(startedAt);
        var requestsBefore = search.Requests + cap.Probes;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // One pass a theme at a time, for the per-name runner's reason: two members of one
        // industry opened together would each buy the same record.
        await using var held = Hold(theme);

        if (held is null)
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(theme, asOf, AlreadyRunning, "a pass for this theme is already running"), 0, 0, cancellation);
        }

        // Researched once a day at most. A cycle waiting on the checker, accepted today or left
        // out today is not bought again today, and a pass that ran to the end today and found
        // nothing to write from is not searched again today, so a second member opened the
        // same evening reads what the first one's pass came to.
        if ((await NewestAsync(connection, theme, cancellation) is { } newest
                && newest.AsOf == asOf
                && newest.Status is ClaimChecker.Pending or ClaimChecker.Accepted or ClaimChecker.Fallback)
            || await RanTodayAsync(connection, theme, asOf, cancellation))
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(theme, asOf, NotWarranted, WrittenToday), 0, 0, cancellation);
        }

        // At peak the pass does not start, and says when the window opens.
        // see: Queued work runs off-peak, and every schedule is written in UTC
        if (pricing.IsPeak(startedAt))
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(theme, asOf, AtPeak, PeakLine(pricing.OffPeakFrom(startedAt))), 0, 0, cancellation);
        }

        // A pass that cannot make its one paid call does not search.
        // see: A research pass does not start where the research model does not answer
        if (await cap.UnreachableAsync(cancellation) is { } unreachable)
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(theme, asOf, Unavailable, unreachable), 0, search.Requests + cap.Probes - requestsBefore, cancellation);
        }

        // One search a site, every one made before anything is stored.
        // see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return
        var queries = ThemeSearch.For(theme, asOf, industrySites);
        var answers = new List<SearchAnswer>();

        // A tool that does not answer, or refuses, leaves the stored record as it was: the
        // pass has not started, so nothing it would write is half written, and the searches
        // after the one that failed are not made.
        try
        {
            foreach (var query in queries)
            {
                answers.Add(await search.SearchAsync(query, cancellation));
            }
        }
        catch (Exception refused) when (refused is SearchToolUnavailable or ProviderRefusal)
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(theme, asOf, Unavailable, refused.Message), 0, search.Requests + cap.Probes - requestsBefore, cancellation);
        }

        var answer = ThemeSearch.Merged(answers);
        var intake = ThemeSearch.Of(answer, industrySites);
        var documents = SourceDocuments.Of(intake.Fetched, asOf.AddMonths(-ThemeSearch.WindowMonths), asOf, startedAt);
        var documentsBefore = await CountAsync(connection, DocumentsHeld, null, cancellation);
        var sectionsBefore = await CountAsync(connection, SectionsHeld, theme, cancellation);

        var stored = new List<StoredDocument>();

        // Stored in one write, as a name's pass stores its documents.
        // see: A writer waits up to ten minutes for another, and a pass stores what it fetched in one write
        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var row in documents.Rows)
            {
                stored.Add(await StoreAsync(connection, transaction, row, cancellation));
            }

            await transaction.CommitAsync(cancellation);
        }

        var written = new List<WrittenSection>();
        var notWritten = new List<UnwrittenSection>();
        var handed = new List<HandedPage>();
        string? stopped = null;

        if (stored.Count == 0)
        {
            notWritten.Add(new UnwrittenSection(ClaimRules.CycleSection, NothingStored(answer, intake) + NoSiteOfItsOwn(sector, SectorHasSites(sector))));
        }
        else
        {
            stopped = await WriteAsync(connection, theme, sector, asOf, stored, null, runId, null, written, notWritten, handed, cancellation);

            // Under the theme's own stage, since a name's pass runs this one inside its run and
            // checks its own sections after it.
            if (written.Count > 0)
            {
                await checker.RunForThemeAsync(runId, cancellation: cancellation);
            }

            // The retries, each told why the draft before it was refused, until one is accepted or the
            // checker leaves the section out at its last retry.
            for (var round = 2; stopped is null && round <= 1 + checker.RetriesAllowed; round++)
            {
                if (await NewestAsync(connection, theme, cancellation) is not { Status: ClaimChecker.Rejected } refused || refused.AsOf != asOf)
                {
                    break;
                }

                var before = written.Count;

                // The retry names each thing the draft before it was refused for, read again against no facts
                // file, since a theme has none, and the pages the pass stored.
                // see: A retry names each thing the check refused, and a section refused on its third retry is left out
                var brief = RetryBrief.For(ClaimRules.CycleSection, refused.Prose, [], ResearchRunner.Resolved(refused.SourceIds, stored), null, refused.Reason);

                stopped = await WriteAsync(connection, theme, sector, asOf, stored, brief, runId, ResearchRunner.Round(round), written, notWritten, handed, cancellation);

                if (written.Count == before)
                {
                    break;
                }

                await checker.RunForThemeAsync(runId, ResearchRunner.Round(round), cancellation);
            }
        }

        var outcome = new ThemePassOutcome(
            theme,
            asOf,
            stopped is null ? Written : Paused,
            written,
            notWritten,
            stored.Count,
            stored.Count(document => document.Admitted),
            intake.OffList,
            intake.Snippets,
            stopped)
        {
            Handed = [.. handed.DistinctBy(page => page.Id)],
        };

        var rows = await CountAsync(connection, DocumentsHeld, null, cancellation) - documentsBefore
            + await CountAsync(connection, SectionsHeld, theme, cancellation) - sectionsBefore;

        return await RecordAsync(connection, runId, startedAt, outcome, rows, search.Requests + cap.Probes - requestsBefore, cancellation, documents.Detail);
    }

    // Why nothing could be written from a search: it returned nothing from the list, or
    // everything it returned was dropped before it could be stored, which section 18 says
    // leaves the section absent with its reason.
    public static string NothingStored(SearchAnswer answer, ThemeIntake intake) =>
        answer.Results.Count == 0
            ? NothingFound
            : FormattableString.Invariant(
                $"the search returned {answer.Results.Count} result(s), {intake.Snippets.Count} {ShortOfADocument} and {intake.OffList.Count} from a site the industry list does not carry, and nothing that could be stored");

    // What the run log and a name's page say where a refresh waits for off-peak.
    public static string PeakLine(DateTimeOffset opens) =>
        "research rates are at peak, and a theme refresh runs off-peak, from " +
        opens.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    // The cycle, written once: nothing admitted is inserted empty citing what was kept, so
    // the checker leaves it out saying no admissible source was found and nothing is paid
    // for, and otherwise the spend cap makes the call. The cap's line where it stopped.
    async Task<string?> WriteAsync(
        SqliteConnection connection,
        string theme,
        string? sector,
        DateOnly asOf,
        IReadOnlyList<StoredDocument> stored,
        string? refusedBecause,
        string runId,
        string? round,
        List<WrittenSection> written,
        List<UnwrittenSection> notWritten,
        List<HandedPage> handed,
        CancellationToken cancellation)
    {
        var version = ((await NewestAsync(connection, theme, cancellation))?.Version ?? 0) + 1;
        var kept = stored.Where(document => document.Admitted).ToArray();
        var admitted = kept.Where(document => ThemeSearch.AboutTheIndustry(document, theme)).ToArray();

        // Pages admitted and none about the industry: nothing is asked and nothing is paid for.
        if (kept.Length > 0 && admitted.Length == 0)
        {
            notWritten.Add(new UnwrittenSection(ClaimRules.CycleSection, NoPageAboutTheIndustry + NoSiteOfItsOwn(sector, SectorHasSites(sector))));

            return null;
        }

        handed.AddRange(admitted.Take(ThemeSearch.MostPages).Select(page => new HandedPage(page.Id, Site(page.Url), page.Title, Math.Round(ThemeSearch.Mentions(page, theme), 2))));

        if (admitted.Length == 0)
        {
            await InsertAsync(connection, theme, version, asOf, cap.Model, string.Empty, [.. stored.Select(document => document.Id)], cancellation);
            written.Add(new WrittenSection(ClaimRules.CycleSection, version, cap.Model, refusedBecause is not null));

            return null;
        }

        var request = SectionPrompt.ThemeRequest(cap.Model, theme, ThemeSearch.Handed(admitted), refusedBecause);

        var call = await cap.AskAsync(request, runId, round, cancellation);

        // An answer that arrived empty or cut short is asked for once more, as a name's pass
        // asks, under a stage of its own so both calls and what each cost stand on the run log.
        // see: An answer that comes back empty or cut short is asked for once more
        if (call.Unusable)
        {
            call = await cap.AskAsync(request, runId, ResearchRunner.AskedAgain(round), cancellation);
        }

        if (call.Paused)
        {
            notWritten.Add(new UnwrittenSection(ClaimRules.CycleSection, call.Verdict.Line));

            return call.Verdict.Line;
        }

        // Handed pages about the industry and answered with nothing twice, or with a draft citing none of them: the
        // model declined, which is named for what the searches found rather than as an empty answer, and a draft
        // citing nothing is not stored or asked for again, since a retry repeated the decline each time it ran.
        // see: An industry cycle the model declined is named as declined for lack of industry sources
        if (call.Unusable || call.Answer is { } uncited && !Marker.IsMatch(uncited.Text))
        {
            notWritten.Add(new UnwrittenSection(ClaimRules.CycleSection, DeclinedLine(admitted, theme, sector, SectorHasSites(sector))));

            return null;
        }

        if (call.Answer is not { } answer)
        {
            notWritten.Add(new UnwrittenSection(ClaimRules.CycleSection, call.Failure ?? "the research model returned nothing"));

            return null;
        }

        await InsertAsync(connection, theme, version, asOf, cap.Model, answer.Text, request.DocumentIds, cancellation);
        written.Add(new WrittenSection(ClaimRules.CycleSection, version, cap.Model, refusedBecause is not null));

        return null;
    }

    FileStream? Hold(string theme)
    {
        var path = databaseFile + ".theme-" + SourceDocuments.Id(theme) + ".lock";

        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    static ThemePassOutcome Outcome(string theme, DateOnly asOf, string outcome, string reason) =>
        new(theme, asOf, outcome, [], [], 0, 0, [], [], reason);

    async Task<ThemePassOutcome> RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        ThemePassOutcome outcome,
        long rows,
        int requests,
        CancellationToken cancellation,
        string? documents = null)
    {
        await using var record = connection.CreateCommand();

        record.CommandText = AppendRun;
        record.Parameters.AddWithValue("$run_id", runId);
        record.Parameters.AddWithValue("$stage", Stage);
        record.Parameters.AddWithValue("$started_at", Instant(startedAt));
        record.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        record.Parameters.AddWithValue("$outcome", outcome.Outcome);
        record.Parameters.AddWithValue("$rows_written", rows);
        record.Parameters.AddWithValue("$network_requests", requests);
        record.Parameters.AddWithValue("$detail", Detail(outcome, documents));

        await record.ExecuteNonQueryAsync(cancellation);

        return outcome;
    }

    // The run log's detail, as JSON. A result dropped for its site is named by its domain
    // and a result short of a document by its address, which is what section 18 says the
    // run log records for each; both are the results' own addresses, never the request's.
    public static string Detail(ThemePassOutcome outcome, string? documents = null) =>
        JsonSerializer.Serialize(new
        {
            theme = outcome.Theme,
            asOf = outcome.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            outcome = outcome.Outcome,
            reason = outcome.Reason,
            written = outcome.Written.Select(section => new { section = section.Section, version = section.Version, model = section.Model, retry = section.Retry }),
            notWritten = outcome.NotWritten.Select(section => new { section = section.Section, reason = section.Reason }),
            fetched = outcome.Fetched,
            admitted = outcome.Admitted,
            offList = outcome.OffList,
            shortOfADocument = outcome.Snippets,
            documents,
            handed = outcome.Handed.Select(page => new { id = page.Id, site = page.Site, title = page.Title, mentions = page.Mentions }),
        });

    async Task<StoredDocument> StoreAsync(SqliteConnection connection, SqliteTransaction transaction, StoredDocument row, CancellationToken cancellation)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = InsertDocument;
            insert.Parameters.AddWithValue("$id", row.Id);
            insert.Parameters.AddWithValue("$url", row.Url);
            insert.Parameters.AddWithValue("$title", row.Title);
            insert.Parameters.AddWithValue("$published_on", row.PublishedOn is { } on ? on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
            insert.Parameters.AddWithValue("$fetched_at", Instant(row.FetchedAt));
            insert.Parameters.AddWithValue("$body", (object?)row.Body ?? DBNull.Value);
            insert.Parameters.AddWithValue("$admissibility", row.Admissibility);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        // The row as stored, which is the verdict taken the first time the address was
        // fetched where an earlier pass stored it.
        await using var read = connection.CreateCommand();

        read.Transaction = transaction;
        read.CommandText = StoredDocument;
        read.Parameters.AddWithValue("$id", row.Id);

        await using var reader = await read.ExecuteReaderAsync(cancellation);

        await reader.ReadAsync(cancellation);

        return new StoredDocument(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6));
    }

    async Task InsertAsync(
        SqliteConnection connection,
        string theme,
        int version,
        DateOnly asOf,
        string model,
        string prose,
        IReadOnlyList<string> sources,
        CancellationToken cancellation)
    {
        await using var insert = connection.CreateCommand();

        insert.CommandText = InsertSection;
        insert.Parameters.AddWithValue("$theme", theme);
        insert.Parameters.AddWithValue("$section", ClaimRules.CycleSection);
        insert.Parameters.AddWithValue("$version", version);
        insert.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$model", model);
        insert.Parameters.AddWithValue("$prose", prose);
        insert.Parameters.AddWithValue("$source_ids", JsonSerializer.Serialize(sources));

        // The industries that map to the theme, which is the one industry the theme is
        // until a decision folds several into one.
        insert.Parameters.AddWithValue("$industries", JsonSerializer.Serialize(new[] { theme }));

        await insert.ExecuteNonQueryAsync(cancellation);
    }

    public sealed record Newest(int Version, DateOnly AsOf, string Status, string? Reason, string Prose = "", string SourceIds = "[]");

    public static async Task<Newest?> NewestAsync(SqliteConnection connection, string theme, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestCycle;
        command.Parameters.AddWithValue("$theme", theme);
        command.Parameters.AddWithValue("$section", ClaimRules.CycleSection);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new Newest(
                reader.GetInt32(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5))
            : null;
    }

    static async Task<bool> RanTodayAsync(SqliteConnection connection, string theme, DateOnly asOf, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = RanOnSession;
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$written", Written);
        command.Parameters.AddWithValue("$theme", theme);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) > 0;
    }

    static async Task<long> CountAsync(SqliteConnection connection, string sql, string? theme, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        if (theme is not null)
        {
            command.Parameters.AddWithValue("$theme", theme);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
