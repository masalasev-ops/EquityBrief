using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// One company that re-filed under a new number, as the long write read it: the facts it copied from the predecessor, or why none.
public sealed record LongFactJoin(FilerSuccession Succession, int Copied, int InCommon, string? Refused);

// What a long facts write read and wrote.
public sealed record LongFactOutcome(int Filers, int Answered, IReadOnlyList<string> Refused, long Facts, int Filed, IReadOnlyList<LongFactJoin> Joins, string? Earliest, bool Written);

// The long history's facts. Each filer the long price pull matched to its company is asked once for its facts, the whole answer
// kept as the archive sent it, compressed, in a folder before anything reads it; the write reads the folder alone and stores
// each figure of a period ending before the standing table's first, as first filed, in `long_fact`, whole, with its run's
// row. A company that re-filed under a new number has its predecessor's figures copied under its new filer where the two
// agree as the standing join asks. No night, card, hook or standing test reads the table, and no source a rule's version pins
// is touched.
// see: The long history's facts are the SEC's figures before the standing table's first, kept as first filed apart from it
// see: A company that re-filed under a new SEC number carries its predecessor's facts up to the day its new filer's first report starts
public sealed class LongFactPull : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongFact, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongFiling, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.FilingsArchive]);

    public const string Stage = "long-facts";

    public const string FactsFolder = "facts";
    public const string RefusedFile = "refused.txt";

    // The time one ask is taken to cost, the archive's own answer and the pause the feed keeps between two.
    public const double SecondsARequest = 0.7;

    const string SelectFilers = "SELECT DISTINCT cik FROM long_filing ORDER BY cik;";

    const string Clear = "DELETE FROM long_fact;";

    const string InsertFact = @"
        INSERT OR IGNORE INTO long_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id)
        VALUES ($cik, $concept, $period_start, $period_end, $dollars, $filed, $form, $accession, $run_id);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    // A filer's kept answer in the folder.
    public static string FileOf(string folder, string cik) => Path.Combine(folder, FactsFolder, Padded(cik) + ".json.gz");

    static string Padded(string cik) => cik.TrimStart('0').PadLeft(10, '0');

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The filers the long price pull matched to their companies, read off its filings.
    public static async Task<IReadOnlyList<string>> FilersAsync(string databaseFile, CancellationToken cancellation = default)
    {
        var builder = StoreConnection.Builder(databaseFile);
        builder.Mode = SqliteOpenMode.ReadOnly;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectFilers;
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        var filers = new List<string>();

        while (await reader.ReadAsync(cancellation))
        {
            filers.Add(Padded(reader.GetString(0)));
        }

        return [.. filers.Distinct(StringComparer.Ordinal)];
    }

    // The line printed before the first request: the filers to ask, those kept already and the time.
    public static string Stated(int toAsk, int kept) =>
        FormattableString.Invariant($"{toAsk:N0} filer(s) to ask the SEC for their facts, {kept:N0} kept already, one request a filer, about {Math.Ceiling(toAsk * SecondsARequest / 60):N0} minute(s), free");

    // Asks each filer not kept already for its facts and keeps the answer as sent; an answer refused is named in the folder's
    // list of refusals and asked again by the next capture.
    public static async Task<int> CaptureAsync(string folder, IReadOnlyList<string> filers, IFilerFactsFeed feed, Action<string> progress, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Path.Combine(folder, FactsFolder));

        var toAsk = filers.Where(cik => !File.Exists(FileOf(folder, cik))).ToArray();

        progress(Stated(toAsk.Length, filers.Count - toAsk.Length));

        var before = feed.Requests;
        var asked = 0;

        foreach (var cik in toAsk)
        {
            var file = FileOf(folder, cik);
            string answer;

            try
            {
                answer = await feed.FactsAsync(cik, cancellation);
            }
            catch (HttpRequestException refusal)
            {
                await File.AppendAllTextAsync(Path.Combine(folder, RefusedFile), Padded(cik) + " " + refusal.Message + Environment.NewLine, cancellation);

                continue;
            }

            using (var packed = new MemoryStream())
            {
                using (var zip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
                {
                    var bytes = Encoding.UTF8.GetBytes(answer);

                    await zip.WriteAsync(bytes, cancellation);
                }

                await File.WriteAllBytesAsync(file + ".part", packed.ToArray(), cancellation);
            }

            File.Move(file + ".part", file, overwrite: true);

            if (++asked % 100 == 0)
            {
                progress(FormattableString.Invariant($"{asked:N0} of {toAsk.Length:N0} filer(s) asked"));
            }
        }

        return feed.Requests - before;
    }

    // A kept answer as the archive sent it.
    public static string Read(string file)
    {
        using var stream = File.OpenRead(file);
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    // Reads the folder and, where asked, writes the long table whole with its run's row; with nothing asked it writes nothing.
    public static async Task<LongFactOutcome> WriteAsync(string databaseFile, string folder, string runId, IClock clock, bool write, Action<string> progress, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var filers = await FilersAsync(databaseFile, cancellation);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = write ? (SqliteTransaction)await connection.BeginTransactionAsync(cancellation) : null;

        if (write)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = Clear;
            await clear.ExecuteNonQueryAsync(cancellation);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertFact;

        var parameters = new[] { "$cik", "$concept", "$period_start", "$period_end", "$dollars", "$filed", "$form", "$accession", "$run_id" }
            .Select(name => insert.Parameters.Add(name, SqliteType.Text)).ToArray();

        async Task<int> InsertAsync(string cik, FiledFactRow row)
        {
            parameters[0].Value = cik;
            parameters[1].Value = row.Concept;
            parameters[2].Value = Text(row.Start);
            parameters[3].Value = Text(row.End);
            parameters[4].Value = Money.ToStorage(row.Value);
            parameters[5].Value = Text(row.Filed);
            parameters[6].Value = row.Form;
            parameters[7].Value = row.Accession;
            parameters[8].Value = runId;

            return await insert.ExecuteNonQueryAsync(cancellation);
        }

        var (answered, facts, filed, done) = (0, 0L, 0, 0);
        var refused = new List<string>();
        DateOnly? earliest = null;

        foreach (var cik in filers)
        {
            if (++done % 250 == 0)
            {
                progress(FormattableString.Invariant($"{done:N0} of {filers.Count:N0} filer(s) read, {facts:N0} fact(s)"));
            }

            var file = FileOf(folder, cik);

            if (!File.Exists(file))
            {
                refused.Add(cik + ": no answer kept");

                continue;
            }

            IReadOnlyList<FiledFactRow> rows;

            try
            {
                rows = LongFacts.Stored(ConceptAnswers.FromFacts(Read(file), cik, FiledFacts.Concepts));
            }
            catch (Exception failure) when (failure is JsonException or FormatException or InvalidDataException)
            {
                refused.Add(cik + ": " + failure.Message);

                continue;
            }

            answered++;

            if (rows.Count > 0)
            {
                filed++;
                earliest = rows.Min(row => row.End) is var first && (earliest is null || first < earliest) ? first : earliest;
            }

            if (write)
            {
                foreach (var row in rows)
                {
                    facts += await InsertAsync(cik, row);
                }
            }
            else
            {
                facts += rows.Count;
            }
        }

        var joins = new List<LongFactJoin>();

        foreach (var succession in FilerSuccessions.Stated)
        {
            var predecessor = await FiguresAsync(connection, transaction, succession.Predecessor, cancellation);
            var successor = await FiguresAsync(connection, transaction, succession.Successor, cancellation);
            var common = predecessor.Keys.Where(successor.ContainsKey).Select(key => (predecessor[key].Value, successor[key].Value)).ToArray();
            var why = FilerSuccessions.LeftOff.FirstOrDefault(one => one.Ticker == succession.Ticker)?.Why ?? FilerSuccessions.Disagreement(common);
            var copied = 0;

            if (why is null)
            {
                foreach (var ((concept, start, end), figure) in predecessor.Where(one => one.Key.End < LongFacts.Before && !successor.ContainsKey(one.Key)))
                {
                    var row = new FiledFactRow(concept, start, end, figure.Value, figure.Filed, figure.Form, figure.Accession);

                    copied += write ? await InsertAsync(succession.Successor, row) : 1;
                }
            }

            joins.Add(new LongFactJoin(succession, copied, common.Length, why));
            facts += write ? 0 : copied;
        }

        if (write)
        {
            facts += joins.Sum(join => join.Copied);

            await using var run = connection.CreateCommand();
            run.Transaction = transaction;
            run.CommandText = AppendRun;
            run.Parameters.AddWithValue("$run_id", runId);
            run.Parameters.AddWithValue("$stage", Stage);
            run.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$rows", facts);
            run.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new
            {
                filers = filers.Count,
                answered,
                filed,
                facts,
                earliest = earliest is { } day ? Text(day) : null,
                joined = joins.Where(join => join.Refused is null).Select(join => FormattableString.Invariant($"{join.Succession.Ticker} {join.Copied}")).ToArray(),
                refused,
            }));
            await run.ExecuteNonQueryAsync(cancellation);
            await transaction!.CommitAsync(cancellation);
        }

        return new LongFactOutcome(filers.Count, answered, refused, facts, filed, joins, earliest is { } last ? Text(last) : null, write);
    }

    // A filer's figures in either table by concept and period, the long table's first.
    static async Task<Dictionary<(string Concept, DateOnly Start, DateOnly End), (decimal Value, DateOnly Filed, string Form, string Accession)>> FiguresAsync(SqliteConnection connection, SqliteTransaction? transaction, string cik, CancellationToken cancellation)
    {
        var figures = new Dictionary<(string, DateOnly, DateOnly), (decimal, DateOnly, string, string)>();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT concept, period_start, period_end, dollars, filed, form, accession FROM long_fact WHERE cik = $cik
            UNION ALL
            SELECT concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE cik = $cik;";
        command.Parameters.AddWithValue("$cik", cik);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var key = (reader.GetString(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture));

            figures.TryAdd(key, (Money.FromStorage(reader.GetString(3)), DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetString(5), reader.GetString(6)));
        }

        return figures;
    }

    // What a person reads of a write: the filers read and answered, the facts, the earliest period and each join.
    public static IReadOnlyList<string> Lines(LongFactOutcome outcome, bool each)
    {
        var lines = new List<string>
        {
            FormattableString.Invariant($"{outcome.Answered:N0} of {outcome.Filers:N0} filer(s) read, {outcome.Filed:N0} stating a period before {Text(LongFacts.Before)}, {outcome.Facts:N0} fact(s), the earliest period ending {outcome.Earliest ?? "none"}, ") + (outcome.Written ? "written" : "nothing written"),
            FormattableString.Invariant($"{outcome.Refused.Count} filer(s) with no answer read") + (outcome.Refused.Count > 0 ? ": " + string.Join("; ", each ? outcome.Refused : outcome.Refused.Take(10)) : string.Empty),
        };

        lines.AddRange(outcome.Joins.Select(join => join.Refused is { } why
            ? $"{join.Succession.Ticker}: not joined, {why}"
            : FormattableString.Invariant($"{join.Succession.Ticker}: {join.Copied} fact(s) of {join.Succession.Predecessor} copied under {join.Succession.Successor}, {join.InCommon} figure(s) in common")));

        return lines;
    }
}
