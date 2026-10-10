using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Moves;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// A name's typical earnings move as the chart averages step stored it, with the pull it read or why none was read.
public sealed record EarningsMoveRow(DateOnly Night, EarningsMove Move, string? Pull, string? Reason);

// A name's long view as the chart averages step stored it: the month-end closes, the pull and the ratio that brought it
// to the stored prices, or why none was read.
public sealed record LongViewRow(DateOnly Night, IReadOnlyList<MonthEnd> Closes, string? Pull, decimal? Ratio, string? Reason);

// A filing the filings refresh stored for a company, once a filing: its form, the day it was filed, its accession number
// and the filer it was filed under.
public sealed record FiledRow(string Form, DateOnly Filed, string Accession, string Cik);

// One stored session's adjusted close beside the provider's raw close, which a dividend's step is read off.
public sealed record RawCloseRow(DateOnly Session, decimal Close, decimal RawClose);

// What the earnings reactions, the long view, the chart's marks and the moves' causes read.
// see: A stock's typical earnings move is the median size of its reaction over every print the store holds, read through the history pulled before its year
// see: The long view draws the month-end closes over the pulled history at the bar store's scale with the plan's levels on the same prices
// see: A move's cause is research's where written, then the labelled news inside it and then the filings dated inside it
public sealed partial class ReadApi
{
    const string EarningsMoveOf = "SELECT night, prints, unread, first_print, median_move, pull, reason FROM earnings_move WHERE ticker = $ticker;";

    const string LongViewOf = "SELECT night, closes, pull, ratio, reason FROM long_view WHERE ticker = $ticker;";

    // The filings stored for the filer the name's newest company row naming one names, between two days, a filing once.
    const string FiledBetween = @"
        SELECT DISTINCT form, filed, accession, cik FROM filed_fact
        WHERE cik = (SELECT cik FROM company WHERE ticker = $ticker AND cik IS NOT NULL ORDER BY fetched_at DESC LIMIT 1)
          AND filed >= $from AND filed <= $to
        ORDER BY filed, accession;
    ";

    // The SEC's facts as first filed for the name's filer, of periods ending from a day on and filed by the night, which
    // the sources check the latest quarter's lines against.
    // see: A figure the filing and the provider's fetch state differently beyond rounding is listed under the sources with the filing first, and the page draws the fetch's
    const string FiledFactsOf = @"
        SELECT concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact
        WHERE cik = (SELECT cik FROM company WHERE ticker = $ticker AND cik IS NOT NULL ORDER BY fetched_at DESC LIMIT 1)
          AND period_end >= $from AND filed <= $on
        ORDER BY concept, period_end, period_start;
    ";

    public async Task<IReadOnlyList<EquityBrief.Core.Ledger.FiledFactRow>> FiledFactsAsync(string ticker, DateOnly from, DateOnly? on = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FiledFactsOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$on", (on ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<EquityBrief.Core.Ledger.FiledFactRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new EquityBrief.Core.Ledger.FiledFactRow(
                reader.GetString(0),
                Day(reader.GetString(1)),
                Day(reader.GetString(2)),
                decimal.Parse(reader.GetString(3), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture),
                Day(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return rows;
    }

    const string RawClosesOf = @"
        SELECT session_date, close, raw_close FROM bar
        WHERE ticker = $ticker AND session_date <= $on
        ORDER BY session_date;
    ";

    public async Task<EarningsMoveRow?> EarningsMoveAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = EarningsMoveOf;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new EarningsMoveRow(
                Day(reader.GetString(0)),
                new EarningsMove(
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : Day(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6))
            : null;
    }

    public async Task<LongViewRow?> LongViewAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LongViewOf;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        using var closes = JsonDocument.Parse(reader.GetString(1));

        return new LongViewRow(
            Day(reader.GetString(0)),
            [.. closes.RootElement.EnumerateArray().Select(point => new MonthEnd(Day(point[0].GetString()!), Money.FromStorage(point[1].GetString()!)))],
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : Money.FromStorage(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task<IReadOnlyList<FiledRow>> FiledBetweenAsync(string ticker, DateOnly from, DateOnly to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FiledBetween;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var filed = new List<FiledRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            filed.Add(new FiledRow(reader.GetString(0), Day(reader.GetString(1)), reader.GetString(2), reader.GetString(3)));
        }

        return filed;
    }

    public async Task<IReadOnlyList<RawCloseRow>> RawClosesAsync(string ticker, DateOnly? on = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RawClosesOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", (on ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<RawCloseRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RawCloseRow(Day(reader.GetString(0)), Money.FromStorage(reader.GetString(1)), Money.FromStorage(reader.GetString(2))));
        }

        return rows;
    }
}
