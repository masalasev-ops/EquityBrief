using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
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
// daily move code worked its prices from, those prices, from 24.14 the keys of the facts it named as deciding it, and from
// 24.24 whether it is in the best quarter its version takes, "a fixed 5" taking every pick its second round ranked.
public sealed record JudgePickRow(int Rank, string Index, string Ticker, string Hold, string Shape, string Reason, decimal LastClose, double TypicalMove, JudgePrices Prices)
{
    public IReadOnlyList<string> Fields { get; init; } = [];

    public bool InQuarter { get; init; } = true;
}

// What one of the picker's runs came to, from 24.24 one index's: its outcome, why where it picked nothing, the members
// screened and those marked, the questions asked and those that failed twice, and its picks.
public sealed record JudgeRunOutcome(string Run, DateOnly Session, string Outcome, string? Why, int Screened, int Kept, int Asks, int AsksFailed, IReadOnlyList<JudgePickRow> Picks)
{
    public string? Index { get; init; }
}

// What a night of the picker came to: each index's run in the order they ran.
public sealed record JudgeNightOutcome(string Run, DateOnly Session, IReadOnlyList<JudgeRunOutcome> Indices)
{
    public IReadOnlyList<JudgePickRow> Picks => [.. Indices.SelectMany(index => index.Picks)];

    // The night's outcome: picked where any index picked, else the first index's that is not none, else none.
    public string Outcome =>
        Indices.Any(index => index.Outcome == JudgePicker.Picked) ? JudgePicker.Picked
        : Indices.FirstOrDefault(index => index.Outcome != JudgePicker.NonePicked)?.Outcome ?? JudgePicker.NonePicked;
}

