using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Insiders;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Insiders;

// One day of the archive's index as the insiders' reader read it: the members' Form 4 and 4/A filings it lists, those
// asked of the archive, the transactions stored, the filings it could not read with why, and whether it read them all.
public sealed record InsiderDay(DateOnly Day, int Filings, int Asked, int Stored, IReadOnlyList<string> Unread, bool Finished);

// What a read of the history or of days by hand did: the spans read, the filings, the asks, the transactions stored, the
// documents asked of the archive, the time, and the refusal where one stopped it; with compare, the fields that differed.
public sealed record InsiderOutcome(
    int Spans,
    int Filings,
    int Asked,
    int Stored,
    int Equal,
    IReadOnlyDictionary<string, int> Differences,
    IReadOnlyList<string> Named,
    IReadOnlyList<string> Unread,
    int Documents,
    double Seconds,
    string? Refusal);

// The insiders' trades. A day's Form 4 and 4/A lines of the members' filers, matched by CIK as the facts are, each
// filing asked of the archive once, one request a filing, and its table of shares stored a row a transaction under the
// issuer the filing names; a filing stored already is not asked again, so a day the refresh's limit stopped is taken up
// where it stopped. The quarters of the SEC's data sets before the nightly reader, one request a quarter and one for the
// page listing them, stored the same way for every company the store knows, departed members among them. A span is
// recorded once every filing of it was read, which is how a reading tells a quiet company's nought from days no reader
// read. An amendment is matched to the one Form 4 its issuer's same owners filed on the day it states its original was
// filed, and stands on its own where none or two match. Free and keyless, from the SEC rather than the provider.
// see: The night refreshes the facts and the insiders' trades of the members that filed since its last read of the archive's daily index, after the close under its own limit
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public sealed class InsiderTrades : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.InsiderTrade, Touch.Read | Touch.Insert),
            new StoreTouch(Store.InsiderRead, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.FilingsArchive]);

    public const string FilingSource = "filing";

    public const string DataSetSource = "data-set";

    public const string HistoryStage = "insiders-history";

    public const string DaysStage = "insiders";

    // The archive's fair access, as the refresh paces it.
    public const int RequestsASecond = 10;

    // The differences a comparison names one by one before it only counts them.
    public const int NamedDifferences = 40;

    const string InsertTrade = @"
        INSERT INTO insider_trade (
            cik, ticker, accession, filed, form, amends, original_filed, owners, owner_ciks, director, officer, title,
            ten_percent, sequence, security, traded, code, shares, price, acquired, held_after, planned, direct, nature,
            source, run_id)
        VALUES (
            $cik, $ticker, $accession, $filed, $form, $amends, $original_filed, $owners, $owner_ciks, $director, $officer, $title,
            $ten_percent, $sequence, $security, $traded, $code, $shares, $price, $acquired, $held_after, $planned, $direct, $nature,
            $source, $run_id)
        ON CONFLICT (accession, sequence) DO NOTHING;
    ";

    const string InsertRead = @"
        INSERT INTO insider_read (first_day, last_day, source, run_id, read_at, filings, stored)
        VALUES ($first_day, $last_day, $source, $run_id, $read_at, $filings, $stored)
        ON CONFLICT (first_day, source) DO NOTHING;
    ";

    const string Held = "SELECT 1 FROM insider_trade WHERE accession = $accession LIMIT 1;";

    // The originals an amendment may amend: the issuer's Forms 4 its same owners filed on the day the amendment states.
    const string Originals = @"
        SELECT DISTINCT accession FROM insider_trade
        WHERE cik = $cik AND owner_ciks = $owner_ciks AND filed = $filed AND form = '4';
    ";

    // Every company the store knows, departed members among them, by its filer.
    const string EveryFiler = "SELECT DISTINCT cik FROM company WHERE cik IS NOT NULL UNION SELECT DISTINCT cik FROM pulled_company WHERE cik IS NOT NULL;";

    const string QuartersRead = "SELECT first_day FROM insider_read WHERE source = 'data-set';";

    // The filers of the members of the three indices on a day.
    const string MembersOn = @"
        SELECT DISTINCT c.cik FROM membership m JOIN company c ON c.ticker = m.ticker
        WHERE c.cik IS NOT NULL
          AND (m.joined IS NULL OR m.joined <= $session)
          AND (m.""left"" IS NULL OR m.""left"" > $session);
    ";

    const string StoredRows = @"
        SELECT sequence, security, traded, code, shares, price, acquired, held_after, planned, direct, nature,
               filed, form, original_filed, owners, owner_ciks, director, officer, title, ten_percent, cik
        FROM insider_trade WHERE accession = $accession ORDER BY sequence;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;
    readonly Func<TimeSpan, CancellationToken, Task> pause;

    // `pause` waits between documents at the archive's pace; a test hands one that waits for nothing.
    public InsiderTrades(IClock clock, string databaseFile, Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
        this.pause = pause ?? Task.Delay;
    }

    // The members' Form 4 and 4/A filings a day's index lists, each once by its accession, in the index's order: those
    // whose line names a member's filer, the issuer's own line or an owner's that is a member.
    public static IReadOnlyList<DailyFiling> Lines(IReadOnlyList<DailyFiling> filings, IReadOnlySet<string> memberFilers) =>
    [
        .. filings
            .Where(filing => InsiderFiling.Forms.Contains(filing.Form, StringComparer.Ordinal) && memberFilers.Contains(filing.Cik))
            .DistinctBy(filing => filing.Accession, StringComparer.Ordinal),
    ];

    // One day's filings: each not stored already asked of the archive, read and stored under the issuer it names where
    // that issuer is a member's filer, until `stop` says the step's limit has passed; the day finished where every one
    // was read or named unread. A filing the reader cannot read is named with why and stores nothing, and the day goes
    // on; a refusal of the archive is not caught here and stops the step.
    public static async Task<InsiderDay> DayAsync(
        SqliteConnection connection,
        IFilingsRefreshFeed feed,
        DateOnly day,
        IReadOnlyList<DailyFiling> filings,
        IReadOnlySet<string> memberFilers,
        Func<bool> stop,
        Func<CancellationToken, Task> pace,
        string runId,
        CancellationToken cancellation)
    {
        var lines = Lines(filings, memberFilers);
        var unread = new List<string>();
        var (asked, stored) = (0, 0);

        foreach (var line in lines)
        {
            if (await HeldAsync(connection, null, line.Accession, cancellation))
            {
                continue;
            }

            if (stop())
            {
                return new InsiderDay(day, lines.Count, asked, stored, unread, false);
            }

            await pace(cancellation);

            asked++;

            if (await feed.OwnershipAsync(line.Cik, line.Accession, cancellation) is not { } submission)
            {
                unread.Add(line.Accession + ": the archive holds no such filing");

                continue;
            }

            InsiderFiling filing;

            try
            {
                filing = SecEdgarOwnership.Parse(submission);
            }
            catch (FormatException failure)
            {
                unread.Add(line.Accession + ": " + failure.Message);

                continue;
            }

            if (memberFilers.Contains(filing.IssuerCik))
            {
                stored += await StoreAsync(connection, null, filing, FilingSource, runId, cancellation);
            }
        }

        return new InsiderDay(day, lines.Count, asked, stored, unread, true);
    }

    // A filing's table of shares stored a row a transaction, an amendment naming the one original it was matched to;
    // how many rows it stored. A filing whose table holds no transaction stores nothing. Inside the transaction handed
    // in, or one of its own where none is.
    public static async Task<int> StoreAsync(
        SqliteConnection connection,
        SqliteTransaction? within,
        InsiderFiling filing,
        string source,
        string runId,
        CancellationToken cancellation)
    {
        if (filing.Transactions.Count == 0)
        {
            return 0;
        }

        var own = within is null ? (SqliteTransaction)await connection.BeginTransactionAsync(cancellation) : null;
        var transaction = within ?? own!;

        try
        {
            string? amends = null;

            if (filing.Amends && filing.OriginalFiled is { } day)
            {
                var originals = new List<string>();

                await using var command = Command(connection, transaction, Originals,
                [
                    ("$cik", filing.IssuerCik),
                    ("$owner_ciks", filing.OwnerKey),
                    ("$filed", Stamp(day)),
                ]);
                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    originals.Add(reader.GetString(0));
                }

                amends = originals.Count == 1 ? originals[0] : null;
            }

            var stored = 0;

            foreach (var row in filing.Transactions)
            {
                await using var command = Command(connection, transaction, InsertTrade,
                [
                    ("$cik", filing.IssuerCik),
                    ("$ticker", (object?)filing.Symbol ?? DBNull.Value),
                    ("$accession", filing.Accession),
                    ("$filed", Stamp(filing.Filed)),
                    ("$form", filing.Form),
                    ("$amends", (object?)amends ?? DBNull.Value),
                    ("$original_filed", filing.OriginalFiled is { } original ? Stamp(original) : DBNull.Value),
                    ("$owners", string.Join("; ", filing.Owners.Select(owner => owner.Name))),
                    ("$owner_ciks", filing.OwnerKey),
                    ("$director", filing.Owners.Any(owner => owner.Director) ? 1 : 0),
                    ("$officer", filing.Owners.Any(owner => owner.Officer) ? 1 : 0),
                    ("$title", (object?)filing.Owners.Select(owner => owner.Title).FirstOrDefault(title => title is not null) ?? DBNull.Value),
                    ("$ten_percent", filing.Owners.Any(owner => owner.TenPercent) ? 1 : 0),
                    ("$sequence", row.Sequence),
                    ("$security", row.Security),
                    ("$traded", Stamp(row.Traded)),
                    ("$code", row.Code),
                    ("$shares", Money.ToStorage(row.Shares)),
                    ("$price", row.Price is { } price ? Money.ToStorage(price) : DBNull.Value),
                    ("$acquired", row.Acquired ? 1 : 0),
                    ("$held_after", row.HeldAfter is { } held ? Money.ToStorage(held) : DBNull.Value),
                    ("$planned", filing.Planned is { } planned ? planned ? 1 : 0 : DBNull.Value),
                    ("$direct", row.Direct ? 1 : 0),
                    ("$nature", (object?)row.Nature ?? DBNull.Value),
                    ("$source", source),
                    ("$run_id", runId),
                ]);

                stored += await command.ExecuteNonQueryAsync(cancellation);
            }

            if (own is not null)
            {
                await own.CommitAsync(cancellation);
            }

            return stored;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    // A span read whole: a quarter of the data sets or a day of the archive's index, the filings it read and the rows it
    // stored.
    public static async Task CoverAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateOnly first,
        DateOnly last,
        string source,
        string runId,
        DateTimeOffset at,
        int filings,
        int stored,
        CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, InsertRead,
        [
            ("$first_day", Stamp(first)),
            ("$last_day", Stamp(last)),
            ("$source", source),
            ("$run_id", runId),
            ("$read_at", Instant(at)),
            ("$filings", filings),
            ("$stored", stored),
        ]);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // The history before the nightly reader: each quarter of the SEC's data sets the listing page links from the one
    // named, the first the history reads where none is, through the one named or the newest posted, that no earlier read
    // recorded, one request a quarter and one for the page; each quarter's Forms 4 and 4/A of every company the store
    // knows stored in one transaction, the originals before the amendments so an amendment finds its original, and the
    // quarter recorded in the same transaction once all of it is stored.
    public async Task<InsiderOutcome> HistoryAsync(
        IInsiderDataSetFeed feed,
        string? from,
        string? through,
        string runId,
        TextWriter output,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var watch = Stopwatch.StartNew();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var issuers = await CiksAsync(connection, EveryFiler, [], cancellation);
        var read = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = Command(connection, null, QuartersRead, []))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                read.Add(reader.GetString(0));
            }
        }

        var first = from ?? SecEdgarInsiderDataSet.FirstQuarter;
        var (spans, filings, stored) = (0, 0, 0);
        string? refusal = null;

        try
        {
            await pause(TimeSpan.FromSeconds(1.0 / RequestsASecond), cancellation);

            var listing = await feed.ListingAsync(cancellation)
                ?? throw new ProviderRefusal("The archive holds no page listing the insider data sets.", transient: false);

            foreach (var (quarter, path) in SecEdgarInsiderDataSet.Quarters(listing)
                .Where(one => string.CompareOrdinal(one.Key, first) >= 0 && (through is null || string.CompareOrdinal(one.Key, through) <= 0)))
            {
                var (firstDay, lastDay) = SecEdgarInsiderDataSet.Span(quarter);

                if (read.Contains(Stamp(firstDay)))
                {
                    continue;
                }

                await pause(TimeSpan.FromSeconds(1.0 / RequestsASecond), cancellation);

                var zip = await feed.QuarterAsync(path, cancellation)
                    ?? throw new ProviderRefusal($"The archive holds no insider data set for {quarter} at {path}.", transient: false);

                IReadOnlyList<InsiderFiling> quarterFilings;

                using (var stream = new MemoryStream(zip))
                {
                    quarterFilings = SecEdgarInsiderDataSet.Read(stream, issuers);
                }

                var quarterStored = 0;

                await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
                {
                    foreach (var filing in quarterFilings.OrderBy(one => one.Amends).ThenBy(one => one.Filed).ThenBy(one => one.Accession, StringComparer.Ordinal))
                    {
                        if (!await HeldAsync(connection, transaction, filing.Accession, cancellation))
                        {
                            quarterStored += await StoreAsync(connection, transaction, filing, DataSetSource, runId, cancellation);
                        }
                    }

                    await CoverAsync(connection, transaction, firstDay, lastDay, DataSetSource, runId, clock.UtcNow, quarterFilings.Count, quarterStored, cancellation);
                    await transaction.CommitAsync(cancellation);
                }

                spans++;
                filings += quarterFilings.Count;
                stored += quarterStored;

                output.WriteLine(FormattableString.Invariant($"{HistoryStage}: {quarter}, {quarterFilings.Count} Form 4 and 4/A filing(s) of the {issuers.Count} companies' filers, {quarterStored} transaction(s) stored from {zip.Length} bytes, in {watch.Elapsed.TotalSeconds:0} s so far"));
            }
        }
        catch (ProviderRefusal refused)
        {
            refusal = refused.Message;
        }

        var outcome = new InsiderOutcome(spans, filings, 0, stored, 0, new Dictionary<string, int>(), [], [], feed.Documents, watch.Elapsed.TotalSeconds, refusal);
        var detail = FormattableString.Invariant($"{spans} quarter(s) of the insider data sets read from {first} through {through ?? "the newest posted"}, {filings} Form 4 and 4/A filing(s), {stored} transaction(s) stored, {feed.Documents} document(s) asked of the archive, in {outcome.Seconds:0} s")
            + (refusal is null ? string.Empty : "; stopped on the archive's refusal: " + refusal);

        await RecordAsync(connection, runId, HistoryStage, startedAt, refusal is null, stored, feed.Documents, detail, cancellation);
        output.WriteLine($"{HistoryStage}: {detail}");

        return outcome;
    }

    // The nightly reader by hand over the weekdays from one day to another, each read whole and recorded as the night
    // records it, at the archive's pace and under no limit; or with compare, each filing the store holds already read
    // again and set against its stored rows field by field, writing nothing, every difference counted by its field and
    // the first of them named.
    public async Task<InsiderOutcome> DaysAsync(
        IFilingsRefreshFeed feed,
        DateOnly from,
        DateOnly through,
        bool compare,
        string runId,
        TextWriter output,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var watch = Stopwatch.StartNew();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var differences = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var named = new List<string>();
        var unread = new List<string>();
        var (days, filings, asked, stored, equal) = (0, 0, 0, 0, 0);
        string? refusal = null;

        Task Pace(CancellationToken token) => pause(TimeSpan.FromSeconds(1.0 / RequestsASecond), token);

        try
        {
            for (var day = from; day <= through; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var members = await CiksAsync(connection, MembersOn, [("$session", Stamp(day))], cancellation);

                await Pace(cancellation);

                var text = await feed.DailyIndexAsync(day, cancellation);
                var lines = text is null ? [] : SecEdgarDailyIndex.Parse(text, day);

                days++;

                if (!compare)
                {
                    var one = await DayAsync(connection, feed, day, lines, members, () => false, Pace, runId, cancellation);

                    filings += one.Filings;
                    asked += one.Asked;
                    stored += one.Stored;
                    unread.AddRange(one.Unread);

                    await CoverAsync(connection, null, day, day, FilingSource, runId, clock.UtcNow, one.Filings, one.Stored, cancellation);
                    output.WriteLine(FormattableString.Invariant($"{DaysStage}: {day:yyyy-MM-dd}, {one.Filings} member filing(s), {one.Asked} asked, {one.Stored} transaction(s) stored, in {watch.Elapsed.TotalSeconds:0} s so far"));

                    continue;
                }

                foreach (var line in Lines(lines, members))
                {
                    filings++;

                    var held = await StoredAsync(connection, line.Accession, cancellation);

                    if (held.Count == 0)
                    {
                        continue;
                    }

                    await Pace(cancellation);

                    asked++;

                    if (await feed.OwnershipAsync(line.Cik, line.Accession, cancellation) is not { } submission)
                    {
                        unread.Add(line.Accession + ": the archive holds no such filing");

                        continue;
                    }

                    InsiderFiling filing;

                    try
                    {
                        filing = SecEdgarOwnership.Parse(submission);
                    }
                    catch (FormatException failure)
                    {
                        unread.Add(line.Accession + ": " + failure.Message);

                        continue;
                    }

                    var found = Compared(filing, held);

                    if (found.Count == 0)
                    {
                        equal++;
                    }

                    foreach (var field in found.Select(one => one.Field).Distinct(StringComparer.Ordinal))
                    {
                        differences[field] = differences.GetValueOrDefault(field) + 1;
                    }

                    foreach (var (_, words) in found)
                    {
                        if (named.Count < NamedDifferences)
                        {
                            named.Add(line.Accession + " " + words);
                        }
                    }
                }

                output.WriteLine(FormattableString.Invariant($"{DaysStage}: {day:yyyy-MM-dd} compared, {asked} filing(s) read again so far, {equal} giving rows equal to the stored ones, in {watch.Elapsed.TotalSeconds:0} s so far"));
            }
        }
        catch (ProviderRefusal refused)
        {
            refusal = refused.Message;
        }

        var outcome = new InsiderOutcome(days, filings, asked, stored, equal, differences, named, unread, feed.Documents, watch.Elapsed.TotalSeconds, refusal);
        var detail = (compare
                ? FormattableString.Invariant($"{days} day(s) of the archive's index read from {from:yyyy-MM-dd} to {through:yyyy-MM-dd} and compared, {filings} member Form 4 and 4/A filing(s), {asked} held already and read again, {equal} giving rows equal to the stored ones")
                    + (differences.Count == 0 ? string.Empty : "; fields that differed: " + string.Join(", ", differences.Select(pair => FormattableString.Invariant($"{pair.Key} on {pair.Value} filing(s)"))))
                : FormattableString.Invariant($"{days} day(s) of the archive's index read from {from:yyyy-MM-dd} to {through:yyyy-MM-dd}, {filings} member Form 4 and 4/A filing(s), {asked} asked, {stored} transaction(s) stored"))
            + FormattableString.Invariant($", {feed.Documents} document(s) asked of the archive, in {outcome.Seconds:0} s")
            + (unread.Count == 0 ? string.Empty : FormattableString.Invariant($"; {unread.Count} filing(s) not read: ") + string.Join("; ", unread.Take(5)))
            + (refusal is null ? string.Empty : "; stopped on the archive's refusal: " + refusal);

        if (!compare)
        {
            await RecordAsync(connection, runId, DaysStage, startedAt, refusal is null, stored, feed.Documents, detail, cancellation);
        }

        output.WriteLine($"{DaysStage}: {detail}");

        foreach (var one in named)
        {
            output.WriteLine($"{DaysStage}: {one}");
        }

        return outcome;
    }

    // Each field a filing read again gives otherwise than its stored rows, by the field's name with the two values; a
    // figure is set against its stored text as a number, so 29724 and 29724.0 are one, and one the data set holds to two
    // places and the filing to more is named apart from any other difference.
    public static IReadOnlyList<(string Field, string Words)> Compared(InsiderFiling filing, IReadOnlyList<string[]> stored)
    {
        var found = new List<(string, string)>();

        if (filing.Transactions.Count != stored.Count)
        {
            found.Add(("rows", FormattableString.Invariant($"rows: {filing.Transactions.Count} read, {stored.Count} stored")));

            return found;
        }

        void Check(string field, string read, string held)
        {
            if (!string.Equals(read, held, StringComparison.Ordinal))
            {
                found.Add((field, $"{field}: '{read}' read, '{held}' stored"));
            }
        }

        void Figure(string field, decimal? read, string held)
        {
            decimal? kept = held.Length == 0 ? null : Money.FromStorage(held);

            if (read == kept)
            {
                return;
            }

            var rounded = read is { } value && kept is { } other && Math.Round(value, 2, MidpointRounding.AwayFromZero) == other;

            found.Add((rounded ? field + " past two places" : field, FormattableString.Invariant($"{field}: '{read}' read, '{held}' stored")));
        }

        foreach (var (row, held) in filing.Transactions.Zip(stored))
        {
            Check("security", row.Security, held[1]);
            Check("traded", Stamp(row.Traded), held[2]);
            Check("code", row.Code, held[3]);
            Figure("shares", row.Shares, held[4]);
            Figure("price", row.Price, held[5]);
            Check("acquired", row.Acquired ? "1" : "0", held[6]);
            Figure("held after", row.HeldAfter, held[7]);
            Check("planned", filing.Planned is { } planned ? planned ? "1" : "0" : string.Empty, held[8]);
            Check("direct", row.Direct ? "1" : "0", held[9]);
            Check("nature", row.Nature ?? string.Empty, held[10]);
        }

        var first = stored[0];

        Check("filed", Stamp(filing.Filed), first[11]);
        Check("form", filing.Form, first[12]);
        Check("original filed", filing.OriginalFiled is { } original ? Stamp(original) : string.Empty, first[13]);
        Check("owners", string.Join("; ", filing.Owners.Select(owner => owner.Name)), first[14]);
        Check("owner ciks", filing.OwnerKey, first[15]);
        Check("director", filing.Owners.Any(owner => owner.Director) ? "1" : "0", first[16]);
        Check("officer", filing.Owners.Any(owner => owner.Officer) ? "1" : "0", first[17]);
        Check("title", filing.Owners.Select(owner => owner.Title).FirstOrDefault(title => title is not null) ?? string.Empty, first[18]);
        Check("ten percent", filing.Owners.Any(owner => owner.TenPercent) ? "1" : "0", first[19]);
        Check("issuer", filing.IssuerCik, first[20]);

        return found;
    }

    // Whether a filing's rows are stored already, by its accession.
    static async Task<bool> HeldAsync(SqliteConnection connection, SqliteTransaction? transaction, string accession, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, Held, [("$accession", accession)]);

        return await command.ExecuteScalarAsync(cancellation) is not null;
    }

    // A filing's rows as stored, each field as text, as the comparison reads them.
    static async Task<IReadOnlyList<string[]>> StoredAsync(SqliteConnection connection, string accession, CancellationToken cancellation)
    {
        var rows = new List<string[]>();

        await using var command = Command(connection, null, StoredRows, [("$accession", accession)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? string.Empty : Convert.ToString(reader.GetValue(at), CultureInfo.InvariantCulture) ?? string.Empty)]);
        }

        return rows;
    }

    static async Task<IReadOnlySet<string>> CiksAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        var ciks = new HashSet<string>(StringComparer.Ordinal);

        await using var command = Command(connection, null, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            ciks.Add(SecEdgarArchive.Padded(reader.GetString(0)));
        }

        return ciks;
    }

    async Task RecordAsync(SqliteConnection connection, string runId, string stage, DateTimeOffset startedAt, bool whole, int stored, int documents, string detail, CancellationToken cancellation)
    {
        await using var command = Command(connection, null, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", stage),
            ("$started_at", Instant(startedAt)),
            ("$ended_at", Instant(clock.UtcNow)),
            ("$outcome", whole ? "ok" : "partial"),
            ("$rows_written", stored),
            ("$network_requests", documents),
            ("$detail", detail),
        ]);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
