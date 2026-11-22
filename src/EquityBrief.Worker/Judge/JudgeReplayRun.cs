using System.Globalization;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Judge;

// The AI stock picker's replay of the nights after the model's cutoff, by hand on the operator's word: its plan read from the
// store, printed before anything starts; each night's facts built again as they stood and held to those stored for it,
// a night whose facts differ named and skipped; and each night picked by the judge's own picker under the replay's
// prefix, with no open to meet. Before each night it yields the local model to the night, the drain and the monthly run,
// and to the labeller or a local run holding the model's lock, and it stops at the instant it is given.
// see: The AI stock picker's replays print their hours before they start on the operator's word, a new version's over the same 60 nights
public static class JudgeReplayRun
{
    const string Buildable = "SELECT DISTINCT session_date FROM ledger_reading ORDER BY session_date;";

    const string Run = "SELECT DISTINCT session_date FROM judge_run WHERE outcome IN ('picked', 'none', 'missed');";

    const string Cutoff = "SELECT cutoff FROM reading_pin ORDER BY id DESC LIMIT 1;";

    // A night's minutes, from 24.24 its indices' runs together.
    const string LiveMinutes = @"
        SELECT SUM((julianday(ended_at) - julianday(started_at)) * 1440) FROM judge_run
        WHERE outcome IN ('picked', 'none') AND run_id NOT LIKE 'judge-replay-%'
        GROUP BY session_date;";

    const string Stored = "SELECT ticker, row_text, sheet_text FROM judge_fact WHERE session_date = $session AND version = $version;";

    public static async Task<JudgeReplayPlan> PlanAsync(string databaseFile, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(JudgeFactReader.ReadOnly(databaseFile));
        await connection.OpenAsync(cancellation);

        var buildable = (await Column(connection, Buildable, cancellation)).Select(Day).ToArray();
        var run = (await Column(connection, Run, cancellation)).Select(Day).ToHashSet();
        var cutoff = (await Column(connection, Cutoff, cancellation)).Select(Day).FirstOrDefault(DateOnly.MaxValue);
        var minutes = new List<double>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LiveMinutes;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                minutes.Add(reader.GetDouble(0));
            }
        }

        return JudgeReplay.Plan(buildable, run, cutoff, minutes);
    }

    // Where a night's facts built again from the store differ from those stored for it under the field list's version.
    public static async Task<IReadOnlyList<string>> FactsDifferAsync(string databaseFile, DateOnly night, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(JudgeFactReader.ReadOnly(databaseFile));
        await connection.OpenAsync(cancellation);

        var stored = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Stored;
            command.Parameters.AddWithValue("$session", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$version", JudgeFacts.Version);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                stored[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
            }
        }

        if (stored.Count == 0)
        {
            return [];
        }

        var built = (await JudgeFactReader.ReadAsync(connection, night, null, cancellation))
            .ToDictionary(fact => fact.Ticker, fact => (JudgeFacts.RowText(fact), JudgeFacts.SheetText(fact)), StringComparer.Ordinal);

        return JudgeReplay.Differences(stored, built);
    }

    // What would wait on the local model: the night, the drain or a monthly run's step, or another local process holding
    // the model's lock, the labeller or a local run.
    public static string? Waiting(string dataRoot)
    {
        if (LocalLocks.Holder(dataRoot) is { } holder)
        {
            return holder;
        }

        var path = Path.Combine(dataRoot, WorkerDrainLauncher.CopiesFolder, LocalLocks.FileName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var free = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return null;
        }
        catch (IOException)
        {
            return "another local process holding the local model's lock";
        }
    }

    // The replay over its plan: nothing without the word or where the plan cannot start; otherwise each night in turn, until
    // the instant given or another process waits on the model, each night's outcome as the night's own step words it.
    public static async Task<IReadOnlyList<(DateOnly Night, string Outcome)>> RunAsync(
        JudgeReplayPlan plan, bool word, DateTimeOffset until, IClock clock, Func<string?> waiting, Func<DateOnly, Task<string>> one)
    {
        var done = new List<(DateOnly, string)>();

        if (!word || plan.Refused is not null)
        {
            return done;
        }

        foreach (var night in plan.Nights)
        {
            if (clock.UtcNow >= until)
            {
                done.Add((night, "stopped at the instant given"));
                break;
            }

            if (waiting() is { } who)
            {
                done.Add((night, "yielded the model to " + who));
                break;
            }

            done.Add((night, await one(night)));
        }

        return done;
    }

    // One night replayed: its facts held to those stored for it, then written and picked by the judge's own picker under
    // the replay's prefix with no open to meet.
    public static async Task<string> NightAsync(Func<IReadingModelFeed> feed, LocalModelSettings settings, IClock clock, string databaseFile, string dataRoot, DateOnly night, CancellationToken cancellation = default)
    {
        if (await FactsDifferAsync(databaseFile, night, cancellation) is { Count: > 0 } differences)
        {
            return "its facts built again differ from those stored, so it was not replayed: " + string.Join("; ", differences);
        }

        var run = FormattableString.Invariant($"{JudgeReplay.Prefix}{night:yyyyMMdd}-{clock.UtcNow:yyyyMMddTHHmmssZ}");

        await new JudgeFactWriter(clock, databaseFile).WriteAsync(night, run, cancellation);

        var outcome = await new JudgePicker(feed(), settings, clock, databaseFile, dataRoot).RunAsync(night, DateTimeOffset.MaxValue, run, cancellation);

        return JudgePicker.Words(outcome);
    }

    static async Task<IReadOnlyList<string>> Column(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        var values = new List<string>();

        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
