using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// What one local run came to, as its run log row states it: its kind, its outcome with why where it did not finish,
// the documents it fetched and read, the answers it counted refused by cause, its median and slowest seconds a call,
// the pin it read, the check of the model's file it made and how long a whole hash took, and for the twice-read the
// share of the releases answered alike.
public sealed record LocalRunOutcome(
    string Kind,
    string Outcome,
    string? Why,
    int Fetched,
    int Read,
    IReadOnlyDictionary<string, int> Refused,
    double? MedianSeconds,
    double? SlowestSeconds,
    ReadingPin? Pin,
    string? FileCheck,
    double? HashSeconds,
    int Compared = 0,
    int Identical = 0,
    int Requests = 0);

// The model's file as the local run reads it off the disk: its path under LM Studio's models folder, which is what the
// pin and the run's row hold, its size and its last-modified instant.
public sealed record ModelFileFacts(string Relative, long Size, DateTimeOffset Modified);

// The model's file under LM Studio's models folder: the folder the profile names, or the one under the machine's home,
// resolved through the platform's own folders, and the file by the forward-slashed path the profile names under it.
public static class LocalModelFile
{
    public static string FolderOf(LocalModelSettings settings) =>
        settings.ModelsFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "models");

    public static string PathOf(string folder, string relative) =>
        Path.Combine([folder, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

    public static ModelFileFacts? Facts(string folder, string relative)
    {
        var path = PathOf(folder, relative);

        if (!File.Exists(path))
        {
            return null;
        }

        var file = new FileInfo(path);

        return new ModelFileFacts(relative, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
    }

    // The file's SHA-256 over every byte, in lower-case hex.
    public static async Task<string> HashAsync(string folder, string relative, CancellationToken cancellation)
    {
        await using var stream = File.OpenRead(PathOf(folder, relative));

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation));
    }
}

// One local process at a time calls the local model, by a lock file of its own in the copies' folder that no store
// reader takes; and the locks the local run waits on before it writes and never takes: the night's and the drain's,
// which the monthly run holds for each step.
public static class LocalLocks
{
    public const string FileName = "local.lock";

    // The local lock, or none where another local process holds it past the instant given.
    public static async Task<FileStream?> TakeAsync(string dataRoot, DateTimeOffset? until, IClock clock, Func<TimeSpan, CancellationToken, Task> pause, CancellationToken cancellation)
    {
        var folder = Path.Combine(dataRoot, WorkerDrainLauncher.CopiesFolder);

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, FileName);

