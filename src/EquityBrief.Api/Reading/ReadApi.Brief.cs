using System.Globalization;

namespace EquityBrief.Api.Reading;

// What a stock's brief read reads beside the chart page's own reads: its next report after the night.
// see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
public sealed partial class ReadApi
{
    const string NextReportAfter = "SELECT event_date FROM calendar WHERE ticker = $ticker AND kind = 'earnings' AND event_date > $after ORDER BY event_date LIMIT 1;";

    // The stock's next report the calendar holds after a night, none where it holds none.
    public async Task<DateOnly?> NextReportAsync(string ticker, DateOnly after)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NextReportAfter;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$after", after.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync() is string day ? DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
}
