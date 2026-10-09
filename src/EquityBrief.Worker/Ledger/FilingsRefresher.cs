using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// One day of the archive's daily index as a refresh read it: whether the archive posted one, its filings, the members
// with a filing that sets a refresh off and the members refreshed.
public sealed record FilingDayRead(DateOnly Day, bool Posted, int Filings, int Members, int Refreshed);

// What one refresh did: the days it read, the first day the archive had not posted where it stopped, the members it
// refreshed, the facts it stored, the documents it asked for of each kind, the members a limit left and the archive's
// refusal where one stopped it.
public sealed record FilingsOutcome(
    IReadOnlyList<FilingDayRead> Days,
    DateOnly? NotYetPosted,
    IReadOnlyList<string> Refreshed,
    int Facts,
    int Indexes,
    int Pages,
    int FactsAsked,
    int LeftAtTheLimit,
    string? Refusal,
    double Seconds)
{
    public int Documents => Indexes + Pages + FactsAsked;
}

// The filings refresh. After the close and the quarters fetch, it reads the archive's daily index for each weekday
// since the last one it read, through the night's own session where the archive has posted it, picks the members
// whose filer filed a quarterly or annual report or an amendment to one, or a results announcement, an 8-K or its
// amendment whose own page carries item 2.02, and asks the archive for those members' facts alone, storing each fact
// not yet stored as first filed. It is the seventh carve-out the nightly rule names: free, keyless and from the SEC
// rather than the provider, bounded by its own limit and never by the night's deadline, its documents counted on its
// own row apart from the provider's requests. A day the archive posted no index for before the session's own is a
// holiday and read as such; the session's own day not yet posted stops the read, which the next night takes up. A
// refusal or the limit leaves the facts stored as they were, the days not finished unread, and the stage's row naming
// why. The whole refresh asks every filer the store knows once, which the monthly run's catch-all repeats.
// see: The night refreshes the facts of the members that filed since its last read of the archive's daily index, after the close under its own limit
// see: The SEC's facts are stored as first filed in a table the night reads, and a setup's business readings read those filed before its session
public sealed class FilingsRefresher : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read | Touch.Insert),
            new StoreTouch(Store.FiledFactPull, Touch.Insert),
            new StoreTouch(Store.FilingDay, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.FilingsArchive]);

    public const string Stage = "filings";

    public const string WholeStage = "filings-whole";

    // The step's own limit: it asks no document once this has passed since it began.
    public static TimeSpan Limit { get; } = TimeSpan.FromMinutes(10);

    // The days of the archive's index one night reads at most, the rest left for the next night.
    public const int DaysANight = 10;

    // Where no day has been read, the refresh starts this many days before the night's session: the week before.
    public const int FirstLookBack = 7;

    // The requests a second the archive's fair access asks a caller to stay within.
    public const int RequestsASecond = 10;

    const string NewestDay = "SELECT MAX(day) FROM filing_day;";

    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE " + IndexScope.Condition + @"
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    // Each ticker's filer, from the newest fetch that filed one.
    const string Filers = "SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;";

    const string EveryFiler = "SELECT DISTINCT cik FROM company WHERE cik IS NOT NULL UNION SELECT DISTINCT cik FROM pulled_company WHERE cik IS NOT NULL ORDER BY 1;";

    const string InsertFact = @"
        INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id)
        VALUES ($cik, $concept, $period_start, $period_end, $dollars, $filed, $form, $accession, $run_id)
        ON CONFLICT (cik, concept, period_start, period_end) DO NOTHING;
    ";

    const string InsertPull = @"
        INSERT INTO filed_fact_pull (cik, pulled_at, run_id, accession, stored)
        VALUES ($cik, $pulled_at, $run_id, $accession, $stored)
        ON CONFLICT (cik, pulled_at) DO NOTHING;
    ";

    const string InsertDay = @"
        INSERT INTO filing_day (day, run_id, read_at, posted, filings, members, refreshed)
        VALUES ($day, $run_id, $read_at, $posted, $filings, $members, $refreshed)
        ON CONFLICT (day) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly IFilingsRefreshFeed feed;
    readonly IClock clock;
    readonly string databaseFile;
    readonly TimeSpan limit;
    readonly Func<TimeSpan, CancellationToken, Task> pause;

    // `pause` waits between documents at the archive's pace; a test hands one that waits for nothing.
    public FilingsRefresher(
        IFilingsRefreshFeed feed,
        IClock clock,
        string databaseFile,
        TimeSpan? limit = null,
        Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        this.feed = feed;
        this.clock = clock;
        this.databaseFile = databaseFile;
        this.limit = limit ?? Limit;
        this.pause = pause ?? Task.Delay;
    }

    // The weekdays a refresh reads: from the day after the newest it read, or the week before the session where it
    // has read none, through the session's own day, at most the days a night reads.
    public static IReadOnlyList<DateOnly> DaysToRead(DateOnly? newestRead, DateOnly session)
    {
        var day = newestRead is { } read ? read.AddDays(1) : session.AddDays(-FirstLookBack);
        var days = new List<DateOnly>();

        for (; day <= session && days.Count < DaysANight; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                days.Add(day);
            }
        }

        return days;
    }

    // Which filers a day's filings set a refresh off for, among the members': each filer with a report or an
    // amendment to one, and each with an announcement whose page carries results, its page asked once an accession.
    public async Task<(IReadOnlyList<(string Cik, string Accession)> Refresh, int Members, int Pages)> ChosenAsync(
        IReadOnlyList<DailyFiling> filings,
        IReadOnlySet<string> memberFilers,
        CancellationToken cancellation)
    {
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        var pages = 0;
        var members = filings.Where(one => memberFilers.Contains(one.Cik)).ToArray();

        // The reports first, so a filer that filed one and an announcement the same day has no page asked.
        foreach (var filing in members.Where(one => SecEdgarDailyIndex.Reports.Contains(one.Form, StringComparer.Ordinal)))
        {
            chosen.TryAdd(filing.Cik, filing.Accession);
        }

        foreach (var filing in members.Where(one => SecEdgarDailyIndex.Announcements.Contains(one.Form, StringComparer.Ordinal)))
        {
            if (chosen.ContainsKey(filing.Cik))
            {
                continue;
            }

            await pause(TimeSpan.FromSeconds(1.0 / RequestsASecond), cancellation);

            pages++;

            if (await feed.FilingPageAsync(filing.Cik, filing.Accession, cancellation) is { } page
                && SecEdgarDailyIndex.CarriesResults(SecEdgarDailyIndex.Items(page)))
            {
                chosen.TryAdd(filing.Cik, filing.Accession);
            }
        }

        return ([.. chosen.Select(pair => (pair.Key, pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal)], chosen.Count, pages);
    }

    // The night's refresh, over the members of the night's indices on its session.
    public async Task<FilingsOutcome> NightAsync(string indexCode, string runId, IReadOnlyList<string>? wider = null, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var watch = Stopwatch.StartNew();
        var session = clock.SessionDateAt(startedAt);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var tickersOf = await MemberFilersAsync(connection, indexCode, session, wider, cancellation);
        var memberFilers = tickersOf.Keys.ToHashSet(StringComparer.Ordinal);
        var newest = await ScalarDateAsync(connection, NewestDay, cancellation);

        var days = new List<FilingDayRead>();
        var refreshed = new List<string>();
        var (facts, indexes, pages, asked, left) = (0, 0, 0, 0, 0);
        DateOnly? notYetPosted = null;
        string? refusal = null;

        try
        {
            foreach (var day in DaysToRead(newest, session))
            {
                if (watch.Elapsed >= limit)
                {
                    break;
                }

                await pause(TimeSpan.FromSeconds(1.0 / RequestsASecond), cancellation);

                indexes++;

                var text = await feed.DailyIndexAsync(day, cancellation);

                if (text is null)
                {
                    if (day < session)
                    {
                        await DayAsync(connection, new FilingDayRead(day, false, 0, 0, 0), runId, cancellation);
                        days.Add(new FilingDayRead(day, false, 0, 0, 0));

                        continue;
                    }

                    notYetPosted = day;

                    break;
                }

                var filings = SecEdgarDailyIndex.Parse(text, day);
                var (chosen, members, read) = await ChosenAsync(filings, memberFilers, cancellation);

                pages += read;

                var finished = true;
                var dayRefreshed = 0;

                foreach (var (cik, accession) in chosen)
                {
                    if (watch.Elapsed >= limit)
                    {
                        left += chosen.Count - dayRefreshed;
                        finished = false;

                        break;
                    }

                    await pause(TimeSpan.FromSeconds(1.0 / RequestsASecond), cancellation);

                    asked++;
                    facts += await StoreAsync(connection, cik, accession, runId, await feed.FactsAsync(cik, FiledFacts.Concepts, cancellation), cancellation);
                    dayRefreshed++;
                    refreshed.AddRange(tickersOf[cik]);
                }

                if (!finished)
                {
                    break;
                }

                var dayRead = new FilingDayRead(day, true, filings.Count, members, dayRefreshed);

                await DayAsync(connection, dayRead, runId, cancellation);
                days.Add(dayRead);
            }
        }
        catch (ProviderRefusal refused)
        {
            refusal = refused.Message;
        }

        var outcome = new FilingsOutcome(
            days,
            notYetPosted,
            [.. refreshed.Distinct(StringComparer.Ordinal).OrderBy(ticker => ticker, StringComparer.Ordinal)],
            facts,
            indexes,
            pages,
            asked,
            left,
            refusal,
            watch.Elapsed.TotalSeconds);

        await RecordAsync(connection, runId, Stage, startedAt, outcome, Detail(outcome), cancellation);

        return outcome;
    }

    // The whole refresh: every filer the store knows, the night's and the pulled companies', asked once at the
    // archive's pace, each fact not yet stored stored as first filed.
    public async Task<FilingsOutcome> WholeAsync(string runId, TextWriter? output = null, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var watch = Stopwatch.StartNew();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var filers = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EveryFiler;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                filers.Add(SecEdgarArchive.Padded(reader.GetString(0)));
            }
        }

        var (facts, asked) = (0, 0);
        string? refusal = null;

        try
        {
            foreach (var cik in filers.Distinct(StringComparer.Ordinal))
            {
                await pause(TimeSpan.FromSeconds(1.0 / RequestsASecond), cancellation);

                asked++;
                facts += await StoreAsync(connection, cik, null, runId, await feed.FactsAsync(cik, FiledFacts.Concepts, cancellation), cancellation);

                if (asked % 100 == 0)
                {
                    output?.WriteLine(FormattableString.Invariant($"filings: {asked} of {filers.Count} filer(s) asked, {facts} fact(s) stored in {watch.Elapsed.TotalSeconds:0} s"));
                }
            }
        }
        catch (ProviderRefusal refused)
        {
            refusal = refused.Message;
        }

        var outcome = new FilingsOutcome([], null, [], facts, 0, 0, asked, 0, refusal, watch.Elapsed.TotalSeconds);

        await RecordAsync(
            connection,
            runId,
            WholeStage,
            startedAt,
            outcome,
            FormattableString.Invariant($"{asked} of {filers.Count} filer(s) asked, {facts} fact(s) stored, in {outcome.Seconds:0} s")
                + (refusal is null ? string.Empty : "; stopped on the archive's refusal: " + refusal),
            cancellation);

        return outcome;
    }

    // What the run page reads of the step: the days read, the members refreshed, the facts stored and the documents
    // asked of each kind, and why it stopped where it did. The step's time is its row's own start and end, so two
    // nights over one fixture write one row.
    public static string Detail(FilingsOutcome outcome)
    {
        var posted = outcome.Days.Where(day => day.Posted).ToArray();
        var span = outcome.Days.Count == 0
            ? "no day of the archive's index read"
            : FormattableString.Invariant($"{outcome.Days.Count} day(s) of the archive's index read, {Stamp(outcome.Days[0].Day)} to {Stamp(outcome.Days[^1].Day)}, {outcome.Days.Count - posted.Length} with none posted");

        return span
            + FormattableString.Invariant($"; {posted.Sum(day => day.Members)} member filer(s) setting a refresh off, {outcome.Refreshed.Count} member(s) refreshed, {outcome.Facts} fact(s) stored")
            + FormattableString.Invariant($"; {outcome.Documents} document(s) asked of the archive: {outcome.Indexes} index(es), {outcome.Pages} 8-K page(s) and {outcome.FactsAsked} filer(s)' facts")
            + (outcome.NotYetPosted is { } waiting ? $"; {Stamp(waiting)} not yet posted, read on a later night" : string.Empty)
            + (outcome.LeftAtTheLimit > 0 ? FormattableString.Invariant($"; {outcome.LeftAtTheLimit} member(s) left at the step's limit, their day read again on the next night") : string.Empty)
            + (outcome.Refusal is { } refused ? "; stopped on the archive's refusal, facts left as they were: " + refused : string.Empty);
    }

    async Task<int> StoreAsync(
        SqliteConnection connection,
        string cik,
        string? accession,
        string runId,
        IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>? answered,
        CancellationToken cancellation)
    {
        var rows = answered is null ? [] : FiledFacts.Stored(answered);
        var stored = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = InsertFact;
            command.Parameters.AddWithValue("$cik", cik);
            command.Parameters.AddWithValue("$concept", row.Concept);
            command.Parameters.AddWithValue("$period_start", Stamp(row.Start));
            command.Parameters.AddWithValue("$period_end", Stamp(row.End));
            command.Parameters.AddWithValue("$dollars", Money.ToStorage(row.Value));
            command.Parameters.AddWithValue("$filed", Stamp(row.Filed));
            command.Parameters.AddWithValue("$form", row.Form);
            command.Parameters.AddWithValue("$accession", row.Accession);
            command.Parameters.AddWithValue("$run_id", runId);

            stored += await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var pull = connection.CreateCommand())
        {
            pull.Transaction = transaction;
            pull.CommandText = InsertPull;
            pull.Parameters.AddWithValue("$cik", cik);
            pull.Parameters.AddWithValue("$pulled_at", Instant(clock.UtcNow));
            pull.Parameters.AddWithValue("$run_id", runId);
            pull.Parameters.AddWithValue("$accession", (object?)accession ?? DBNull.Value);
            pull.Parameters.AddWithValue("$stored", stored);

            await pull.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return stored;
    }

    async Task DayAsync(SqliteConnection connection, FilingDayRead day, string runId, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = InsertDay;
        command.Parameters.AddWithValue("$day", Stamp(day.Day));
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$read_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$posted", day.Posted ? 1 : 0);
        command.Parameters.AddWithValue("$filings", day.Filings);
        command.Parameters.AddWithValue("$members", day.Members);
        command.Parameters.AddWithValue("$refreshed", day.Refreshed);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(SqliteConnection connection, string runId, string stage, DateTimeOffset startedAt, FilingsOutcome outcome, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$started_at", Instant(startedAt));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome.Refusal is null && outcome.LeftAtTheLimit == 0 ? "ok" : "partial");
        command.Parameters.AddWithValue("$rows_written", outcome.Facts);
        command.Parameters.AddWithValue("$network_requests", outcome.Documents);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // Each member filer of the night's indices with the tickers it files for.
    static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> MemberFilersAsync(
        SqliteConnection connection,
        string indexCode,
        DateOnly session,
        IReadOnlyList<string>? wider,
        CancellationToken cancellation)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MembersOn;
            IndexScope.Bind(command, indexCode, wider);
            command.Parameters.AddWithValue("$session", Stamp(session));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                members.Add(reader.GetString(0));
            }
        }

        var filerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Filers;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                filerOf[reader.GetString(0)] = SecEdgarArchive.Padded(reader.GetString(1));
            }
        }

        return members
            .Where(filerOf.ContainsKey)
            .GroupBy(ticker => filerOf[ticker], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.OrderBy(ticker => ticker, StringComparer.Ordinal)],
                StringComparer.Ordinal);
    }

    static async Task<DateOnly?> ScalarDateAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        return await command.ExecuteScalarAsync(cancellation) is string stored
            ? DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
