using System.Globalization;
using EquityBrief.Core.Loop;

namespace EquityBrief.Api.Reading;

// The pullbacks' search as the Improvement loop page reads it for one index: its newest run's lines in the notebook, counted
// by verdict, best on 2005 to 2018 first.
// see: The pullbacks' search walks the swing filter's dials as grammar lines, chosen on 2005 to 2018 and judged together from 2019
public sealed partial class ReadApi
{
    const string NewestPullbackSearch = @"
        SELECT run_id, MIN(entered_at) FROM notebook WHERE source = $source AND index_code = $index AND line LIKE $holds
        GROUP BY run_id ORDER BY MIN(id) DESC LIMIT 1;";

    const string PullbackSearchLines = @"
        SELECT words, line, early_picks, early_edge, late_picks, late_edge, adjusted, verdict, why
        FROM notebook WHERE source = $source AND index_code = $index AND run_id = $run_id AND line LIKE $holds;";

    public async Task<PullbackSearchView?> PullbackSearchAsync(string index)
    {
        await using var connection = Open();
        string runId;
        DateOnly day;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestPullbackSearch;
            command.Parameters.AddWithValue("$source", PullbackSearchView.Source);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$holds", PullbackSearchView.LineHolds);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync() || reader.IsDBNull(0))
            {
                return null;
            }

            (runId, day) = (reader.GetString(0), DateOnly.ParseExact(reader.GetString(1)[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var rows = new List<PullbackSearchRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PullbackSearchLines;
            command.Parameters.AddWithValue("$source", PullbackSearchView.Source);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$holds", PullbackSearchView.LineHolds);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                double? Figure(int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);

                rows.Add(new PullbackSearchRow(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1), reader.GetInt32(2), Figure(3), reader.GetInt32(4), Figure(5), Figure(6), reader.GetString(7), reader.GetString(8)));
            }
        }

        return new PullbackSearchView(
            index,
            day,
            rows.Count,
            rows.Count(row => row.Verdict == IdeaTester.Passed),
            rows.Count(row => row.Verdict == IdeaTester.NearMiss),
            [.. PullbackSearchView.Ordered(rows).Take(PullbackSearchView.Drawn)]);
    }
}
