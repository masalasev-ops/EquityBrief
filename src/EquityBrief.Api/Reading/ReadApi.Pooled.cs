using System.Text.Json;
using EquityBrief.Core.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// From 23.2 the one verdict across the three indices as the Loop page and paper trading read it: the newest pooled run of a
// month, or the newest of all, with every verdict it stored. The pages draw them and compute none.
// see: One verdict across the three indices stands beside each index's own, and an approval still changes one index's rule
public sealed partial class ReadApi
{
    // The newest pooled run, of a month where one is named, by the order the runs were written.
    const string PooledOf = @"
        SELECT run_id, month, family, kind, indices, rules, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, judged_from, carried_by
        FROM loop_pooled
        WHERE run_id = (SELECT p.run_id FROM loop_pooled p WHERE $month IS NULL OR p.month = $month ORDER BY p.rowid DESC LIMIT 1)
        ORDER BY rowid;";

    public async Task<IReadOnlyList<LoopPooledRow>> PooledAsync(string? month = null)
    {
        var rows = new List<LoopPooledRow>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = PooledOf;
        command.Parameters.AddWithValue("$month", (object?)month ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();

        static bool Flag(SqliteDataReader reader, int at) => reader.GetInt64(at) == 1;

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopPooledRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4).Split(' ', StringSplitOptions.RemoveEmptyEntries),
                JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                Flag(reader, 10),
                Flag(reader, 11),
                reader.GetInt32(12),
                reader.GetInt32(13),
                reader.IsDBNull(14) ? null : reader.GetDouble(14),
                Flag(reader, 15),
                Flag(reader, 16),
                reader.IsDBNull(17) ? null : reader.GetInt32(17),
                reader.IsDBNull(18) ? null : reader.GetString(18)));
        }

        return rows;
    }
}
