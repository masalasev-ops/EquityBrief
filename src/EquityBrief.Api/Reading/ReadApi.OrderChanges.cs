using EquityBrief.Core.Paper;

namespace EquityBrief.Api.Reading;

// What Paper trading's Order changes section and the waiting cards read: the newest order changes written for a night, list by
// list in their order, and each live rule's fill odds on an index from the newest run over the ledger's past orders.
// see: Each night ends with the order changes for every live rule, book and the AI stock picker, on Paper trading and in one phone message
public sealed partial class ReadApi
{
    const string OrderChangeRows = @"
        SELECT list, index_code, who, ticker, words, checked, place FROM order_change
        WHERE run_id = (SELECT run_id FROM order_change WHERE session_date = $night ORDER BY written_at DESC, run_id DESC LIMIT 1)
        ORDER BY list, place;";

    const string FillOddsRows = @"
        SELECT family, band, days_left, orders, filled FROM fill_odds
        WHERE run_id = (SELECT run_id FROM fill_odds WHERE index_code = $index ORDER BY run_id DESC LIMIT 1);";

    const string TrailStopRows = @"
        SELECT family, ticker, listed_on, session_date, first_stop, stop FROM trail_stop
        WHERE index_code = $index
        ORDER BY session_date, run_id;";

    // From the operator's answers of 2026-11-28, item 4: every trailing stop the nights stored on an index, which a closed
    // trade's sale is read against.
    // see: A trailing trade's stop is stored as each night raises it, and a sale above its first stop is sold on its trailing stop
    public async Task<IReadOnlyList<TrailStopRow>> TrailStopsAsync(string index)
    {
        var rows = new List<TrailStopRow>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = TrailStopRows;
        command.Parameters.AddWithValue("$index", index);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new TrailStopRow(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                EquityBrief.Data.Money.FromStorage(reader.GetString(4)),
                EquityBrief.Data.Money.FromStorage(reader.GetString(5))));
        }

        return rows;
    }

    // The night's order changes as written, none where no run wrote them for the night.
    public async Task<OrderSummary?> OrderChangesAsync(DateOnly night)
    {
        var changes = new List<OrderChange>();
        var written = false;
        var checkedOn = false;

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = OrderChangeRows;
        command.Parameters.AddWithValue("$night", Stamp(night));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            written = true;
            checkedOn = reader.GetInt32(5) == 1;

            if (reader.GetInt32(6) >= 0)
            {
                changes.Add(new OrderChange(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            }
        }

        return written ? new OrderSummary(night, changes, checkedOn) : null;
    }

    // Each live rule's fill odds on an index, by its family, from the newest run; none where no run wrote them.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FillOddsCell>>> FillOddsAsync(string index)
    {
        var cells = new Dictionary<string, List<FillOddsCell>>(StringComparer.Ordinal);

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FillOddsRows;
        command.Parameters.AddWithValue("$index", index);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var family = reader.GetString(0);

            if (!cells.TryGetValue(family, out var list))
            {
                cells[family] = list = [];
            }

            list.Add(new FillOddsCell(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }

        return cells.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FillOddsCell>)pair.Value, StringComparer.Ordinal);
    }
}
