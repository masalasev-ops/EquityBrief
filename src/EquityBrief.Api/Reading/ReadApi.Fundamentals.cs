using System.Globalization;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Api.Reading;

// A member's fundamentals scorecard as the night stored it: its index and night, the newest quarter it read and the one a
// year before, a newer quarter reported and not yet filed, whether it is a financial company, its count passed and read,
// its word, its nine checks, gross profit over total assets with its place on its index, and its price over earnings
// beside its industry's median.
public sealed record FundamentalScoreRow(
    string Index,
    DateOnly Session,
    string Ticker,
    DateOnly? Quarter,
    DateOnly? YearBefore,
    DateOnly? Awaited,
    bool Financial,
    int Passed,
    int Applied,
    string? Word,
    IReadOnlyList<ScorecardCheck> Checks,
    double? GrossProfitability,
    int? GrossRank,
    int? GrossRanked,
    double? Multiple,
    string? Industry,
    double? IndustryMultiple);

// What a stock's fundamentals card reads: the scorecard the night stored for it, and its own readings the ledger stored
// on the night.
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
public sealed partial class ReadApi
{
    const string OwnReadingsOn = "SELECT readings FROM ledger_reading WHERE index_code = $index AND session_date = $night AND ticker = $ticker;";

    // A stock's readings the ledger stored on a night in the catalogue's order, a row stored before a reading joined
    // holding it as none; none where the night stored none for the stock.
    public async Task<IReadOnlyList<double?>?> OwnLedgerReadingsAsync(string index, DateOnly night, string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = OwnReadingsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ticker", ticker);

        if (await command.ExecuteScalarAsync() is not string stored
            || System.Text.Json.JsonSerializer.Deserialize<double?[]>(stored) is not { Length: var length } readings || length > LedgerReadings.Count)
        {
            return null;
        }

        var whole = new double?[LedgerReadings.Count];

        readings.CopyTo(whole, 0);

        return whole;
    }

    const string ScoreOnOrBefore = @"
        SELECT index_code, session_date, ticker, quarter, year_before, awaited, financial, passed, applied, word, checks,
               gross_profitability, gross_rank, gross_ranked, multiple, industry, industry_multiple
        FROM fundamental_score
        WHERE ticker = $ticker AND session_date <= $night
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    // The scorecard the newest night on or before the given one stored for the stock, none where none stored one.
    public async Task<FundamentalScoreRow?> FundamentalScoreAsync(string ticker, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ScoreOnOrBefore;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        DateOnly? DayAt(int at) => reader.IsDBNull(at) ? null : DateOnly.ParseExact(reader.GetString(at), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        double? RealAt(int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);
        int? CountAt(int at) => reader.IsDBNull(at) ? null : reader.GetInt32(at);

        return new FundamentalScoreRow(
            reader.GetString(0),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(2),
            DayAt(3),
            DayAt(4),
            DayAt(5),
            reader.GetInt32(6) == 1,
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            ScorecardText.ChecksOf(reader.GetString(10)),
            RealAt(11),
            CountAt(12),
            CountAt(13),
            RealAt(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            RealAt(16));
    }
}
