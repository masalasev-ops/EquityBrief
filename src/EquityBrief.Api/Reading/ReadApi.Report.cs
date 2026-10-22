using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Insiders;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// What the name page's quarter, margins, analysts, dividend and valuation regions read, as the store holds it on or before
// the page's night: the newest fetch's quarters, the revenue estimates fetches kept before the newest quarter's release with
// the instant it was released and the instant the stock's estimates are kept from, the newest fetch's estimate trend, every
// fetch's rating counts, mean and target, every dividend kept, the Treasury's 10-year over the year, and the night's
// readings of the stock's industry's S&P 500 members; from 19.13 its filer's insiders' transactions filed in the year to
// the night and the spans the store's insider history holds.
public sealed record ReportInputs(
    IReadOnlyList<QuarterRow> Quarters,
    DateOnly? FetchedOn,
    KeptEstimates? Estimates,
    IReadOnlyList<EstimatePeriod> Trend,
    DateOnly? TrendFetchedOn,
    IReadOnlyList<RatingFetch> Ratings,
    IReadOnlyList<KeptDividend> Dividends,
    IReadOnlyList<(DateOnly Session, double TenYear)> TenYears,
    string? Industry,
    IReadOnlyList<PeerValue> Peers,
    IReadOnlyList<InsiderRow>? Insiders = null,
    InsiderCoverage? InsiderSpans = null,
    NewestReading? Release = null,
    string? ReleaseModel = null,
    string? Sector = null);

