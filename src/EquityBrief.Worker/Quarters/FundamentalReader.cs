using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Quarters;

public sealed record FundamentalReadOutcome(int Members, int RowsWritten, IReadOnlyDictionary<string, int> States);

// The fundamental readings. Writes, for every member of the index on the night, the four readings of
// its reported quarters and the state they give it, from the quarters fetched on the nights before
// this one, so a member reporting on a Tuesday is asked on Wednesday's night and read on Thursday's.
//
// A row for every member, one holding no quarter among them, because absent is never a failure: a
// member with no fundamentals or too few quarters is judged and listed as any other, and its row says
// which it is. It makes no request and calls no model: everything it reads is already in the store.
// see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
// see: The nightly run is arithmetic only
//
// The arithmetic is in `QuarterReadings`. What is here is reading, writing and the run log.
public sealed class FundamentalReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "fundamental-readings";

    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // Each member's newest fetch made on a night before this one, and the quarters it stored.
    const string QuartersBefore = @"
        SELECT r.ticker, r.fetched_at, r.period_end, r.filing_date, r.report_date, r.revenue, r.operating_income,
               r.net_income, r.operating_cash_flow, r.eps_actual, r.eps_estimate, r.eps_trailing, r.sales_growth,
               r.sales_growth_before, r.operating_margin, r.margin_year_earlier, r.close_after, r.close_after_session,
               r.basis_session, r.basis_close
        FROM reported_quarter r
        WHERE r.fetched_at = (
            SELECT MAX(fetched_at) FROM reported_quarter
            WHERE ticker = r.ticker AND session_date < $session)
        ORDER BY r.ticker, r.period_end DESC;
    ";

    const string Closes = "SELECT ticker, session_date, close FROM bar;";

    const string ReportsBefore = @"
        SELECT ticker, event_date, detail
        FROM calendar
        WHERE kind = 'earnings' AND event_date < $session;
    ";

    // A night run again replaces its own set whole, so a name no longer a member keeps no row on it.
    const string ClearTheNight = "DELETE FROM fundamental_reading WHERE session_date = $session;";

    const string Insert = @"
        INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings)
        VALUES ($ticker, $session_date, $state, $read_from, $fetched_at, $awaited, $readings);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public FundamentalReader(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<FundamentalReadOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The night is the newest session any name holds, read off the store for the reason the swing
        // reader reads it there: a replay run on a weekend has a clock a session ahead of every bar.
        var night = await NewestAsync(connection, cancellation) ?? clock.SessionDateAt(clock.UtcNow);
        var members = await MembersAsync(connection, indexCode, clock.SessionDateAt(clock.UtcNow), cancellation);
        var fetched = await QuartersAsync(connection, night, cancellation);
        var closes = await ClosesAsync(connection, cancellation);
        var reports = await ReportsAsync(connection, night, cancellation);

        var states = FundamentalState.All.ToDictionary(state => state, _ => 0, StringComparer.Ordinal);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = ClearTheNight;
            clear.Parameters.AddWithValue("$session", Stamp(night));

            await clear.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var ticker in members)
        {
            var fetch = fetched.GetValueOrDefault(ticker);
            var quarters = fetch?.Quarters ?? [];
            var readings = QuarterReadings.Of(quarters, OnBasis(ticker, night, fetch, closes), Awaited(quarters, reports.GetValueOrDefault(ticker)));

            states[readings.State]++;

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session_date", Stamp(night));
            command.Parameters.AddWithValue("$state", readings.State);
            command.Parameters.AddWithValue("$read_from", readings.ReadFrom is { } from ? Stamp(from) : DBNull.Value);
            command.Parameters.AddWithValue("$fetched_at", fetch?.FetchedAt is { } at ? at : DBNull.Value);
            command.Parameters.AddWithValue("$awaited", readings.Awaited is { } awaited ? Stamp(awaited) : DBNull.Value);
            command.Parameters.AddWithValue("$readings", readings.ToJson());

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var log = connection.CreateCommand())
        {
            log.Transaction = (SqliteTransaction)transaction;
            log.CommandText = AppendRun;
            log.Parameters.AddWithValue("$run_id", runId);
            log.Parameters.AddWithValue("$stage", Stage);
            log.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            log.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            log.Parameters.AddWithValue("$rows_written", members.Count);
            log.Parameters.AddWithValue(
                "$detail",
                FormattableString.Invariant($"{members.Count} member(s) read for {Stamp(night)}: ")
                    + string.Join(", ", FundamentalState.All.Select(state => FormattableString.Invariant($"{states[state]} {state}"))));

            await log.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return new FundamentalReadOutcome(members.Count, members.Count, states);
    }

    // Tonight's close on the fetch's basis: the stored close brought over by the ratio of the fetch's
    // own close to the stored one on the fetch's newest session, so a split or a dividend since the
    // fetch moves neither side of the multiple. None where the store holds either close no longer.
    static decimal? OnBasis(
        string ticker,
        DateOnly night,
        Fetched? fetch,
        IReadOnlyDictionary<(string, DateOnly), decimal> closes) =>
        fetch is { BasisSession: { } session, BasisClose: { } basis }
            && closes.TryGetValue((ticker, night), out var tonight)
            && closes.TryGetValue((ticker, session), out var stored)
            && stored != 0m
                ? decimal.Round(tonight * basis / stored, 6, MidpointRounding.ToEven)
                : null;

    // The quarter the newest report before tonight covers, where the fetch read from does not hold it.
    static DateOnly? Awaited(IReadOnlyList<ReportedQuarter> quarters, (DateOnly On, DateOnly? Period)? report) =>
        report is { Period: { } period }
            && quarters.Count > 0
            && period > quarters.Max(quarter => quarter.PeriodEnd).AddDays(QuarterFetch.NearDays)
                ? period
                : null;

    // One member's newest fetch before tonight: its quarters, the instant it was made and its basis.
    sealed record Fetched(string FetchedAt, IReadOnlyList<ReportedQuarter> Quarters, DateOnly? BasisSession, decimal? BasisClose);

    static async Task<IReadOnlyDictionary<string, Fetched>> QuartersAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = QuartersBefore;
        command.Parameters.AddWithValue("$session", Stamp(night));

        var rows = new Dictionary<string, (string FetchedAt, List<ReportedQuarter> Quarters, DateOnly? Session, decimal? Close)>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!rows.TryGetValue(ticker, out var fetch))
            {
                rows[ticker] = fetch = (reader.GetString(1), [], Date(reader, 18), Figure(reader, 19));
            }

            fetch.Quarters.Add(new ReportedQuarter(
                Date(reader, 2)!.Value,
                Date(reader, 3),
                Date(reader, 4),
                Figure(reader, 5),
                Figure(reader, 6),
                Figure(reader, 7),
                Figure(reader, 8),
                Figure(reader, 9),
                Figure(reader, 10),
                Figure(reader, 11),
                Figure(reader, 12),
                Figure(reader, 13),
                Figure(reader, 14),
                Figure(reader, 15),
                Figure(reader, 16),
                Date(reader, 17)));
        }

        return rows.ToDictionary(
            pair => pair.Key,
            pair => new Fetched(pair.Value.FetchedAt, pair.Value.Quarters, pair.Value.Session, pair.Value.Close),
            StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<(string, DateOnly), decimal>> ClosesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Closes;

        var closes = new Dictionary<(string, DateOnly), decimal>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            closes[(reader.GetString(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture))] = Money.FromStorage(reader.GetString(2));
        }

        return closes;
    }

    static async Task<IReadOnlyDictionary<string, (DateOnly On, DateOnly? Period)>> ReportsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReportsBefore;
        command.Parameters.AddWithValue("$session", Stamp(night));

        var reports = new Dictionary<string, (DateOnly On, DateOnly? Period)>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var on = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (reports.TryGetValue(reader.GetString(0), out var newer) && newer.On >= on)
            {
                continue;
            }

            DateOnly? period = null;

            if (!reader.IsDBNull(2))
            {
                using var detail = JsonDocument.Parse(reader.GetString(2));

                if (detail.RootElement.TryGetProperty("periodEnd", out var end)
                    && end.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(end.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ended))
                {
                    period = ended;
                }
            }

            reports[reader.GetString(0)] = (on, period);
        }

        return reports;
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static async Task<IReadOnlyList<string>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", Stamp(session));

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static DateOnly? Date(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : DateOnly.ParseExact(reader.GetString(column), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static decimal? Figure(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : Money.FromStorage(reader.GetString(column));

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
