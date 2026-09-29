using System.Globalization;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Nights;

// The newest night the run log holds, read for a run of its rest: the id of its first try, the session it
// is for, the number its next try takes, and the stages its tries finished, each as the newest try that
// wrote it left it. A night run again whole for its session is a night of its own, and a pass of the
// overnight queue is none.
// see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file
public sealed record NightToResume(string FirstTry, DateOnly Session, int NextTry, IReadOnlySet<string> Done)
{
    public bool Finished => Done.Contains(NightClose.Stage);
}

public static class NightResume
{
    const string NightRows = @"
        SELECT run_id, stage, outcome
        FROM run_log
        WHERE run_id LIKE 'night-%' AND instr(run_id, '-queue-') = 0
        ORDER BY rowid;
    ";

    // The words a stop is written with, which leave a stage unfinished.
    static readonly string[] Stops = [NightClose.Failed, NightClose.Stopped, NightClose.Refused];

    public static async Task<NightToResume?> NewestAsync(string databaseFile, IClock clock)
    {
        if (!File.Exists(databaseFile))
        {
            return null;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = NightRows;

        var nights = new List<string>();
        var rows = new Dictionary<string, List<(string RunId, string Stage, string Outcome)>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var runId = reader.GetString(0);
            var first = FirstTry(runId);

            if (!rows.TryGetValue(first, out var held))
            {
                rows[first] = held = [];
                nights.Add(first);
            }

            held.Add((runId, reader.GetString(1), reader.GetString(2)));
        }

        if (nights.Count == 0)
        {
            return null;
        }

        var newest = nights[^1];
        var done = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, stage, outcome) in rows[newest]
                     .Where(row => row.Stage != NightClose.TryAgainStage)
                     .OrderBy(row => TryNumber(row.RunId)))
        {
            if (Stops.Contains(outcome))
            {
                done.Remove(stage);
            }
            else
            {
                done.Add(stage);
            }
        }

        return new NightToResume(
            newest,
            SessionOf(newest, clock),
            rows[newest].Max(row => TryNumber(row.RunId)) + 1,
            done);
    }

    // The session a night's first try is for: the one its id names after `-for-` where a person named it,
    // and otherwise the session the clock falls on at the instant its id carries.
    static DateOnly SessionOf(string firstTry, IClock clock)
    {
        var named = firstTry.IndexOf("-for-", StringComparison.Ordinal);

        if (named >= 0
            && DateOnly.TryParseExact(firstTry[(named + 5)..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session))
        {
            return session;
        }

        var instant = firstTry["night-".Length..];

        return DateTimeOffset.TryParseExact(instant[..Math.Min(16, instant.Length)], "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? clock.SessionDateAt(at)
            : clock.SessionDateAt(clock.UtcNow);
    }

    static string FirstTry(string runId)
    {
        var at = runId.IndexOf(NightClose.TryMark, StringComparison.Ordinal);

        return at < 0 ? runId : runId[..at];
    }

    static int TryNumber(string runId)
    {
        var at = runId.IndexOf(NightClose.TryMark, StringComparison.Ordinal);

        return at >= 0 && int.TryParse(runId[(at + NightClose.TryMark.Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 1;
    }
}
