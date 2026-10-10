using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// What the name page's quarter, margins, analysts, dividend and valuation regions read, as the store holds it on or before
// the page's night: the newest fetch's quarters, the revenue estimate a fetch kept before the newest quarter's report, the
// newest fetch's estimate trend, every fetch's rating counts, mean and target, every dividend kept, the Treasury's
// 10-year over the year, and the night's readings of the stock's industry's S&P 500 members.
public sealed record ReportInputs(
    IReadOnlyList<QuarterRow> Quarters,
    DateOnly? FetchedOn,
    RevenueEstimate? RevenueEstimate,
    IReadOnlyList<EstimatePeriod> Trend,
    DateOnly? TrendFetchedOn,
    IReadOnlyList<RatingFetch> Ratings,
    IReadOnlyList<KeptDividend> Dividends,
    IReadOnlyList<(DateOnly Session, double TenYear)> TenYears,
    string? Industry,
    IReadOnlyList<PeerValue> Peers);

// The reads of the regions phase 18 added between the card and the earnings reactions. The page draws what they return
// through the core's functions and computes none of it.
// see: A screen reads and renders, and each figure it works out has one function in the core
public sealed partial class ReadApi
{
    const string ReportQuartersOf = @"
        SELECT period_end, report_date, revenue, gross_profit, operating_income, net_income, operating_cash_flow,
               free_cash_flow, dividends_paid, eps_actual, eps_estimate, eps_trailing, close_after, lines_read, fetched_at
        FROM reported_quarter
        WHERE ticker = $ticker
          AND fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = $ticker AND session_date <= $on)
        ORDER BY period_end;
    ";

    // The current quarter's revenue estimates kept by fetches made before a report, newest first, for a quarter ending
    // within a week of the one reported.
    const string RevenueEstimatesOf = @"
        SELECT revenue_average, fetched_at FROM estimate_trend
        WHERE ticker = $ticker AND period = '0q' AND period_end >= $from AND period_end <= $to AND substr(fetched_at, 1, 10) < $reported
        ORDER BY fetched_at DESC;
    ";

    const string TrendOf = @"
        SELECT period, period_end, eps_average, eps_low, eps_high, eps_year_ago, eps_analysts,
               revenue_average, revenue_low, revenue_high, revenue_year_ago, revenue_analysts,
               eps_now, eps_7_days_ago, eps_30_days_ago, eps_60_days_ago, eps_90_days_ago,
               up_last_7_days, up_last_30_days, down_last_7_days, down_last_30_days, fetched_at
        FROM estimate_trend
        WHERE ticker = $ticker
          AND fetched_at = (SELECT MAX(fetched_at) FROM estimate_trend WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on);
    ";

    const string RatingFetchesOf = @"
        SELECT fetched_at, strong_buy, buy, hold, sell, strong_sell, rating_mean, target_price
        FROM company WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on
        ORDER BY fetched_at;
    ";

    const string KeptDividendsOf = "SELECT ex_date, amount FROM dividend_event WHERE ticker = $ticker ORDER BY ex_date;";

    const string TenYearsOf = "SELECT session_date, ten_year FROM treasury_yield WHERE session_date >= $from AND session_date <= $on ORDER BY session_date;";

    // The stock's own industry as its member reading on the night filed it, under the index that held it.
    const string IndustryOf = @"
        SELECT industry FROM member_reading WHERE ticker = $ticker AND session_date = $night
        ORDER BY CASE index_code WHEN 'GSPC' THEN 0 WHEN 'MID' THEN 1 ELSE 2 END
        LIMIT 1;
    ";

    // The industry's S&P 500 members on the night and the stock's own row whatever its index: each close, the night's
    // reading of its quarters and the forward rate its newest fetch on or before the night kept.
    const string PeerValuesOf = @"
        SELECT m.ticker, m.close, f.readings,
               (SELECT d.forward_rate FROM dividend_reading d WHERE d.ticker = m.ticker AND substr(d.fetched_at, 1, 10) <= $night ORDER BY d.fetched_at DESC LIMIT 1)
        FROM member_reading m
        LEFT JOIN fundamental_reading f ON f.ticker = m.ticker AND f.session_date = m.session_date
        WHERE m.session_date = $night
          AND ((m.index_code = 'GSPC' AND m.industry = $industry) OR m.ticker = $ticker);
    ";

    public async Task<ReportInputs> ReportInputsAsync(string ticker, DateOnly? on, DateOnly night)
    {
        var day = Day(on ?? DateOnly.MaxValue);

        await using var connection = Open();

        var quarters = new List<QuarterRow>();
        DateOnly? fetchedOn = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ReportQuartersOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", day);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                quarters.Add(new QuarterRow(
                    Parse(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : Parse(reader.GetString(1)),
                    MoneyAt(reader, 2),
                    MoneyAt(reader, 3),
                    MoneyAt(reader, 4),
                    MoneyAt(reader, 5),
                    MoneyAt(reader, 6),
                    MoneyAt(reader, 7),
                    MoneyAt(reader, 8),
                    MoneyAt(reader, 9),
                    MoneyAt(reader, 10),
                    MoneyAt(reader, 11),
                    MoneyAt(reader, 12),
                    reader.GetInt64(13) == 1));
                fetchedOn = Parse(reader.GetString(14)[..10]);
            }
        }

        // The newest quarter's revenue estimate, kept by a fetch made before its report.
        RevenueEstimate? estimate = null;

        if (quarters.Where(quarter => quarter.Revenue is not null || quarter.EpsActual is not null).MaxBy(quarter => quarter.PeriodEnd) is { ReportDate: { } reported } newest)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = RevenueEstimatesOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$from", Day(newest.PeriodEnd.AddDays(-QuarterFetch.NearDays)));
            command.Parameters.AddWithValue("$to", Day(newest.PeriodEnd.AddDays(QuarterFetch.NearDays)));
            command.Parameters.AddWithValue("$reported", Day(reported));

            await using var reader = await command.ExecuteReaderAsync();

            while (estimate is null && await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    estimate = new RevenueEstimate(Money.FromStorage(reader.GetString(0)), Parse(reader.GetString(1)[..10]));
                }
            }
        }

        var trend = new List<EstimatePeriod>();
        DateOnly? trendOn = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TrendOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", day);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int? Count(int column) => reader.IsDBNull(column) ? null : reader.GetInt32(column);

                trend.Add(new EstimatePeriod(
                    reader.GetString(0),
                    Parse(reader.GetString(1)),
                    MoneyAt(reader, 2),
                    MoneyAt(reader, 3),
                    MoneyAt(reader, 4),
                    MoneyAt(reader, 5),
                    Count(6),
                    MoneyAt(reader, 7),
                    MoneyAt(reader, 8),
                    MoneyAt(reader, 9),
                    MoneyAt(reader, 10),
                    Count(11),
                    MoneyAt(reader, 12),
                    MoneyAt(reader, 13),
                    MoneyAt(reader, 14),
                    MoneyAt(reader, 15),
                    MoneyAt(reader, 16),
                    Count(17),
                    Count(18),
                    Count(19),
                    Count(20)));
                trendOn = Parse(reader.GetString(21)[..10]);
            }
        }

        var ratings = new List<RatingFetch>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RatingFetchesOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", day);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int? Count(int column) => reader.IsDBNull(column) ? null : reader.GetInt32(column);

                ratings.Add(new RatingFetch(
                    Parse(reader.GetString(0)[..10]),
                    Count(1),
                    Count(2),
                    Count(3),
                    Count(4),
                    Count(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    MoneyAt(reader, 7)));
            }
        }

        var dividends = new List<KeptDividend>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = KeptDividendsOf;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                dividends.Add(new KeptDividend(Parse(reader.GetString(0)), Money.FromStorage(reader.GetString(1))));
            }
        }

        var tenYears = new List<(DateOnly, double)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TenYearsOf;
            command.Parameters.AddWithValue("$from", Day(night.AddYears(-1)));
            command.Parameters.AddWithValue("$on", Day(night));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tenYears.Add((Parse(reader.GetString(0)), reader.GetDouble(1)));
            }
        }

        string? industry = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = IndustryOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$night", Day(night));

            industry = await command.ExecuteScalarAsync() as string;
        }

        var peers = new List<PeerValue>();

        if (industry is not null)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = PeerValuesOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$industry", industry);
            command.Parameters.AddWithValue("$night", Day(night));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var readings = reader.IsDBNull(2) ? null : Readings.FromJson(reader.GetString(2));

                peers.Add(new PeerValue(
                    reader.GetString(0),
                    readings?.Valuation.Multiple is { } multiple ? Statistic.FromRatio(multiple) : null,
                    MoneyAt(reader, 1) is { } close ? EquityBrief.Core.Tiles.NameTiles.Yield(MoneyAt(reader, 3), close)?.Percent : null,
                    readings?.Trajectory.Quarters is [var newestQuarter, ..] ? Statistic.FromRatio(newestQuarter.SalesGrowth) * 100 : null,
                    false));
            }
        }

        return new ReportInputs(quarters, fetchedOn, estimate, trend, trendOn, ratings, dividends, tenYears, industry, peers);
    }

    static decimal? MoneyAt(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : Money.FromStorage(reader.GetString(column));

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Parse(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