// The reads of the regions phase 18 added between the card and the earnings reactions. The page draws what they return
// through the core's functions and computes none of it.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
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

    // The revenue estimates kept by fetches made before a quarter's release, newest first, each for a period ending within a
    // week of the quarter reported: a quarter's, filed as the current or the next quarter as the answer had rolled its words
    // or not when it was fetched, and the fiscal year's, which the answer files in a quarter's place where the quarter ends
    // the year.
    // see: The latest quarter is read line by line against the estimates kept before its release and the quarter a year earlier, a financial company's revenue against none and a fiscal fourth quarter's against its year's estimate less three quarters
    const string RevenueEstimatesOf = @"
        SELECT period, period_end, revenue_average, fetched_at FROM estimate_trend
        WHERE ticker = $ticker AND period IN ('0q', '+1q', '0y', '+1y') AND period_end >= $from AND period_end <= $to AND fetched_at < $released
        ORDER BY fetched_at DESC;
    ";

    // The calendar's timing for the stock's report on the day its quarter was reported.
    const string ReleaseTimingOf = "SELECT timing FROM calendar WHERE ticker = $ticker AND event_date = $reported AND kind = 'earnings';";

    // The instant the stock's first estimate fetch on or before the page's night was made, from which its estimates are
    // kept as the store stood that night.
    const string EstimatesKeptFromOf = "SELECT MIN(fetched_at) FROM estimate_trend WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on;";

    const string TrendOf = @"
        SELECT period, period_end, eps_average, eps_low, eps_high, eps_year_ago, eps_analysts,
               revenue_average, revenue_low, revenue_high, revenue_year_ago, revenue_analysts,
               eps_now, eps_seven_days_ago, eps_thirty_days_ago, eps_sixty_days_ago, eps_ninety_days_ago,
               up_last_seven_days, up_last_thirty_days, down_last_seven_days, down_last_thirty_days, fetched_at
        FROM estimate_trend
        WHERE ticker = $ticker
          AND fetched_at = (SELECT MAX(fetched_at) FROM estimate_trend WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on)
        ORDER BY CASE period WHEN '0q' THEN 0 WHEN '+1q' THEN 1 WHEN '0y' THEN 2 ELSE 3 END;
    ";

    const string RatingFetchesOf = @"
        SELECT fetched_at, strong_buy, buy, hold, sell, strong_sell, rating_mean, target_price
        FROM company WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on
        ORDER BY fetched_at;
    ";

    // The GICS sector the stock's newest company fetch on or before the page's night files, which says whether its revenue
    // is read against the analysts' estimate.
    // see: The latest quarter is read line by line against the estimates kept before its release and the quarter a year earlier, a financial company's revenue against none and a fiscal fourth quarter's against its year's estimate less three quarters
    const string SectorOf = "SELECT sector FROM company WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on ORDER BY fetched_at DESC, rowid DESC LIMIT 1;";

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

        // The newest quarter's revenue estimates, kept by fetches made before its release as the calendar's timing on its
        // report date places it, and the instant the stock's estimates are kept from.
        KeptEstimates? estimates = null;

        if (quarters.Where(quarter => quarter.Revenue is not null || quarter.EpsActual is not null).MaxBy(quarter => quarter.PeriodEnd) is { } newest)
        {
            DateTimeOffset? keptFrom = null;
            DateTimeOffset? released = null;
            RevenueEstimate? own = null;
            YearEstimate? year = null;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = EstimatesKeptFromOf;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$on", day);

                if (await command.ExecuteScalarAsync() is string first)
                {
                    keptFrom = DateTimeOffset.Parse(first, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                }
            }

            if (newest.ReportDate is { } reported)
            {
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = ReleaseTimingOf;
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$reported", Day(reported));

                    released = QuarterLines.ReleasedAt(reported, await command.ExecuteScalarAsync() as string, clock.SessionZone);
                }

                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = RevenueEstimatesOf;
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$from", Day(newest.PeriodEnd.AddDays(-QuarterFetch.NearDays)));
                    command.Parameters.AddWithValue("$to", Day(newest.PeriodEnd.AddDays(QuarterFetch.NearDays)));
                    command.Parameters.AddWithValue("$released", released.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));

                    await using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        if (reader.IsDBNull(2))
                        {
                            continue;
                        }

                        var average = Money.FromStorage(reader.GetString(2));
                        var keptOn = Parse(reader.GetString(3)[..10]);

                        if (reader.GetString(0) is EstimatePeriod.CurrentQuarter or EstimatePeriod.NextQuarter)
                        {
                            own ??= new RevenueEstimate(average, keptOn);
                        }
                        else
                        {
                            year ??= new YearEstimate(average, Parse(reader.GetString(1)), keptOn);
                        }
                    }
                }
            }

            estimates = new KeptEstimates(own, year, keptFrom, released);
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

        string? sector;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SectorOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", day);

            sector = await command.ExecuteScalarAsync() as string;
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

        var (insiders, spans) = await InsidersAsync(connection, ticker, night);
        var (release, model) = await ReleaseReadingAsync(connection, ticker, night);

        return new ReportInputs(quarters, fetchedOn, estimates, trend, trendOn, ratings, dividends, tenYears, industry, peers, insiders, spans, release, model, sector);
    }

    // From 20.3 the newest results release of the stock's filer the night may read, as the local model read it under the
    // newest reading version, with the model that version names; none where the store holds no reading of one.
    // see: A release reading is readable from the first night whose late slot starts after the release was accepted
    const string PinModel = "SELECT model FROM reading_pin WHERE id = $pin;";

    async Task<(NewestReading? Release, string? Model)> ReleaseReadingAsync(SqliteConnection connection, string ticker, DateOnly night)
    {
        string? cik;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FilerOf;
            command.Parameters.AddWithValue("$ticker", ticker);

            cik = await command.ExecuteScalarAsync() as string;
        }

        if (cik is null || await ReleaseReadings.NewestAsync(connection, EquityBrief.Core.Providers.SecEdgarArchive.Padded(cik), night) is not { } newest)
        {
            return (null, null);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PinModel;
            command.Parameters.AddWithValue("$pin", newest.Pin);

            return (newest, await command.ExecuteScalarAsync() as string);
        }
    }

    // The stock's filer's insiders' transactions filed in the year to the night, every code, and the spans the store's
    // insider history holds; for a stock no fetch filed a filer for no transaction and no span, so its view is none and
    // says the store holds no insider history for it, where a filer filing nothing reads nought.
    // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
    const string FilerOf = "SELECT cik FROM company WHERE ticker = $ticker AND cik IS NOT NULL ORDER BY fetched_at DESC LIMIT 1;";

    const string InsiderRowsOf = @"
        SELECT accession, filed, form, amends, original_filed, owner_ciks, owners, director, officer, title, ten_percent,
               sequence, traded, code, shares, price, acquired, planned, direct
        FROM insider_trade
        WHERE cik = $cik AND filed >= $from AND filed <= $night
        ORDER BY filed, accession, sequence;
    ";

    const string InsiderSpansOf = "SELECT first_day, last_day FROM insider_read;";

    async Task<(IReadOnlyList<InsiderRow> Rows, InsiderCoverage Spans)> InsidersAsync(SqliteConnection connection, string ticker, DateOnly night)
    {
        var spans = new List<(DateOnly First, DateOnly Last)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = InsiderSpansOf;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                spans.Add((Parse(reader.GetString(0)), Parse(reader.GetString(1))));
            }
        }

        string? cik;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FilerOf;
            command.Parameters.AddWithValue("$ticker", ticker);

            cik = await command.ExecuteScalarAsync() as string;
        }

        var rows = new List<InsiderRow>();

        if (cik is null)
        {
            return (rows, InsiderCoverage.None);
        }

        if (night.DayNumber > InsiderView.YearDays)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = InsiderRowsOf;
            command.Parameters.AddWithValue("$cik", EquityBrief.Core.Providers.SecEdgarArchive.Padded(cik));
            command.Parameters.AddWithValue("$from", Day(night.AddDays(-InsiderView.YearDays)));
            command.Parameters.AddWithValue("$night", Day(night));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows.Add(new InsiderRow(
                    reader.GetString(0),
                    Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : Parse(reader.GetString(4)),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt64(7) == 1,
                    reader.GetInt64(8) == 1,
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt64(10) == 1,
                    reader.GetInt32(11),
                    Parse(reader.GetString(12)),
                    reader.GetString(13),
                    Money.FromStorage(reader.GetString(14)),
                    MoneyAt(reader, 15),
                    reader.GetInt64(16) == 1,
                    reader.IsDBNull(17) ? null : reader.GetInt64(17) == 1,
                    reader.GetInt64(18) == 1));
            }
        }

        return (rows, new InsiderCoverage(spans));
    }

    static decimal? MoneyAt(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : Money.FromStorage(reader.GetString(column));

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Parse(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