// The AI stock picker's nightly picks, a process of its own the night starts after its last step, from 24.24 once for each
// index on that index's members alone. It reads the night's facts under the field list's version, each figure placed among
// its index's stocks, picks in an index only under a passed naming test of that version for that index, waits for the
// night to let go of its lock and then for the local model's lock, refuses any model, model file, window or prompt but its
// pin's, and asks the model in two rounds: the index's short rows 50 at a time, marking every member it rates worth buying,
// then the marked members' full sheets at most 16 a question, each question ranking its best until one ranks the night's:
// the best quarter of the marked rounded up and at most 5, "a fixed 5" taking the first 5 of the same ranking and asking
// nothing of its own. A question the answer to which cannot be read is asked once more and then counted, never guessed.
// Code works every price from the last close and the typical daily move. Each index's running version runs first, then
// the rest in the order they were made, one past its index's running version only while the night can hold it; it takes
// the drain's lock for each index's one write, and an index it cannot store before the next open is written as missed with
// no pick and never filled in later.
// see: The AI stock picker picks straight after the night under the local model's lock, its picks stored before the next open or shown as missed
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
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
    public const string NotRun = JudgeNight.NotRun;

    public const string Screen = "screen";
    public const string Pick = "pick";

    // How many times a question is asked: once, and once more where its answer cannot be read.
    public const int Attempts = 2;

    public const string NotNamed = "the reason names the company";

    public const string NoneMarked = "it rated none of the index's stocks worth buying";

    // A night an index has run for already under any version, or across the three indices before 24.24.
    const string Decided = "SELECT outcome FROM judge_run WHERE session_date = $session AND (index_code = $index OR index_code IS NULL) AND outcome IN ('picked', 'none', 'missed') LIMIT 1;";

    const string Facts = @"
        SELECT f.index_code, f.ticker, f.row_text, f.sheet_text,
            COALESCE((SELECT l.name FROM listed_name l WHERE l.ticker = f.ticker),
                     (SELECT m.name FROM membership m WHERE m.ticker = f.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1))
        FROM judge_fact f WHERE f.session_date = $session AND f.version = $version AND f.index_code = $index ORDER BY f.ticker;";

    const string PinOfRun = "SELECT pin_id FROM judge_run WHERE run_id = $run;";

    const string Close = "SELECT close FROM bar WHERE ticker = $ticker AND session_date = $session;";

    const string Typical = "SELECT value FROM indicator WHERE ticker = $ticker AND session_date = $session AND name = $name;";

    const string NewestReadingPin = "SELECT id, model, model_file, sha256, size, modified, context FROM reading_pin ORDER BY id DESC LIMIT 1;";

    const string NewestJudgePin = "SELECT id, model, file, size, modified, context, prompt_version FROM judge_pin ORDER BY id DESC LIMIT 1;";

    const string InsertPin = @"
        INSERT INTO judge_pin (reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at)
        VALUES ($reading, $model, $file, $sha, $size, $modified, $context, $prompt, $at) RETURNING id;";

    const string InsertRun = @"
        INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code, standing)
        VALUES ($run, $session, $version, $pin, $seed, $outcome, $started, $ended, $screened, $kept, $asks, $failed, $detail, $index, $standing);";

    const string InsertAsk = @"
        INSERT INTO judge_ask (run_id, round, batch, attempt, outcome, cause, answer, model, asked_at)
        VALUES ($run, $round, $batch, $attempt, $outcome, $cause, $answer, $model, $at);";

    const string InsertPick = @"
        INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance, fields, in_quarter)
        VALUES ($session, $rank, $run, $index, $ticker, $hold, $shape, $reason, $close, $typical, $stop, $target, $giveup, $fields, $quarter);";

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

    // Every member of the index shuffled by the seed, then cut into batches of the size given.
    public static IReadOnlyList<IReadOnlyList<T>> Batches<T>(IReadOnlyList<T> members, int seed, int size = JudgePrompt.BatchSize)
    {
        var shuffled = members.ToArray();
        var random = new Random(seed);

        for (var at = shuffled.Length - 1; at > 0; at--)
        {
            var other = random.Next(at + 1);

            (shuffled[at], shuffled[other]) = (shuffled[other], shuffled[at]);
        }

        return [.. shuffled.Chunk(size).Select(batch => (IReadOnlyList<T>)batch)];
    }

    // Members cut into the fewest questions of at most the size given, as even as they can be, in the order given.
    public static IReadOnlyList<IReadOnlyList<T>> Even<T>(IReadOnlyList<T> members, int size)
    {
        var count = (members.Count + size - 1) / size;
        var groups = new List<IReadOnlyList<T>>();
        var at = 0;

        for (var group = 0; group < count; group++)
        {
            var take = (members.Count - at + (count - group) - 1) / (count - group);

            groups.Add([.. members.Skip(at).Take(take)]);
            at += take;
        }

        return groups;
    }

    // An index's own run of a night: the night's run with its index's word, and a version past the running one its standing
    // and the order it was made in.
    public static string RunOf(string runId, JudgeSlot slot) =>
        runId + "-" + OwnStepCandidate.WordOf(slot.Index) + (slot.Standing == JudgeStanding.Running ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"-{Standing(slot.Standing)}-{slot.Made}"));

    public static string Standing(JudgeStanding standing) => standing switch
    {
        JudgeStanding.Running => "running",
        JudgeStanding.Replaced => "replaced",
        _ => "paper",
    };

    public async Task<JudgeNightOutcome> RunAsync(DateOnly session, DateTimeOffset deadline, string runId, CancellationToken cancellation = default) =>
        await RunAsync(session, deadline, runId, JudgeNight.RunningOnly, JudgeNight.HoldsUntil(session), cancellation);

    public async Task<JudgeNightOutcome> RunAsync(DateOnly session, DateTimeOffset deadline, string runId, IReadOnlyList<JudgeSlot> slots, DateTimeOffset holdsUntil, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var seed = SeedOf(session);
        var wait = pause ?? Task.Delay;
        var ordered = JudgeNight.Order(slots);
        var outcomes = new List<JudgeRunOutcome>();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // Each index the night may still run for, its members read; an index already run for, holding no facts or no passed
        // naming test is written as such and asked nothing.
        var asking = new List<(JudgeSlot Slot, IReadOnlyList<Member> Members)>();

        foreach (var slot in ordered)
        {
            var run = RunOf(runId, slot);

            if (await ScalarAsync(connection, Decided, [("$session", day), ("$index", slot.Index)], cancellation) is string decided)
            {
                outcomes.Add(new JudgeRunOutcome(run, session, Refused, $"the night of {day} has its run on the {NameOf(slot)} already, {decided}, and a night is never filled in again", 0, 0, 0, 0, []) { Index = slot.Index });
                continue;
            }

            var members = await MembersAsync(connection, day, slot.Index, cancellation);

            if (members.Count == 0)
            {
                outcomes.Add(await EndAsync(connection, run, slot, day, seed, started, Refused, $"no facts of {day} on the {NameOf(slot)} stand under field list {JudgeFacts.Version}", null, 0, 0, [], 0, [], cancellation));
                continue;
            }

            var (may, why) = await JudgeNamingTest.MayStartAsync(connection, JudgeFacts.Version, slot.Index, cancellation);

            if (!may)
            {
                outcomes.Add(await EndAsync(connection, run, slot, day, seed, started, Refused, why, null, members.Count, 0, [], 0, [], cancellation));
                continue;
            }

            asking.Add((slot, members));
        }

        if (asking.Count > 0)
        {
            outcomes.AddRange(await AskAllAsync(connection, runId, day, seed, started, deadline, holdsUntil, asking, wait, cancellation));
        }

        var night = new JudgeNightOutcome(runId, session, [.. ordered.Select(slot => outcomes.Single(outcome => outcome.Run == RunOf(runId, slot)))]);

        await LogAsync(connection, runId, started, night, cancellation);

        return night;
    }

    // The questions of every index the night asks, under one wait for the night's lock, one take of the local model's lock
    // and one load of the model, the indices in the night's order.
    async Task<IReadOnlyList<JudgeRunOutcome>> AskAllAsync(
        SqliteConnection connection, string runId, string day, int seed, DateTimeOffset started, DateTimeOffset deadline, DateTimeOffset holdsUntil,
        IReadOnlyList<(JudgeSlot Slot, IReadOnlyList<Member> Members)> asking, Func<TimeSpan, CancellationToken, Task> wait, CancellationToken cancellation)
    {
        async Task<IReadOnlyList<JudgeRunOutcome>> Each(string outcome, string why, PinRead? pin)
        {
            var written = new List<JudgeRunOutcome>();

            foreach (var (slot, members) in asking)
            {
                written.Add(await EndAsync(connection, RunOf(runId, slot), slot, day, seed, started, outcome, why, pin, members.Count, 0, [], 0, [], cancellation));
            }

            return written;
        }

        // The night lets go of its lock before the picker asks anything, and then the local model's lock is taken, each
        // waited for until the next open at the latest.
        while (NightLock.Holder(dataRoot) is not null)
        {
            if (clock.UtcNow >= deadline)
            {
                return await Each(Missed, "the night still held its lock at the next open", null);
            }

            await wait(LocalRun.Poll, cancellation);
        }

        await using var local = await LocalLocks.TakeAsync(dataRoot, deadline, clock, wait, cancellation);

        if (local is null)
        {
            return await Each(Missed, "another local process held the local model's lock at the next open", null);
        }

        var (pin, refused) = await PinAsync(connection, cancellation);

        if (pin is null)
        {
            return await Each(Refused, refused, null);
        }

        bool loaded;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await Each(Failed, gone.Message, pin);
        }

        var outcomes = new List<JudgeRunOutcome>();

        try
        {
            foreach (var (slot, members) in asking)
            {
                var run = RunOf(runId, slot);

                if (!JudgeNight.MayStart(slot, clock.UtcNow, holdsUntil))
                {
                    outcomes.Add(await EndAsync(connection, run, slot, day, seed, clock.UtcNow, NotRun, FormattableString.Invariant($"the night could not hold it before {holdsUntil:HH:mm} UTC, when the off-peak run needs the model and the store"), pin, members.Count, 0, [], 0, [], cancellation));
                    continue;
                }

                outcomes.Add(await IndexAsync(connection, run, slot, members, day, seed, deadline, pin, cancellation));

                // A pin written by an index's run is the pin the next index asks under.
                if (pin.JudgePin is null && await ScalarAsync(connection, PinOfRun, [("$run", run)], cancellation) is long written)
                {
                    pin = pin with { JudgePin = written };
                }
            }
        }
        finally
        {
            await feed.ReleaseAsync(loaded, cancellation);
        }

        return outcomes;
    }

    // One index's two rounds and its one write.
    async Task<JudgeRunOutcome> IndexAsync(SqliteConnection connection, string run, JudgeSlot slot, IReadOnlyList<Member> members, string day, int seed, DateTimeOffset deadline, PinRead pin, CancellationToken cancellation)
    {
        var started = clock.UtcNow;
        var name = NameOf(slot);
        var asked = new List<Asked>();
        var marked = new List<Member>();
        var failed = 0;
        string? stopped = null;

        var batches = Batches(members, seed);

        for (var batch = 0; batch < batches.Count && stopped is null; batch++)
        {
            var labels = JudgePrompt.Labels(seed + batch, batches[batch].Count);
            var shown = batches[batch].Select((member, at) => (labels[at], member.Row)).ToArray();
            var request = new ReadingRequest(settings.Model, JudgePrompt.ScreenSystem, JudgePrompt.ScreenPrompt(name, shown), seed, JudgePrompt.ScreenTokens, JudgePrompt.ScreenSchema(labels));
            var read = false;

            for (var attempt = 1; attempt <= Attempts && !read; attempt++)
            {
                if (clock.UtcNow >= deadline)
                {
                    stopped = "the next open came while it screened";
                    break;
                }

                var (answer, model, cause) = await AskAsync(request, cancellation);

                if (cause is null)
                {
                    var (marks, unread) = JudgePrompt.ReadScreen(answer, labels);

                    cause = unread;

                    if (unread is null)
                    {
                        read = true;
                        marked.AddRange(marks.Select(label => batches[batch][labels.ToList().IndexOf(label)]));
                    }
                }

                asked.Add(new Asked(Screen, batch, attempt, read ? "answered" : model is null && answer is null ? "refused" : "unreadable", cause, answer, model, clock.UtcNow));
            }

            if (!read && stopped is null)
            {
                failed++;
            }
        }

        var ranking = JudgePrompt.Ranked(marked.Count);
        IReadOnlyList<(Member Member, JudgeChoice Choice)> ranked = [];

        if (stopped is null && ranking > 0)
        {
            // Round two: the marked members' sheets at most 16 a question, each question ranking its best, the best of every
            // question asked together again until one question holds them all.
            var pool = (IReadOnlyList<Member>)[.. Batches(marked, seed - 1, marked.Count).SelectMany(batch => batch)];
            var ask = 0;

            while (stopped is null)
            {
                var final = pool.Count <= JudgePrompt.SheetsPerAsk;
                var groups = final ? [pool] : Even(pool, JudgePrompt.SheetsPerAsk);
                var winners = new List<(Member, JudgeChoice)>();

                foreach (var group in groups)
                {
                    var count = Math.Min(ranking, group.Count);
                    var labels = JudgePrompt.Labels(seed - 1 - ask, group.Count);
                    var shown = group.Select((member, at) => (labels[at], member.Sheet)).ToArray();
                    var request = new ReadingRequest(settings.Model, JudgePrompt.PickSystem, JudgePrompt.PickPrompt(name, count, shown), seed, JudgePrompt.PickTokens, JudgePrompt.PickSchema(labels, count));
                    var read = false;

                    for (var attempt = 1; attempt <= Attempts && !read; attempt++)
                    {
                        if (clock.UtcNow >= deadline)
                        {
                            stopped = "the next open came while it picked";
                            break;
                        }

                        var (answer, model, cause) = await AskAsync(request, cancellation);

                        if (cause is null)
                        {
                            var (picks, unread) = JudgePrompt.ReadPick(answer, labels, count);

                            cause = unread ?? (final && picks.Any(pick => JudgePrompt.NamesTheMember(pick.Reason, group[labels.ToList().IndexOf(pick.Label)].Ticker, group[labels.ToList().IndexOf(pick.Label)].Name)) ? NotNamed : null);

                            if (cause is null)
                            {
                                read = true;
                                winners.AddRange(picks.Select(pick => (group[labels.ToList().IndexOf(pick.Label)], pick)));
                            }
                        }

                        asked.Add(new Asked(Pick, ask, attempt, read ? "answered" : model is null && answer is null ? "refused" : "unreadable", cause, answer, model, clock.UtcNow));
                    }

                    ask++;

                    if (!read && stopped is null)
                    {
                        failed++;
                        stopped = "a question of round two could not be read twice";
                    }

                    if (stopped is not null)
                    {
                        break;
                    }
                }

                if (final || stopped is not null)
                {
                    ranked = winners;
                    break;
                }

                pool = [.. winners.Select(winner => winner.Item1)];
            }
        }

        if (stopped is not null)
        {
            var outcome = clock.UtcNow >= deadline ? Missed : Failed;

            return await EndAsync(connection, run, slot, day, seed, started, outcome, stopped, pin, members.Count, marked.Count, asked, failed, [], cancellation);
        }

        var quarter = JudgePrompt.Quarter(marked.Count);
        var rows = new List<JudgePickRow>();
        var unpriced = new List<string>();
        var refusedPicks = new List<string>();

        // A pick refused for the facts it named, or priced at nothing, is skipped, and the best quarter is the first of the
        // picks stored in the order ranked.
        foreach (var (member, choice) in ranked)
        {
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
                InQuarter = rows.Count < quarter,
            });
        }

        var said = rows.Count == 0
            ? marked.Count == 0 ? NoneMarked : refusedPicks.Count > 0 ? "every pick the model made was refused" : "no pick could be stored"
            : null;

        if (refusedPicks.Count > 0)
        {
            said = (said is null ? string.Empty : said + "; ") + "refused for the facts it named, " + string.Join("; ", refusedPicks);
        }

        if (unpriced.Count > 0)
        {
            said = (said is null ? string.Empty : said + "; ") + "no price could be worked for " + string.Join(", ", unpriced) + ", so it was not stored";
        }

        return await EndAsync(connection, run, slot, day, seed, started, rows.Count > 0 ? Picked : NonePicked, said, pin, members.Count, marked.Count, asked, failed, rows, cancellation, deadline);
    }

    static string NameOf(JudgeSlot slot) => OwnStepCandidate.NameOf(slot.Index);

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

    // The pin the run asks under: the picker's newest, where the model, its file by size and modified instant, the window
    // and the prompt's version are its own, or a first pin written from the newest reading pin where the picker holds none.
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

                // A pin under a prompt version this one moves from, its model, file and window the run's own, is followed by a
                // new pin under this version: a new picker version, its record counted from its first night.
                if (part == ReadingPins.PromptPart && JudgePrompt.Earlier.Contains(reader.GetString(6), StringComparer.Ordinal))
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

    async Task<IReadOnlyList<Member>> MembersAsync(SqliteConnection connection, string day, string index, CancellationToken cancellation)
    {
        var members = new List<Member>();

        await using var command = connection.CreateCommand();

        command.CommandText = Facts;
        command.Parameters.AddWithValue("$session", day);
        command.Parameters.AddWithValue("$version", JudgeFacts.Version);
        command.Parameters.AddWithValue("$index", index);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(new Member(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return members;
    }

    // An index's one write: its pin where it is the first, every question asked, its picks and its row, in one transaction
    // under the drain's lock where it stores picks. Picks reaching the next open before they are stored are dropped and the
    // index's night written as missed.
    async Task<JudgeRunOutcome> EndAsync(
        SqliteConnection connection, string runId, JudgeSlot slot, string day, int seed, DateTimeOffset started, string outcome, string? why, PinRead? pin,
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
                    ("$quarter", pick.InQuarter ? 1 : 0),
                ], cancellation);
            }

            await ExecuteAsync(connection, transaction, InsertRun,
            [
                ("$run", runId), ("$session", day), ("$version", JudgeFacts.Version), ("$pin", pinId), ("$seed", seed), ("$outcome", outcome),
                ("$started", Stamp(started)), ("$ended", Stamp(clock.UtcNow)), ("$screened", screened), ("$kept", kept), ("$asks", asked.Count),
                ("$failed", failed), ("$detail", why ?? string.Empty), ("$index", slot.Index), ("$standing", Standing(slot.Standing)),
            ], cancellation);

            await transaction.CommitAsync(cancellation);
        }

        return new JudgeRunOutcome(runId, DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture), outcome, why, screened, kept, asked.Count, failed, picks) { Index = slot.Index };
    }

    // The night's one run log row, its calls the questions every index asked.
    async Task LogAsync(SqliteConnection connection, string runId, DateTimeOffset started, JudgeNightOutcome night, CancellationToken cancellation)
    {
        var calls = night.Indices.Sum(index => index.Asks);
        var detail = JsonSerializer.Serialize(new
        {
            indices = night.Indices.Select(index => new { index = index.Index, outcome = index.Outcome, why = index.Why, screened = index.Screened, marked = index.Kept, asks = index.Asks, failed = index.AsksFailed, picks = index.Picks.Count, quarter = index.Picks.Count(pick => pick.InQuarter) }),
            prompt = JudgePrompt.Version,
            facts = JudgeFacts.Version,
        });

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run", runId), ("$stage", Stage), ("$started", Stamp(started)), ("$ended", Stamp(clock.UtcNow)), ("$outcome", night.Outcome),
            ("$rows", night.Picks.Count), ("$calls", calls), ("$detail", detail),
        ], cancellation);

        await transaction.CommitAsync(cancellation);
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

    // What a night came to, in words for its verb's line: each index's run in the order they ran.
    public static string Words(JudgeNightOutcome night) =>
        FormattableString.Invariant($"the picker's night of {night.Session:yyyy-MM-dd}: ") + string.Join("; ", night.Indices.Select(Words));

    // What one index's run came to.
    public static string Words(JudgeRunOutcome outcome) =>
        FormattableString.Invariant($"{(outcome.Index is { } index ? OwnStepCandidate.NameOf(index) : "every index")} {outcome.Outcome}, {outcome.Screened} screened, {outcome.Kept} marked, {outcome.Picks.Count(pick => pick.InQuarter)} picked of {outcome.Picks.Count} ranked, {outcome.Asks} questions asked, {outcome.AsksFailed} failed twice")
        + (outcome.Why is { } why ? " (" + why + ")" : string.Empty);

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
