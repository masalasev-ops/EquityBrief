using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// What the History page reads of the long ledger: each family's years on each index under the two readings, as the long build
// wrote them at its end.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public sealed partial class ReadApi
{
    const string LongYears = @"
        SELECT index_code, family, year, reading, settled, targets, edge_after_cost_mean, crossing, edge_after_cost_without, read_through
        FROM long_reading
        ORDER BY family, year, index_code, reading;";

    public async Task<LongYearsView> LongYearsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LongYears;

        var rows = new List<LongYearRow>();
        string? through = null;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LongYearRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8)));

            through = reader.GetString(9) is var read && (through is null || string.CompareOrdinal(read, through) > 0) ? read : through;
        }

        return new LongYearsView(through, rows);
    }
}