        while (true)
        {
            cancellation.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (until is not { } last || clock.UtcNow < last)
            {
                await pause(LocalRun.Poll, cancellation);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    // What holds a lock the local run waits on, or none: a night, or the drain's lock, which a drain and each step of
    // the monthly run hold.
    public static string? Holder(string dataRoot)
    {
        if (NightLock.Holder(dataRoot) is { } night)
        {
            return night;
        }

        var drain = Path.Combine(dataRoot, WorkerDrainLauncher.CopiesFolder, DrainLock.FileName);

        if (!File.Exists(drain))
        {
            return null;
        }

        try
        {
            using var probe = new FileStream(drain, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return "the drain or the monthly run";
        }
    }
}

// The local run. A process of its own, never inside the night, started by tools/localrun at its two slots and by hand:
// before it reads anything it checks the newest pin, the model the profile names, the model's file by its whole hash
// or by its size and modified time as the pin's rule says, and the instruction's version, and refuses naming the part
// that differs; it loads the model where it is not loaded and unloads it at its end only where it loaded it; it asks
// one reading at a time, reading the answering model and the runtime off each answer and refusing the run where either
// is not the pin's; an answer it cannot read is asked once more and then counted refused with its cause; it holds none
// of the night's, the drain's or the monthly run's locks and waits to write while one is held, one local process at a
// time by a lock of its own; and it writes one run log row a run, every call on its own row and never on a night's.
// see: The local run is a process of its own at two slots outside the night and refuses any model file, runtime or prompt but its pin
public sealed partial class LocalRun(
    IReadingModelFeed feed,
    LocalModelSettings settings,
    IClock clock,
    string databaseFile,
    string dataRoot,
    Func<TimeSpan, CancellationToken, Task>? pause = null,
    ReleaseFetch? fetch = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ReadingPin, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read),
            new StoreTouch(Store.ReleaseFiler, Touch.Read),
            new StoreTouch(Store.ReleaseReading, Touch.Read | Touch.Insert),
            new StoreTouch(Store.ReleaseProbe, Touch.Read | Touch.Insert),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.SecTicker, Touch.Read),
            new StoreTouch(Store.InsiderTrade, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Stage = LocalSlots.Stage;

    // The kinds of run a row names.
    public const string EarlySlot = "early slot";
    public const string LateSlot = "late slot";
    public const string Pinning = "pin";
    public const string Twice = "twice";

    // What a run came to, beside the runtime's own three.
    public const string Finished = "finished";
    public const string NothingToRead = "nothing to read";
    public const string NoPin = "no pin";
    public const string PinRefused = "pin refused";
    public const string PinUnchanged = "pin unchanged";
    public const string NotSet = "not set";
    public const string LimitReached = "limit reached";
    public const string LockHeld = "lock held";
    public const string FetchRefused = "fetch refused";

    // How long a run waits between looks at a held lock.
    public static TimeSpan Poll { get; } = TimeSpan.FromSeconds(30);

    // The probe the pin asks to read the runtime off its answer.
    public const string ProbeInstruction = "Reply with the one word ready.";
    public const string ProbeText = "Are you ready?";
    public const int ProbeTokens = 16;

    readonly Func<TimeSpan, CancellationToken, Task> wait = pause ?? ((span, token) => Task.Delay(span, token));

    // The results releases a research pass stored, by the titles the research runner gives them, which the twice-read
    // reads whole.
    const string StoredReleases = @"
        SELECT id, body FROM source_document
        WHERE body IS NOT NULL AND (title LIKE 'Results release, %' OR title LIKE 'Previous results release, %')
        ORDER BY id;
    ";

    const string NewestPin = @"
        SELECT id, model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version,
               seed, answer_tokens, context, cutoff, written_at, history_completed_at
        FROM reading_pin ORDER BY id DESC LIMIT 1;
    ";

    const string InsertPin = @"
        INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version,
                                 temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id)
        VALUES ($model, $model_file, $sha256, $size, $modified, $runtime_name, $runtime_version, $prompt_version,
                $temperature, $seed, $answer_tokens, $context, $cutoff, $written_at, NULL, $run_id);
    ";

    // From 20.3 the history read marks its pin complete once, where no stored release is left unread under it.
    const string CompleteHistory = "UPDATE reading_pin SET history_completed_at = $at WHERE id = $id AND history_completed_at IS NULL;";

    // From 20.3 a release's reading under a pin, kept once, its four words or the cause it was refused for.
    const string InsertReading = @"
        INSERT INTO release_reading (accession, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id)
        VALUES ($accession, $pin, $outcome, $cause, $results, $outlook, $tone, $demand, $seconds, $prompt_tokens, $read_at, $run_id)
        ON CONFLICT (accession, pin) DO NOTHING;
    ";

    // From 20.3 a probe's answer over one release under a pin, kept once beside what it is set against.
    const string InsertProbe = @"
        INSERT INTO release_probe (kind, accession, pin, outcome, cause, answer, against, matched, seconds, read_at, run_id)
        VALUES ($kind, $accession, $pin, $outcome, $cause, $answer, $against, $matched, $seconds, $read_at, $run_id)
        ON CONFLICT (kind, accession, pin) DO NOTHING;
    ";

    // The newest start of a local run that hashed the model's file whole and found it the pin's.
    const string LastHashedWhole = @"
        SELECT MAX(started_at) FROM run_log
        WHERE stage = '" + Stage + @"'
          AND json_extract(detail, '$.fileCheck') = '" + ReadingPins.WholeHash + @"'
          AND outcome <> '" + PinRefused + @"';
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, $model_calls, $network_requests, '0', $detail);
    ";

