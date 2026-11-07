using System.Globalization;

namespace EquityBrief.Api.Reading;

// What a stock's brief read reads beside the chart page's own reads: its next report after the night.
// see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
public sealed partial class ReadApi
{
    const string NextReportAfter = "SELECT event_date FROM calendar WHERE ticker = $ticker AND kind = 'earnings' AND event_date > $after ORDER BY event_date LIMIT 1;";

    // Each stock's newest report on or before a night: a stored quarter's reported day or the calendar's earnings date.
    const string NewestReportsTo = @"
        SELECT ticker, MAX(day) FROM (
            SELECT ticker, report_date AS day FROM reported_quarter WHERE report_date IS NOT NULL AND report_date <= $night
            UNION ALL
            SELECT ticker, event_date AS day FROM calendar WHERE kind = 'earnings' AND event_date <= $night)
        GROUP BY ticker;
    ";

    // Every stock's reports over the reach a next report is estimated from, to a night, from the same two sources as its
    // newest: the quarters' report dates and the calendar's earnings.
    // see: A page draws one next report date, the calendar's where it stands and otherwise one estimate stated with its source
    const string ReportDaysTo = @"
        SELECT DISTINCT ticker, day FROM (
            SELECT ticker, report_date AS day FROM reported_quarter WHERE report_date IS NOT NULL AND report_date <= $night AND report_date >= $from
            UNION ALL
            SELECT ticker, event_date AS day FROM calendar WHERE kind = 'earnings' AND event_date <= $night AND event_date >= $from)
        ORDER BY ticker, day;
    ";

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DateOnly>>> ReportDaysToAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ReportDaysTo;
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$from", night.AddDays(-EquityBrief.Core.Cards.CardLines.EstimateReach).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var days = new Dictionary<string, List<DateOnly>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var ticker = reader.GetString(0);

            if (!days.TryGetValue(ticker, out var held))
            {
                days[ticker] = held = [];
            }

            held.Add(DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return days.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DateOnly>)pair.Value, StringComparer.Ordinal);
    }

    // One stock's newest report on or before a night, as the cards read it, its quarter filed or not.
    // see: A figure fetched before the newest report says so with its fetch date, and a company's value names the day its shares were filed
    public async Task<DateOnly?> NewestReportAsync(string ticker, DateOnly night) =>
        (await NewestReportsAsync(night)).TryGetValue(ticker, out var newest) ? newest : null;

    // The newest report of every stock on or before a night, which a page's cards say a stock reported on.
    // see: A page draws one next report date, the calendar's where it stands and otherwise one estimate stated with its source
    public async Task<IReadOnlyDictionary<string, DateOnly>> NewestReportsAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestReportsTo;
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var newest = new Dictionary<string, DateOnly>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            newest[reader.GetString(0)] = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return newest;
    }

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
