using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// The days of every split the split tables hold, by stock, the only steps in a stock's closes a reading takes for a split.
// see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
public static class SplitDays
{
    // The split tables the night and the history since 2019 read: the night's day files' and the splits pull's, the long
    // price pull's being read by no night.
    public static IReadOnlyList<string> NightTables { get; } = ["split_event", "pulled_split"];

    // Every day the named tables hold, by stock, a table the store does not hold read as none.
    public static async Task<Dictionary<string, List<DateOnly>>> ReadAsync(SqliteConnection connection, IReadOnlyList<string> tables, CancellationToken cancellation)
    {
        var days = new Dictionary<string, List<DateOnly>>(StringComparer.Ordinal);

        foreach (var table in tables)
        {
            await using (var held = connection.CreateCommand())
            {
                held.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
                held.Parameters.AddWithValue("$name", table);

                if (Convert.ToInt64(await held.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) == 0)
                {
                    continue;
                }
            }

            await using var command = connection.CreateCommand();

            command.CommandText = $"SELECT ticker, ex_date FROM {table};";

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var ticker = reader.GetString(0);

                if (!days.TryGetValue(ticker, out var stock))
                {
                    days[ticker] = stock = [];
                }

                stock.Add(DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }

        return days;
    }
}
