using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The notebook as the Improvement loop page reads it for one index: its ideas newest first, those that name no index among
// them, each with its source, its words and grammar line, its figures, its verdict and why; and the count of every verdict.
// see: Every idea from any source is tested by the one tester and kept in a notebook nothing is deleted from
public sealed partial class ReadApi
{
    // The most rows the page draws, newest first, the count of all beside them.
    public const int NotebookShown = 50;

    const string NotebookRows = @"
        SELECT COALESCE(tested_on, substr(entered_at, 1, 10)), source, index_code, words, line, early_edge, late_edge, verdict, why
        FROM notebook WHERE index_code IS NULL OR index_code = $index
        ORDER BY COALESCE(tested_on, substr(entered_at, 1, 10)) DESC, id DESC LIMIT $shown;";

    const string NotebookCounts = "SELECT verdict, COUNT(*) FROM notebook WHERE index_code IS NULL OR index_code = $index GROUP BY verdict;";

    public async Task<NotebookView> NotebookAsync(string index)
    {
        var rows = new List<NotebookRow>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        await using var connection = Open();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NotebookRows;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$shown", NotebookShown);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows.Add(new NotebookRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.GetString(7),
                    reader.GetString(8)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NotebookCounts;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                counts[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        return new NotebookView(rows, counts);
    }
}
