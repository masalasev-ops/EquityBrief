using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Nights;

// Which named sessions a night may be replayed for, and why the others are
// refused. The rule lives here rather than inside the argument handling so it
// can be asserted without running the process.
//
// Two refusals, and they are opposite ends of the same argument. A session later
// than tonight's stamps every row it writes with a date in the future and takes
// over the run page, which no later run undoes. A session older than the newest
// the store holds is worse than useless: the corporate action check deletes a
// due name's whole series and refetches it only as far as the session being
// replayed, so the bars after it go, and the next night reads the hole as a gap
// and stops that name. Traced at 8.0 from the phase 7 sign-off's carried item.
//
// What stays allowed is what the runbook asks for: a re-run of the newest
// session the store holds, and a session the store has not reached yet, which is
// the catch-up night after a machine was off.
// see: A session the night finds missing is fetched in bulk before tonight's
public static class NightSession
{
    public static string? Refusal(DateOnly named, DateOnly tonight, DateOnly? newestStored)
    {
        var date = named.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (named > tonight)
        {
            return $"nightly: '--session {date}' is later than tonight's session. A night replayed for a " +
                "future session stamps every row it writes with that date and takes over the run page, " +
                "which no later run can undo.";
        }

        if (newestStored is { } held && named < held)
        {
            return $"nightly: '--session {date}' is older than {held.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, " +
                "the newest session the store holds. The corporate action check refetches a due name's year up to the " +
                "session being replayed after dropping its series, so an older replay removes the bars after it and the " +
                "next night reads the hole as a gap. Re-run the newest session, or name one the store has not reached.";
        }

        return null;
    }

    // The newest session the store holds a bar for, or null where the store has
    // none, which is a first run rather than an empty answer.
    public static DateOnly? NewestStored(string databaseFile)
    {
        if (!File.Exists(databaseFile))
        {
            return null;
        }

        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        return command.ExecuteScalar() is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }
}
