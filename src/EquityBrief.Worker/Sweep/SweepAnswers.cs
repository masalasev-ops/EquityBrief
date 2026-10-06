using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Sweep;

// The command run after a sweep that records the answer its run states, which a card reads to say its sweep found no
// setting that passed the floors. It reads the run's own answer file and decides nothing itself: a run that states no
// answer, made before runs stated one or the pullback's base, which searches nothing, is refused with nothing written.
// Recording a run again writes its row again.
// see: No family on any index is set aside or hidden by a test result without the operator's word
public sealed class SweepAnswers(IClock clock, string databaseFile, string sweepFolder, TextWriter output) : IComponent
{
    public const string Verb = "sweep-answer";

    public const string Stage = "sweep-answer";

    public const string RunPrefix = "sweep-answer-";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.SweepAnswer, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    // The indices a run may name and the families a card draws, which are the answers a card can read.
    static readonly string[] IndexCodes = [EquityBrief.Worker.Indices.IndexFamilies.LargeIndex, "MID", "SML"];

    static readonly string[] Families = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, HeavyweightRule.Name];

    const string Write = @"
        INSERT INTO sweep_answer (run, index_code, family, design, answer, recorded_at)
        VALUES ($run, $index, $family, $design, $answer, $recorded)
        ON CONFLICT (run) DO UPDATE SET
            index_code = excluded.index_code, family = excluded.family, design = excluded.design,
            answer = excluded.answer, recorded_at = excluded.recorded_at;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);";

    public async Task<int> RecordAsync(string run, CancellationToken cancellation = default)
    {
        var refusal = Refusal(run, out var answer);

        if (refusal is not null)
        {
            output.WriteLine($"{Verb}: {refusal}; nothing was recorded");

            return 2;
        }

        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = Write;
            command.Parameters.AddWithValue("$run", run);
            command.Parameters.AddWithValue("$index", answer!.Index);
            command.Parameters.AddWithValue("$family", answer.Family);
            command.Parameters.AddWithValue("$design", (object?)answer.Design ?? DBNull.Value);
            command.Parameters.AddWithValue("$answer", answer.Word);
            command.Parameters.AddWithValue("$recorded", Instant(started));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", RunPrefix + run);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", Instant(started));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", "ok");
            command.Parameters.AddWithValue("$rows_written", 1);
            command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { run, index = answer.Index, family = answer.Family, design = answer.Design, answer = answer.Word }));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        output.WriteLine($"recorded run {run}: {answer.Index} {answer.Family}{(answer.Design is { } design ? " design " + design : string.Empty)}, {answer.Word}");

        return 0;
    }

    // Why a run cannot be recorded, or none where it can, with the answer it states.
    string? Refusal(string run, out SweepAnswer? answer)
    {
        answer = null;

        if (!SweepFolder.IsRunName(run))
        {
            return $"'{run}' is not a run's name, the instant it started as yyyyMMddTHHmmssZ";
        }

        var folder = Path.Combine(sweepFolder, run);

        if (!Directory.Exists(folder))
        {
            return $"the sweep's folder holds no run {run}";
        }

        var file = Path.Combine(folder, SweepAnswer.File);

        if (!File.Exists(file))
        {
            return $"run {run} states no answer, being a run made before runs stated one or the pullback's base, which searches nothing; run its sweep again from this build";
        }

        try
        {
            answer = SweepAnswer.Read(File.ReadAllText(file));
        }
        catch (JsonException)
        {
            answer = null;
        }

        if (answer is null || string.IsNullOrEmpty(answer.Index) || string.IsNullOrEmpty(answer.Family))
        {
            answer = null;

            return $"run {run}'s answer cannot be read";
        }

        if (!IndexCodes.Contains(answer.Index, StringComparer.Ordinal))
        {
            return $"run {run} names the index '{answer.Index}', which no page reads";
        }

        if (!Families.Contains(answer.Family, StringComparer.Ordinal))
        {
            return $"run {run} names the family '{answer.Family}', which no card draws";
        }

        return null;
    }

    static string Instant(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
