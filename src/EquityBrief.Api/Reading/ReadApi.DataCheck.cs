using System.Globalization;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// What the History page reads of the data check: the run that wrote its rows, the counts by class, kind and era, and the
// findings a person reads.
// see: The data check classifies every stock's pulled sessions by stated rules into a table of its own, and moves no price row
public sealed partial class ReadApi
{
    // The run whose rows the table holds, the check over the years the pull holds or over the long prices from 2005.
    const string DataCheckRun = "SELECT run_id, ended_at, detail FROM run_log WHERE stage IN ('data-check', 'data-check-long') AND run_id IN (SELECT DISTINCT run_id FROM data_check) ORDER BY rowid DESC LIMIT 1;";

    const string DataCheckCounts = @"
        SELECT class, kind, era, COUNT(*), COUNT(DISTINCT ticker) FROM data_check GROUP BY class, kind, era
        UNION ALL SELECT class, kind, NULL, COUNT(*), COUNT(DISTINCT ticker) FROM data_check GROUP BY class, kind
        UNION ALL SELECT class, NULL, era, COUNT(*), COUNT(DISTINCT ticker) FROM data_check GROUP BY class, era
        UNION ALL SELECT class, NULL, NULL, COUNT(*), COUNT(DISTINCT ticker) FROM data_check GROUP BY class;";

    const string DataCheckToRead = @"
        SELECT ticker, session_date, class, kind, says FROM data_check
        WHERE class IN ('ticker change', 'when-issued start', 'bankruptcy emergence') OR (class = 'jump' AND kind IN ('unexplained', 'spike back'))
        ORDER BY class, kind, session_date, ticker;";

    public async Task<DataCheckView> DataCheckAsync()
    {
        await using var connection = Open();

        string? run = null, ranAt = null, read = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = DataCheckRun;

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                // The day the run ended, the date of its UTC instant.
                (run, ranAt, read) = (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)[..10], reader.IsDBNull(2) ? null : reader.GetString(2));
            }
        }

        var counts = new List<DataCheckCount>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = DataCheckCounts;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                counts.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4)));
            }
        }

        var rows = new List<DataCheckRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = DataCheckToRead;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows.Add(new(reader.GetString(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            }
        }

        return new DataCheckView(run, ranAt, read, counts, rows);
    }
}
