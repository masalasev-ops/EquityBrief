using System.Globalization;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// One night of Quality at a fair price on one index as its step stored it: the members it read, how many held a
// scorecard and how many qualified, how many it holds after the night, whether it reshuffled, the settings it ran on, and
// the failure that stopped its part where one did.
public sealed record QualityNightRow(string Index, DateOnly Session, int Members, int Scored, int Qualified, int Held, bool Reshuffle, string Settings, string? Fault);

// One member's answer on a night: whether it qualified, its place in the family's order, its scorecard's checks passed and
// applied, its valuation's place, why it did not qualify, and its growth carried from its first answer.
public sealed record QualityResultRow(string Ticker, bool Passed, int? Place, int PassedChecks, int Applied, double? ValuePlace, string? Reason, double Carried);

// One holding as stored, read as of a night: the night it was chosen and its close there, its fill and the gaps it and its
// benchmark opened at, and its end, result, benchmark, round trip and edge where it ended by the night.
public sealed record QualityHoldingRow(
    string Ticker,
    DateOnly ChosenOn,
    decimal BuyClose,
    DateOnly? BoughtOn,
    decimal? Fill,
    double? Gap,
    double? BenchGap,
    DateOnly? EndedOn,
    string EndReason,
    double? Result,
    double? Benchmark,
    double? Cost,
    double? Edge);

// What the read surface reads of the quality family's own rows, which its step alone writes.
// see: Quality at a fair price holds the companies the scorecard rates strongest that are not dear against their own history, reshuffled on each month's first session
public sealed partial class ReadApi
{
    const string QualityNightOn = @"
        SELECT index_code, session_date, members, scored, qualified, held, reshuffle, settings, fault
        FROM quality_night WHERE rule = 'provisional' AND index_code = $index AND session_date = $on;
    ";

    const string QualityLastReshuffle = @"
        SELECT MAX(session_date) FROM quality_night
        WHERE rule = 'provisional' AND index_code = $index AND reshuffle = 1 AND fault IS NULL AND session_date <= $on;
    ";

    const string QualityResultsOn = @"
        SELECT ticker, passed, place, passed_checks, applied, value_place, reason, carried
        FROM quality_result WHERE rule = 'provisional' AND index_code = $index AND session_date = $on
        ORDER BY place IS NULL, place, ticker;
    ";

    // Every holding chosen by the night that had not ended before a session, its buy only where it was bought by the night
    // and its end and its figures only where it ended by the night.
    const string QualityHoldingsFrom = @"
        SELECT ticker, chosen_on, buy_close,
               CASE WHEN bought_on <= $on THEN bought_on END, CASE WHEN bought_on <= $on THEN fill END,
               CASE WHEN bought_on <= $on THEN gap END, CASE WHEN bought_on <= $on THEN bench_gap END,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN end_reason ELSE 'open' END,
               CASE WHEN ended_on <= $on THEN result END, CASE WHEN ended_on <= $on THEN benchmark END,
               CASE WHEN ended_on <= $on THEN cost END, CASE WHEN ended_on <= $on THEN edge END
        FROM quality_holding
        WHERE rule = 'provisional' AND index_code = $index AND chosen_on <= $on AND (ended_on IS NULL OR ended_on >= $since)
        ORDER BY chosen_on, ticker;
    ";

    // Quality at a fair price's night on an index, none for a night its step did not read.
    public async Task<QualityNightRow?> QualityNightAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = QualityNightOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new QualityNightRow(
                reader.GetString(0),
                QualityDate(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt64(6) == 1,
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8))
            : null;
    }

    // The newest reshuffle on or before a night, none before the family's first.
    public async Task<DateOnly?> QualityLastReshuffleAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = QualityLastReshuffle;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));

        return await command.ExecuteScalarAsync() is string stored ? QualityDate(stored) : null;
    }

    // Every member's answer on a night, in the family's order and the rest by ticker.
    public async Task<IReadOnlyList<QualityResultRow>> QualityResultsAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = QualityResultsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<QualityResultRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new QualityResultRow(
                reader.GetString(0),
                reader.GetInt64(1) == 1,
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDouble(7)));
        }

        return rows;
    }

    // Every holding chosen by a night that was still open on a session or ended after it, read as of the night.
    public async Task<IReadOnlyList<QualityHoldingRow>> QualityHoldingsAsync(string index, DateOnly on, DateOnly since)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = QualityHoldingsFrom;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));
        command.Parameters.AddWithValue("$since", Day(since));

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<QualityHoldingRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new QualityHoldingRow(
                reader.GetString(0),
                QualityDate(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                reader.IsDBNull(3) ? null : QualityDate(reader.GetString(3)),
                reader.IsDBNull(4) ? null : Money.FromStorage(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : QualityDate(reader.GetString(7)),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12)));
        }

        return rows;
    }

    static DateOnly QualityDate(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
