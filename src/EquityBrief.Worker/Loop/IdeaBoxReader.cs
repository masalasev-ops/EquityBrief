using System.Globalization;
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

// The idea box's worker, started by the box's press as a process of its own: it takes the local model's lock, waiting for the
// night, a local run, the labeller or the picker to let it go, checks the local run's pin, and asks the pinned model once to
// read the idea typed into one grammar line, an answer that cannot be read asked once more. Each answer is written as an
// attempt of its own with what it understood or why it was refused. Nothing it writes joins a test: the operator's confirm
// does.
// see: An idea typed in the box is read into one grammar line by the local model and joins the week's tests only on the operator's confirm
public sealed class IdeaBoxReader(
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
            new StoreTouch(Store.IdeaBox, Touch.Read),
            new StoreTouch(Store.IdeaBoxAnswer, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Verb = "idea-box";

    public const string Understood = "understood";
    public const string NoIdea = "none";
    public const string Refused = "refused";

    readonly Func<TimeSpan, CancellationToken, Task> wait = pause ?? ((span, token) => Task.Delay(span, token));

    const string Words = "SELECT words FROM idea_box WHERE id = $id;";

    const string Attempts = "SELECT COALESCE(MAX(attempt), 0), COALESCE(SUM(outcome = 'understood' OR outcome = 'none'), 0) FROM idea_box_answer WHERE press_id = $id;";

    const string InsertAnswer = @"
        INSERT INTO idea_box_answer (press_id, attempt, outcome, cause, line, reason, prompt_key, pin, version, answer, seconds, asked_at)
        VALUES ($press_id, $attempt, $outcome, $cause, $line, $reason, $prompt_key, $pin, $version, $answer, $seconds, $asked_at);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows, $calls, 0, '0', $detail);";

    sealed record Attempt(string Outcome, string? Cause, IdeaBoxRead? Read, string? Answer, double? Seconds);

    // One press read: its words asked under the lock and the pin, each attempt written; 0 where an answer was read, whether an
    // idea or none, and 1 where none could be.
    public async Task<int> RunAsync(long press, TextWriter output, CancellationToken cancellation = default)
    {
        string? words;
        int before;
        bool settled;

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Words;
                command.Parameters.AddWithValue("$id", press);
                words = await command.ExecuteScalarAsync(cancellation) as string;
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Attempts;
                command.Parameters.AddWithValue("$id", press);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                await reader.ReadAsync(cancellation);
                (before, settled) = (reader.GetInt32(0), reader.GetInt64(1) > 0);
            }
        }

        if (words is null)
        {
            await output.WriteLineAsync(FormattableString.Invariant($"{Verb}: no idea was typed under press {press}"));

            return 1;
        }

        if (settled)
        {
            await output.WriteLineAsync(FormattableString.Invariant($"{Verb}: press {press} was read already"));

            return 0;
        }

        var prompt = IdeaBoxPrompt.For(words);
        var attempts = new List<Attempt>();
        ReadingPin pin;

        await using (var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation))
        {
            var ready = await LocalRun.ReadyAsync(settings, databaseFile, clock.UtcNow, null, cancellation);

            if (ready.Refusal is { } refused)
            {
                await output.WriteLineAsync($"{Verb}: the local model was not asked: {refused.Why ?? refused.Outcome}");

                return 1;
            }

            pin = ready.Pin!;
            bool loaded;

            try
            {
                loaded = await feed.PrepareAsync(cancellation);
            }
            catch (ReadingRuntimeUnavailable gone)
            {
                await output.WriteLineAsync($"{Verb}: the local model was not asked: {gone.Message}");

                return 1;
            }

            try
            {
                for (var again = 0; again <= ReadingSettings.AskedAgain; again++)
                {
                    ReadingReply reply;

                    try
                    {
                        reply = await feed.AskAsync(new ReadingRequest(pin.Model, IdeaBoxPrompt.Instruction, prompt, pin.Seed, IdeaBoxPrompt.AnswerTokens, IdeaBoxPrompt.Schema()), cancellation);
                    }
                    catch (ReadingRefused refusedAsk)
                    {
                        attempts.Add(new Attempt(Refused, refusedAsk.Cause, null, null, null));

                        break;
                    }

                    if (ReadingPins.AnswerDiffers(pin, reply.Model) is { } part)
                    {
                        attempts.Add(new Attempt(Refused, $"an answer came from {part} the pin does not name", null, reply.Content, reply.Seconds));

                        break;
                    }

                    var read = IdeaBoxPrompt.Read(reply.Content, reply.FinishReason);

                    attempts.Add(read.Cause is null
                        ? new Attempt(read.None ? NoIdea : Understood, null, read, reply.Content, reply.Seconds)
                        : new Attempt(Refused, read.Cause, null, reply.Content, reply.Seconds));

                    if (read.Cause is null)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await feed.ReleaseAsync(loaded, cancellation);
            }
        }

        await WriteAsync(press, before, prompt, pin, attempts, cancellation);

        var last = attempts[^1];

        await output.WriteLineAsync(last.Outcome switch
        {
            Understood => $"{Verb}: understood: {last.Read!.Reason} {last.Read.Line!.Canonical}",
            NoIdea => $"{Verb}: the model read no idea the grammar can say in the words",
            _ => $"{Verb}: refused: {last.Cause}",
        });

        return last.Outcome == Refused ? 1 : 0;
    }

    async Task WriteAsync(long press, int before, string prompt, ReadingPin pin, IReadOnlyList<Attempt> attempts, CancellationToken cancellation)
    {
        using (await DrainLock.AcquireAsync(dataRoot, () => wait(LocalRun.Poll, cancellation), cancellation))
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            for (var at = 0; at < attempts.Count; at++)
            {
                var one = attempts[at];

                await using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = InsertAnswer;
                command.Parameters.AddWithValue("$press_id", press);
                command.Parameters.AddWithValue("$attempt", before + at + 1);
                command.Parameters.AddWithValue("$outcome", one.Outcome);
                command.Parameters.AddWithValue("$cause", (object?)one.Cause ?? DBNull.Value);
                command.Parameters.AddWithValue("$line", (object?)one.Read?.Line?.Canonical ?? DBNull.Value);
                command.Parameters.AddWithValue("$reason", (object?)one.Read?.Reason ?? DBNull.Value);
                command.Parameters.AddWithValue("$prompt_key", IdeaBoxPrompt.Key(prompt));
                command.Parameters.AddWithValue("$pin", pin.Id);
                command.Parameters.AddWithValue("$version", IdeaBoxPrompt.Version);
                command.Parameters.AddWithValue("$answer", (object?)one.Answer ?? DBNull.Value);
                command.Parameters.AddWithValue("$seconds", (object?)one.Seconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$asked_at", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync(cancellation);
            }

            // The run's row: the press, its attempts and what the last came to.
            await using (var command = connection.CreateCommand())
            {
                var now = clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                var last = attempts[^1];

                command.Transaction = transaction;
                command.CommandText = AppendRun;
                command.Parameters.AddWithValue("$run_id", FormattableString.Invariant($"{Verb}-{press}-{before + attempts.Count}-{clock.UtcNow.UtcDateTime:yyyyMMddTHHmmssZ}"));
                command.Parameters.AddWithValue("$stage", Verb);
                command.Parameters.AddWithValue("$started_at", now);
                command.Parameters.AddWithValue("$ended_at", now);
                command.Parameters.AddWithValue("$outcome", last.Outcome == Refused ? "failed" : "ok");
                command.Parameters.AddWithValue("$rows", attempts.Count);
                command.Parameters.AddWithValue("$calls", attempts.Count(one => one.Answer is not null || one.Outcome != Refused));
                command.Parameters.AddWithValue("$detail", FormattableString.Invariant($"press {press}: {attempts.Count} attempt(s), the last {last.Outcome}{(last.Cause is { } cause ? ": " + cause : string.Empty)}"));
                await command.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }
    }
}
