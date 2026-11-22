using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Judge;

// One of the judge's picks as stored: its rank, the member, the menu's choices, its reason, the last close and typical
// daily move code worked its prices from, those prices, and from 24.14 the keys of the facts it named as deciding it.
public sealed record JudgePickRow(int Rank, string Index, string Ticker, string Hold, string Shape, string Reason, decimal LastClose, double TypicalMove, JudgePrices Prices)
{
    public IReadOnlyList<string> Fields { get; init; } = [];
}

// What one of the judge's runs came to: its outcome, why where it picked nothing, the members screened and kept, the
// questions asked and those that failed twice, and its picks.
public sealed record JudgeRunOutcome(string Run, DateOnly Session, string Outcome, string? Why, int Screened, int Kept, int Asks, int AsksFailed, IReadOnlyList<JudgePickRow> Picks);

// The AI stock picker's nightly picks, a process of its own the night starts after its last step. It reads the night's facts
// under the field list's version, starts only under a passed naming test of that version, waits for the night to let go
// of its lock and then for the local model's lock, refuses any model, model file, window or prompt but its pin's, and
// asks the model in two rounds: every member's short row about 50 at a time, keeping at most one a batch, then the kept
// members' full sheets together, picking up to 5 ranked with a hold and a take-profit from the menu and a reason. A
// question the answer to which cannot be read is asked once more and then counted, never guessed. Code works every
// price from the last close and the typical daily move. It takes the drain's lock for its one write, and a night it
// cannot store before the next open is written as missed with no pick and never filled in later.
// see: The AI stock picker picks straight after the night under the local model's lock, its picks stored before the next open or shown as missed
public sealed class JudgePicker(
    IReadingModelFeed feed,
    LocalModelSettings settings,
    IClock clock,
    string databaseFile,
    string dataRoot,
    Func<TimeSpan, CancellationToken, Task>? pause = null,
    Func<ModelFileFacts?>? modelFile = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.JudgeFact, Touch.Read),
            new StoreTouch(Store.JudgeNaming, Touch.Read),
            new StoreTouch(Store.ListedName, Touch.Read),
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.ReadingPin, Touch.Read),
            new StoreTouch(Store.JudgePin, Touch.Read | Touch.Insert),
            new StoreTouch(Store.JudgeRun, Touch.Read | Touch.Insert),
            new StoreTouch(Store.JudgeAsk, Touch.Insert),
            new StoreTouch(Store.JudgePick, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Stage = "judge-pick";

    public const string Picked = "picked";
    public const string NonePicked = "none";
    public const string Missed = "missed";
    public const string Refused = "refused";
    public const string Failed = "failed";

    public const string Screen = "screen";
    public const string Pick = "pick";

    // How many times a question is asked: once, and once more where its answer cannot be read.
    public const int Attempts = 2;

    public const string NotNamed = "the reason names the company";

    const string Decided = "SELECT outcome FROM judge_run WHERE session_date = $session AND outcome IN ('picked', 'none', 'missed') LIMIT 1;";

    const string Facts = @"
        SELECT f.index_code, f.ticker, f.row_text, f.sheet_text,
            COALESCE((SELECT l.name FROM listed_name l WHERE l.ticker = f.ticker),
                     (SELECT m.name FROM membership m WHERE m.ticker = f.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1))
        FROM judge_fact f WHERE f.session_date = $session AND f.version = $version ORDER BY f.index_code, f.ticker;";

    const string Close = "SELECT close FROM bar WHERE ticker = $ticker AND session_date = $session;";

    const string Typical = "SELECT value FROM indicator WHERE ticker = $ticker AND session_date = $session AND name = $name;";

    const string NewestReadingPin = "SELECT id, model, model_file, sha256, size, modified, context FROM reading_pin ORDER BY id DESC LIMIT 1;";

    const string NewestJudgePin = "SELECT id, model, file, size, modified, context, prompt_version FROM judge_pin ORDER BY id DESC LIMIT 1;";

    const string InsertPin = @"
        INSERT INTO judge_pin (reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at)
        VALUES ($reading, $model, $file, $sha, $size, $modified, $context, $prompt, $at) RETURNING id;";

    const string InsertRun = @"
        INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail)
        VALUES ($run, $session, $version, $pin, $seed, $outcome, $started, $ended, $screened, $kept, $asks, $failed, $detail);";

    const string InsertAsk = @"
        INSERT INTO judge_ask (run_id, round, batch, attempt, outcome, cause, answer, model, asked_at)
        VALUES ($run, $round, $batch, $attempt, $outcome, $cause, $answer, $model, $at);";

    const string InsertPick = @"
        INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance, fields)
        VALUES ($session, $rank, $run, $index, $ticker, $hold, $shape, $reason, $close, $typical, $stop, $target, $giveup, $fields);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run, $stage, $started, $ended, $outcome, $rows, $calls, 0, '0', $detail);";

    sealed record Member(string Index, string Ticker, string Row, string Sheet, string? Name);

    sealed record Asked(string Round, int Batch, int Attempt, string Outcome, string? Cause, string? Answer, string? Model, DateTimeOffset At);

    sealed record PinRead(long ReadingPin, string Model, string File, string Sha, long Size, DateTimeOffset Modified, int Context, long? JudgePin);

    // The open its picks buy at, the instant they must be stored by: 9:30 in New York on the next weekday after the night,
    // a holiday read as a weekday, so the bound falls a day early and never late.
    public static DateTimeOffset NextOpen(DateOnly session, TimeZoneInfo zone)
    {
        var next = session.AddDays(1);

        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }

        var local = next.ToDateTime(new TimeOnly(9, 30));

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    // The night's seed: its date as a number, so a night asked again shows its members the same labels in the same order.
    public static int SeedOf(DateOnly session) => int.Parse(session.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    // Every member of the night shuffled by the seed, then cut into batches of the batch size.
    public static IReadOnlyList<IReadOnlyList<T>> Batches<T>(IReadOnlyList<T> members, int seed)
    {
        var shuffled = members.ToArray();
        var random = new Random(seed);

        for (var at = shuffled.Length - 1; at > 0; at--)
        {
            var other = random.Next(at + 1);

            (shuffled[at], shuffled[other]) = (shuffled[other], shuffled[at]);
        }

        return [.. shuffled.Chunk(JudgePrompt.BatchSize).Select(batch => (IReadOnlyList<T>)batch)];
    }

    public async Task<JudgeRunOutcome> RunAsync(DateOnly session, DateTimeOffset deadline, string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var seed = SeedOf(session);
        var wait = pause ?? Task.Delay;
        var asked = new List<Asked>();
        var calls = 0;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await ScalarAsync(connection, Decided, [("$session", day)], cancellation) is string decided)
        {
            return new JudgeRunOutcome(runId, session, Refused, $"the night of {day} has its run already, {decided}, and a night is never filled in again", 0, 0, 0, 0, []);
        }

        var members = await MembersAsync(connection, day, cancellation);

        if (members.Count == 0)
        {
            return await EndAsync(connection, runId, day, seed, started, Refused, $"no facts of {day} stand under field list {JudgeFacts.Version}", null, 0, 0, asked, 0, [], cancellation);
        }

        var (may, why) = await JudgeNamingTest.MayStartAsync(connection, JudgeFacts.Version, cancellation);

        if (!may)
        {
            return await EndAsync(connection, runId, day, seed, started, Refused, why, null, members.Count, 0, asked, 0, [], cancellation);
        }

        // The night lets go of its lock before the judge asks anything, and then the local model's lock is taken, each
        // waited for until the next open at the latest.
        while (NightLock.Holder(dataRoot) is not null)
        {
            if (clock.UtcNow >= deadline)
            {
                return await EndAsync(connection, runId, day, seed, started, Missed, "the night still held its lock at the next open", null, members.Count, 0, asked, 0, [], cancellation);
            }

            await wait(LocalRun.Poll, cancellation);
        }

        await using var local = await LocalLocks.TakeAsync(dataRoot, deadline, clock, wait, cancellation);

        if (local is null)
        {
            return await EndAsync(connection, runId, day, seed, started, Missed, "another local process held the local model's lock at the next open", null, members.Count, 0, asked, 0, [], cancellation);
        }

        var (pin, refused) = await PinAsync(connection, cancellation);

        if (pin is null)
        {
            return await EndAsync(connection, runId, day, seed, started, Refused, refused, null, members.Count, 0, asked, 0, [], cancellation);
        }

        bool loaded;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(connection, runId, day, seed, started, Failed, gone.Message, pin, members.Count, 0, asked, 0, [], cancellation);
        }

        var kept = new List<Member>();
        var failed = 0;
        IReadOnlyList<JudgeChoice> choices = [];
        string? stopped = null;

        try
        {
            var batches = Batches(members, seed);

            for (var batch = 0; batch < batches.Count && stopped is null; batch++)
            {
                var labels = JudgePrompt.Labels(seed + batch, batches[batch].Count);
                var shown = batches[batch].Select((member, at) => (labels[at], member.Row)).ToArray();
                var request = new ReadingRequest(settings.Model, JudgePrompt.ScreenSystem, JudgePrompt.ScreenPrompt(shown), seed, JudgePrompt.ScreenTokens, JudgePrompt.ScreenSchema(labels));
                var read = false;

                for (var attempt = 1; attempt <= Attempts && !read; attempt++)
                {
                    if (clock.UtcNow >= deadline)
                    {
                        stopped = "the next open came while it screened";
                        break;
                    }

                    calls++;

                    var (answer, model, cause) = await AskAsync(request, cancellation);

                    if (cause is null)
                    {
                        var (keep, unread) = JudgePrompt.ReadScreen(answer, labels);

                        cause = unread;

                        if (unread is null)
                        {
                            read = true;

                            if (keep is not null)
                            {
                                kept.Add(batches[batch][labels.ToList().IndexOf(keep)]);
                            }
                        }
                    }

                    asked.Add(new Asked(Screen, batch, attempt, read ? "answered" : model is null && answer is null ? "refused" : "unreadable", cause, answer, model, clock.UtcNow));
                }

                if (!read && stopped is null)
                {
                    failed++;
                }
            }

            if (stopped is null && kept.Count > 0)
            {
                var labels = JudgePrompt.Labels(seed - 1, kept.Count);
                var shown = kept.Select((member, at) => (labels[at], member.Sheet)).ToArray();
                var request = new ReadingRequest(settings.Model, JudgePrompt.PickSystem, JudgePrompt.PickPrompt(shown), seed, JudgePrompt.PickTokens, JudgePrompt.PickSchema(labels));
                var read = false;

                for (var attempt = 1; attempt <= Attempts && !read; attempt++)
                {
                    if (clock.UtcNow >= deadline)
                    {
                        stopped = "the next open came while it picked";
                        break;
                    }

                    calls++;

                    var (answer, model, cause) = await AskAsync(request, cancellation);

                    if (cause is null)
                    {
                        var (picks, unread) = JudgePrompt.ReadPick(answer, labels);

                        cause = unread ?? (picks.Any(pick => JudgePrompt.NamesTheMember(pick.Reason, kept[labels.ToList().IndexOf(pick.Label)].Ticker, kept[labels.ToList().IndexOf(pick.Label)].Name)) ? NotNamed : null);

                        if (cause is null)
                        {
                            read = true;
                            choices = picks;
                        }
                    }

                    asked.Add(new Asked(Pick, 0, attempt, read ? "answered" : model is null && answer is null ? "refused" : "unreadable", cause, answer, model, clock.UtcNow));
                }

                if (!read && stopped is null)
                {
                    failed++;
                    stopped = "round two's answer could not be read twice";
                }
            }
        }
        finally
        {
            await feed.ReleaseAsync(loaded, cancellation);
        }

        if (stopped is not null)
        {
            var outcome = clock.UtcNow >= deadline ? Missed : Failed;

            return await EndAsync(connection, runId, day, seed, started, outcome, stopped, pin, members.Count, kept.Count, asked, failed, [], cancellation);
        }

        var pickLabels = JudgePrompt.Labels(seed - 1, Math.Max(kept.Count, 1));
        var rows = new List<JudgePickRow>();
        var unpriced = new List<string>();
        var refusedPicks = new List<string>();

        foreach (var choice in choices)
        {
            var member = kept[pickLabels.ToList().IndexOf(choice.Label)];

            // From 24.14 a pick naming no fact, or one its sheet lacks, is refused and logged, and skipped.
            if (JudgePrompt.FieldsRefused(choice.Fields, member.Sheet) is { } refusal)
            {
                refusedPicks.Add(member.Ticker + ": " + refusal);
                continue;
            }

            decimal? close = null;

            if (await ScalarAsync(connection, Close, [("$ticker", member.Ticker), ("$session", day)], cancellation) is string text)
            {
                close = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
            }

            var typical = await ScalarAsync(connection, Typical, [("$ticker", member.Ticker), ("$session", day), ("$name", IndicatorSeries.Atr14)], cancellation) as double?;

            if (close is not { } last || typical is not { } move || JudgeMenu.Prices(last, move, choice.Hold, choice.Shape) is not { } prices)
            {
                unpriced.Add(member.Ticker);
                continue;
            }

            rows.Add(new JudgePickRow(rows.Count + 1, member.Index, member.Ticker, choice.Hold, choice.Shape, choice.Reason, last, move, prices)
            {
                Fields = [.. choice.Fields.Select(words => JudgePrompt.KeyOf(words)!)],
            });
        }

        var said = rows.Count == 0
            ? kept.Count == 0 ? "no member was kept in round one" : refusedPicks.Count > 0 ? "every pick the model made was refused" : "the model picked none of the members it kept"
            : null;

        if (refusedPicks.Count > 0)
        {
            said = (said is null ? string.Empty : said + "; ") + "refused for the facts it named, " + string.Join("; ", refusedPicks);
        }

        if (unpriced.Count > 0)
        {
            said = (said is null ? string.Empty : said + "; ") + "no price could be worked for " + string.Join(", ", unpriced) + ", so it was not stored";
        }

        return await EndAsync(connection, runId, day, seed, started, rows.Count > 0 ? Picked : NonePicked, said, pin, members.Count, kept.Count, asked, failed, rows, cancellation, deadline);
    }

    async Task<(string? Answer, string? Model, string? Cause)> AskAsync(ReadingRequest request, CancellationToken cancellation)
    {
        try
        {
            var reply = await feed.AskAsync(request, cancellation);

            return (reply.Content, reply.Model, null);
        }
        catch (ReadingRefused refused)
        {
            return (null, null, $"{refused.Cause}: {refused.Message}");
        }
    }

    // The pin the run asks under: the judge's newest, where the model, its file by size and modified instant, the window and
    // the prompt's version are its own, or a first pin written from the newest reading pin where the judge holds none.
    // Any part differing refuses the run, naming it.
    async Task<(PinRead? Pin, string Why)> PinAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        long readingId;
        string model, file, sha;
        long size;
        DateTimeOffset modified;
        int context;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestReadingPin;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (!await reader.ReadAsync(cancellation))
            {
                return (null, "no reading pin stands for the picker to be pinned under");
            }

            (readingId, model, file, sha, size, modified, context) = (reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), Instant(reader.GetString(5)), reader.GetInt32(6));
        }

        var facts = (modelFile ?? (() => settings.ModelFile is { } relative ? LocalModelFile.Facts(LocalModelFile.FolderOf(settings), relative) : null))();

        if (facts is null)
        {
            return (null, "no model file stands where the local profile names it");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestJudgePin;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                var id = reader.GetInt64(0);
                var part =
                    !string.Equals(settings.Model, reader.GetString(1), StringComparison.Ordinal) ? ReadingPins.ModelPart
                    : !string.Equals(facts.Relative, reader.GetString(2), StringComparison.Ordinal) || facts.Size != reader.GetInt64(3) || facts.Modified.UtcTicks != Instant(reader.GetString(4)).UtcTicks ? ReadingPins.FilePart
                    : settings.ContextTokens != reader.GetInt32(5) ? ReadingPins.WindowPart
                    : !string.Equals(JudgePrompt.Version, reader.GetString(6), StringComparison.Ordinal) ? ReadingPins.PromptPart
                    : null;

                // From 24.14 a pin under the one prompt version this one moves from, its model, file and window the run's own,
                // is followed by a new pin under this version: a new judge version, its record counted from its first night.
                if (part == ReadingPins.PromptPart && string.Equals(reader.GetString(6), JudgePrompt.Previous, StringComparison.Ordinal))
                {
                    return (new PinRead(readingId, settings.Model, facts.Relative, sha, facts.Size, facts.Modified, settings.ContextTokens, null), string.Empty);
                }

                return part is null
                    ? (new PinRead(readingId, settings.Model, facts.Relative, sha, facts.Size, facts.Modified, settings.ContextTokens, id), string.Empty)
                    : (null, FormattableString.Invariant($"{part} is not picker pin {id}'s"));
            }
        }

        // The first pin: the reading pin's model, file and window, which the run's own must match, and the prompt's version.
        var first =
            !string.Equals(settings.Model, model, StringComparison.Ordinal) ? ReadingPins.ModelPart
            : !string.Equals(facts.Relative, file, StringComparison.Ordinal) || facts.Size != size || facts.Modified.UtcTicks != modified.UtcTicks ? ReadingPins.FilePart
            : settings.ContextTokens != context ? ReadingPins.WindowPart
            : null;

        return first is null
            ? (new PinRead(readingId, model, file, sha, size, modified, context, null), string.Empty)
            : (null, FormattableString.Invariant($"{first} is not reading pin {readingId}'s, which the picker's first pin is written from"));
    }

    async Task<IReadOnlyList<Member>> MembersAsync(SqliteConnection connection, string day, CancellationToken cancellation)
    {
        var members = new List<Member>();

        await using var command = connection.CreateCommand();

        command.CommandText = Facts;
        command.Parameters.AddWithValue("$session", day);
        command.Parameters.AddWithValue("$version", JudgeFacts.Version);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(new Member(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return members;
    }

    // The run's one write: its pin where it is the first, every question asked, its picks and its row, with the run log's
    // row, in one transaction under the drain's lock where it stores picks. Picks reaching the next open before they are
    // stored are dropped and the night written as missed.
    async Task<JudgeRunOutcome> EndAsync(
        SqliteConnection connection, string runId, string day, int seed, DateTimeOffset started, string outcome, string? why, PinRead? pin,
        int screened, int kept, IReadOnlyList<Asked> asked, int failed, IReadOnlyList<JudgePickRow> picks, CancellationToken cancellation, DateTimeOffset? deadline = null)
    {
        IDisposable? drain = null;

        if (picks.Count > 0 && deadline is { } last)
        {
            try
            {
                drain = await DrainLock.AcquireAsync(dataRoot, async () =>
                {
                    if (clock.UtcNow >= last)
                    {
                        throw new TimeoutException();
                    }

                    await (pause ?? Task.Delay)(LocalRun.Poll, cancellation);
                }, cancellation);
            }
            catch (TimeoutException)
            {
                (outcome, why, picks) = (Missed, "the drain's lock was held at the next open, so its picks were not stored", []);
            }

            if (drain is not null && clock.UtcNow >= last)
            {
                (outcome, why, picks) = (Missed, "the next open came before its picks were stored", []);
            }
        }

        using (drain)
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            long? pinId = pin?.JudgePin;

            if (pin is { JudgePin: null })
            {
                pinId = (long)(await ScalarAsync(connection, InsertPin,
                [
                    ("$reading", pin.ReadingPin), ("$model", pin.Model), ("$file", pin.File), ("$sha", pin.Sha), ("$size", pin.Size),
                    ("$modified", Stamp(pin.Modified)), ("$context", pin.Context), ("$prompt", JudgePrompt.Version), ("$at", Stamp(clock.UtcNow)),
                ], cancellation, transaction))!;
            }

            foreach (var ask in asked)
            {
                await ExecuteAsync(connection, transaction, InsertAsk,
                [
                    ("$run", runId), ("$round", ask.Round), ("$batch", ask.Batch), ("$attempt", ask.Attempt), ("$outcome", ask.Outcome),
                    ("$cause", ask.Cause), ("$answer", ask.Answer), ("$model", ask.Model), ("$at", Stamp(ask.At)),
                ], cancellation);
            }

            foreach (var pick in picks)
            {
                await ExecuteAsync(connection, transaction, InsertPick,
                [
                    ("$session", day), ("$rank", pick.Rank), ("$run", runId), ("$index", pick.Index), ("$ticker", pick.Ticker), ("$hold", pick.Hold),
                    ("$shape", pick.Shape), ("$reason", pick.Reason), ("$close", Text(pick.LastClose)), ("$typical", pick.TypicalMove),
                    ("$stop", Text(pick.Prices.Stop)), ("$target", Text(pick.Prices.TakeProfit)),
                    ("$giveup", pick.Prices.GiveUpDistance is { } distance ? Text(distance) : null),
                    ("$fields", pick.Fields.Count == 0 ? null : string.Join(",", pick.Fields)),
                ], cancellation);
            }

            var calls = asked.Count;
            var detail = JsonSerializer.Serialize(new { why, screened, kept, asks = calls, failed, picks = picks.Count, prompt = JudgePrompt.Version, facts = JudgeFacts.Version });

            await ExecuteAsync(connection, transaction, InsertRun,
            [
                ("$run", runId), ("$session", day), ("$version", JudgeFacts.Version), ("$pin", pinId), ("$seed", seed), ("$outcome", outcome),
                ("$started", Stamp(started)), ("$ended", Stamp(clock.UtcNow)), ("$screened", screened), ("$kept", kept), ("$asks", calls),
                ("$failed", failed), ("$detail", why ?? string.Empty),
            ], cancellation);

            await ExecuteAsync(connection, transaction, AppendRun,
            [
                ("$run", runId), ("$stage", Stage), ("$started", Stamp(started)), ("$ended", Stamp(clock.UtcNow)), ("$outcome", outcome),
                ("$rows", picks.Count), ("$calls", calls), ("$detail", detail),
            ], cancellation);

            await transaction.CommitAsync(cancellation);
        }

        return new JudgeRunOutcome(runId, DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture), outcome, why, screened, kept, asked.Count, failed, picks);
    }

    // The night's own row for its last step, saying whether it started the judge; the judge's calls sit on its own row.
    public const string NightStage = "judge";

    public static async Task RecordTheNightAsync(string databaseFile, string runId, DateTimeOffset startedAt, DateTimeOffset endedAt, string said)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$stage", NightStage);
        command.Parameters.AddWithValue("$started", Stamp(startedAt));
        command.Parameters.AddWithValue("$ended", Stamp(endedAt));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows", 0);
        command.Parameters.AddWithValue("$calls", 0);
        command.Parameters.AddWithValue("$detail", said);

        await command.ExecuteNonQueryAsync();
    }

    // What a run came to, in words for its verb's line.
    public static string Words(JudgeRunOutcome outcome) =>
        FormattableString.Invariant($"the picker's run for {outcome.Session:yyyy-MM-dd}: {outcome.Outcome}, {outcome.Screened} screened, {outcome.Kept} kept, {outcome.Picks.Count} picked, {outcome.Asks} questions asked, {outcome.AsksFailed} failed twice")
        + (outcome.Why is { } why ? "; " + why : string.Empty);

    static DateTimeOffset Instant(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static string Text(decimal price) => price.ToString(CultureInfo.InvariantCulture);

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object? Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteScalarAsync(cancellation);
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object? Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
