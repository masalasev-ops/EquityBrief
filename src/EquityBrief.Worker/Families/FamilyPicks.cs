using System.Globalization;
using EquityBrief.Core.Families;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

// The page's list on a night as the worker's readers of it take it: the stocks listed, in the order the
// page draws them, or nothing where the families did not draw the night's list, which is every night
// before them. A reader handed nothing reads the list as it did before.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public static class FamilyPicks
{
    const string Drawn = "SELECT COUNT(*) FROM family_night WHERE session_date = $night;";

    const string Listed = "SELECT ticker FROM family_pick WHERE session_date = $night AND state = '" + FamilyList.Listed + "' ORDER BY place, ticker;";

    public static async Task<IReadOnlyList<string>?> ListedAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation = default)
    {
        var session = night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using (var drawn = connection.CreateCommand())
        {
            drawn.CommandText = Drawn;
            drawn.Parameters.AddWithValue("$night", session);

            if (Convert.ToInt32(await drawn.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) == 0)
            {
                return null;
            }
        }

        await using var command = connection.CreateCommand();

        command.CommandText = Listed;
        command.Parameters.AddWithValue("$night", session);

        var tickers = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }
}
