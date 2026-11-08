using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// What the Loop page's row of the long history reads on an index: each family's newest search over the long history and each
// price family's newest recommended buy and sell points, a family's newest by the order its rows were written.
// see: Every price family's traded plan's buy and sell points are tested over the long history and one combination is recommended a family an index
public sealed partial class ReadApi
{
    const string LongSearchesOn = @"
        SELECT family, through, first_test_year, words, current_words, units, adjusted, better, counted, searches, passed
        FROM long_search WHERE rowid IN (SELECT MAX(rowid) FROM long_search WHERE index_code = $index GROUP BY family)
        ORDER BY family;";

    const string PlanPointsOn = @"
        SELECT family, through, setups, combination, fills, fill_rate, edge_per_pick, traded_fills, traded_edge_per_pick, later_edge_per_pick, traded_later_edge_per_pick
        FROM plan_point WHERE rowid IN (SELECT MAX(rowid) FROM plan_point WHERE index_code = $index GROUP BY family)
        ORDER BY family;";

    public async Task<(IReadOnlyList<LongSearchRow> Searches, IReadOnlyList<PlanPointRow> Points)> LongSearchAsync(string index)
    {
        await using var connection = Open();
        var searches = new List<LongSearchRow>();
        var points = new List<PlanPointRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LongSearchesOn;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                searches.Add(new LongSearchRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10) == 1));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PlanPointsOn;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                points.Add(new PlanPointRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    reader.IsDBNull(10) ? null : reader.GetDouble(10)));
            }
        }

        return (searches, points);
    }
}
