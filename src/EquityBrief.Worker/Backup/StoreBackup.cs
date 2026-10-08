using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.News;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Backup;

public sealed record StoreBackupOutcome(string Outcome, string? Copy, string Detail);

// The store's copy, made once the night, the drain it started and the labeller have finished. It waits while
// the night holds its lock and, started by the night, until the labeller has written its last row or the latest
// instant a labeller started with it could end has passed, its time limit counted over the hours outside the news
// profile's peak windows as the labeller counts it, then holds the drain's lock so no pass writes beneath it, copies the store page by
// page through SQLite's own backup into a file of its own in the copies' folder, names it by its instant once
// written, and opens and reads it against the store. It keeps the newest three of the copies its own rows
// name, opening and reading each before any older one is removed, and removes nothing where one of them does
// not open and read; a copy another store made in the same folder is neither counted nor removed. It gives up
// waiting after twenty hours, well before the next night is built. It writes two rows of its own on the run
// log and nothing else: one as it starts, so a copy ended before it finished leaves a start with no end, and
// one as it ends, naming the folder relative to the data root and never as an absolute path, and naming the
// copies its own earlier rows kept that are gone from the folder, removed by no copy.
// see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
// see: A store's copy counts and removes only the copies its own rows name, and a test or a rehearsal names a copies' folder of its own
// see: The operator's store is never deleted, and every site that removes a file is stated where a check holds it
// see: The news labeller waits for the end of a peak window rather than stopping at one, and its time limit counts the time it labels
public sealed class StoreBackup(IClock clock, string dataRoot, string databaseFile, string folder, TimeSpan labellerLimit, Func<TimeSpan, CancellationToken, Task>? wait = null, EquityBrief.Core.Providers.ResearchPricing? labellerPricing = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "backup";

    // The flag the night starts it with, which has it wait for the labeller the night started.
    public const string AfterTheLabeller = "--after-labeller";

    // The night's own step, which starts it.
    public const string NightStage = "backup";

    public const string RunPrefix = "backup-";

    public const string Copied = "ok";

    public const string NotCopied = "failed";

    // The outcome of the row a copy writes as it starts, before it has waited for anything.
    public const string Started = "started";

    // How long between looks at what still holds the store.
    public static readonly TimeSpan Between = TimeSpan.FromSeconds(30);

    // How long past the labeller's own time limit its end is waited for, its last answer and its row among it.
    public static readonly TimeSpan PastTheLabellersLimit = TimeSpan.FromMinutes(10);

    // The newest session and the bars a store holds, which a copy is read against.
    const string Bars = "SELECT MAX(session_date), COUNT(*) FROM bar;";

    // Whether a labeller has written its run's last row since a moment, which it writes refused or not.
    const string LabellerEnded = "SELECT COUNT(*) FROM run_log WHERE stage = $stage AND started_at >= $since;";

    // The ending rows this store's copies wrote, newest first: each naming the copy it made, kept or refused, and
    // the copies it kept.
    const string OwnRows = "SELECT detail FROM run_log WHERE stage = $stage ORDER BY rowid DESC;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, 0, 0, '0', $detail);
    ";

    // A row's words as written, an apostrophe in a message left as one rather than escaped behind a backslash,
    // which a store row's reader would take for the root of a path.
    static readonly JsonSerializerOptions Written = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly Func<TimeSpan, CancellationToken, Task> pause = wait ?? Task.Delay;

    public static string RunIdAt(DateTimeOffset at) => RunPrefix + at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public async Task<StoreBackupOutcome> RunAsync(bool afterTheLabeller, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var givesUpAt = started + StoreCopies.WaitsAtMost;
        var labellerBy = NewsLabelling.EndsBy(started, labellerLimit, labellerPricing) + PastTheLabellersLimit;
        var waitedFor = new SortedSet<string>(StringComparer.Ordinal);

        // The row that says the copy started, written before any wait under a stage of its own, so a copy ended
        // before it finished is a start the run log holds with no end.
        await RecordAsync(started, started, StoreCopies.StartStage, Started, new { afterTheLabeller }, cancellation);

        // The night, then the labeller the night started, each looked at again until neither holds.
        while (true)
        {
            var holding = new List<string>();

            if (NightLock.Holder(dataRoot) is not null)
            {
                holding.Add("the night");
            }

            if (afterTheLabeller && clock.UtcNow < labellerBy && !await LabellerEndedAsync(started, cancellation))
            {
                holding.Add("the news labeller");
            }

            if (holding.Count == 0)
            {
                break;
            }

            if (clock.UtcNow >= givesUpAt)
            {
                return await NotMadeAsync(started, waitedFor, FormattableString.Invariant($"no copy was made, since {string.Join(" and ", holding)} still held the store after {StoreCopies.WaitsAtMost.TotalHours} hours"), cancellation);
            }

            waitedFor.UnionWith(holding);
            await pause(Between, cancellation);
        }

        // The drain's lock, held while the copy is made so no pass writes beneath it.
        FileStream? drain;

        while ((drain = TheDrainsLock()) is null)
        {
            if (clock.UtcNow >= givesUpAt)
            {
                return await NotMadeAsync(started, waitedFor, FormattableString.Invariant($"no copy was made, since the drain still held the queue after {StoreCopies.WaitsAtMost.TotalHours} hours"), cancellation);
            }

            waitedFor.Add("the drain");
            await pause(Between, cancellation);
        }

        using (drain)
        {
            return await CopyAsync(started, waitedFor, cancellation);
        }
    }

    async Task<StoreBackupOutcome> CopyAsync(DateTimeOffset started, IReadOnlyCollection<string> waitedFor, CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);

        var name = StoreCopies.NameAt(clock.UtcNow);
        var finished = Path.Combine(folder, name);
        var partial = finished + StoreCopies.Unfinished;
        var stored = StoreCopies.AsStored(folder, dataRoot);
        var elsewhere = stored is null ? Path.GetFileName(folder) : null;

        // A copy an earlier run left unfinished holds nothing a copy needs, once it is named for an instant
        // further back than a copy waits; a newer one may be another store's copy still being written.
        foreach (var leftover in Directory.EnumerateFiles(folder, "*" + StoreCopies.Unfinished))
        {
            if (StoreCopies.MadeAt(Path.GetFileName(leftover)[..^StoreCopies.Unfinished.Length]) is { } begun
                && begun < clock.UtcNow - StoreCopies.WaitsAtMost)
            {
                File.Delete(leftover);
            }
        }

        // The copies this store's own rows name, and the ones its newest ending row kept that are gone from the
        // folder before this copy is counted: removed by no copy, since a copy removes only beyond the three it
        // keeps and names each removal, so a copy gone unnamed went by another hand, and the row says so.
        var (own, lastKept) = await OwnCopiesAsync(cancellation);
        var missing = lastKept.Where(kept => !File.Exists(Path.Combine(folder, kept))).ToArray();

        (string? Newest, long Bars) source;

        await using (var store = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await store.OpenAsync(cancellation);
            source = await BarsAsync(store, cancellation);

            // Unpooled, so the file is let go once the copy is written and can be named.
            var target = StoreConnection.Builder(partial);

            target.Pooling = false;

            await using var copy = new SqliteConnection(target.ConnectionString);

            await copy.OpenAsync(cancellation);
            store.BackupDatabase(copy);
        }

        File.Move(partial, finished);

        var check = await CheckAsync(finished, cancellation);

        if (!check.Reads || check.Newest != source.Newest || check.Bars != source.Bars)
        {
            var why = check.Reads
                ? FormattableString.Invariant($"it reads {check.Bars} bars to {check.Newest ?? "none"} where the store holds {source.Bars} to {source.Newest ?? "none"}")
                : check.Said;

            return await RecordAsync(started, NotCopied, new
            {
                copy = name,
                folder = stored,
                elsewhere,
                reason = $"the copy did not open and read as the store does: {why}, so no older copy was removed",
                missing,
                waited = waitedFor,
            }, name, cancellation);
        }

        // The newest three of this store's own copies kept, each opened and read before any older one is removed.
        own.Add(name);

        var copies = StoreCopies.In(folder).Where(one => own.Contains(Path.GetFileName(one.File))).ToArray();
        var removed = new List<string>();
        var unread = new List<string>();

        if (copies.Length > StoreCopies.Kept)
        {
            foreach (var (kept, _) in copies.Take(StoreCopies.Kept).Where(one => !string.Equals(one.File, finished, StringComparison.Ordinal)))
            {
                var keptCheck = await CheckAsync(kept, cancellation);

                if (!keptCheck.Reads)
                {
                    unread.Add(Path.GetFileName(kept) + ", " + keptCheck.Said);
                }
            }

            if (unread.Count == 0)
            {
                foreach (var (old, _) in copies.Skip(StoreCopies.Kept))
                {
                    File.Delete(old);
                    removed.Add(Path.GetFileName(old));
                }
            }
        }

        return await RecordAsync(started, Copied, new
        {
            copy = name,
            folder = stored,
            elsewhere,
            bytes = new FileInfo(finished).Length,
            newest = check.Newest,
            bars = check.Bars,
            kept = StoreCopies.In(folder).Select(one => Path.GetFileName(one.File)).Where(own.Contains).ToArray(),
            removed,
            unread,
            missing,
            waited = waitedFor,
        }, name, cancellation);
    }

    // Whether a copy opens and reads: SQLite's own quick check of its pages, and its bars read back.
    public static async Task<(bool Reads, string Said, string? Newest, long Bars)> CheckAsync(string copy, CancellationToken cancellation = default)
    {
        try
        {
            var reading = StoreConnection.Builder(copy);

            reading.Mode = SqliteOpenMode.ReadOnly;
            reading.Pooling = false;

            await using var connection = new SqliteConnection(reading.ConnectionString);

            await connection.OpenAsync(cancellation);

            await using (var quick = connection.CreateCommand())
            {
                quick.CommandText = "PRAGMA quick_check;";

                var answer = await quick.ExecuteScalarAsync(cancellation);

                if (answer is not "ok")
                {
                    return (false, "its quick check answered " + (answer ?? "nothing"), null, 0);
                }
            }

            var (newest, bars) = await BarsAsync(connection, cancellation);

            return bars > 0 ? (true, "opened and read", newest, bars) : (false, "it holds no bar", newest, bars);
        }
        catch (SqliteException failed)
        {
            return (false, "it did not open: " + failed.Message, null, 0);
        }
    }

    // The night's own row for its step, saying the copy was started or why it was not.
    public static async Task RecordTheNightAsync(string databaseFile, string runId, DateTimeOffset startedAt, DateTimeOffset endedAt, string said)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", NightStage);
        command.Parameters.AddWithValue("$started_at", startedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", endedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$detail", said);

        await command.ExecuteNonQueryAsync();
    }

    static async Task<(string? Newest, long Bars)> BarsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Bars;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) ? (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt64(1)) : (null, 0);
    }

    // The copies this store's own rows name, read off each ending row's copy, whether it was kept or refused, and
    // the copies the newest row that kept any says it kept; a row written as a copy started names none.
    async Task<(HashSet<string> Own, IReadOnlyList<string> LastKept)> OwnCopiesAsync(CancellationToken cancellation)
    {
        var own = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<string>? lastKept = null;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = OwnRows;
        command.Parameters.AddWithValue("$stage", StoreCopies.Stage);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var (copy, kept) = Named(reader.GetString(0));

            if (copy is not null)
            {
                own.Add(copy);
            }

            lastKept ??= kept;
        }

        return (own, lastKept ?? []);
    }

    // The copy an ending row names, and the copies it kept where it kept any.
    static (string? Copy, IReadOnlyList<string>? Kept) Named(string detail)
    {
        try
        {
            using var parsed = JsonDocument.Parse(detail);

            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var copy = parsed.RootElement.TryGetProperty("copy", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() : null;
            var kept = parsed.RootElement.TryGetProperty("kept", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(one => one.ValueKind == JsonValueKind.String).Select(one => one.GetString()!).ToArray()
                : null;

            return (copy, kept);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    async Task<bool> LabellerEndedAsync(DateTimeOffset started, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = LabellerEnded;
        command.Parameters.AddWithValue("$stage", NewsLabelling.Stage);
        command.Parameters.AddWithValue("$since", (started - PastTheLabellersLimit).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) > 0;
    }

    FileStream? TheDrainsLock()
    {
        var drains = Path.Combine(dataRoot, WorkerDrainLauncher.CopiesFolder);

        Directory.CreateDirectory(drains);

        try
        {
            return new FileStream(Path.Combine(drains, DrainLock.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    Task<StoreBackupOutcome> NotMadeAsync(DateTimeOffset started, IReadOnlyCollection<string> waitedFor, string reason, CancellationToken cancellation) =>
        RecordAsync(started, NotCopied, new { reason, waited = waitedFor }, null, cancellation);

    // The copy's ending row, stamped with the instant it ends.
    async Task<StoreBackupOutcome> RecordAsync(DateTimeOffset started, string outcome, object detail, string? copy, CancellationToken cancellation)
    {
        var said = await RecordAsync(started, clock.UtcNow, StoreCopies.Stage, outcome, detail, cancellation);

        return new StoreBackupOutcome(outcome, copy, said);
    }

    // A row of the copy's own under its run and the stage given, the one as it starts stamped with its start at
    // both ends.
    async Task<string> RecordAsync(DateTimeOffset started, DateTimeOffset endedAt, string stage, string outcome, object detail, CancellationToken cancellation)
    {
        var said = JsonSerializer.Serialize(detail, Written);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", RunIdAt(started));
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$started_at", started.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", endedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$detail", said);

        await command.ExecuteNonQueryAsync(cancellation);

        return said;
    }
}
