using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// What a new reading version carried from the one before it: the readings carried, and the releases left to be read
// again by why: the filing's release is an exhibit the version before did not read, the version before was refused by
// the runtime or not answered in time, a name the anonymiser removes from the release was stored after it was read, its
// filer's names were written again after it was read, and the filings the version before read nothing of.
public sealed record CarryCounts(long From, int Carried, int Replaced, int Runtime, int Named, int NamesWritten, int Unread);

// From the 20.3 correction of 2026-10-22, as a new version is written: the version before's readings carried into it
// where the new version would send each release the request the version before sent, so only what changed is read again.
// see: A new reading version carries each reading whose request it would send unchanged and reads the rest
public sealed partial class LocalRun
{
    const string ReadingsBefore = @"
        SELECT accession, exhibit, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at
        FROM release_reading WHERE pin = $pin;
    ";

    const string CurrentReleases = "SELECT accession, exhibit, cik, text_gzip, text_sha256 FROM release_document WHERE release = 1 ORDER BY cik, accession;";

    const string FilerRowsWritten = "SELECT cik, read_at, names, tickers FROM release_filer;";

    const string TitlesStored = "SELECT cik, title, MIN(read_at) FROM sec_ticker GROUP BY cik, title;";

    // The instant each of a filer's tickers was first stored by any of the three tables the anonymiser reads tickers from,
    // a pulled company by the start of the pull that wrote it.
    const string TickersStored = @"
        SELECT cik, ticker, MIN(read_at) FROM sec_ticker GROUP BY cik, ticker
        UNION ALL SELECT cik, ticker, MIN(fetched_at) FROM company WHERE cik IS NOT NULL GROUP BY cik, ticker
        UNION ALL SELECT p.cik, p.ticker, (SELECT MIN(l.started_at) FROM run_log l WHERE l.run_id = p.pull) FROM pulled_company p WHERE p.cik IS NOT NULL;
    ";

    const string OwnersStored = @"
        SELECT i.owners, MIN(l.started_at)
        FROM (SELECT DISTINCT owners, run_id FROM insider_trade WHERE cik = $cik) i
        LEFT JOIN run_log l ON l.run_id = i.run_id
        GROUP BY i.owners;
    ";

    // A reading under the version before, as the carry copies it.
    sealed record ReadingBefore(string Outcome, string? Cause, string? Results, string? Outlook, string? Tone, string? Demand, double? Seconds, long? PromptTokens, string ReadAt);

    sealed record CarriedReading(string Accession, string Exhibit, ReadingBefore Reading, string RequestSha256);

    // A filer's own row of names and tickers read off its submissions, and the instant it was last written.
    sealed record FilerRow(DateTimeOffset WrittenAt, HashSet<string> Names, HashSet<string> Tickers);

    // The causes an answer is refused for that the request settles, so the same request is refused for them again: those
    // read off the answer. The runtime's refusal, an answer not given in time and one the pin's model did not give are
    // the runtime's on the day, and a text not its hash is the store's, so each is read again.
    static readonly HashSet<string> AnswerCauses = new(
        [ReleaseAnswers.Empty, ReleaseAnswers.CutOff, ReleaseAnswers.NotOneObject, ReleaseAnswers.MissingAnAnswer, ReleaseAnswers.WordNotListed],
        StringComparer.Ordinal);

    // Whether a new version carries the readings of the one before: the same model, its file by its hash, the same
    // runtime and the same seed, answer's budget and context, the version before asked with today's instruction and
    // anonymiser and the new one adding the fit's rule alone, which asks nothing of a release the runtime reads whole.
    public static bool Carries(ReadingPin before, ReadingPin pin) =>
        string.Equals(before.Model, pin.Model, StringComparison.Ordinal)
        && string.Equals(before.File, pin.File, StringComparison.Ordinal)
        && string.Equals(before.Sha256, pin.Sha256, StringComparison.Ordinal)
        && string.Equals(before.RuntimeName, pin.RuntimeName, StringComparison.Ordinal)
        && string.Equals(before.RuntimeVersion, pin.RuntimeVersion, StringComparison.Ordinal)
        && before.Seed == pin.Seed
        && before.AnswerTokens == pin.AnswerTokens
        && before.Context == pin.Context
        && string.Equals(before.PromptVersion, ReleaseQuestions.VersionBeforeTheFit, StringComparison.Ordinal)
        && string.Equals(pin.PromptVersion, ReleaseQuestions.Version, StringComparison.Ordinal);

    // Each filing's release judged alone: carried where the version before read the exhibit the filing holds as its
    // release now, read it or was refused for what its answer said, and no name the anonymiser removes from it was stored
    // after it was read, the filer read as the store held it at that instant and the text made from it the text made now;
    // a filer whose names were written again after the reading left to be read again, since the store keeps no earlier
    // copy of them. Each carried reading keeps the instant it was read and the hash of the request it stands for.
    async Task<(CarryCounts Counts, List<CarriedReading> Rows)> CarryAsync(ReadingPin before, ReadingPin pin, TextWriter output, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);

