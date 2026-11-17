using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// Every split the split tables hold, by stock, with the shares one share became, the only steps in a stock's closes a
// reading takes for a split being those of a held split's size.
// see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
public static class SplitDays
{
    // The split tables the night and the history since 2019 read: the night's day files' and the splits pull's, the long
    // price pull's being read by no night.
    public static IReadOnlyList<string> NightTables { get; } = ["split_event", "pulled_split"];

    // Every split the named tables hold, by stock, a table the store does not hold read as none and a ratio with a side of
    // nothing left out.
    public static async Task<Dictionary<string, List<StoredSplit>>> ReadAsync(SqliteConnection connection, IReadOnlyList<string> tables, CancellationToken cancellation)
    {
        var splits = new Dictionary<string, List<StoredSplit>>(StringComparer.Ordinal);

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

            command.CommandText = $"SELECT ticker, ex_date, new_shares, old_shares FROM {table};";

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var newShares = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
                var oldShares = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture);

                if (newShares <= 0m || oldShares <= 0m)
                {
                    continue;
                }

                var ticker = reader.GetString(0);

                if (!splits.TryGetValue(ticker, out var stock))
                {
                    splits[ticker] = stock = [];
                }

                stock.Add(new StoredSplit(DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), Statistic.FromRatio(newShares / oldShares)));
            }
        }

        return splits;
    }
}
