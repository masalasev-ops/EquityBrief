using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The background variants entered to wait for a slot as the Run page reads them: each with the live rule it moves from, the
// session its live rule's orders are walked after, whether it has registered since, and its record over the orders that
// ended, beside the live rule's over the same orders.
// see: A background variant a full family has no room for is walked each night from its live rule's own orders and registers in the first slot that frees
public sealed partial class ReadApi
{
    const string WaitingVariantRows = @"
        SELECT w.candidate, w.live, w.walked_from,
               EXISTS (SELECT 1 FROM candidate_register r WHERE r.candidate = w.candidate AND r.event = 'registered'),
               COUNT(t.ticker),
               COALESCE(SUM(t.under_floor), 0),
               COUNT(t.result),
               AVG(t.result),
               COUNT(t.live_result),
               AVG(t.live_result)
        FROM waiting_variant w
        LEFT JOIN waiting_variant_trade t ON t.waiting_id = w.id
        GROUP BY w.id
        ORDER BY w.id;";

    public async Task<IReadOnlyList<WaitingVariantRow>> WaitingVariantsAsync()
    {
        var rows = new List<WaitingVariantRow>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = WaitingVariantRows;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new WaitingVariantRow(
                reader.GetString(0),
                reader.GetString(1),
                Day(reader.GetString(2)),
                reader.GetInt64(3) == 1,
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9)));
        }

        return rows;
    }
}