    // A run's id: the stage, the instant it started, its kind and six hex characters of its own, so two runs started in
    // one second, a slot and a run by hand among them, each write a row of their own.
    public static string RunIdFor(DateTimeOffset startedAt, string kind) =>
        Stage + "-" + startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + kind.Replace(' ', '-') + "-" + Guid.NewGuid().ToString("n")[..6];

    // The run this instance is running, set as each form starts.
    string runId = string.Empty;

    // A slot: from 20.2 the releases its members filed since the last day read fetched and stored, then the newest pin
    // checked before anything is read, and from 20.3 the releases read. A slot asks nothing past its own last instant.
    public async Task<LocalRunOutcome> SlotAsync(string slot, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var kind = slot == LocalSlots.Late ? LateSlot : EarlySlot;
        var limit = LocalSlots.AsksNothingAfter(slot, DateOnly.FromDateTime(started.UtcDateTime));

        runId = RunIdFor(started, kind);

        await using var held = await LocalLocks.TakeAsync(dataRoot, limit, clock, wait, cancellation);

        if (held is null)
        {
            return await EndAsync(started, kind, Ended(kind, LockHeld, "another local process held the local model's lock to the slot's last instant"), limit, output, cancellation);
        }

        var fetched = fetch is null ? null : await fetch.SlotAsync(slot, runId, limit, cancellation);
        var (stored, requests) = (fetched?.Stored ?? 0, fetched?.Documents ?? 0);
        var ready = await ReadyAsync(started, cancellation);

        if (ready.Refusal is { } refused)
        {
            return await EndAsync(started, kind, refused with { Kind = kind, Fetched = stored, Requests = requests }, limit, output, cancellation);
        }

        // From 20.3 the releases accepted in the week before the slot's day that no reading under the pin holds.
        var recent = await ReadRecentAsync(ready.Pin!, started, limit, cancellation);
        var kept = recent.Read + recent.Refused.Values.Sum();

        if (recent.Ended is { } ended)
        {
            return await EndAsync(
                started,
                kind,
                ended with { Kind = kind, Fetched = stored, Requests = requests, Read = recent.Read, Refused = recent.Refused, Pin = ready.Pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds },
                limit,
                output,
                cancellation,
                rowsWritten: kept);
        }

        var outcome = fetched is null && kept == 0
            ? new LocalRunOutcome(kind, NothingToRead, "no stored release is waiting for a reading under the pin", 0, 0, NoRefusals, null, null, ready.Pin, ready.Check, ready.HashSeconds)
            : new LocalRunOutcome(
                kind,
                fetched?.Refusal is not null ? FetchRefused : recent.Stopped ? LimitReached : Finished,
                fetched?.Refusal ?? (recent.Stopped ? "the slot's last instant passed before every release was read" : fetched?.Stopped),
                stored,
                recent.Read,
                recent.Refused,
                Median(recent.Seconds),
                recent.Seconds.Count == 0 ? null : recent.Seconds.Max(),
                ready.Pin,
                ready.Check,
                ready.HashSeconds,
                Requests: requests);

        return await EndAsync(started, kind, outcome, limit, output, cancellation, rowsWritten: kept);
    }

    // A new reading version: the model's file hashed whole, the runtime read off a probe's answer and the
    // instruction's version, written where any part differs from the newest pin and refused where none does.
    public async Task<LocalRunOutcome> PinAsync(TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        runId = RunIdFor(started, Pinning);

        await using var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation);

        if (settings.ModelFile is not { } relative || settings.KnowledgeCutoff is not { } cutoff)
        {
            return await EndAsync(started, Pinning, Ended(Pinning, NotSet, $"the local profile {settings.Profile} names no {LocalModelSettings.ModelFileName} or no {LocalModelSettings.KnowledgeCutoffName}"), null, output, cancellation);
        }

