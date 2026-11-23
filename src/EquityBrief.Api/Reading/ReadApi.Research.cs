using EquityBrief.Core.Loop;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The researcher's newest week as the Improvement loop page reads it for one index: when it ran and what it came to, each
// setup's failure list over the early years and the live nights, and each idea it placed with its sentence, its line, what
// became of it and, where it was tested, the notebook's verdict and why.
// see: The researcher reads each rule's failure list over the early years and the nights since the restart, and the later years as one word
public sealed partial class ReadApi
{
    const string NewestResearch = "SELECT run_id, started_at, outcome, detail FROM run_log WHERE stage = 'research' ORDER BY rowid DESC LIMIT 1;";

    const string ResearchFailures = @"
        SELECT family, population, reading, highest, picks, losers, in_band, losers_in_band
        FROM research_failure WHERE run_id = $run_id AND index_code = $index
        ORDER BY family, population, (losers_in_band * 1.0 / losers) - (in_band * 1.0 / picks) DESC, reading;";

    const string ResearchIdeas = @"
        SELECT i.family, i.rank, i.line, i.reason, i.outcome, n.verdict, n.why
        FROM research_idea i LEFT JOIN notebook n ON n.id = i.notebook_id
        WHERE i.run_id = $run_id AND i.index_code = $index
        ORDER BY i.family, i.rank;";

    public async Task<ResearchWeekView?> ResearchAsync(string index)
    {
        await using var connection = Open();
        string runId;
        ResearchWeekView week;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestResearch;

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            runId = reader.GetString(0);
            week = new ResearchWeekView(reader.GetString(1)[..10], reader.GetString(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3), [], []);
        }

        var failures = new List<ResearchFailureView>();
        var ideas = new List<ResearchIdeaView>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ResearchFailures;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                failures.Add(new ResearchFailureView(
                    reader.GetString(0),
                    reader.GetString(1),
                    new FailureRow(reader.GetString(2), reader.GetInt64(3) == 1, reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7))));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ResearchIdeas;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                ideas.Add(new ResearchIdeaView(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        return week with { Failures = failures, Ideas = ideas };
    }
}
