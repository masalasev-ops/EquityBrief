using EquityBrief.Core.Loop;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The buy range's newest widths test as the Improvement loop page reads it for one index: the day it ran and each row it
// wrote on the index, every setup together and each on its own, the buy at the open first.
// see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
public sealed partial class ReadApi
{
    const string NewestRangeWidths = "SELECT run_id, started_at, detail FROM run_log WHERE stage = 'buy-range-widths' ORDER BY rowid DESC LIMIT 1;";

    const string RangeWidthRows = @"
        SELECT family, ceiling, floor, years, offered, bought, above, below, edge, missed, saved, chosen
        FROM buy_range_width WHERE run_id = $run_id AND index_code = $index
        ORDER BY family = 'all' DESC, family, ceiling IS NOT NULL, ceiling, floor IS NULL, floor, years;";

    public async Task<BuyRangeTableView?> BuyRangesAsync(string index)
    {
        await using var connection = Open();
        string runId;
        string day;
        bool provisional;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestRangeWidths;

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            (runId, day) = (reader.GetString(0), reader.GetString(1)[..10]);
            provisional = !reader.IsDBNull(2) && reader.GetString(2).StartsWith(BuyRanges.PickerRun, StringComparison.Ordinal);
        }

        var rows = new List<RangeWidthRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RangeWidthRows;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                RangeWidths? widths = reader.IsDBNull(1) ? null : new RangeWidths(reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetDouble(2));
                var offered = reader.GetInt32(4);
                double Sum(int column) => reader.IsDBNull(column) ? 0 : reader.GetDouble(column) * offered;

                rows.Add(new RangeWidthRow(
                    index,
                    reader.GetString(0),
                    widths,
                    reader.GetString(3),
                    new RangeTally(offered, reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), Sum(8), Sum(9), Sum(10)),
                    reader.GetInt64(11) == 1));
            }
        }

        return new BuyRangeTableView(day, rows) { Provisional = provisional };
    }
}