        var readings = new Dictionary<(string, string), ReadingBefore>();
        var readFilings = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ReadingsBefore;
            command.Parameters.AddWithValue("$pin", before.Id);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                readings[(reader.GetString(0), reader.GetString(1))] = new ReadingBefore(
                    reader.GetString(2),
                    Text(reader, 3),
                    Text(reader, 4),
                    Text(reader, 5),
                    Text(reader, 6),
                    Text(reader, 7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    reader.GetString(10));
                readFilings.Add(reader.GetString(0));
            }
        }

        var filerRows = new Dictionary<string, FilerRow>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FilerRowsWritten;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                filerRows[reader.GetString(0)] = new FilerRow(
                    Parsed(reader.GetString(1))!.Value,
                    new HashSet<string>(JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? [], StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [], StringComparer.Ordinal));
            }
        }

        var titles = await FirstStoredAsync(connection, TitlesStored, StringComparer.OrdinalIgnoreCase, cancellation);
        var tickers = await FirstStoredAsync(connection, TickersStored, StringComparer.Ordinal, cancellation);
        var owners = new Dictionary<string, Dictionary<string, DateTimeOffset?>>(StringComparer.Ordinal);
        var filers = new Dictionary<string, ReleaseFiler>(StringComparer.Ordinal);
        var rows = new List<CarriedReading>();
        var (carried, replaced, runtime, named, written, unread) = (0, 0, 0, 0, 0, 0);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = CurrentReleases;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (accession, exhibit, cik) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));

                if (!readings.TryGetValue((accession, exhibit), out var reading))
                {
                    (replaced, unread) = readFilings.Contains(accession) ? (replaced + 1, unread) : (replaced, unread + 1);

                    continue;
                }

                if (reading.Outcome != Read && !AnswerCauses.Contains(reading.Cause ?? string.Empty))
                {
                    runtime++;

                    continue;
                }

                var readAt = Parsed(reading.ReadAt)!.Value;
                var row = filerRows.GetValueOrDefault(cik);

                if (row is not null && row.WrittenAt > readAt)
                {
                    written++;

                    continue;
                }

                var text = ResultsReleases.Decompressed((byte[])reader[3]);

                if (!string.Equals(ResultsReleases.HashOf(text), reader.GetString(4), StringComparison.Ordinal))
                {
                    runtime++;

                    continue;
                }

                if (!filers.TryGetValue(cik, out var filer))
                {
                    filer = await FilerAsync(cik, cancellation);
                    filers[cik] = filer;
                }

                if (!owners.TryGetValue(cik, out var people))
                {
                    people = await OwnersFirstStoredAsync(connection, cik, cancellation);
                    owners[cik] = people;
                }

                var now = ReleaseAnonymiser.Anonymise(text, filer);

                if (StoredAfter(filer, row, cik, readAt, titles, tickers, people)
                    && !string.Equals(ReleaseAnonymiser.Anonymise(text, AsOf(filer, row, cik, readAt, titles, tickers, people)), now, StringComparison.Ordinal))
                {
                    named++;

                    continue;
                }

                rows.Add(new CarriedReading(accession, exhibit, reading, LmStudioReadingFeed.BodyHash(new ReadingRequest(pin.Model, ReleaseQuestions.Instruction, now, pin.Seed, pin.AnswerTokens))));
                carried++;
            }
        }

        var counts = new CarryCounts(before.Id, carried, replaced, runtime, named, written, unread);

        await output.WriteLineAsync(Carried(counts));

        return (counts, rows);
    }

    public static string Carried(CarryCounts counts) =>
        FormattableString.Invariant(
            $"{Stage}: carried {counts.Carried} reading(s) from pin {counts.From}; to read again, {counts.Replaced + counts.Runtime} for a changed document or the runtime's refusal of its length ({counts.Replaced} an exhibit pin {counts.From} did not read, {counts.Runtime} refused by the runtime), {counts.Named + counts.NamesWritten} for a name stored after the reading ({counts.Named} changing the text it reads, {counts.NamesWritten} whose filer's names were written again after it), and {counts.Unread} filing(s) pin {counts.From} read nothing of");

    // Whether a name or ticker the anonymiser reads for the filer from outside its own row, or an insider, was first
    // stored after the instant, or at an instant the store does not hold; the caller has found the row written by then.
    static bool StoredAfter(
        ReleaseFiler filer,
        FilerRow? row,
        string cik,
        DateTimeOffset at,
        IReadOnlyDictionary<string, Dictionary<string, DateTimeOffset?>> titles,
        IReadOnlyDictionary<string, Dictionary<string, DateTimeOffset?>> tickers,
        IReadOnlyDictionary<string, DateTimeOffset?> people) =>
        filer.Names.Any(name => Later(name, row?.Names, titles, cik, at))
        || filer.Tickers.Any(ticker => Later(ticker, row?.Tickers, tickers, cik, at))
        || filer.People.Any(owner => !Known(owner, people, at));

    static bool Later(string value, HashSet<string>? row, IReadOnlyDictionary<string, Dictionary<string, DateTimeOffset?>> stored, string cik, DateTimeOffset at) =>
        row?.Contains(value) != true
        && stored.TryGetValue(cik, out var values) && values.TryGetValue(value, out var first) && (first is not { } instant || instant > at);

    static bool Known(string owner, IReadOnlyDictionary<string, DateTimeOffset?> people, DateTimeOffset at) =>
        !people.TryGetValue(owner, out var first) || first is { } stored && stored <= at;

    // The filer as the store held it at the instant, its lists in the order the reading took them: what its own row
    // holds, which the caller has found written by then, and the titles, tickers and insiders first stored by then.
    static ReleaseFiler AsOf(
        ReleaseFiler filer,
        FilerRow? row,
        string cik,
        DateTimeOffset at,
        IReadOnlyDictionary<string, Dictionary<string, DateTimeOffset?>> titles,
        IReadOnlyDictionary<string, Dictionary<string, DateTimeOffset?>> tickers,
        IReadOnlyDictionary<string, DateTimeOffset?> people) =>
        new(
            [.. filer.Names.Where(name => !Later(name, row?.Names, titles, cik, at))],
            [.. filer.Tickers.Where(ticker => !Later(ticker, row?.Tickers, tickers, cik, at))],
            [.. filer.People.Where(owner => Known(owner, people, at))]);

    // Each filer's values a query names with the instant each was first stored, the earliest where a value is named twice,
    // none where an instant is not stored.
    static async Task<Dictionary<string, Dictionary<string, DateTimeOffset?>>> FirstStoredAsync(SqliteConnection connection, string sql, StringComparer comparer, CancellationToken cancellation)
    {
        var stored = new Dictionary<string, Dictionary<string, DateTimeOffset?>>(StringComparer.Ordinal);

        foreach (var (cik, value, at) in await TriplesAsync(connection, sql, cancellation))
        {
            if (!stored.TryGetValue(cik, out var values))
            {
                stored[cik] = values = new Dictionary<string, DateTimeOffset?>(comparer);
            }

            var instant = Parsed(at);

            values[value] = !values.TryGetValue(value, out var held) ? instant
                : held is not { } earlier || instant is not { } later ? null
                : earlier < later ? earlier : later;
        }

        return stored;
    }

    static async Task<Dictionary<string, DateTimeOffset?>> OwnersFirstStoredAsync(SqliteConnection connection, string cik, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = OwnersStored;
        command.Parameters.AddWithValue("$cik", cik);

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var people = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            if (!reader.IsDBNull(0))
            {
                people[reader.GetString(0)] = reader.IsDBNull(1) ? null : Parsed(reader.GetString(1));
            }
        }

        return people;
    }

    static async Task<List<(string First, string Second, string? Third)>> TriplesAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var triples = new List<(string, string, string?)>();

        while (await reader.ReadAsync(cancellation))
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                triples.Add((reader.GetString(0), reader.GetString(1), reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetString(2) : null));
            }
        }

        return triples;
    }

    // The carried readings written under the new version in the write that writes it, each keeping the instant the
    // version before read it, the hash of its request and the version it came from.
    async Task<int> WriteCarriedAsync(SqliteConnection connection, long pinId, long from, IReadOnlyList<CarriedReading> rows, CancellationToken cancellation)
    {
        var written = 0;

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            var reading = row.Reading;

            command.CommandText = InsertReading;
            command.Parameters.AddWithValue("$accession", row.Accession);
            command.Parameters.AddWithValue("$exhibit", row.Exhibit);
            command.Parameters.AddWithValue("$pin", pinId);
            command.Parameters.AddWithValue("$outcome", reading.Outcome);
            command.Parameters.AddWithValue("$cause", (object?)reading.Cause ?? DBNull.Value);
            command.Parameters.AddWithValue("$results", (object?)reading.Results ?? DBNull.Value);
            command.Parameters.AddWithValue("$outlook", (object?)reading.Outlook ?? DBNull.Value);
            command.Parameters.AddWithValue("$tone", (object?)reading.Tone ?? DBNull.Value);
            command.Parameters.AddWithValue("$demand", (object?)reading.Demand ?? DBNull.Value);
            command.Parameters.AddWithValue("$seconds", (object?)reading.Seconds ?? DBNull.Value);
            command.Parameters.AddWithValue("$prompt_tokens", (object?)reading.PromptTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$cut_at", DBNull.Value);
            command.Parameters.AddWithValue("$request_sha256", row.RequestSha256);
            command.Parameters.AddWithValue("$carried_from", from);
            command.Parameters.AddWithValue("$read_at", reading.ReadAt);
            command.Parameters.AddWithValue("$run_id", runId);

            written += await command.ExecuteNonQueryAsync(cancellation);
        }

        return written;
    }

    static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    static DateTimeOffset? Parsed(string? text) =>
        text is null ? null : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
