using System.Globalization;
using EquityBrief.Core.History;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// The check's marks as every reader of the every-session table reads them, the stock-sessions it skips.
// see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
public static class UnreliableMarkRows
{
    public static async Task<MarkedSessions> ReadAsync(SqliteConnection connection, CancellationToken cancellation = default)
    {
        var marks = new List<UnreliableMark>();

        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker, first_session, last_session, reading, kind, marked_at FROM unreliable_mark;";

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            marks.Add(new UnreliableMark(reader.GetString(0), Day(reader.GetString(1)), Day(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        return new MarkedSessions(marks);
    }

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
