using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// What one release fetch did: the days of the archive's index and the pages of the current feed it read, the members'
// announcements it found, those carrying results, the releases stored, the documents asked for, where and why it
// stopped short, the filers the history could not read and the instant it reached.
public sealed record ReleaseFetchOutcome(
    int Days,
    int Pages,
    int Announcements,
    int Results,
    int Stored,
    int Documents,
    string? Stopped,
    string? Refusal,
    IReadOnlyList<string> Unread);

// What one check of the stored filings' exhibits did, from the 20.3 correction of 2026-10-22: the filings no check had
// read, those whose page it read, those whose page names an exhibit as the release, those whose release it replaced,
// those the archive did not answer for, the documents asked for, and where and why it stopped short.
public sealed record ExhibitsChecked(
    int Unchecked,
    int Checked,
    int Named,
    int Replaced,
    int Unread,
    int Requests,
    string? Stopped,
    string? Refusal);

// The release fetch, from 20.2. A member's results release is the EX-99 exhibit of an 8-K or 8-K/A its filer filed
// carrying item 2.02, stored once by its accession with its text compressed and the instant the SEC accepted it. The
// early slot reads the archive's index for each weekday from the day after the newest it read through the day before
// its own, and asks each member's announcement's own page for its items and its accepted instant, printed in New York's
// time; the late slot reads the current filings feed back to the start of the late slot before it, which states both,
// and asks the page for the exhibit alone. The history, by hand, reads each ledger filer's submissions and stores every
// release from the one before the filer's first session in the ledger through its last. Every request is the SEC's,
// free and keyless, at its pace under the archive's contact; the night makes none of them. It waits to write while the
// night, the drain or the monthly run holds its lock, and the history pauses for the night's window and while a slot
// runs.
// see: A member's results release is the EX-99 exhibit of its 8-K carrying item 2.02, stored compressed with the instant the SEC accepted it
// see: The local run is a process of its own at two slots outside the night and refuses any model file or prompt but its pin, whatever runtime answers
public sealed class ReleaseFetch(
    IReleaseArchiveFeed feed,
    IClock clock,
    string databaseFile,
    string dataRoot,
    Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.ReleaseCheck, Touch.Read | Touch.Insert),
            new StoreTouch(Store.ReleaseDay, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.ReleaseFiler, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.FilingsArchive]);

    // The history's own row on the run log, from 20.3 the filers' names' own, and from the 20.3 correction of 2026-10-22
    // the stored filings' check of the exhibit each page names as its release.
    public const string HistoryStage = "release-history";
    public const string NamesStage = "release-names";
    public const string ExhibitsStage = "release-exhibits";

    // The two sources a day is read from.
    public const string DailyIndexSource = "daily index";
    public const string CurrentSource = "current filings";

    // The days of the archive's index one early slot reads at most, the rest left for the next slot.
    public const int DaysASlot = 10;

    // Where no day has been read, the early slot starts this many days before its own.
    public const int FirstLookBack = 7;

    // The pages of the current feed one late slot reads at most, a hundred filings a page.
    public const int CurrentPages = 10;

    // The requests a second the archive's fair access asks a caller to stay within.
    public const int RequestsASecond = 10;

    // How far before a filer's first session in the ledger a further page of its submissions is read, so the release
    // before that session is found.
    public const int MonthsBefore = 12;

    // How long the history waits between looks at the night, a slot or a held lock.
    public static TimeSpan Poll { get; } = TimeSpan.FromSeconds(30);

    readonly Func<TimeSpan, CancellationToken, Task> wait = pause ?? ((span, token) => Task.Delay(span, token));

    static readonly TimeSpan Pace = TimeSpan.FromSeconds(1.0 / RequestsASecond);

    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE " + IndexScope.Condition + @"
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    // Each ticker's filer, the pulled companies' first and then the newest fetch that filed one.
    const string PulledFilers = "SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;";

    const string Filers = "SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;";

    // Each ledger ticker's first and last session from the day given.
    const string LedgerSpans = "SELECT ticker, MIN(session_date), MAX(session_date) FROM setup WHERE session_date >= $from GROUP BY ticker;";

    const string StoredAccessions = "SELECT accession FROM release_document;";

    const string NewestDayRead = "SELECT MAX(day) FROM release_day WHERE source = $source;";

    const string NewestRead = "SELECT MAX(read_at) FROM release_day WHERE source = $source;";

    const string InsertRelease = @"
        INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id)
        VALUES ($accession, $cik, $form, $filed, $accepted_at, $exhibit, $url, $text_gzip, $text_sha256, $text_length, $run_id)
        ON CONFLICT (accession, exhibit) DO NOTHING;
    ";

    // From the 20.3 correction of 2026-10-22: each stored filing no check has read the page of, oldest first, with the
    // exhibit it holds as its release; a check of what a filing's page names as its release, kept once; and the
    // exhibit stored before marked no release where the one named replaces it.
    const string Unchecked = @"
        SELECT d.accession, d.cik, d.form, d.filed, d.accepted_at, d.exhibit
        FROM release_document d
        WHERE d.release = 1 AND NOT EXISTS (SELECT 1 FROM release_check c WHERE c.accession = d.accession)
        ORDER BY d.accepted_at, d.accession;
    ";

    const string InsertCheck = @"
        INSERT INTO release_check (accession, exhibit, named, replaced, checked_at, run_id)
        VALUES ($accession, $exhibit, $named, $replaced, $checked_at, $run_id)
        ON CONFLICT (accession) DO NOTHING;
    ";

    const string MarkNoRelease = "UPDATE release_document SET release = 0 WHERE accession = $accession AND exhibit = $exhibit;";

    const string RecordDay = @"
        INSERT INTO release_day (day, source, posted, requests, announcements, results, stored, read_at, run_id)
        VALUES ($day, $source, $posted, $requests, $announcements, $results, $stored, $read_at, $run_id)
        ON CONFLICT (day, source) DO UPDATE SET
            posted = excluded.posted, requests = excluded.requests, announcements = excluded.announcements,
            results = excluded.results, stored = excluded.stored, read_at = excluded.read_at, run_id = excluded.run_id;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, 0, $network_requests, '0', $detail);
    ";

    // A filer's names and tickers as its newest submissions read states them, from 20.3.
    const string StoreFiler = @"
        INSERT INTO release_filer (cik, names, tickers, read_at, run_id)
        VALUES ($cik, $names, $tickers, $read_at, $run_id)
        ON CONFLICT (cik) DO UPDATE SET
            names = excluded.names, tickers = excluded.tickers, read_at = excluded.read_at, run_id = excluded.run_id;
    ";

    // The filers holding a stored release, those whose names no read has kept first.
    const string FilersToName = @"
        SELECT DISTINCT d.cik FROM release_document d
        WHERE $again = 1 OR NOT EXISTS (SELECT 1 FROM release_filer f WHERE f.cik = d.cik)
        ORDER BY d.cik;
    ";

    // A slot's fetch, asking nothing after the instant given: the early slot reads the archive's index, the late slot
    // the current filings of its own day.
    public async Task<ReleaseFetchOutcome> SlotAsync(string slot, string runId, DateTimeOffset asksNothingAfter, CancellationToken cancellation = default)
    {
        var today = clock.SessionDateAt(clock.UtcNow);

        await using var connection = await OpenAsync(cancellation);

        var filers = await MemberFilersAsync(connection, today, cancellation);
        var stored = await StoredAsync(connection, cancellation);
        var named = new HashSet<string>(StringComparer.Ordinal);

        return slot == LocalSlots.Late
            ? await CurrentAsync(connection, filers, stored, named, today, runId, asksNothingAfter, cancellation)
            : await DaysAsync(connection, filers, stored, named, today, runId, asksNothingAfter, cancellation);
    }

    // The weekdays an early slot reads: from the day after the newest it read, or the week before its own day where it
    // has read none, through the day before its own, at most the days a slot reads.
    public static IReadOnlyList<DateOnly> DaysToRead(DateOnly? newestRead, DateOnly today)
    {
        var through = today.AddDays(-1);
        var from = newestRead is { } read ? read.AddDays(1) : today.AddDays(-FirstLookBack);
        var days = new List<DateOnly>();

        for (var day = from; day <= through && days.Count < DaysASlot; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return days;
    }

    async Task<ReleaseFetchOutcome> DaysAsync(
        SqliteConnection connection,
        IReadOnlySet<string> filers,
        HashSet<string> stored,
        HashSet<string> named,
        DateOnly today,
        string runId,
        DateTimeOffset asksNothingAfter,
        CancellationToken cancellation)
    {
        var days = DaysToRead(await NewestReadAsync(connection, DailyIndexSource, cancellation), today);
        var (read, announcements, results, saved) = (0, 0, 0, 0);
        string? stopped = null;

        try
        {
            foreach (var day in days)
            {
                if (clock.UtcNow > asksNothingAfter)
                {
                    stopped = "the slot's last instant passed";

                    break;
                }

                var before = feed.Documents;
                var index = await AskAsync(token => feed.DailyIndexAsync(day, token), cancellation);

                // A day the archive posted no index for: a holiday where a later day was posted, and not posted yet where
                // it is the newest day wanted, which the next slot reads.
                if (index is null)
                {
                    if (day == days[^1])
                    {
                        stopped = FormattableString.Invariant($"the archive has not posted its index of {day:yyyy-MM-dd} yet");

                        break;
                    }

                    await RecordDayAsync(connection, day, DailyIndexSource, false, feed.Documents - before, 0, 0, 0, runId, cancellation);
                    read++;

                    continue;
                }

                var filings = SecEdgarDailyIndex.Parse(index, day)
                    .Where(filing => ResultsReleases.IsAnnouncement(filing.Form) && filers.Contains(filing.Cik))
                    .DistinctBy(filing => filing.Accession)
                    .ToArray();
                var (dayResults, dayStored) = (0, 0);

                foreach (var filing in filings)
                {
                    if (stored.Contains(filing.Accession))
                    {
                        dayResults++;

                        continue;
                    }

                    var page = await AskAsync(token => feed.FilingPageAsync(filing.Cik, filing.Accession, token), cancellation);

                    if (page is null || !SecEdgarDailyIndex.CarriesResults(SecEdgarDailyIndex.Items(page)))
                    {
                        continue;
                    }

                    dayResults++;

                    if (ResultsReleases.AcceptedOn(page, clock.SessionZone) is not { } accepted)
                    {
                        continue;
                    }

                    var release = new ReleaseFiling(filing.Cik, filing.Accession, filing.Form, filing.Filed, accepted, SecEdgarDailyIndex.Items(page));

                    if (await StoreFromPageAsync(connection, release, page, runId, cancellation))
                    {
                        stored.Add(filing.Accession);
                        dayStored++;
                        await NameOnceAsync(connection, filing.Cik, named, runId, cancellation);
                    }
                }

                await RecordDayAsync(connection, day, DailyIndexSource, true, feed.Documents - before, filings.Length, dayResults, dayStored, runId, cancellation);

                (read, announcements, results, saved) = (read + 1, announcements + filings.Length, results + dayResults, saved + dayStored);
            }
        }
        catch (Exception refused) when (refused is ProviderRefusal or FormatException)
        {
            return new ReleaseFetchOutcome(read, 0, announcements, results, saved, feed.Documents, stopped, refused.Message, []);
        }

        return new ReleaseFetchOutcome(read, 0, announcements, results, saved, feed.Documents, stopped, null, []);
    }

    // The late slot reads the current feed back to the instant the late slot before it started, so a release accepted in
    // the evening after that slot is read by this one; where none has read it, back to the start of its own day in New
    // York.
    async Task<ReleaseFetchOutcome> CurrentAsync(
        SqliteConnection connection,
        IReadOnlySet<string> filers,
        HashSet<string> stored,
        HashSet<string> named,
        DateOnly today,
        string runId,
        DateTimeOffset asksNothingAfter,
        CancellationToken cancellation)
    {
        var startedAt = clock.UtcNow;
        var since = await NewestCurrentReadAsync(connection, cancellation)
            ?? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), clock.SessionZone), TimeSpan.Zero);
        var before = feed.Documents;
        var (pages, announcements, results, saved) = (0, 0, 0, 0);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? stopped = null;
        string? refusal = null;

        try
        {
            for (var at = 0; at < CurrentPages; at++)
            {
                if (clock.UtcNow > asksNothingAfter)
                {
                    stopped = "the slot's last instant passed";

                    break;
                }

                var atom = await AskAsync(token => feed.CurrentAsync(at * ResultsReleases.CurrentPageSize, token), cancellation);

                if (atom is null)
                {
                    break;
                }

                pages++;

                var listed = ResultsReleases.FromCurrent(atom);

                foreach (var filing in listed.Where(filing => filing.Accepted >= since && filers.Contains(filing.Cik) && seen.Add(filing.Accession)))
                {
                    announcements++;

                    if (!SecEdgarDailyIndex.CarriesResults(filing.Items))
                    {
                        continue;
                    }

                    results++;

                    if (stored.Contains(filing.Accession))
                    {
                        continue;
                    }

                    var page = await AskAsync(token => feed.FilingPageAsync(filing.Cik, filing.Accession, token), cancellation);

                    if (page is not null && await StoreFromPageAsync(connection, filing, page, runId, cancellation))
                    {
                        stored.Add(filing.Accession);
                        saved++;
                        await NameOnceAsync(connection, filing.Cik, named, runId, cancellation);
                    }
                }

                // The feed lists the newest first, so a page reaching back before the instant read from is the last it needs.
                if (listed.Count == 0 || listed.Min(filing => filing.Accepted) < since)
                {
                    break;
                }
            }
        }
        catch (Exception refused) when (refused is ProviderRefusal or FormatException)
        {
            refusal = refused.Message;
        }

        // Kept under the instant the slot started, which the next late slot reads back to, and as answered only where a
        // page of the feed was.
        await RecordDayAsync(connection, today, CurrentSource, pages > 0, feed.Documents - before, announcements, results, saved, runId, cancellation, startedAt);

        return new ReleaseFetchOutcome(0, pages, announcements, results, saved, feed.Documents, stopped, refusal, []);
    }

    // The history by hand: each ledger filer's releases from the one before its first session from the day given
    // through its last, those not yet stored, stopping cleanly at the instant given and named on its own row.
    public async Task<ReleaseFetchOutcome> HistoryAsync(DateOnly from, DateTimeOffset? until, string runId, TextWriter output, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        Dictionary<string, (DateOnly First, DateOnly Last)> spans;
        HashSet<string> stored;

        await using (var connection = await OpenAsync(cancellation))
        {
            spans = await LedgerFilersAsync(connection, from, cancellation);
            stored = await StoredAsync(connection, cancellation);
        }

        await output.WriteLineAsync(FormattableString.Invariant($"releases: {spans.Count} ledger filer(s) from {from:yyyy-MM-dd}, {stored.Count} release(s) stored already"));

        var (filersRead, results, saved) = (0, 0, 0);
        var unread = new List<string>();
        string? stopped = null;
        string? refusal = null;

        try
        {
            foreach (var (cik, span) in spans.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (until is { } end && clock.UtcNow >= end)
                {
                    stopped = FormattableString.Invariant($"stopped at {end:yyyy-MM-ddTHH:mm:ssZ} as asked, {filersRead} of {spans.Count} filer(s) read");

                    break;
                }

                await PauseAsync(output, cancellation);

                var submissions = await AskAsync(token => feed.SubmissionsAsync(cik, token), cancellation);

                if (submissions is null)
                {
                    unread.Add(cik);

                    continue;
                }

                await using (var connection = await OpenAsync(cancellation))
                {
                    await StoreFilerAsync(connection, submissions, runId, cancellation);
                }

                var (_, recent, further) = ResultsReleases.FromSubmissions(submissions);
                var filings = recent.ToList();

                foreach (var page in FurtherPages(further, span.First))
                {
                    if (await AskAsync(token => feed.SubmissionsPageAsync(page.Name, token), cancellation) is { } more)
                    {
                        filings.AddRange(ResultsReleases.FromSubmissionsPage(more, cik));
                    }
                }

                var wanted = Wanted(filings, span.First, span.Last);

                results += wanted.Count;

                foreach (var release in wanted.Where(release => !stored.Contains(release.Accession)))
                {
                    if (until is { } stop && clock.UtcNow >= stop)
                    {
                        stopped = FormattableString.Invariant($"stopped at {stop:yyyy-MM-ddTHH:mm:ssZ} as asked, inside filer {cik}, {filersRead} of {spans.Count} filer(s) read whole");

                        break;
                    }

                    await PauseAsync(output, cancellation);

                    var page = await AskAsync(token => feed.FilingPageAsync(release.Cik, release.Accession, token), cancellation);

                    await using var connection = await OpenAsync(cancellation);

                    if (page is not null && await StoreFromPageAsync(connection, release, page, runId, cancellation))
                    {
                        stored.Add(release.Accession);
                        saved++;
                    }
                }

                if (stopped is not null)
                {
                    break;
                }

                filersRead++;
            }
        }
        catch (Exception refused) when (refused is ProviderRefusal or FormatException)
        {
            refusal = refused.Message;
        }

        var outcome = new ReleaseFetchOutcome(0, 0, 0, results, saved, feed.Documents, stopped, refusal, unread);

        await using (var connection = await OpenAsync(cancellation))
        {
            await WaitToWriteAsync(output, cancellation);
            await AppendRunAsync(connection, runId, startedAt, outcome, spans.Count, filersRead, cancellation);
        }

        await output.WriteLineAsync(FormattableString.Invariant(
            $"releases: {filersRead} of {spans.Count} filer(s) read, {results} release(s) in their spans, {saved} stored, {feed.Documents} request(s)")
            + (unread.Count > 0 ? FormattableString.Invariant($", {unread.Count} filer(s) the archive holds no submissions for") : string.Empty)
            + (stopped is { } why ? "; " + why : string.Empty)
            + (refusal is { } refused2 ? "; stopped by the archive: " + refused2 : string.Empty));

        return outcome;
    }

    // From 20.3 the names of every filer holding a stored release that no read has kept them for, or of every one with
    // `again`, from its submissions a request a filer, pausing as the history pauses and named on a row of its own.
    public async Task<(int Filers, int Named, int Requests, string? Refusal)> NamesAsync(bool again, string runId, TextWriter output, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var ciks = new List<string>();

        await using (var connection = await OpenAsync(cancellation))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FilersToName;
            command.Parameters.AddWithValue("$again", again ? 1 : 0);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                ciks.Add(reader.GetString(0));
            }
        }

        var before = feed.Documents;
        var named = 0;
        string? refusal = null;

        try
        {
            foreach (var cik in ciks)
            {
                await PauseAsync(output, cancellation);

                if (await AskAsync(token => feed.SubmissionsAsync(cik, token), cancellation) is { } submissions)
                {
                    await using var connection = await OpenAsync(cancellation);

                    await StoreFilerAsync(connection, submissions, runId, cancellation);
                    named++;
                }
            }
        }
        catch (Exception refused) when (refused is ProviderRefusal or FormatException)
        {
            refusal = refused.Message;
        }

        var requests = feed.Documents - before;

        await using (var connection = await OpenAsync(cancellation))
        {
            await WaitToWriteAsync(output, cancellation);

            await using var command = connection.CreateCommand();

            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", NamesStage);
            command.Parameters.AddWithValue("$started_at", Instant(startedAt));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", refusal is null ? "ok" : "refused");
            command.Parameters.AddWithValue("$rows_written", named);
            command.Parameters.AddWithValue("$network_requests", requests);
            command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { filers = ciks.Count, named, requests, refusal }));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await output.WriteLineAsync(FormattableString.Invariant($"releases: {named} of {ciks.Count} filer(s) named from {requests} request(s)") + (refusal is { } why ? "; stopped by the archive: " + why : string.Empty));

        return (ciks.Count, named, requests, refusal);
    }

    // From the 20.3 correction of 2026-10-22, by hand: each stored filing's page read again, oldest first and once, for
    // the exhibit it names as the release. Where that is another exhibit than the one stored, the one named is fetched and
    // stored as the filing's release beside the one before, which is marked no release, in one write with the check; each
    // check is kept, a page or an exhibit the archive does not answer left for the next run, pausing as the history
    // pauses, stopping cleanly at the instant given and named on a row of its own.
    // see: A results release is the exhibit its filing's index page names as the release, and the lowest exhibit only where none is named so
    public async Task<ExhibitsChecked> ExhibitsAsync(DateTimeOffset? until, string runId, TextWriter output, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var filings = new List<(ReleaseFiling Filing, string Exhibit)>();

        await using (var connection = await OpenAsync(cancellation))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Unchecked;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                filings.Add((
                    new ReleaseFiling(
                        reader.GetString(1),
                        reader.GetString(0),
                        reader.GetString(2),
                        DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DateTimeOffset.ParseExact(reader.GetString(4), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                        []),
                    reader.GetString(5)));
            }
        }

        await output.WriteLineAsync(FormattableString.Invariant($"releases: {filings.Count} stored filing(s) whose page no check has read"));

        var before = feed.Documents;
        var (checkedPages, named, replaced, unread) = (0, 0, 0, 0);
        string? stopped = null;
        string? refusal = null;

        try
        {
            foreach (var (filing, stored) in filings)
            {
                if (until is { } end && clock.UtcNow >= end)
                {
                    stopped = FormattableString.Invariant($"stopped at {end:yyyy-MM-ddTHH:mm:ssZ} as asked, {checkedPages} of {filings.Count} filing(s) checked");

                    break;
                }

                await PauseAsync(output, cancellation);

                if (await AskAsync(token => feed.FilingPageAsync(filing.Cik, filing.Accession, token), cancellation) is not { } page)
                {
                    unread++;

                    continue;
                }

                var chosen = SecEdgarArchive.NamedRelease(SecEdgarArchive.Documents(page));
                var replacing = chosen is not null && !string.Equals(chosen.FileName, stored, StringComparison.OrdinalIgnoreCase);
                string? exhibit = null;

                if (replacing && (exhibit = await AskAsync(token => feed.ExhibitAsync(filing.Cik, filing.Accession, chosen!.FileName, token), cancellation)) is null)
                {
                    unread++;

                    continue;
                }

                await using var connection = await OpenAsync(cancellation);

                await WaitToWriteAsync(output, cancellation);

                await using (var transaction = await connection.BeginTransactionAsync(cancellation))
                {
                    if (replacing)
                    {
                        await InsertReleaseAsync(connection, filing, chosen!.FileName, exhibit!, runId, cancellation);

                        await using var mark = connection.CreateCommand();

                        mark.CommandText = MarkNoRelease;
                        mark.Parameters.AddWithValue("$accession", filing.Accession);
                        mark.Parameters.AddWithValue("$exhibit", stored);

                        await mark.ExecuteNonQueryAsync(cancellation);
                    }

                    await InsertCheckAsync(connection, filing.Accession, chosen, replacing, runId, cancellation);
                    await transaction.CommitAsync(cancellation);
                }

                checkedPages++;
                named += chosen is not null && SecEdgarArchive.NamesTheRelease(chosen) ? 1 : 0;
                replaced += replacing ? 1 : 0;
            }
        }
        catch (Exception refused) when (refused is ProviderRefusal or FormatException)
        {
            refusal = refused.Message;
        }

        var outcome = new ExhibitsChecked(filings.Count, checkedPages, named, replaced, unread, feed.Documents - before, stopped, refusal);

        await using (var connection = await OpenAsync(cancellation))
        {
            await WaitToWriteAsync(output, cancellation);

            await using var command = connection.CreateCommand();

            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", ExhibitsStage);
            command.Parameters.AddWithValue("$started_at", Instant(startedAt));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", refusal is not null ? "refused" : stopped is not null ? "stopped" : "ok");
            command.Parameters.AddWithValue("$rows_written", checkedPages + replaced);
            command.Parameters.AddWithValue("$network_requests", outcome.Requests);
            command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { filings = outcome.Unchecked, @checked = checkedPages, named, replaced, unread, requests = outcome.Requests, stopped, refusal }));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await output.WriteLineAsync(FormattableString.Invariant(
            $"releases: {checkedPages} of {filings.Count} filing(s) checked, {named} naming an exhibit the release, {replaced} replaced, {unread} not answered, {outcome.Requests} request(s)")
            + (stopped is { } why ? "; " + why : string.Empty)
            + (refusal is { } refused2 ? "; stopped by the archive: " + refused2 : string.Empty));

        return outcome;
    }

    // A filer's names read from its submissions once a slot, where the slot stored a release of its.
    async Task NameOnceAsync(SqliteConnection connection, string cik, HashSet<string> named, string runId, CancellationToken cancellation)
    {
        if (!named.Add(cik))
        {
            return;
        }

        if (await AskAsync(token => feed.SubmissionsAsync(cik, token), cancellation) is { } submissions)
        {
            await StoreFilerAsync(connection, submissions, runId, cancellation);
        }
    }

    async Task StoreFilerAsync(SqliteConnection connection, string submissions, string runId, CancellationToken cancellation)
    {
        var (cik, names, tickers) = ResultsReleases.FilerOf(submissions);

        await WaitToWriteAsync(TextWriter.Null, cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = StoreFiler;
        command.Parameters.AddWithValue("$cik", cik);
        command.Parameters.AddWithValue("$names", JsonSerializer.Serialize(names));
        command.Parameters.AddWithValue("$tickers", JsonSerializer.Serialize(tickers));
        command.Parameters.AddWithValue("$read_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // The further pages of a filer's submissions the history reads: those whose filings reach the months before its
    // first session in the ledger, where the release before that session is found.
    public static IReadOnlyList<SubmissionsPage> FurtherPages(IEnumerable<SubmissionsPage> pages, DateOnly first) =>
        [.. pages.Where(page => page.To >= first.AddMonths(-MonthsBefore))];

    // A filer's releases the history stores: every one filed from its first session in the ledger through its last,
    // and the newest one filed before its first, oldest first.
    public static IReadOnlyList<ReleaseFiling> Wanted(IEnumerable<ReleaseFiling> filings, DateOnly first, DateOnly last)
    {
        var all = filings.DistinctBy(filing => filing.Accession).ToArray();
        var before = all.Where(filing => filing.Filed < first).OrderBy(filing => filing.Accepted).LastOrDefault();

        return
        [
            .. (before is null ? [] : new[] { before }),
            .. all.Where(filing => filing.Filed >= first && filing.Filed <= last).OrderBy(filing => filing.Accepted),
        ];
    }

    // The release a filing's page names, fetched and stored: from the 20.3 correction of 2026-10-22 the exhibit the page
    // names as the release, the lowest exhibit only where none is named so, its text as the archive's reader makes it text,
    // compressed, under the instant the filing was accepted, with the filing's check kept beside it so no later check asks
    // its page again. False where the page names no release or the archive holds no such exhibit.
    async Task<bool> StoreFromPageAsync(SqliteConnection connection, ReleaseFiling filing, string page, string runId, CancellationToken cancellation)
    {
        if (SecEdgarArchive.NamedRelease(SecEdgarArchive.Documents(page)) is not { } release)
        {
            return false;
        }

        var exhibit = await AskAsync(token => feed.ExhibitAsync(filing.Cik, filing.Accession, release.FileName, token), cancellation);

        if (exhibit is null)
        {
            return false;
        }

        await WaitToWriteAsync(TextWriter.Null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        var stored = await InsertReleaseAsync(connection, filing, release.FileName, exhibit, runId, cancellation) == 1;

        await InsertCheckAsync(connection, filing.Accession, release, replaced: false, runId, cancellation);
        await transaction.CommitAsync(cancellation);

        return stored;
    }

    async Task<int> InsertReleaseAsync(SqliteConnection connection, ReleaseFiling filing, string file, string exhibit, string runId, CancellationToken cancellation)
    {
        var text = ResultsReleases.Compressed(SecEdgarArchive.Plain(exhibit));

        await using var command = connection.CreateCommand();

        command.CommandText = InsertRelease;
        command.Parameters.AddWithValue("$accession", filing.Accession);
        command.Parameters.AddWithValue("$cik", filing.Cik);
        command.Parameters.AddWithValue("$form", filing.Form);
        command.Parameters.AddWithValue("$filed", Stamp(filing.Filed));
        command.Parameters.AddWithValue("$accepted_at", Instant(filing.Accepted));
        command.Parameters.AddWithValue("$exhibit", file);
        command.Parameters.AddWithValue("$url", SecEdgarArchive.Address(SecEdgarArchive.Request(ArchiveDocument.ReleaseExhibit, filing.Cik, filing.Accession, file)));
        command.Parameters.AddWithValue("$text_gzip", text.Gzip);
        command.Parameters.AddWithValue("$text_sha256", text.Sha256);
        command.Parameters.AddWithValue("$text_length", text.Length);
        command.Parameters.AddWithValue("$run_id", runId);

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task InsertCheckAsync(SqliteConnection connection, string accession, FiledDocument? chosen, bool replaced, string runId, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = InsertCheck;
        command.Parameters.AddWithValue("$accession", accession);
        command.Parameters.AddWithValue("$exhibit", (object?)chosen?.FileName ?? DBNull.Value);
        command.Parameters.AddWithValue("$named", chosen is not null && SecEdgarArchive.NamesTheRelease(chosen) ? 1 : 0);
        command.Parameters.AddWithValue("$replaced", replaced ? 1 : 0);
        command.Parameters.AddWithValue("$checked_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // One document asked of the archive, at its pace.
    async Task<string?> AskAsync(Func<CancellationToken, Task<string?>> ask, CancellationToken cancellation)
    {
        var answer = await ask(cancellation);

        await wait(Pace, cancellation);

        return answer;
    }

    // The history pauses while the night holds its lock or would run into its window, and while a slot holds the local
    // model's lock, so its requests and a slot's never run together.
    async Task PauseAsync(TextWriter output, CancellationToken cancellation)
    {
        var said = false;

        while (NightLock.Holder(dataRoot) is not null || SweepRunner.InTheNightsWindow(clock.UtcNow, TimeSpan.FromMinutes(10)) || SlotHeld())
        {
            if (!said)
            {
                await output.WriteLineAsync("releases: pausing for the night or a slot of the local run");
                said = true;
            }

            await wait(Poll, cancellation);
        }
    }

    bool SlotHeld()
    {
        var path = Path.Combine(dataRoot, WorkerDrainLauncher.CopiesFolder, LocalLocks.FileName);

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    // Waits while the night, the drain or the monthly run holds its lock.
    async Task WaitToWriteAsync(TextWriter output, CancellationToken cancellation)
    {
        while (LocalLocks.Holder(dataRoot) is { } holder)
        {
            await output.WriteLineAsync($"releases: waiting to write while {holder} holds its lock");
            await wait(Poll, cancellation);
        }
    }

    // Each member filer of the three indices on the day.
    async Task<IReadOnlySet<string>> MemberFilersAsync(SqliteConnection connection, DateOnly day, CancellationToken cancellation)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MembersOn;
            IndexScope.Bind(command, WalkForwardTester.Indices[0], [.. WalkForwardTester.Indices.Skip(1)]);
            command.Parameters.AddWithValue("$session", Stamp(day));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                members.Add(reader.GetString(0));
            }
        }

        var filerOf = await FilerOfAsync(connection, pulled: false, cancellation);

        return members.Where(filerOf.ContainsKey).Select(ticker => filerOf[ticker]).ToHashSet(StringComparer.Ordinal);
    }

    // Each ledger filer with the first and last session its tickers hold in the ledger from the day given.
    static async Task<Dictionary<string, (DateOnly First, DateOnly Last)>> LedgerFilersAsync(SqliteConnection connection, DateOnly from, CancellationToken cancellation)
    {
        var filerOf = await FilerOfAsync(connection, pulled: true, cancellation);
        var spans = new Dictionary<string, (DateOnly First, DateOnly Last)>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();

        command.CommandText = LedgerSpans;
        command.Parameters.AddWithValue("$from", Stamp(from));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!filerOf.TryGetValue(reader.GetString(0), out var cik))
            {
                continue;
            }

            var (first, last) = (Day(reader.GetString(1)), Day(reader.GetString(2)));

            spans[cik] = spans.TryGetValue(cik, out var held)
                ? (first < held.First ? first : held.First, last > held.Last ? last : held.Last)
                : (first, last);
        }

        return spans;
    }

    // Each ticker's filer: the night's own companies', and for the history by hand the pulled companies' first, so a
    // ledger ticker that left the indices before the store's year is read by the filer its pull named.
    static async Task<Dictionary<string, string>> FilerOfAsync(SqliteConnection connection, bool pulled, CancellationToken cancellation)
    {
        var filerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var query in pulled ? new[] { PulledFilers, Filers } : [Filers])
        {
            await using var command = connection.CreateCommand();

            command.CommandText = query;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                filerOf[reader.GetString(0)] = SecEdgarArchive.Padded(reader.GetString(1));
            }
        }

        return filerOf;
    }

    static async Task<HashSet<string>> StoredAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var stored = new HashSet<string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();

        command.CommandText = StoredAccessions;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            stored.Add(reader.GetString(0));
        }

        return stored;
    }

    static async Task<DateOnly?> NewestReadAsync(SqliteConnection connection, string source, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestDayRead;
        command.Parameters.AddWithValue("$source", source);

        return await command.ExecuteScalarAsync(cancellation) is string day ? Day(day) : null;
    }

    // The instant the newest late slot started, which the next reads the current feed back to.
    static async Task<DateTimeOffset?> NewestCurrentReadAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestRead;
        command.Parameters.AddWithValue("$source", CurrentSource);

        return await command.ExecuteScalarAsync(cancellation) is string at
            ? DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null;
    }

    async Task RecordDayAsync(SqliteConnection connection, DateOnly day, string source, bool posted, int requests, int announcements, int results, int stored, string runId, CancellationToken cancellation, DateTimeOffset? readAt = null)
    {
        await WaitToWriteAsync(TextWriter.Null, cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = RecordDay;
        command.Parameters.AddWithValue("$day", Stamp(day));
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$posted", posted ? 1 : 0);
        command.Parameters.AddWithValue("$requests", requests);
        command.Parameters.AddWithValue("$announcements", announcements);
        command.Parameters.AddWithValue("$results", results);
        command.Parameters.AddWithValue("$stored", stored);
        command.Parameters.AddWithValue("$read_at", Instant(readAt ?? clock.UtcNow));
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task AppendRunAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, ReleaseFetchOutcome outcome, int filers, int read, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", HistoryStage);
        command.Parameters.AddWithValue("$started_at", Instant(startedAt));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome.Refusal is not null ? "refused" : outcome.Stopped is not null ? "stopped" : "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.Stored);
        command.Parameters.AddWithValue("$network_requests", outcome.Documents);
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new
        {
            filers,
            read,
            releases = outcome.Results,
            stored = outcome.Stored,
            requests = outcome.Documents,
            stopped = outcome.Stopped,
            refusal = outcome.Refusal,
            unread = outcome.Unread,
        }));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task<SqliteConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        return connection;
    }

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
