using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// What an open asked of a pass.
//
// `PaidForLocal` is the page's option to have the paid model write the local lane's
// sections, which section 18 offers where the local model is unavailable or cannot
// hold a section: for this pass the local lane is empty and every section is paid.
public sealed record ResearchPassRequest(bool Refresh = false, bool PaidForLocal = false);

public sealed record ResearchPassOutcome(
    string Ticker,
    DateOnly AsOf,
    string Outcome,
    ResearchState State,
    IReadOnlyList<string> Warranted,
    IReadOnlyList<WrittenSection> Written,
    IReadOnlyList<UnwrittenSection> NotWritten,
    int Fetched,
    int Admitted,
    string? Reason,
    IReadOnlyList<string>? LeftForThePaidPath = null);

// The research runner. One pass for one name, when the name is opened and asked.
//
// It decides nothing a component already decides. The staleness judge says whether
// the stored research stands; the prose writer writes the local lane; the spend cap
// makes every paid call and refuses one that would pass a cap; the claim checker
// moves every section a pass wrote. What the runner owns is the order, the documents,
// and what each lane is handed: it fetches the name's own filing and its own news,
// tests every document as it arrives and stores it with the verdict, and hands each
// section the documents code picks for it.
// see: Everything expensive happens when a name is opened
// see: The model never fetches; components fetch and hand it documents
// see: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first
// see: Every paid call is made through the spend cap, which holds the research model
//
// One pass, one run id, however many rounds it takes. A section refused on its first
// draft is written once more inside the same pass, and figure 12.2's short version is
// written after the sections it summarises have been checked, so a pass runs up to
// three rounds of writing and checking, each stage of each round a row of its own
// under the one run. Opening a name twice runs one pass, because the second open finds
// every section it would write already written today.
//
// The industry cycle is not one of the name's own sections. It is the theme's, written once
// for the industry the index names the member in and read by every member of it, so where
// the name's cycle is missing or stale the pass has the theme research runner refresh the
// theme first, and a theme that could not be refreshed leaves the cycle named as not written
// with its reason and every other section written.
// see: Industry research is per theme, not per name
// see: A theme is the industry the index names for a member, and one theme pass serves every member it names
public sealed class ResearchRunner(
    StalenessJudge judge,
    Func<IReadOnlyList<string>, ProseWriter> writerFor,
    SpendCap cap,
    ClaimChecker checker,
    ThemeResearchRunner themes,
    IFilingsArchiveFeed archive,
    INameNewsFeed news,
    IReadOnlyList<string> localLane,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.Fundamentals, Touch.Read),
            new StoreTouch(Store.ResearchSection, Touch.Read | Touch.Insert),
            new StoreTouch(Store.ThemeSection, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.FilingsArchive, Feed.News]);

    public const string Stage = "research";

    // What a pass came to, which the run log's outcome column holds and the name page
    // reads back.
    public const string Written = "ok";
    public const string NotWarranted = "not warranted";
    public const string Unavailable = "unavailable";
    public const string Paused = "paused";
    public const string NoFactsFile = "no facts file";
    public const string AlreadyRunning = "already running";

    // The reasons the industry cycle is not written, stated once so the run log and the
    // page say them the same way.
    public const string NoIndustry = "the index names no industry for the name, and a theme is an industry";
    public const string ThemeNotRefreshed = "the theme could not be refreshed: ";

    // Why a plain open on the day a pass ran starts nothing.
    public const string RanToday = "a research pass for this name already ran today, and opening it again writes nothing until a later day or a rewrite";

    // The window a document a pass keeps may be dated inside, which is the stored year: a
    // move's cause rests on a document inside that move, and no stored move is older than the
    // bars kept. The news a pass asks for is narrower, the windows the rule hands a section
    // from, which 6.11 found a large company's year needed.
    // see: One year of bars, and no more
    public const int WindowYears = 1;

    // The rounds a pass runs, each a stage suffix on the rows its components write.
    public const string SecondRound = "round 2";
    public const string ThirdRound = "round 3";

    const string FactsFor = @"
        SELECT payload, session_date FROM facts
        WHERE ticker = $ticker AND session_date <= $as_of AND payload != ''
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    const string NewestFiling = @"
        SELECT payload FROM fundamentals
        WHERE ticker = $ticker
        ORDER BY filing_date DESC
        LIMIT 1;
    ";

    const string NewestVersions = @"
        SELECT r.section, r.version, r.as_of, r.status, r.reject_reason, r.prose
        FROM research_section r
        WHERE r.ticker = $ticker
          AND r.version = (SELECT MAX(s.version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section);
    ";

    // The industry the index last named for the member, which is the theme its cycle is
    // read under.
    const string IndustryFor = @"
        SELECT industry FROM membership
        WHERE ticker = $ticker AND industry IS NOT NULL
        ORDER BY observed_at DESC
        LIMIT 1;
    ";

    // The theme's newest industry cycle, which is what the name's cycle is judged by.
    const string NewestThemeCycle = @"
        SELECT version, as_of, status, reject_reason, prose
        FROM theme_section
        WHERE theme = $theme AND section = $section
        ORDER BY version DESC
        LIMIT 1;
    ";

    // A pass for this name that ran to the end on this session. The name is inside the
    // detail rather than in a column, so it is read with the store's own JSON function,
    // and a detail that is not JSON is passed over, since other stages write a sentence.
    const string RanOnSession = @"
        SELECT COUNT(*) FROM run_log
        WHERE stage = $stage
          AND outcome = $written
          AND CASE WHEN json_valid(detail) THEN json_extract(detail, '$.ticker') END = $ticker
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
        INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason)
        VALUES ($ticker, $section, $version, $as_of, $model, 'pending', $prose, $source_ids, NULL);
    ";

    const string DocumentsHeld = @"
        SELECT COUNT(*) FROM source_document;
    ";

    const string PaidSectionsHeld = @"
        SELECT COUNT(*) FROM research_section WHERE ticker = $ticker AND model = $model;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<ResearchPassOutcome> RunAsync(
        string ticker,
        string runId,
        ResearchPassRequest? asked = null,
        CancellationToken cancellation = default)
    {
        asked ??= new ResearchPassRequest();

        var startedAt = clock.UtcNow;
        var asOf = clock.SessionDateAt(startedAt);

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        // One pass a name at a time. A second press while a pass runs is refused by
        // name rather than run beside it, because two passes over one name would each
        // ask for the same sections and pay for both.
        await using var held = Hold(ticker);

        if (held is null)
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(ticker, asOf, AlreadyRunning, ResearchState.Missing, [], "a pass for this name is already running"), 0, 0, cancellation);
        }

        var verdict = await judge.JudgeAsync(ticker, asked.Refresh, runId, cancellation);

        // A plain open on the day a pass for the name ran to the end starts nothing. The
        // rule below makes a second open free only where every section the first pass
        // warranted got a row, and a section that pass found nothing to write from has
        // none, so a second open would fetch the year's news and the release again to
        // find the same nothing. A pass the cap paused or the model did not answer did
        // not run to the end, and the page's two explicit asks run because each asks for
        // something the first pass did not do.
        // see: A name opened again on the day its research pass ran starts no second pass unless the page asks for one
        if (!asked.Refresh && !asked.PaidForLocal && await RanTodayAsync(connection, ticker, asOf, cancellation))
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(ticker, asOf, NotWarranted, verdict.State, [], RanToday), 0, 0, cancellation);
        }

        var newest = await NewestAsync(connection, ticker, cancellation);
        var industry = await IndustryAsync(connection, ticker, cancellation);

        var notWritten = new List<UnwrittenSection>();
        var warranted = new List<string>();
        var themeWanted = false;

        foreach (var section in ClaimRules.Sections)
        {
            // The industry cycle is judged by the theme's newest version, and it is never one
            // of the name's own sections: a theme written today by another member of the
            // industry is not bought again.
            if (string.Equals(section, ClaimRules.CycleSection, StringComparison.Ordinal))
            {
                if (industry is null)
                {
                    notWritten.Add(new UnwrittenSection(section, NoIndustry));
                }
                else if (ThemeWarranted(await NewestThemeAsync(connection, industry, cancellation), verdict, asOf))
                {
                    themeWanted = true;
                }

                continue;
            }

            if (Warranted(section, newest.GetValueOrDefault(section), verdict, asOf))
            {
                warranted.Add(section);
            }
        }

        if (warranted.Count == 0 && !themeWanted)
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(ticker, asOf, NotWarranted, verdict.State, [], verdict.Line, notWritten), 0, 0, cancellation);
        }

        // What the pass's row says it warranted, in figure 12.2's order: the name's own
        // sections, and the cycle where the theme it is read under wanted a refresh.
        IReadOnlyList<string> recorded = themeWanted
            ? [.. ClaimRules.Sections.Where(section => section == ClaimRules.CycleSection || warranted.Contains(section, StringComparer.Ordinal))]
            : warranted;

        var local = asked.PaidForLocal ? [] : warranted.Where(section => localLane.Contains(section, StringComparer.Ordinal)).ToList();
        var paid = warranted.Except(local, StringComparer.Ordinal).ToList();

        // A pass whose paid lane has work asks whether the research model answers before
        // it fetches anything, and does not start where it does not. That includes the
        // theme, which is part of the pass.
        // see: A research pass does not start where the research model does not answer
        if (paid.Count > 0 && await cap.UnreachableAsync(cancellation) is { } unreachable)
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(ticker, asOf, Unavailable, verdict.State, recorded, unreachable, notWritten), 0, cap.Probes, cancellation);
        }

        // The theme next, where that is what went stale, and before the name's own documents,
        // so a name's other sections do not wait on a theme that fails: a theme that could not
        // be refreshed leaves its record as it was and the name's cycle named as not written
        // with why. Its probes and its search are on its own row, not this one.
        // see: Industry research is per theme, not per name
        var themeProbes = 0;

        if (themeWanted)
        {
            var probesBefore = cap.Probes;
            var themed = await themes.RunAsync(industry!, runId, cancellation);

            themeProbes = cap.Probes - probesBefore;

            var stands = await NewestThemeAsync(connection, industry!, cancellation);

            if (stands is not { Status: ClaimChecker.Accepted } || stands.AsOf != asOf)
            {
                notWritten.Add(new UnwrittenSection(ClaimRules.CycleSection, ThemeNotRefreshed + ThemeReason(themed, stands, asOf)));
            }

            if (warranted.Count == 0)
            {
                return await RecordAsync(connection, runId, startedAt, Outcome(ticker, asOf, Written, verdict.State, recorded, null, notWritten), 0, cap.Probes - themeProbes, cancellation);
            }
        }

        var (facts, night) = await FactsAsync(connection, ticker, asOf, cancellation);

        if (facts is null)
        {
            return await RecordAsync(connection, runId, startedAt, Outcome(ticker, asOf, NoFactsFile, verdict.State, recorded, "no facts file is stored for the name on or before today", notWritten), 0, cap.Probes - themeProbes, cancellation);
        }

        var documentsBefore = await CountAsync(connection, DocumentsHeld, null, null, cancellation);
        var paidBefore = await CountAsync(connection, PaidSectionsHeld, ticker, cap.Model, cancellation);

        // ---- the documents ----

        var from = asOf.AddYears(-WindowYears);
        var fetched = new List<(FetchedDocument Document, int Symbols)>();
        var unread = new List<string>();

        // The company's own filing first, because the news the sections built across the
        // evidence are handed is the news published since it.
        string? ownFiling = null;
        (FetchedDocument Document, int Symbols)? filing = null;

        if (await CikAsync(connection, ticker, cancellation) is { } cik)
        {
            try
            {
                if ((await archive.FilingsAsync(ticker, cik, cancellation)).Release is { } release)
                {
                    filing = (
                        new FetchedDocument(DocumentChannel.FilingsArchive, release.Url, "Results release, " + release.Document, release.FiledOn, release.Text),
                        Evidence.OwnFiling);
                    ownFiling = SourceDocuments.Id(release.Url);
                }
            }
            catch (ProviderRefusal refused)
            {
                unread.Add("filings archive: " + refused.Message);
            }
        }
        else
        {
            unread.Add("filings archive: no CIK is stored for the name, which the fundamentals fetch writes");
        }

        // The news inside each stored move and since the filing, overlapping spans once, and
        // never the stored year: a window the provider has more of than a query reads is named
        // as unread and the others are still read.
        // see: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (windowFrom, windowTo) in NewsWindows.For(MoveWindows.In(facts), filing?.Document.PublishedOn, asOf))
        {
            try
            {
                foreach (var article in await news.ArticlesAsync(ticker, windowFrom, windowTo, cancellation))
                {
                    if (seen.Add(article.Url))
                    {
                        fetched.Add((
                            new FetchedDocument(DocumentChannel.NewsFeed, article.Url, article.Title, DateOnly.FromDateTime(article.Published.UtcDateTime), article.Text),
                            Math.Max(1, article.Symbols.Count)));
                    }
                }
            }
            catch (ProviderRefusal refused)
            {
                unread.Add("news: " + refused.Message);
            }
        }

        if (filing is { } own)
        {
            fetched.Add(own);
        }

        var intake = SourceDocuments.Of([.. fetched.Select(one => one.Document)], from, asOf, startedAt);
        var evidence = new List<EvidenceDocument>();

        for (var index = 0; index < intake.Rows.Count; index++)
        {
            evidence.Add(new EvidenceDocument(await StoreAsync(connection, intake.Rows[index], cancellation), fetched[index].Symbols));
        }

        var handed = Evidence.ForSections(facts, evidence, ownFiling);
        var written = new List<WrittenSection>();
        var leftForThePaidPath = new List<string>();

        // ---- round one: every section but the short version ----

        var summary = ClaimRules.Sections[^1];
        var firstLocal = local.Where(section => section != summary).ToArray();

        if (firstLocal.Length > 0)
        {
            var prose = await writerFor(firstLocal).WriteAsync(ticker, handed, runId, cancellation: cancellation);

            written.AddRange(prose.Written);

            foreach (var left in prose.NotWritten)
            {
                // A section the machine cannot hold is left for the paid path, in this
                // pass, which is what section 18 says happens to it. The writer's own
                // row names the section and the reason; this pass writes it.
                if (left.Reason.StartsWith(ProseWriter.CannotHold, StringComparison.Ordinal))
                {
                    paid.Add(left.Section);
                    local.Remove(left.Section);
                    leftForThePaidPath.Add(left.Section);
                }
                else
                {
                    notWritten.Add(left);
                }
            }
        }

        var stopped = await PaidAsync(connection, ticker, asOf, night, facts!, handed, [.. paid.Where(section => section != summary)], null, runId, null, written, notWritten, cancellation);
        var paused = stopped is not null;

        if (written.Count > 0)
        {
            await checker.RunAsync(runId, cancellation: cancellation);
        }

        // ---- round two: the one retry, and the short version from what was accepted ----

        var afterFirst = await NewestAsync(connection, ticker, cancellation);

        bool RejectedToday(string section) =>
            afterFirst.GetValueOrDefault(section) is { Status: ClaimChecker.Rejected } refused && refused.AsOf == asOf;

        var retryLocal = local.Where(section => section != summary && RejectedToday(section)).ToArray();
        var retryPaid = paid.Where(section => section != summary && RejectedToday(section)).ToArray();
        var summaryWarranted = warranted.Contains(summary, StringComparer.Ordinal);
        var beforeSecond = written.Count;

        // The local lane's round is one call of the writer, since the run log holds one
        // row a stage for a run: the retries, and the short version where the lane holds it.
        // The writer hands it no sections, which is the paid lane's alone to do.
        string[] secondLocal = summaryWarranted && local.Contains(summary, StringComparer.Ordinal)
            ? [.. retryLocal, summary]
            : retryLocal;

        if (secondLocal.Length > 0)
        {
            var prose = await writerFor(secondLocal).WriteAsync(ticker, handed, runId, SecondRound, cancellation);

            written.AddRange(prose.Written);
            notWritten.AddRange(prose.NotWritten);
        }

        if (!paused)
        {
            stopped = await PaidAsync(connection, ticker, asOf, night, facts!, handed, retryPaid, afterFirst, runId, SecondRound, written, notWritten, cancellation);
            paused = stopped is not null;
        }

        if (summaryWarranted && !paused && !local.Contains(summary, StringComparer.Ordinal))
        {
            stopped = await PaidAsync(connection, ticker, asOf, night, facts!, handed, [summary], null, runId, SecondRound, written, notWritten, cancellation, Accepted(afterFirst, summary));
            paused = stopped is not null;
        }

        if (written.Count > beforeSecond)
        {
            await checker.RunAsync(runId, SecondRound, cancellation);
        }

        // ---- round three: the short version's one retry ----

        if (summaryWarranted && !paused)
        {
            var afterSecond = await NewestAsync(connection, ticker, cancellation);

            if (afterSecond.GetValueOrDefault(summary) is { Status: ClaimChecker.Rejected } refused && refused.AsOf == asOf)
            {
                var beforeThird = written.Count;

                if (local.Contains(summary, StringComparer.Ordinal))
                {
                    var prose = await writerFor([summary]).WriteAsync(ticker, handed, runId, ThirdRound, cancellation);

                    written.AddRange(prose.Written);
                    notWritten.AddRange(prose.NotWritten);
                }
                else
                {
                    stopped = await PaidAsync(connection, ticker, asOf, night, facts!, handed, [summary], afterSecond, runId, ThirdRound, written, notWritten, cancellation, Accepted(afterSecond, summary));
                    paused = stopped is not null;
                }

                if (written.Count > beforeThird)
                {
                    await checker.RunAsync(runId, ThirdRound, cancellation);
                }
            }
        }

        // A cap that stopped the paid lane stopped every paid section after it, the short
        // version included where the pass never reached it, and each is named with the line.
        if (stopped is not null)
        {
            foreach (var section in paid.Where(section =>
                !written.Any(one => one.Section == section) && !notWritten.Any(one => one.Section == section)))
            {
                notWritten.Add(new UnwrittenSection(section, stopped));
            }
        }

        var outcome = new ResearchPassOutcome(
            ticker,
            asOf,
            paused ? Paused : Written,
            verdict.State,
            recorded,
            written,
            notWritten,
            intake.Rows.Count,
            intake.Admitted.Count,
            unread.Count == 0 ? null : string.Join("; ", unread),
            leftForThePaidPath);

        // Measured off the store: the documents this pass stored and the sections the
        // paid lane inserted. The prose writer's rows are on its own row.
        var rows = await CountAsync(connection, DocumentsHeld, null, null, cancellation) - documentsBefore
            + await CountAsync(connection, PaidSectionsHeld, ticker, cap.Model, cancellation) - paidBefore;

        return await RecordAsync(connection, runId, startedAt, outcome, rows, news.Requests + archive.Requests + cap.Probes - themeProbes, cancellation, intake.Detail);
    }

    // Whether this pass writes a section, from its newest version and the judge's
    // verdict. A section waiting on the checker, accepted today, or left out today is
    // not written again today, so a second open of a name costs nothing; an accepted
    // section is written again where the judge names it stale; anything refused, or
    // left out on an earlier day, or never written, is written.
    //
    // Over the judge's own standings from 6.10, so the overnight queue asks the question
    // this pass asks rather than a second statement of it.
    public static bool Warranted(string section, SectionStanding? newest, StalenessVerdict verdict, DateOnly asOf) =>
        newest switch
        {
            null => true,
            { Status: ClaimChecker.Pending } => false,
            { Status: ClaimChecker.Accepted } accepted => accepted.AsOf != asOf && verdict.StaleSections.Contains(section, StringComparer.Ordinal),
            { Status: ClaimChecker.Fallback } left => left.AsOf != asOf,
            _ => true,
        };

    static bool Warranted(string section, Newest? newest, StalenessVerdict verdict, DateOnly asOf) =>
        Warranted(section, newest is null ? null : new SectionStanding(section, newest.AsOf, newest.Status), verdict, asOf);

    // Whether a name's pass refreshes its theme, by the rule its own sections are judged by
    // with one difference. The judge's triggers are the name's, and the theme is not one of
    // the name's rows, so an accepted theme stands until a trigger the judge fired for this
    // name is dated after the theme was written: a new filing, a passed earnings date or a
    // news spike of the name's since then, or a rewrite asked for. A name opened for the
    // first time fires nothing and reads the theme its industry already has.
    static bool ThemeWarranted(Newest? theme, StalenessVerdict verdict, DateOnly asOf) =>
        theme switch
        {
            null => true,
            { Status: ClaimChecker.Pending } => false,
            { Status: ClaimChecker.Accepted } accepted => accepted.AsOf != asOf && verdict.Fired.Any(fired => fired.Since > accepted.AsOf),
            { Status: ClaimChecker.Fallback } left => left.AsOf != asOf,
            _ => true,
        };

    // The sections the checker has accepted, in figure 12.2's order, less the one being
    // written from them.
    static IReadOnlyList<(string Section, string Prose)> Accepted(IReadOnlyDictionary<string, Newest> newest, string except) =>
    [
        .. ClaimRules.Sections
            .Where(section => section != except
                && newest.GetValueOrDefault(section) is { Status: ClaimChecker.Accepted, Prose.Length: > 0 })
            .Select(section => (section, newest[section].Prose)),
    ];

    // The paid lane for a set of sections. The cap's line where a cap stopped it, in which
    // case every section it had not reached is named as not written with that line.
    async Task<string?> PaidAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        DateOnly night,
        IReadOnlyList<Fact> facts,
        IReadOnlyDictionary<string, IReadOnlyList<StoredDocument>> handed,
        IReadOnlyList<string> sections,
        IReadOnlyDictionary<string, Newest>? refusedIn,
        string runId,
        string? round,
        List<WrittenSection> written,
        List<UnwrittenSection> notWritten,
        CancellationToken cancellation,
        IReadOnlyList<(string Section, string Prose)>? summarised = null)
    {
        for (var at = 0; at < sections.Count; at++)
        {
            var section = sections[at];
            var given = handed.TryGetValue(section, out var documents) ? documents : [];

            if (ClaimRules.IsResearched(section) && given.Count == 0)
            {
                notWritten.Add(new UnwrittenSection(section, ProseWriter.NothingHanded));

                continue;
            }

            var admitted = given.Where(document => document.Admitted).ToArray();

            // A cause with no admitted document inside any move has nothing a sentence could
            // rest on, which code knows before a call is paid for, as the prose writer knows it.
            // see: A cause of a move rests only on a document published inside that move
            if (string.Equals(section, ClaimRules.CauseSection, StringComparison.Ordinal)
                && admitted.Length > 0
                && SectionPrompt.MovesWithDocuments(facts, [.. admitted.Select(document => new PromptDocument(document.Id, document.Title, document.PublishedOn, document.Body!))]).Count == 0)
            {
                notWritten.Add(new UnwrittenSection(section, ProseWriter.NoDocumentInsideAMove));

                continue;
            }

            var newest = (await NewestAsync(connection, ticker, cancellation)).GetValueOrDefault(section);
            var version = (newest?.Version ?? 0) + 1;
            var retry = refusedIn?.GetValueOrDefault(section) is { Status: ClaimChecker.Rejected } refused ? refused.Reason : null;

            // Nothing admitted: inserted empty, citing what was handed, so the checker
            // leaves it out saying no admissible source was found and no call is paid for.
            if (ClaimRules.IsResearched(section) && admitted.Length == 0)
            {
                await InsertAsync(connection, ticker, section, version, asOf, cap.Model, string.Empty, [.. given.Select(document => document.Id)], cancellation);
                written.Add(new WrittenSection(section, version, cap.Model, retry is not null));

                continue;
            }

            var request = SectionPrompt.PaidRequest(
                cap.Model,
                ticker,
                section,
                facts,
                [.. admitted.Select(document => new PromptDocument(document.Id, document.Title, document.PublishedOn, document.Body!))],
                retry,
                summarised,
                night);

            var call = await cap.AskAsync(request, runId, round, cancellation);

            if (call.Paused)
            {
                foreach (var stopped in sections.Skip(at))
                {
                    notWritten.Add(new UnwrittenSection(stopped, call.Verdict.Line));
                }

                return call.Verdict.Line;
            }

            if (call.Answer is not { } answer)
            {
                notWritten.Add(new UnwrittenSection(section, call.Failure ?? "the research model returned nothing"));

                continue;
            }

            await InsertAsync(connection, ticker, section, version, asOf, cap.Model, answer.Text, request.DocumentIds, cancellation);
            written.Add(new WrittenSection(section, version, cap.Model, retry is not null));
        }

        return null;
    }

    // A lock held for as long as the pass runs, and released when the process ends
    // however it ends, because an open handle is what holds it rather than a file
    // someone has to remember to delete. Beside the store, since that is the one place
    // every process asking about this store can find.
    FileStream? Hold(string ticker)
    {
        var path = databaseFile + ".research-" + ticker.ToUpperInvariant() + ".lock";

        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    static ResearchPassOutcome Outcome(
        string ticker,
        DateOnly asOf,
        string outcome,
        ResearchState state,
        IReadOnlyList<string> warranted,
        string? reason,
        IReadOnlyList<UnwrittenSection>? notWritten = null) =>
        new(ticker, asOf, outcome, state, warranted, [], notWritten ?? [], 0, 0, reason);

    async Task<ResearchPassOutcome> RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        ResearchPassOutcome outcome,
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

    // The run log's detail, as JSON, which is what lets the name page say for one name
    // what its newest pass did and why a section is not there.
    public static string Detail(ResearchPassOutcome outcome, string? documents = null) =>
        JsonSerializer.Serialize(new
        {
            ticker = outcome.Ticker,
            asOf = outcome.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            outcome = outcome.Outcome,
            state = outcome.State.ToString().ToLowerInvariant(),
            reason = outcome.Reason,
            warranted = outcome.Warranted,
            written = outcome.Written.Select(section => new { section = section.Section, version = section.Version, model = section.Model, retry = section.Retry }),
            notWritten = outcome.NotWritten.Select(section => new { section = section.Section, reason = section.Reason }),
            leftForThePaidPath = outcome.LeftForThePaidPath ?? [],
            fetched = outcome.Fetched,
            admitted = outcome.Admitted,
            documents,
        });

    async Task<StoredDocument> StoreAsync(SqliteConnection connection, StoredDocument row, CancellationToken cancellation)
    {
        await using (var insert = connection.CreateCommand())
        {
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
        // fetched where an earlier pass stored it, and this pass's where it did not.
        await using var read = connection.CreateCommand();

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
        string ticker,
        string section,
        int version,
        DateOnly asOf,
        string model,
        string prose,
        IReadOnlyList<string> sources,
        CancellationToken cancellation)
    {
        await using var insert = connection.CreateCommand();

        insert.CommandText = InsertSection;
        insert.Parameters.AddWithValue("$ticker", ticker);
        insert.Parameters.AddWithValue("$section", section);
        insert.Parameters.AddWithValue("$version", version);
        insert.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$model", model);
        insert.Parameters.AddWithValue("$prose", prose);
        insert.Parameters.AddWithValue("$source_ids", JsonSerializer.Serialize(sources));

        await insert.ExecuteNonQueryAsync(cancellation);
    }

    sealed record Newest(int Version, DateOnly AsOf, string Status, string? Reason, string Prose);

    static async Task<IReadOnlyDictionary<string, Newest>> NewestAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestVersions;
        command.Parameters.AddWithValue("$ticker", ticker);

        var newest = new Dictionary<string, Newest>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            newest[reader.GetString(0)] = new Newest(
                reader.GetInt32(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5));
        }

        return newest;
    }

    static async Task<string?> IndustryAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = IndustryFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        return await command.ExecuteScalarAsync(cancellation) as string;
    }

    static async Task<Newest?> NewestThemeAsync(SqliteConnection connection, string theme, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestThemeCycle;
        command.Parameters.AddWithValue("$theme", theme);
        command.Parameters.AddWithValue("$section", ClaimRules.CycleSection);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new Newest(
                reader.GetInt32(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4))
            : null;
    }

    // Why a theme pass left the cycle unwritten: what it could not write and why, else the
    // checker's reason where today's draft was refused, else why the pass did not run.
    static string ThemeReason(ThemePassOutcome themed, Newest? newest, DateOnly asOf) =>
        themed.NotWritten.FirstOrDefault()?.Reason
        ?? (newest is { Status: ClaimChecker.Fallback or ClaimChecker.Rejected } refused && refused.AsOf == asOf ? refused.Reason : null)
        ?? themed.Reason
        ?? "the theme's industry cycle was not accepted today";

    static async Task<(IReadOnlyList<Fact>? Facts, DateOnly Night)> FactsAsync(SqliteConnection connection, string ticker, DateOnly asOf, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = FactsFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (FactsFile.Read(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture))
            : (null, asOf);
    }

    static async Task<bool> RanTodayAsync(SqliteConnection connection, string ticker, DateOnly asOf, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = RanOnSession;
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$written", Written);
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) > 0;
    }

    // The company's identifier at the archive, off the newest filing the store holds.
    static async Task<string?> CikAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestFiling;
        command.Parameters.AddWithValue("$ticker", ticker);

        if (await command.ExecuteScalarAsync(cancellation) is not string payload)
        {
            return null;
        }

        using var parsed = JsonDocument.Parse(payload);

        return parsed.RootElement.TryGetProperty("cik", out var cik) && cik.ValueKind == JsonValueKind.String && cik.GetString() is { Length: > 0 } value
            ? value
            : null;
    }

    static async Task<long> CountAsync(SqliteConnection connection, string sql, string? ticker, string? model, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        if (ticker is not null)
        {
            command.Parameters.AddWithValue("$ticker", ticker);
        }

        if (model is not null)
        {
            command.Parameters.AddWithValue("$model", model);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