        var folder = LocalModelFile.FolderOf(settings);

        if (LocalModelFile.Facts(folder, relative) is not { } facts)
        {
            return await EndAsync(started, Pinning, Ended(Pinning, ReadingRuntimeUnavailable.ModelNotHeld, $"no file {relative} stands under LM Studio's models folder"), null, output, cancellation);
        }

        var hashing = Stopwatch.StartNew();
        var sha = await LocalModelFile.HashAsync(folder, relative, cancellation);
        var hashSeconds = hashing.Elapsed.TotalSeconds;

        bool loaded;
        ReadingReply probe;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(started, Pinning, Ended(Pinning, gone.Outcome, gone.Message) with { FileCheck = ReadingPins.WholeHash, HashSeconds = hashSeconds }, null, output, cancellation);
        }

        try
        {
            probe = await feed.AskAsync(new ReadingRequest(settings.Model, ProbeInstruction, ProbeText, ReadingSettings.Seed, ProbeTokens), cancellation);
        }
        catch (Exception refused) when (refused is ReadingRefused or ReadingRuntimeUnavailable)
        {
            await feed.ReleaseAsync(loaded, cancellation);

            return await EndAsync(started, Pinning, Ended(Pinning, refused is ReadingRuntimeUnavailable gone ? gone.Outcome : ReleaseAnswers.RuntimeRefused, refused.Message) with { FileCheck = ReadingPins.WholeHash, HashSeconds = hashSeconds }, null, output, cancellation);
        }

        await feed.ReleaseAsync(loaded, cancellation);

        if (!string.Equals(probe.Model, settings.Model, StringComparison.Ordinal) || probe.RuntimeName is not { } runtime || probe.RuntimeVersion is not { } version)
        {
            return await EndAsync(started, Pinning, Ended(Pinning, PinRefused, $"the probe was answered by {probe.Model ?? "no model"} on {probe.RuntimeName ?? "no runtime"}, so no version is written for {settings.Model}") with { FileCheck = ReadingPins.WholeHash, HashSeconds = hashSeconds }, null, output, cancellation);
        }

        var candidate = new ReadingPin(
            0, settings.Model, relative, sha, facts.Size, facts.Modified, runtime, version, ReleaseQuestions.Version,
            ReadingSettings.Seed, ReadingSettings.AnswerTokens, settings.ContextTokens, cutoff, started, null);

        await using var connection = await OpenAsync(cancellation);

        if (await NewestPinAsync(connection, cancellation) is { } newest && Same(newest, candidate))
        {
            return await EndAsync(started, Pinning, Ended(Pinning, PinUnchanged, FormattableString.Invariant($"pin {newest.Id} holds this model, its file, the runtime and the prompt already, so no version is written")) with { Pin = newest, FileCheck = ReadingPins.WholeHash, HashSeconds = hashSeconds }, null, output, cancellation);
        }

        await WaitToWriteAsync(null, output, cancellation);

        var run = runId;
        int inserted;

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = InsertPin;
            insert.Parameters.AddWithValue("$model", candidate.Model);
            insert.Parameters.AddWithValue("$model_file", candidate.File);
            insert.Parameters.AddWithValue("$sha256", candidate.Sha256);
            insert.Parameters.AddWithValue("$size", candidate.Size);
            insert.Parameters.AddWithValue("$modified", Precise(candidate.Modified));
            insert.Parameters.AddWithValue("$runtime_name", candidate.RuntimeName);
            insert.Parameters.AddWithValue("$runtime_version", candidate.RuntimeVersion);
            insert.Parameters.AddWithValue("$prompt_version", candidate.PromptVersion);
            insert.Parameters.AddWithValue("$temperature", ReadingSettings.Temperature);
            insert.Parameters.AddWithValue("$seed", candidate.Seed);
            insert.Parameters.AddWithValue("$answer_tokens", candidate.AnswerTokens);
            insert.Parameters.AddWithValue("$context", candidate.Context);
            insert.Parameters.AddWithValue("$cutoff", candidate.Cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$written_at", Instant(started));
            insert.Parameters.AddWithValue("$run_id", run);

            inserted = await insert.ExecuteNonQueryAsync(cancellation);
        }

        var written = await NewestPinAsync(connection, cancellation);

        return await EndAsync(started, Pinning, new LocalRunOutcome(Pinning, Finished, null, 0, 0, NoRefusals, null, null, written, ReadingPins.WholeHash, hashSeconds), null, output, cancellation, rowsWritten: inserted);
    }

    // The stored results releases read twice under the pin, a whole pass and then a second, and how many were answered
    // alike: the same four words both times, or refused both times for the same cause.
    public async Task<LocalRunOutcome> TwiceAsync(TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        runId = RunIdFor(started, Twice);

        await using var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation);

        var ready = await ReadyAsync(started, cancellation);

        if (ready.Refusal is { } refused)
        {
            return await EndAsync(started, Twice, refused with { Kind = Twice }, null, output, cancellation);
        }

        var pin = ready.Pin!;
        var releases = new List<(string Id, string Text)>();

        await using (var connection = await OpenAsync(cancellation))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = StoredReleases;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                releases.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        if (releases.Count == 0)
        {
            return await EndAsync(started, Twice, new LocalRunOutcome(Twice, NothingToRead, "the store holds no results release a research pass stored", 0, 0, NoRefusals, null, null, pin, ready.Check, ready.HashSeconds), null, output, cancellation);
        }

        bool loaded;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(started, Twice, Ended(Twice, gone.Outcome, gone.Message) with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds }, null, output, cancellation);
        }

        var seconds = new List<double>();
        var causes = new Dictionary<string, int>(StringComparer.Ordinal);
        var passes = new List<IReadOnlyList<ReleaseAnswerRead>>();

        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var answers = new List<ReleaseAnswerRead>();

                foreach (var (_, text) in releases)
                {
                    var read = await ReadOnceAsync(pin, text, seconds, cancellation);

                    if (read.Cause is { } cause)
                    {
                        causes[cause] = causes.GetValueOrDefault(cause) + 1;
                    }

                    answers.Add(read);
                }

                passes.Add(answers);
            }
        }
        catch (PinBroken broken)
        {
            await feed.ReleaseAsync(loaded, cancellation);

            return await EndAsync(started, Twice, Ended(Twice, PinRefused, broken.Message) with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds }, null, output, cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(started, Twice, Ended(Twice, gone.Outcome, gone.Message) with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds }, null, output, cancellation);
        }

        await feed.ReleaseAsync(loaded, cancellation);

        // A release is read where either pass read it into its four words; the answers refused are counted by cause.
        var identical = passes[0].Zip(passes[1]).Count(pair => Alike(pair.First, pair.Second));
        var readings = passes[0].Zip(passes[1]).Count(pair => pair.First.Answer is not null || pair.Second.Answer is not null);

        return await EndAsync(
            started,
            Twice,
            new LocalRunOutcome(Twice, Finished, null, 0, readings, causes, Median(seconds), seconds.Count == 0 ? null : seconds.Max(), pin, ready.Check, ready.HashSeconds, releases.Count, identical),
            null,
            output,
            cancellation);
    }

    // Two answers alike: the same four words, or refused for the same cause.
    public static bool Alike(ReleaseAnswerRead first, ReleaseAnswerRead second) =>
        first.Answer is { } one && second.Answer is { } two
            ? one == two
            : first.Answer is null && second.Answer is null && string.Equals(first.Cause, second.Cause, StringComparison.Ordinal);

    // One reading of one document: asked once more after an answer that cannot be read, and refused at once where the
    // runtime refuses it or does not answer in time; an answer neither the pin's model nor its runtime gave ends the run.
    async Task<ReleaseAnswerRead> ReadOnceAsync(ReadingPin pin, string text, List<double> seconds, CancellationToken cancellation)
    {
        ReleaseAnswerRead read = new(null, ReleaseAnswers.Empty);

        for (var ask = 0; ask <= ReadingSettings.AskedAgain; ask++)
        {
            ReadingReply reply;

            try
            {
                reply = await feed.AskAsync(new ReadingRequest(pin.Model, ReleaseQuestions.Instruction, text, pin.Seed, pin.AnswerTokens), cancellation);
            }
            catch (ReadingRefused refused)
            {
                return new ReleaseAnswerRead(null, refused.Cause);
            }

            seconds.Add(reply.Seconds);

            if (ReadingPins.AnswerDiffers(pin, reply.Model, reply.RuntimeName, reply.RuntimeVersion) is { } part)
            {
                throw new PinBroken($"an answer came from {part} the pin does not name: {reply.Model ?? "no model"} on {reply.RuntimeName ?? "no runtime"} {reply.RuntimeVersion}");
            }

            read = ReleaseAnswers.Read(reply.Content, reply.FinishReason);

            if (read.Answer is not null)
            {
                return read;
            }
        }

        return read;
    }

    sealed class PinBroken(string message) : Exception(message);

    // Whether the run may read: the profile's file and cutoff named, a pin written, the model, the file and the
    // instruction the pin's, the file hashed whole where the pin's rule says.
    async Task<(ReadingPin? Pin, string? Check, double? HashSeconds, LocalRunOutcome? Refusal)> ReadyAsync(DateTimeOffset started, CancellationToken cancellation)
    {
        if (settings.ModelFile is not { } relative || settings.KnowledgeCutoff is null)
        {
            return (null, null, null, Ended(string.Empty, NotSet, $"the local profile {settings.Profile} names no {LocalModelSettings.ModelFileName} or no {LocalModelSettings.KnowledgeCutoffName}"));
        }

        await using var connection = await OpenAsync(cancellation);

        if (await NewestPinAsync(connection, cancellation) is not { } pin)
        {
            return (null, null, null, Ended(string.Empty, NoPin, "no reading version is pinned yet; write one with the pin by hand"));
        }

        var folder = LocalModelFile.FolderOf(settings);

        if (LocalModelFile.Facts(folder, relative) is not { } facts)
        {
            return (pin, null, null, Ended(string.Empty, ReadingRuntimeUnavailable.ModelNotHeld, $"no file {relative} stands under LM Studio's models folder") with { Pin = pin });
        }

        DateTimeOffset? last = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LastHashedWhole;

            if (await command.ExecuteScalarAsync(cancellation) is string at)
            {
                last = DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            }
        }

        string? sha = null;
        double? hashSeconds = null;
        var check = ReadingPins.SizeAndTime;

        if (ReadingPins.HashesWhole(pin, facts.Size, facts.Modified, last, started))
        {
            var hashing = Stopwatch.StartNew();

            sha = await LocalModelFile.HashAsync(folder, relative, cancellation);
            hashSeconds = hashing.Elapsed.TotalSeconds;
            check = ReadingPins.WholeHash;
        }

        if (ReadingPins.Differs(pin, settings.Model, sha, facts.Size, facts.Modified, ReleaseQuestions.Version) is { } part)
        {
            return (pin, check, hashSeconds, Ended(string.Empty, PinRefused, $"{part} is not pin {pin.Id}'s") with { Pin = pin, FileCheck = check, HashSeconds = hashSeconds });
        }

        return (pin, check, hashSeconds, null);
    }

    static LocalRunOutcome Ended(string kind, string outcome, string why) =>
        new(kind, outcome, why, 0, 0, NoRefusals, null, null, null, null, null);

    static readonly IReadOnlyDictionary<string, int> NoRefusals = new Dictionary<string, int>(StringComparer.Ordinal);

    // Waits while the night, the drain or the monthly run holds its lock, up to the instant given where there is one.
    async Task<bool> WaitToWriteAsync(DateTimeOffset? until, TextWriter output, CancellationToken cancellation)
    {
        while (LocalLocks.Holder(dataRoot) is { } holder)
        {
            if (until is { } last && clock.UtcNow >= last)
            {
                return false;
            }

            await output.WriteLineAsync($"{Stage}: waiting to write while {holder} holds its lock");
            await wait(Poll, cancellation);
        }

        return true;
    }

    // The run's one row, written once any lock it waits on is let go, and its line on the output.
    async Task<LocalRunOutcome> EndAsync(DateTimeOffset started, string kind, LocalRunOutcome outcome, DateTimeOffset? limit, TextWriter output, CancellationToken cancellation, int rowsWritten = 0)
    {
        var ended = outcome with { Kind = kind };

        if (!await WaitToWriteAsync(limit is { } last ? last.AddMinutes(5) : null, output, cancellation))
        {
            ended = ended with { Outcome = LimitReached, Why = "the slot ended waiting to write while another held its lock" };
        }

        await using var connection = await OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Instant(started));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", ended.Outcome);
        command.Parameters.AddWithValue("$rows_written", rowsWritten + ended.Fetched);
        command.Parameters.AddWithValue("$model_calls", feed.Requests);
        command.Parameters.AddWithValue("$network_requests", ended.Requests);
        command.Parameters.AddWithValue("$detail", Detail(ended));

        await command.ExecuteNonQueryAsync(cancellation);

        await output.WriteLineAsync($"{Stage}: {ended.Kind} came to {ended.Outcome}" + (ended.Why is { } why ? ": " + why : string.Empty) + Said(ended));

        return ended;
    }

    static string Said(LocalRunOutcome outcome) =>
        outcome.Compared > 0
            ? FormattableString.Invariant($"; {outcome.Identical} of {outcome.Compared} release(s) answered alike when read twice")
            : string.Empty;

    // The row's detail, which the Run page draws: what the run was and came to, its counts, its times, the pin and the
    // file check. It carries no path of any file.
    public static string Detail(LocalRunOutcome outcome) =>
        JsonSerializer.Serialize(new
        {
            kind = outcome.Kind,
            outcome = outcome.Outcome,
            why = outcome.Why,
            fetched = outcome.Fetched,
            requests = outcome.Requests,
            read = outcome.Read,
            refused = outcome.Refused,
            medianSeconds = outcome.MedianSeconds,
            slowestSeconds = outcome.SlowestSeconds,
            pin = outcome.Pin?.Id,
            model = outcome.Pin?.Model,
            hash = outcome.Pin?.Sha256[..12],
            runtime = outcome.Pin is { } pin ? pin.RuntimeName + " " + pin.RuntimeVersion : null,
            prompt = outcome.Pin?.PromptVersion,
            fileCheck = outcome.FileCheck,
            hashSeconds = outcome.HashSeconds,
            compared = outcome.Compared,
            identical = outcome.Identical,
        });

    static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();

        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    static bool Same(ReadingPin one, ReadingPin two) =>
        one.Model == two.Model && one.File == two.File && one.Sha256 == two.Sha256 && one.Size == two.Size
        && one.Modified.UtcTicks == two.Modified.UtcTicks && one.RuntimeName == two.RuntimeName && one.RuntimeVersion == two.RuntimeVersion
        && one.PromptVersion == two.PromptVersion && one.Seed == two.Seed && one.AnswerTokens == two.AnswerTokens
        && one.Context == two.Context && one.Cutoff == two.Cutoff;

    async Task<SqliteConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        return connection;
    }

    static async Task<ReadingPin?> NewestPinAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestPin;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new ReadingPin(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                DateOnly.ParseExact(reader.GetString(12), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal))
            : null;
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    // The model file's modified instant to its tick, so a file written again within the same second is not read as
    // the one the pin holds.
    static string Precise(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}
