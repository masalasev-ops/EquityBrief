using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// An idea of the week as the researcher placed it: its setup and index, its rank in its answer, none for one that waited
// from an earlier week, its line and sentence, and whether it is tested this week, held already or waiting.
public sealed record ResearchChoice(string Index, string Family, int Rank, string Line, string Reason, string Outcome);

// The researcher, the local run's weekly form. For each setup the grammar's events name on each index it builds the rule's
// failure list, over its own closed picks of the early years and of the nights since the rules restarted, the tenths those
// of the event's setups over the early years; puts it to the local model under the local run's pin and lock with the ideas
// the notebook holds for the setup, each with its early edge and its later verdict as one word; and reads each answer's
// closed choices into a line, an answer that cannot be read asked once more first. A run whose model gives no answer at all
// is asked once more whole, and then counted as failed. The week's lines, those that waited from earlier weeks first and
// then each answer's strongest, at most ten and none the notebook holds, go to the one tester as one run, which stops
// starting a line once the run's cap has passed; the rest wait for next week. Its failure lists, its ideas and their
// outcomes are written with its row on the run log.
// see: The researcher reads each rule's failure list over the early years and the nights since the restart, and the later years as one word
// see: The Sunday run's added model time is measured before its first run, and a part past its cap waits a week by name
public sealed class Researcher(
    IReadingModelFeed feed,
    LocalModelSettings settings,
    IClock clock,
    string databaseFile,
    string dataRoot,
    Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ReadingPin, Touch.Read),
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.Notebook, Touch.Read),
            new StoreTouch(Store.ResearchAsk, Touch.Read | Touch.Insert),
            new StoreTouch(Store.ResearchFailure, Touch.Insert),
            new StoreTouch(Store.ResearchIdea, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Kind = "research";

    public const string Stage = "research";

    // The most lines a week's run tests, and the hours after which it starts no more.
    public const int TestsAWeek = 10;

    public const int CapHours = 8;

    public const string Answered = "answered";
    public const string Refused = "refused";

    // What became of an idea: tested this week, held already by the notebook, or waiting for next week.
    public const string Tested = "tested";
    public const string Held = "held";
    public const string Waiting = "waiting";

    // The populations a failure list reads.
    public const string Early = "early";
    public const string Live = "live";

    static readonly IReadOnlyList<string> Indices = ["GSPC", "MID", "SML"];

    readonly Func<TimeSpan, CancellationToken, Task> wait = pause ?? ((span, token) => Task.Delay(span, token));

    // One setup on one index asked: its input, its prompt and each round's outcome.
    sealed record Asked(ResearchInput Input, string Prompt, int Round, int Ask, string Outcome, string? Cause, string? Answer, double? Seconds, ResearchIdea? Idea, bool None)
    {
        public IReadOnlyList<ResearchIdea> Ideas => Idea is { } idea ? [idea] : [];
    }

    sealed class PinBroken(string message) : Exception(message);

    const string LongSql = "SELECT picked, edge, 0{0} FROM long_setup WHERE index_code = $index AND family = $family AND live_pass = 1 AND session_date <= $through;";

    const string LiveSql = "SELECT edge{0} FROM setup WHERE index_code = $index AND family = $family AND picked = 1 AND settled = 1 AND edge IS NOT NULL AND session_date >= $from;";

    const string NotedSql = "SELECT line, early_edge, verdict FROM notebook WHERE index_code = $index AND family = $family AND line IS NOT NULL ORDER BY id;";

    const string HeldSql = "SELECT DISTINCT line FROM notebook WHERE line IS NOT NULL;";

    // The ideas earlier weeks left waiting, oldest first; one the notebook has tested since is passed over as held.
    const string WaitingSql = "SELECT line, reason, index_code, family FROM research_idea WHERE outcome = 'waiting' ORDER BY written_at, index_code, family, rank;";

    const string InsertAsk = @"
        INSERT INTO research_ask (run_id, index_code, family, round, ask, prompt_key, pin, version, prompt, outcome, cause, answer, seconds, asked_at)
        VALUES ($run_id, $index_code, $family, $round, $ask, $prompt_key, $pin, $version, $prompt, $outcome, $cause, $answer, $seconds, $asked_at);";

    const string InsertFailure = @"
        INSERT INTO research_failure (run_id, index_code, family, population, reading, highest, picks, losers, in_band, losers_in_band)
        VALUES ($run_id, $index_code, $family, $population, $reading, $highest, $picks, $losers, $in_band, $losers_in_band);";

    const string InsertIdea = @"
        INSERT INTO research_idea (run_id, index_code, family, rank, line, reason, outcome, notebook_id, written_at)
        VALUES ($run_id, $index_code, $family, $rank, $line, $reason, $outcome, $notebook_id, $written_at);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, $model_calls, 0, '0', $detail);";

    // The week's run: its failure lists, its asks, its ideas tested by the one tester, and its row.
    public async Task<LocalRunOutcome> RunAsync(TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = LocalRun.RunIdFor(started, Kind);

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            await output.WriteLineAsync($"{Kind}: the night holds the store ({holder}); run it once the night has finished");

            return LocalRun.Ended(Kind, LocalRun.LockHeld, "the night holds the store");
        }

        var inputs = new List<ResearchInput>();

        foreach (var index in Indices)
        {
            foreach (var family in IdeaGrammar.Families)
            {
                inputs.Add(await InputAsync(index, family, cancellation));
            }
        }

        var (outcome, asked) = await AskAllAsync(inputs, runId, 1, cancellation);

        // A run the model gave no answer at all is asked once more whole, and then counted.
        if (!asked.Any(one => one.Outcome == Answered))
        {
            await output.WriteLineAsync($"{Kind}: no answer on the first asking ({outcome.Outcome}), asked once more");

            var (again, more) = await AskAllAsync(inputs, runId, 2, cancellation);

            outcome = again;
            asked = [.. asked, .. more];
        }

        // Each setup answered on the round it last answered, its ideas in the order its asks gave them.
        var answered = asked.Where(one => one.Outcome == Answered)
            .GroupBy(one => (one.Input.Index, one.Input.Family))
            .Select(group => (group.Key.Index, group.Key.Family, Ideas: (IReadOnlyList<ResearchIdea>)[.. group.Where(one => one.Round == group.Max(last => last.Round)).OrderBy(one => one.Ask).SelectMany(one => one.Ideas).DistinctBy(idea => idea.Line.Canonical)]))
            .ToArray();
        var failed = answered.Length == 0;

        var ideas = Choose(await WaitingAsync(cancellation), answered, await HeldAsync(cancellation), TestsAWeek);
        var chosen = ideas.Where(one => one.Outcome == Tested).Select(one => one.Line).ToArray();

        IReadOnlyDictionary<string, long> written = new Dictionary<string, long>(StringComparer.Ordinal);
        IReadOnlySet<string> unstarted = new HashSet<string>(StringComparer.Ordinal);
        var testsStarted = clock.UtcNow;

        if (chosen.Length > 0)
        {
            var tester = new IdeaTestRun(clock, databaseFile, dataRoot, output);

            await tester.RunAsync(chosen, "the researcher", cancellation, started.AddHours(CapHours));
            written = tester.Written;
            unstarted = tester.Unstarted;
        }

        var testSeconds = (clock.UtcNow - testsStarted).TotalSeconds;
        var final = ideas.Select(one => one.Outcome == Tested && unstarted.Contains(one.Line) ? one with { Outcome = Waiting } : one).ToArray();
        var modelSeconds = asked.Sum(one => one.Seconds ?? 0);
        var detail = failed
            ? FormattableString.Invariant($"failed: no answer from the model on either asking ({outcome.Outcome}{(outcome.Why is { } why ? ": " + why : string.Empty)}); the week counted as failed")
            : FormattableString.Invariant($"{answered.Length} of {inputs.Count} setups answered, {final.Count(one => one.Outcome == Tested)} idea(s) tested, {final.Count(one => one.Outcome == Waiting)} waiting, {final.Count(one => one.Outcome == Held)} the notebook held; the model {modelSeconds:0} s, the tests {testSeconds:0} s");

        await WriteAsync(runId, started, inputs, asked, final, failed ? "failed" : "ok", detail, written, cancellation);
        await output.WriteLineAsync($"{Kind}: {detail}");

        return outcome;
    }

    // The week's lines: those earlier weeks left waiting first, oldest first, then each answer's ideas by their rank across
    // the setups, so every setup's strongest comes before any setup's second; at most the cap tested and the rest waiting, a
    // line the notebook holds or the week has already passed over as held.
    public static IReadOnlyList<ResearchChoice> Choose(
        IReadOnlyList<(string Line, string Reason, string Index, string Family)> waiting,
        IReadOnlyList<(string Index, string Family, IReadOnlyList<ResearchIdea> Ideas)> answered,
        IReadOnlySet<string> held,
        int cap)
    {
        var ideas = new List<ResearchChoice>();
        var tested = 0;

        void Place(string index, string family, int rank, string line, string reason, bool fromAnswer)
        {
            if (held.Contains(line) || ideas.Any(earlier => earlier.Line == line))
            {
                if (fromAnswer)
                {
                    ideas.Add(new ResearchChoice(index, family, rank, line, reason, Held));
                }

                return;
            }

            var take = tested < cap;

            ideas.Add(new ResearchChoice(index, family, rank, line, reason, take ? Tested : Waiting));
            tested += take ? 1 : 0;
        }

        foreach (var (line, reason, index, family) in waiting)
        {
            Place(index, family, 0, line, reason, fromAnswer: false);
        }

        for (var rank = 1; rank <= ResearchPrompt.MostIdeas; rank++)
        {
            foreach (var (index, family, given) in answered.Where(one => one.Ideas.Count >= rank))
            {
                Place(index, family, rank, given[rank - 1].Line.Canonical, given[rank - 1].Reason, fromAnswer: true);
            }
        }

        return ideas;
    }

    // The model's time for a week's asks measured over constructed inputs, before the run's first week: each setup on each
    // index asked once under the lock and the pin with a failure list of the most rows and ten ideas the notebook holds, each
    // ask's seconds and the whole printed, and nothing written.
    // see: The Sunday run's added model time is measured before its first run, and a part past its cap waits a week by name
    public async Task<LocalRunOutcome> MeasureAsync(TextWriter output, CancellationToken cancellation = default)
    {
        var inputs = Indices.SelectMany(index => IdeaGrammar.Families.Select(family => Constructed(index, family))).ToArray();
        var started = clock.UtcNow;
        var (outcome, asked) = await AskAllAsync(inputs, string.Empty, 1, cancellation, write: false);

        foreach (var one in asked)
        {
            await output.WriteLineAsync(FormattableString.Invariant($"{Kind}: measured {one.Input.Family} on {IdeaGrammar.IndexWords(one.Input.Index)}: {one.Outcome}{(one.Cause is { } cause ? " (" + cause + ")" : string.Empty)}, {one.Seconds ?? 0:0.0} s, {one.Ideas.Count} idea(s)"));

            if (one.Outcome == Refused && one.Answer is { } answer)
            {
                await output.WriteLineAsync($"{Kind}: its answer as sent: {answer}");
            }
        }

        var seconds = asked.Sum(one => one.Seconds ?? 0);

        await output.WriteLineAsync(FormattableString.Invariant($"{Kind}: measured {asked.Count} ask(s) over {inputs.Length} setups, {asked.Count(one => one.Outcome == Answered)} answered, the model {seconds:0} s, the whole {(clock.UtcNow - started).TotalSeconds:0} s; {outcome.Outcome}{(outcome.Why is { } why ? ": " + why : string.Empty)}"));

        return outcome;
    }

    // A constructed setup's input as a week of the store's full lists would give it: the most rows of failures over the early
    // years, none yet over the live nights, and ten ideas held.
    public static ResearchInput Constructed(string index, string family) =>
        new(
            family,
            index,
            [.. IdeaGrammar.Readings.Take(FailureList.MostRows).Select((reading, at) => new FailureRow(reading, at % 2 == 1, 4000, 1800, 1200, 700 - (at * 20)))],
            4000,
            1800,
            [],
            12,
            6,
            [.. Enumerable.Range(1, 10).Select(at => new ResearchNoted(
                ResearchPrompt.LineOf(family, index, [(IdeaGrammar.Readings[at], 4, 10)], "next open", 0, IdeaGrammar.StopDays[at % IdeaGrammar.StopDays.Count], IdeaGrammar.Whole, 2, IdeaGrammar.Readings[0], true),
                0.01 * at,
                at % 3 == 0 ? IdeaTester.NearMiss : IdeaTester.Failed))]);

    // One setup on one index as the researcher reads it: the event's setups of the early years for the tenths, the rule's
    // own closed picks of the early years and of the nights since the restart, and the ideas the notebook holds for it.
    public async Task<ResearchInput> InputAsync(string index, string family, CancellationToken cancellation = default)
    {
        var readings = IdeaGrammar.Readings;
        var columns = string.Concat(readings.Select(reading => ", " + reading));
        var values = readings.Select(_ => new List<double>()).ToArray();
        var early = new List<FailurePick>();
        var live = new List<FailurePick>();
        var noted = new List<ResearchNoted>();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        Dictionary<string, double> Readings(SqliteDataReader reader, int from)
        {
            var held = new Dictionary<string, double>(StringComparer.Ordinal);

            for (var at = 0; at < readings.Count; at++)
            {
                if (!reader.IsDBNull(from + at))
                {
                    held[readings[at]] = reader.GetDouble(from + at);
                }
            }

            return held;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = string.Format(CultureInfo.InvariantCulture, LongSql, columns);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$through", FormattableString.Invariant($"{IdeaTester.EarlyThrough}-12-31"));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                for (var at = 0; at < readings.Count; at++)
                {
                    if (!reader.IsDBNull(3 + at))
                    {
                        values[at].Add(reader.GetDouble(3 + at));
                    }
                }

                if (!reader.IsDBNull(0) && reader.GetInt64(0) == 1 && !reader.IsDBNull(1))
                {
                    early.Add(new FailurePick(reader.GetDouble(1), Readings(reader, 3)));
                }
            }
        }

        var cuts = new Dictionary<string, double[]>(StringComparer.Ordinal);

        for (var at = 0; at < readings.Count; at++)
        {
            if (IdeaGrammar.CutsOf(values[at]) is { } held)
            {
                cuts[readings[at]] = held;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = string.Format(CultureInfo.InvariantCulture, LiveSql, columns);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$from", RuleFreezeDates.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                live.Add(new FailurePick(reader.GetDouble(0), Readings(reader, 1)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NotedSql;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$family", family);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                noted.Add(new ResearchNoted(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDouble(1), reader.GetString(2)));
            }
        }

        return new ResearchInput(
            family,
            index,
            FailureList.Of(early, cuts),
            early.Count,
            early.Count(pick => pick.Edge <= 0),
            FailureList.Of(live, cuts),
            live.Count,
            live.Count(pick => pick.Edge <= 0),
            noted);
    }

    // Every setup's prompt asked once under the local lock and the pin, each answer that cannot be read asked once more.
    async Task<(LocalRunOutcome Outcome, List<Asked> Asked)> AskAllAsync(IReadOnlyList<ResearchInput> inputs, string runId, int round, CancellationToken cancellation, bool write = true)
    {
        var started = clock.UtcNow;
        var asked = new List<Asked>();

        await using var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation);

        var ready = await LocalRun.ReadyAsync(settings, databaseFile, started, null, cancellation);

        if (ready.Refusal is { } refused)
        {
            return (refused with { Kind = Kind }, asked);
        }

        var pin = ready.Pin!;
        bool loaded;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return (LocalRun.Ended(Kind, gone.Outcome, gone.Message) with { Pin = pin }, asked);
        }

        LocalRunOutcome outcome;

        try
        {
            // Each setup asked for up to three ideas, each ask shown the lines given before it, stopping at an answer of no
            // idea, at one refused and at a line given already.
            foreach (var input in inputs)
            {
                var given = new List<string>();

                for (var ask = 1; ask <= ResearchPrompt.MostIdeas; ask++)
                {
                    var one = await AskAsync(pin, input, round, ask, given, cancellation);

                    asked.Add(one);

                    if (one.Idea is not { } idea || given.Contains(idea.Line.Canonical))
                    {
                        break;
                    }

                    given.Add(idea.Line.Canonical);
                }
            }

            await feed.ReleaseAsync(loaded, cancellation);
            outcome = new LocalRunOutcome(Kind, LocalRun.Finished, null, 0, asked.Count(one => one.Outcome == Answered), LocalRun.NoRefusals, null, null, pin, ready.Check, ready.HashSeconds);
        }
        catch (PinBroken broken)
        {
            await feed.ReleaseAsync(loaded, cancellation);
            outcome = LocalRun.Ended(Kind, LocalRun.PinRefused, broken.Message) with { Pin = pin };
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            outcome = LocalRun.Ended(Kind, gone.Outcome, gone.Message) with { Pin = pin };
        }

        if (write)
        {
            await WriteAsksAsync(runId, pin, asked, cancellation);
        }

        return (outcome, asked);
    }

    async Task<Asked> AskAsync(ReadingPin pin, ResearchInput input, int round, int ask, IReadOnlyList<string> given, CancellationToken cancellation)
    {
        var prompt = ResearchPrompt.For(input, given);
        Asked? last = null;

        for (var again = 0; again <= ReadingSettings.AskedAgain; again++)
        {
            ReadingReply reply;

            try
            {
                reply = await feed.AskAsync(new ReadingRequest(pin.Model, ResearchPrompt.Instruction, prompt, pin.Seed, ResearchPrompt.AnswerTokens, ResearchPrompt.Schema()), cancellation);
            }
            catch (ReadingRefused refused)
            {
                return new Asked(input, prompt, round, ask, Refused, refused.Cause, null, null, null, false);
            }

            if (ReadingPins.AnswerDiffers(pin, reply.Model) is { } part)
            {
                throw new PinBroken($"an answer came from {part} the pin does not name: {reply.Model ?? "no model"} on {reply.RuntimeName ?? "no runtime"} {reply.RuntimeVersion}");
            }

            var read = ResearchPrompt.Read(reply.Content, reply.FinishReason, input.Family, input.Index);

            last = read.Cause is null
                ? new Asked(input, prompt, round, ask, Answered, null, reply.Content, reply.Seconds, read.Idea, read.None)
                : new Asked(input, prompt, round, ask, Refused, read.Cause, reply.Content, reply.Seconds, null, false);

            if (read.Cause is null)
            {
                return last;
            }
        }

        return last!;
    }

    async Task<HashSet<string>> HeldAsync(CancellationToken cancellation)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = HeldSql;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held.Add(reader.GetString(0));
        }

        return held;
    }

    async Task<List<(string Line, string Reason, string Index, string Family)>> WaitingAsync(CancellationToken cancellation)
    {
        var waiting = new List<(string, string, string, string)>();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = WaitingSql;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            waiting.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return waiting;
    }

    // The asks of one round, written with the drain's lock held as they are made, so a run that ends later keeps them.
    async Task WriteAsksAsync(string runId, ReadingPin pin, IReadOnlyList<Asked> asked, CancellationToken cancellation)
    {
        using (await DrainLock.AcquireAsync(dataRoot, () => wait(LocalRun.Poll, cancellation), cancellation))
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await using var transaction = connection.BeginTransaction();
            var askedAt = Instant(clock.UtcNow);

            foreach (var one in asked)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = InsertAsk;
                insert.Parameters.AddWithValue("$run_id", runId);
                insert.Parameters.AddWithValue("$index_code", one.Input.Index);
                insert.Parameters.AddWithValue("$family", one.Input.Family);
                insert.Parameters.AddWithValue("$round", one.Round);
                insert.Parameters.AddWithValue("$ask", one.Ask);
                insert.Parameters.AddWithValue("$prompt_key", ResearchPrompt.Key(one.Prompt));
                insert.Parameters.AddWithValue("$pin", pin.Id);
                insert.Parameters.AddWithValue("$version", ResearchPrompt.Version);
                insert.Parameters.AddWithValue("$prompt", one.Prompt);
                insert.Parameters.AddWithValue("$outcome", one.Outcome);
                insert.Parameters.AddWithValue("$cause", (object?)one.Cause ?? DBNull.Value);
                insert.Parameters.AddWithValue("$answer", (object?)one.Answer ?? DBNull.Value);
                insert.Parameters.AddWithValue("$seconds", (object?)one.Seconds ?? DBNull.Value);
                insert.Parameters.AddWithValue("$asked_at", askedAt);
                await insert.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }
    }

    // The failure lists, the ideas and the run's row, written in one transaction with the drain's lock held.
    async Task WriteAsync(
        string runId,
        DateTimeOffset started,
        IReadOnlyList<ResearchInput> inputs,
        IReadOnlyList<Asked> asked,
        IReadOnlyList<ResearchChoice> ideas,
        string runOutcome,
        string detail,
        IReadOnlyDictionary<string, long> written,
        CancellationToken cancellation)
    {
        using (await DrainLock.AcquireAsync(dataRoot, () => wait(LocalRun.Poll, cancellation), cancellation))
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await using var transaction = connection.BeginTransaction();
            var at = Instant(clock.UtcNow);
            var rows = 0;

            foreach (var input in inputs)
            {
                foreach (var (population, list) in new[] { (Early, input.Early), (Live, input.Live) })
                {
                    foreach (var row in list)
                    {
                        await using var insert = connection.CreateCommand();

                        insert.Transaction = transaction;
                        insert.CommandText = InsertFailure;
                        insert.Parameters.AddWithValue("$run_id", runId);
                        insert.Parameters.AddWithValue("$index_code", input.Index);
                        insert.Parameters.AddWithValue("$family", input.Family);
                        insert.Parameters.AddWithValue("$population", population);
                        insert.Parameters.AddWithValue("$reading", row.Reading);
                        insert.Parameters.AddWithValue("$highest", row.Highest ? 1 : 0);
                        insert.Parameters.AddWithValue("$picks", row.Picks);
                        insert.Parameters.AddWithValue("$losers", row.Losers);
                        insert.Parameters.AddWithValue("$in_band", row.InBand);
                        insert.Parameters.AddWithValue("$losers_in_band", row.LosersInBand);
                        rows += await insert.ExecuteNonQueryAsync(cancellation);
                    }
                }
            }

            var ranked = new Dictionary<(string, string), int>();

            foreach (var idea in ideas)
            {
                var place = ranked[(idea.Index, idea.Family)] = ranked.GetValueOrDefault((idea.Index, idea.Family)) + 1;

                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = InsertIdea;
                insert.Parameters.AddWithValue("$run_id", runId);
                insert.Parameters.AddWithValue("$index_code", idea.Index);
                insert.Parameters.AddWithValue("$family", idea.Family);
                insert.Parameters.AddWithValue("$rank", place);
                insert.Parameters.AddWithValue("$line", idea.Line);
                insert.Parameters.AddWithValue("$reason", idea.Reason);
                insert.Parameters.AddWithValue("$outcome", idea.Outcome);
                insert.Parameters.AddWithValue("$notebook_id", idea.Outcome == Tested && written.TryGetValue(idea.Line, out var row) ? row : DBNull.Value);
                insert.Parameters.AddWithValue("$written_at", at);
                rows += await insert.ExecuteNonQueryAsync(cancellation);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = AppendRun;
                command.Parameters.AddWithValue("$run_id", runId);
                command.Parameters.AddWithValue("$stage", Stage);
                command.Parameters.AddWithValue("$started_at", Instant(started));
                command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
                command.Parameters.AddWithValue("$outcome", runOutcome);
                command.Parameters.AddWithValue("$rows_written", rows + asked.Count);
                command.Parameters.AddWithValue("$model_calls", feed.Requests);
                command.Parameters.AddWithValue("$detail", detail);
                await command.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
