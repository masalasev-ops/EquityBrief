using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// The idea generator. A step of the monthly run on each index before its tester runs, and a verb by hand: it puts to the
// local model the prompts the tester's own read of the index makes, one at a time under the local lock, after the newest
// pin's check of the model and its file, a prompt the pin has answered already asked nothing again; each answer is read
// into its labels or refused with its cause, an answer that cannot be read asked once more first, and an answer neither
// the pin's model nor its runtime gave ends the run; and what it asked is stored under each prompt's key in one write
// with its one run log row, under the local run's stage, with the drain's lock held as it writes. It hands the tester
// nothing but the stored answers.
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
// see: The local run is a process of its own at two slots outside the night and refuses any model file, runtime or prompt but its pin
public sealed class IdeaGenerator(
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
            new StoreTouch(Store.LoopIdea, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Verb = WalkForwardTester.IdeasVerb;

    // The kind a run's row names among the local model's runs, by the index it put prompts for.
    public static string KindOf(string index) => "ideas " + index;

    const string InsertIdea = @"
        INSERT INTO loop_idea (prompt_key, pin, version, index_code, family, year, prompt, outcome, cause, answer, seconds, prompt_tokens, asked_at, run_id)
        VALUES ($prompt_key, $pin, $version, $index_code, $family, $year, $prompt, $outcome, $cause, $answer, $seconds, $prompt_tokens, $asked_at, $run_id)
        ON CONFLICT (prompt_key, pin) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, $model_calls, 0, '0', $detail);
    ";

    readonly Func<TimeSpan, CancellationToken, Task> wait = pause ?? ((span, token) => Task.Delay(span, token));

    // One prompt asked: the ask, what it came to, its cause where refused, the answer as sent, its seconds and tokens.
    sealed record Asked(IdeaAsk Ask, string Outcome, string? Cause, string? Answer, double? Seconds, int? PromptTokens);

    sealed class PinBroken(string message) : Exception(message);

    // The prompts for an index put to the local model, each one no answer under the pin holds asked once.
    public async Task<LocalRunOutcome> RunAsync(string index, IReadOnlyList<IdeaAsk> asks, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var kind = KindOf(index);
        var runId = LocalRun.RunIdFor(started, kind);

        await using var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation);

        var ready = await LocalRun.ReadyAsync(settings, databaseFile, started, null, cancellation);

        if (ready.Refusal is { } refused)
        {
            return await EndAsync(runId, index, started, refused with { Kind = kind }, [], output, cancellation);
        }

        var pin = ready.Pin!;
        IReadOnlySet<string> answered;

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);
            answered = await LoopIdeas.AskedAsync(connection, pin.Id, cancellation);
        }

        // A prompt two rules' folds make alike is asked once.
        IdeaAsk[] waiting = [.. asks.Where(ask => !answered.Contains(ask.Key)).DistinctBy(ask => ask.Key)];

        if (waiting.Length == 0)
        {
            return await EndAsync(
                runId,
                index,
                started,
                new LocalRunOutcome(kind, LocalRun.NothingToRead, FormattableString.Invariant($"each of the {asks.Count} prompt(s) is answered under pin {pin.Id} already"), 0, 0, LocalRun.NoRefusals, null, null, pin, ready.Check, ready.HashSeconds),
                [],
                output,
                cancellation);
        }

        bool loaded;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(runId, index, started, LocalRun.Ended(kind, gone.Outcome, gone.Message) with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds }, [], output, cancellation);
        }

        var asked = new List<Asked>();
        var seconds = new List<double>();

        LocalRunOutcome Counted(string outcome, string? why)
        {
            var causes = asked.Where(one => one.Cause is not null).GroupBy(one => one.Cause!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            return new LocalRunOutcome(
                kind,
                outcome,
                why,
                0,
                asked.Count(one => one.Outcome == LoopIdeas.Answered),
                causes,
                LocalRun.Median(seconds),
                seconds.Count == 0 ? null : seconds.Max(),
                pin,
                ready.Check,
                ready.HashSeconds);
        }

        try
        {
            foreach (var ask in waiting)
            {
                asked.Add(await AskAsync(pin, ask, seconds, cancellation));
            }
        }
        catch (PinBroken broken)
        {
            await feed.ReleaseAsync(loaded, cancellation);

            return await EndAsync(runId, index, started, Counted(LocalRun.PinRefused, broken.Message), asked, output, cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(runId, index, started, Counted(gone.Outcome, gone.Message), asked, output, cancellation);
        }

        await feed.ReleaseAsync(loaded, cancellation);

        return await EndAsync(runId, index, started, Counted(LocalRun.Finished, null), asked, output, cancellation);
    }

    // One prompt asked under the pin: asked once more after an answer that cannot be read, refused at once where the
    // runtime refuses it or does not answer in time, and an answer neither the pin's model nor its runtime gave ending the
    // run.
    async Task<Asked> AskAsync(ReadingPin pin, IdeaAsk ask, List<double> seconds, CancellationToken cancellation)
    {
        Asked? last = null;

        for (var round = 0; round <= ReadingSettings.AskedAgain; round++)
        {
            ReadingReply reply;

            try
            {
                reply = await feed.AskAsync(new ReadingRequest(pin.Model, IdeaPrompt.Instruction, ask.Prompt, pin.Seed, IdeaPrompt.AnswerTokens), cancellation);
            }
            catch (ReadingRefused refused)
            {
                return new Asked(ask, LoopIdeas.Refused, refused.Cause, null, null, null);
            }

            seconds.Add(reply.Seconds);

            if (ReadingPins.AnswerDiffers(pin, reply.Model, reply.RuntimeName, reply.RuntimeVersion) is { } part)
            {
                throw new PinBroken($"an answer came from {part} the pin does not name: {reply.Model ?? "no model"} on {reply.RuntimeName ?? "no runtime"} {reply.RuntimeVersion}");
            }

            var read = IdeaPrompt.Read(reply.Content, reply.FinishReason);

            last = read.Labels is not null
                ? new Asked(ask, LoopIdeas.Answered, null, reply.Content, reply.Seconds, reply.PromptTokens)
                : new Asked(ask, LoopIdeas.Refused, read.Cause, reply.Content, reply.Seconds, reply.PromptTokens);

            if (read.Labels is not null)
            {
                return last;
            }
        }

        return last!;
    }

    // The answers asked and the run's row, written in one transaction with the drain's lock held, and its line on the
    // output.
    async Task<LocalRunOutcome> EndAsync(string runId, string index, DateTimeOffset started, LocalRunOutcome outcome, IReadOnlyList<Asked> asked, TextWriter output, CancellationToken cancellation)
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
                insert.CommandText = InsertIdea;
                insert.Parameters.AddWithValue("$prompt_key", one.Ask.Key);
                insert.Parameters.AddWithValue("$pin", outcome.Pin!.Id);
                insert.Parameters.AddWithValue("$version", IdeaPrompt.Version);
                insert.Parameters.AddWithValue("$index_code", index);
                insert.Parameters.AddWithValue("$family", one.Ask.Family);
                insert.Parameters.AddWithValue("$year", one.Ask.Year);
                insert.Parameters.AddWithValue("$prompt", one.Ask.Prompt);
                insert.Parameters.AddWithValue("$outcome", one.Outcome);
                insert.Parameters.AddWithValue("$cause", (object?)one.Cause ?? DBNull.Value);
                insert.Parameters.AddWithValue("$answer", (object?)one.Answer ?? DBNull.Value);
                insert.Parameters.AddWithValue("$seconds", (object?)one.Seconds ?? DBNull.Value);
                insert.Parameters.AddWithValue("$prompt_tokens", (object?)one.PromptTokens ?? DBNull.Value);
                insert.Parameters.AddWithValue("$asked_at", askedAt);
                insert.Parameters.AddWithValue("$run_id", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = AppendRun;
                command.Parameters.AddWithValue("$run_id", runId);
                command.Parameters.AddWithValue("$stage", LocalRun.Stage);
                command.Parameters.AddWithValue("$started_at", Instant(started));
                command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
                command.Parameters.AddWithValue("$outcome", outcome.Outcome);
                command.Parameters.AddWithValue("$rows_written", asked.Count);
                command.Parameters.AddWithValue("$model_calls", feed.Requests);
                command.Parameters.AddWithValue("$detail", LocalRun.Detail(outcome));

                await command.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        await output.WriteLineAsync($"{Verb}: {outcome.Kind} came to {outcome.Outcome}" + (outcome.Why is { } why ? ": " + why : string.Empty) + FormattableString.Invariant($"; {outcome.Read} prompt(s) answered and {outcome.Refused.Values.Sum()} refused"));

        return outcome;
    }

    // The monthly run's words for the step: what the generator came to, and that the tester reads what is stored.
    public static string Words(LocalRunOutcome outcome, int prompts) =>
        outcome.Outcome is LocalRun.Finished or LocalRun.NothingToRead
            ? FormattableString.Invariant($"{outcome.Read} of {prompts} prompt(s) answered now and {outcome.Refused.Values.Sum()} refused, the rest answered before")
            : $"the generator came to {outcome.Outcome}" + (outcome.Why is { } why ? ": " + why : string.Empty) + "; the tester reads the answers stored before";

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
