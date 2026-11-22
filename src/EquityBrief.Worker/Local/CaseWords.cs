using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// The case's words. A step of the local run after each slot's releases, and a verb by hand: it puts to the local model the
// prompt each case the newest monthly run stored makes, one case's figures at a time under the local lock, after the newest
// pin's check of the model and its file; a case whose figures hold words already, answered or refused, is asked nothing
// again, so words are asked again only where a later run stored a case's figures changed, and the earlier words stay. An
// answer holding a number, a ticker or a company's name is refused and asked once more, then kept refused with its cause, the
// page drawing code's sentences in its place; and what it asked is stored in one write with its one run log row, the
// drain's lock held as it writes.
// see: The monthly run stores each case standing from Paper trading's accounts in Core, and the local run writes its words
// see: The local run is a process of its own at two slots outside the night and refuses any model file or prompt but its pin, whatever runtime answers
public sealed class CaseWords(
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
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.PromotionCase, Touch.Read),
            new StoreTouch(Store.PromotionCaseWords, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Verb = "case-words";

    public const string Kind = "case words";

    public const string Answered = "answered";
    public const string Refused = "refused";

    // The newest monthly run's cases whose figures hold no words.
    const string Waiting = @"
        SELECT c.index_code, c.family, c.variant, c.figures, c.figures_key
        FROM promotion_case c
        WHERE c.month = (SELECT MAX(month) FROM promotion_case)
          AND NOT EXISTS (
              SELECT 1 FROM promotion_case_words w
              WHERE w.index_code = c.index_code AND w.family = c.family AND w.variant = c.variant AND w.figures_key = c.figures_key)
        ORDER BY c.index_code, c.family, c.variant;
    ";

    // Every ticker and company name the indices hold, which a case's words may not name.
    const string Named = "SELECT ticker, name FROM membership;";

    const string InsertWords = @"
        INSERT INTO promotion_case_words (index_code, family, variant, figures_key, pin, version, outcome, cause, for_words, against_words, answer, model, seconds, asked_at, run_id)
        VALUES ($index_code, $family, $variant, $figures_key, $pin, $version, $outcome, $cause, $for_words, $against_words, $answer, $model, $seconds, $asked_at, $run_id)
        ON CONFLICT (index_code, family, variant, figures_key) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, $model_calls, 0, '0', $detail);
    ";

    readonly Func<TimeSpan, CancellationToken, Task> wait = pause ?? ((span, token) => Task.Delay(span, token));

    // One stored case waiting for its words.
    sealed record Case(string Index, string Family, string Variant, PromotionCaseView View, string FiguresKey);

    // One case asked: what it came to, its cause where refused, its words, the answer as sent and its seconds.
    sealed record Asked(Case Case, string Outcome, string? Cause, string? For, string? Against, string? Answer, double? Seconds);

    sealed class PinBroken(string message) : Exception(message);

    string? guarded;

    // Each case the newest monthly run stored whose figures hold no words, asked once.
    public async Task<LocalRunOutcome> RunAsync(TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = LocalRun.RunIdFor(started, Kind);

        await using var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation);

        var waiting = new List<Case>();
        var tickers = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Waiting;

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    if (PromotionCases.FromFigures(reader.GetString(3)) is { } view)
                    {
                        waiting.Add(new Case(reader.GetString(0), reader.GetString(1), reader.GetString(2), view, reader.GetString(4)));
                    }
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Named;

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    tickers.Add(reader.GetString(0));

                    if (!reader.IsDBNull(1))
                    {
                        names.Add(reader.GetString(1));
                    }
                }
            }
        }

        if (waiting.Count == 0)
        {
            await output.WriteLineAsync($"{Verb}: no stored case is waiting for its words");

            return new LocalRunOutcome(Kind, LocalRun.NothingToRead, "no stored case is waiting for its words", 0, 0, LocalRun.NoRefusals, null, null, null, null, null);
        }

        var ready = await LocalRun.ReadyAsync(settings, databaseFile, started, null, cancellation);

        guarded = ready.Structured;

        if (ready.Refusal is { } refused)
        {
            return await EndAsync(runId, started, refused with { Kind = Kind }, [], output, cancellation);
        }

        var pin = ready.Pin!;
        bool loaded;

        try
        {
            loaded = await feed.PrepareAsync(cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(runId, started, LocalRun.Ended(Kind, gone.Outcome, gone.Message) with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds }, [], output, cancellation);
        }

        var asked = new List<Asked>();
        var seconds = new List<double>();

        LocalRunOutcome Counted(string outcome, string? why) =>
            new(
                Kind,
                outcome,
                why,
                0,
                asked.Count(one => one.Outcome == Answered),
                asked.Where(one => one.Cause is not null).GroupBy(one => one.Cause!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                LocalRun.Median(seconds),
                seconds.Count == 0 ? null : seconds.Max(),
                pin,
                ready.Check,
                ready.HashSeconds);

        try
        {
            foreach (var one in waiting)
            {
                asked.Add(await AskAsync(pin, one, tickers, names, seconds, cancellation));
            }
        }
        catch (PinBroken broken)
        {
            await feed.ReleaseAsync(loaded, cancellation);

            return await EndAsync(runId, started, Counted(LocalRun.PinRefused, broken.Message), asked, output, cancellation);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(runId, started, Counted(gone.Outcome, gone.Message), asked, output, cancellation);
        }

        await feed.ReleaseAsync(loaded, cancellation);

        return await EndAsync(runId, started, Counted(LocalRun.Finished, null), asked, output, cancellation);
    }

    // One case asked under the pin: asked once more after an answer refused for what it holds or that cannot be read,
    // refused at once where the runtime refuses it, and an answer neither the pin's model nor its runtime gave ending the run.
    async Task<Asked> AskAsync(ReadingPin pin, Case one, IReadOnlySet<string> tickers, IReadOnlyCollection<string> names, List<double> seconds, CancellationToken cancellation)
    {
        Asked? last = null;
        var prompt = CasePrompt.For(one.View, "the " + one.Family);

        for (var round = 0; round <= ReadingSettings.AskedAgain; round++)
        {
            ReadingReply reply;

            try
            {
                reply = await feed.AskAsync(new ReadingRequest(pin.Model, CasePrompt.Instruction, prompt, pin.Seed, CasePrompt.AnswerTokens, CasePrompt.Schema()), cancellation);
            }
            catch (ReadingRefused refused)
            {
                return new Asked(one, Refused, refused.Cause, null, null, null, null);
            }

            seconds.Add(reply.Seconds);

            if (ReadingPins.AnswerDiffers(pin, reply.Model) is { } part)
            {
                throw new PinBroken($"an answer came from {part} the pin does not name: {reply.Model ?? "no model"} on {reply.RuntimeName ?? "no runtime"} {reply.RuntimeVersion}");
            }

            var read = CasePrompt.Read(reply.Content, reply.FinishReason, tickers, names);

            last = read.Cause is null
                ? new Asked(one, Answered, null, read.For, read.Against, reply.Content, reply.Seconds)
                : new Asked(one, Refused, read.Cause, null, null, reply.Content, reply.Seconds);

            if (read.Cause is null)
            {
                return last;
            }
        }

        return last!;
    }

    // The words asked and the run's row, written in one transaction with the drain's lock held, and its line on the output.
    async Task<LocalRunOutcome> EndAsync(string runId, DateTimeOffset started, LocalRunOutcome outcome, IReadOnlyList<Asked> asked, TextWriter output, CancellationToken cancellation)
    {
        outcome = outcome with { Structured = outcome.Structured ?? guarded };

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
                insert.CommandText = InsertWords;
                insert.Parameters.AddWithValue("$index_code", one.Case.Index);
                insert.Parameters.AddWithValue("$family", one.Case.Family);
                insert.Parameters.AddWithValue("$variant", one.Case.Variant);
                insert.Parameters.AddWithValue("$figures_key", one.Case.FiguresKey);
                insert.Parameters.AddWithValue("$pin", outcome.Pin!.Id);
                insert.Parameters.AddWithValue("$version", CasePrompt.Version);
                insert.Parameters.AddWithValue("$outcome", one.Outcome);
                insert.Parameters.AddWithValue("$cause", (object?)one.Cause ?? DBNull.Value);
                insert.Parameters.AddWithValue("$for_words", (object?)one.For ?? DBNull.Value);
                insert.Parameters.AddWithValue("$against_words", (object?)one.Against ?? DBNull.Value);
                insert.Parameters.AddWithValue("$answer", (object?)one.Answer ?? DBNull.Value);
                insert.Parameters.AddWithValue("$model", outcome.Pin!.Model);
                insert.Parameters.AddWithValue("$seconds", (object?)one.Seconds ?? DBNull.Value);
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

        await output.WriteLineAsync($"{Verb}: {outcome.Kind} came to {outcome.Outcome}" + (outcome.Why is { } why ? ": " + why : string.Empty) + FormattableString.Invariant($"; {outcome.Read} case(s) answered and {outcome.Refused.Values.Sum()} refused"));

        return outcome;
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
