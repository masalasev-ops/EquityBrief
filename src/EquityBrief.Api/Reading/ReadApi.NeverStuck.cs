using System.Globalization;
using EquityBrief.Core.Loop;

namespace EquityBrief.Api.Reading;

// Never stuck as the Improvement loop page reads it: the monthly run before the newest, or the newest where only one has run;
// the ideas the one tester passed since; the near misses it read since with the ideas the researcher placed after them; and the
// researcher's weeks since, those failed counted apart.
// see: A month with no idea passed names its nearest misses and what the researcher tried next, and a failed week is counted and never stops the next
public sealed partial class ReadApi
{
    const string MonthlyRuns = "SELECT MIN(started_at) FROM run_log WHERE stage LIKE 'loop-month-%' GROUP BY run_id ORDER BY 1 DESC LIMIT 2;";

    const string PassedSince = "SELECT COUNT(*) FROM survivor WHERE entered_at >= $since;";

    const string NearMissesSince = "SELECT id, index_code, family, words, line, adjusted, bar, entered_at FROM notebook WHERE verdict = 'near miss' AND index_code IS NOT NULL AND family IS NOT NULL AND entered_at >= $since;";

    const string PlacedSince = "SELECT index_code, family, line, reason, written_at FROM research_idea WHERE written_at >= $since;";

    const string WeeksSince = "SELECT COUNT(*), COALESCE(SUM(outcome = 'failed'), 0) FROM run_log WHERE stage = 'research' AND started_at >= $since;";

    public async Task<StuckView> NeverStuckAsync()
    {
        await using var connection = Open();
        var runs = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MonthlyRuns;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                runs.Add(reader.GetString(0));
            }
        }

        if (runs.Count == 0)
        {
            return NeverStuck.Of(null, 0, [], [], 0, 0, clock.UtcNow);
        }

        var since = runs[^1];
        var misses = new List<NearMissRow>();
        var placed = new List<PlacedIdea>();
        int passed;
        int weeks;
        int failed;

        static DateTimeOffset Instant(string stamp) => DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PassedSince;
            command.Parameters.AddWithValue("$since", since);
            passed = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NearMissesSince;
            command.Parameters.AddWithValue("$since", since);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                misses.Add(new NearMissRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                    Instant(reader.GetString(7))));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PlacedSince;
            command.Parameters.AddWithValue("$since", since);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                placed.Add(new PlacedIdea(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), Instant(reader.GetString(4))));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = WeeksSince;
            command.Parameters.AddWithValue("$since", since);

            await using var reader = await command.ExecuteReaderAsync();

            await reader.ReadAsync();
            (weeks, failed) = (reader.GetInt32(0), reader.GetInt32(1));
        }

        return NeverStuck.Of(DateOnly.FromDateTime(Instant(since).UtcDateTime), passed, misses, placed, failed, weeks, clock.UtcNow);
    }
}
