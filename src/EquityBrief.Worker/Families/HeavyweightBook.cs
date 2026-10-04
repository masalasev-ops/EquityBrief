using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

// What one night's book did: the night, whether it rebalanced, how many members it valued, the holdings it entered
// and ended with why, and why it read nothing where it did not.
public sealed record HeavyweightBookOutcome(
    DateOnly? Night,
    bool Rebalanced,
    int Valued,
    IReadOnlyList<string> Entered,
    IReadOnlyList<string> Ended,
    int Held,
    string? ReadNothing = null);

// The sector heavyweights' book. Every night it carries each open holding's growth, and its size cut's, from the
// session it was last carried to tonight by tonight's closes over that session's, as the store holds both tonight;
// ends a holding whose stock is no longer a member at its last session as one, and one closing under its 200-session
// average tonight. On a rebalance, the first night of a month the book reads, it reads every sector's
// largest companies, values each member from its newest fetch, stores each sector's reading, ends the holdings the
// rule would not buy and buys the leaders it does not hold, each with the sector's largest as its size cut.
//
// It runs in the swing filter's step after the family recorder, reads what the night and the quarters step stored,
// makes no request and calls no model, and no market check closes it. A night run again replaces what it wrote for
// that night, and a night before one it has read is read for nothing.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
// see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it
// see: A heavyweight leaving the index is sold at its last session's close as a member
// see: A heavyweight's result is the product of its daily close ratios since its buy, carried each night
// see: The market check closes every swing family's list together, and the sector heavyweights read none
// see: The nightly run is arithmetic only
public sealed class HeavyweightBook : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "heavyweights";

    // Why a holding ended, as the book stores it and the pages draw it.
    public const string NoLongerTheLeader = "no longer the leader";
    public const string UnderTheAverage = "a close under its 200-day average";
    public const string LeftTheIndex = "left the index";

    const string Sessions = "SELECT DISTINCT session_date FROM bar ORDER BY session_date;";

    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session);
    ";

    const string Bars = "SELECT ticker, session_date, close, raw_close, volume FROM bar ORDER BY ticker, session_date;";

    const string Averages = "SELECT ticker, name, value FROM indicator WHERE session_date = $night AND name IN ($fifty, $twoHundred) AND value IS NOT NULL;";

    // Each member's company as its newest storing fetch answered for it.
    const string Companies = @"
        SELECT c.ticker, c.cik, c.sector
        FROM company c
        WHERE c.fetched_at = (SELECT MAX(fetched_at) FROM company WHERE ticker = c.ticker);
    ";

    // The counts of each member's newest fetch storing any, with the day each sheet was filed and the fetch's basis.
    const string Counts = @"
        SELECT r.ticker, r.period_end, r.filing_date, r.shares, r.basis_session, r.basis_close
        FROM reported_quarter r
        WHERE r.shares IS NOT NULL AND r.filing_date IS NOT NULL AND r.basis_session IS NOT NULL AND r.basis_close IS NOT NULL
          AND r.fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = r.ticker AND shares IS NOT NULL);
    ";

    const string NewestRead = "SELECT MAX(at) FROM (SELECT MAX(session_date) AS at FROM heavyweight_night UNION ALL SELECT MAX(through) FROM heavyweight_holding);";

    const string LastRebalance = "SELECT MAX(session_date) FROM heavyweight_night WHERE session_date < $night;";

    const string Open = "SELECT ticker, entered_on, sector, company, entry_close, growth, cut, through FROM heavyweight_holding WHERE ended_on IS NULL ORDER BY ticker;";

    // A night run again: what it entered goes, what it ended opens again, and its reading is written again.
    const string ClearTheNight = @"
        DELETE FROM heavyweight_night WHERE session_date = $night;
        DELETE FROM heavyweight_holding WHERE entered_on = $night;
        UPDATE heavyweight_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL WHERE ended_on = $night;
    ";

    const string InsertRead = @"
        INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader)
        VALUES ($night, $sector, $place, $ticker, $company, $company_value, $look_back, $sector_return, $lead, $trend, $leader);
    ";

    const string Carry = "UPDATE heavyweight_holding SET growth = $growth, cut = $cut, through = $through WHERE ticker = $ticker AND entered_on = $entered_on;";

    const string End = @"
        UPDATE heavyweight_holding
        SET ended_on = $ended_on, exit_close = $exit_close, reason = $reason, result = $result, cut_return = $cut_return
        WHERE ticker = $ticker AND entered_on = $entered_on;
    ";

    const string Enter = @"
        INSERT INTO heavyweight_holding (ticker, entered_on, sector, company, entry_close, growth, cut, through)
        VALUES ($ticker, $night, $sector, $company, $entry_close, 1.0, $cut, $night);
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

    public HeavyweightBook(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // One session's bar as the book reads it.
    sealed record Bar(DateOnly Session, decimal Close, decimal RawClose, long Volume);

    // A size cut member's growth since the buy and the session it was carried to.
    public sealed record CutMember(string Ticker, double Growth, DateOnly On);

    sealed record Holding(string Ticker, DateOnly EnteredOn, string Sector, string Company, decimal EntryClose, double Growth, IReadOnlyList<CutMember> Cut, DateOnly Through);

    public async Task<HeavyweightBookOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var sessions = await ColumnAsync(connection, Sessions, [], cancellation);

        if (sessions.Count == 0)
        {
            return await RecordAsync(connection, runId, startedAt, new HeavyweightBookOutcome(null, false, 0, [], [], 0, "the store holds no session"), cancellation);
        }

        var night = Day(sessions[^1]);

        if (await ScalarAsync(connection, NewestRead, [], cancellation) is string newest && Day(newest) > night)
        {
            return await RecordAsync(connection, runId, startedAt, new HeavyweightBookOutcome(night, false, 0, [], [], 0, FormattableString.Invariant($"the book has read {newest}, a later session than {night:yyyy-MM-dd}, and reads no earlier one")), cancellation);
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheNight, [("$night", Stamp(night))], cancellation);

        var members = (await ColumnAsync(connection, MembersOn, [("$index", indexCode), ("$session", Stamp(night))], cancellation, transaction)).ToHashSet(StringComparer.Ordinal);
        var bars = await BarsAsync(connection, transaction, cancellation);
        var averages = await AveragesAsync(connection, transaction, night, cancellation);
        var holdings = await OpenAsync(connection, transaction, cancellation);
        var ended = new List<string>();
        var entered = new List<string>();

        decimal? CloseOn(string ticker, DateOnly session) =>
            bars.TryGetValue(ticker, out var series) && series.TryGetValue(session, out var bar) ? bar.Close : null;

        // Each open holding carried to tonight, then ended where its stock left the index or broke its average.
        var open = new List<Holding>();

        foreach (var holding in holdings)
        {
            if (!members.Contains(holding.Ticker))
            {
                await EndAsync(connection, transaction, holding, holding.Through, CloseOn(holding.Ticker, holding.Through) ?? holding.EntryClose, LeftTheIndex, cancellation);
                ended.Add($"{holding.Ticker}: {LeftTheIndex}");

                continue;
            }

            var carried = Carried(holding, night, CloseOn);

            await ExecuteAsync(connection, transaction, Carry,
            [
                ("$growth", carried.Growth),
                ("$cut", Cut(carried.Cut)),
                ("$through", Stamp(carried.Through)),
                ("$ticker", carried.Ticker),
                ("$entered_on", Stamp(carried.EnteredOn)),
            ], cancellation);

            if (CloseOn(carried.Ticker, night) is { } close
                && HeavyweightRule.Broken(Statistic.FromPrice(close), averages.TryGetValue((carried.Ticker, IndicatorSeries.Sma200), out var average) ? average : null))
            {
                await EndAsync(connection, transaction, carried, night, close, UnderTheAverage, cancellation);
                ended.Add($"{carried.Ticker}: {UnderTheAverage}");

                continue;
            }

            open.Add(carried);
        }

        var rebalanced = HeavyweightRule.Rebalances(night, await ScalarAsync(connection, LastRebalance, [("$night", Stamp(night))], cancellation, transaction) is string last ? Day(last) : null);
        var valued = 0;
        var written = 0;

        if (rebalanced)
        {
            var read = await ReadMembersAsync(connection, transaction, night, members, bars, averages, cancellation);
            var sectors = HeavyweightRule.Read(read, HeavyweightRule.Provisional);
            var buys = HeavyweightRule.Buys(sectors);

            valued = read.Count(member => member.Value is not null);

            foreach (var sector in sectors)
            {
                foreach (var ranked in sector.Largest)
                {
                    written += await ExecuteAsync(connection, transaction, InsertRead,
                    [
                        ("$night", Stamp(night)),
                        ("$sector", sector.Sector),
                        ("$place", ranked.Place),
                        ("$ticker", ranked.Ticker),
                        ("$company", ranked.Company),
                        ("$company_value", Money.ToStorage(ranked.Value)),
                        ("$look_back", (object?)ranked.Return ?? DBNull.Value),
                        ("$sector_return", (object?)sector.Return ?? DBNull.Value),
                        ("$lead", (object?)ranked.Lead ?? DBNull.Value),
                        ("$trend", ranked.Trend ? 1 : 0),
                        ("$leader", ranked.Leader ? 1 : 0),
                    ], cancellation);
                }
            }

            foreach (var holding in open.Where(holding => !buys.Contains(holding.Ticker)).ToArray())
            {
                await EndAsync(connection, transaction, holding, night, CloseOn(holding.Ticker, night) ?? holding.EntryClose, NoLongerTheLeader, cancellation);
                ended.Add($"{holding.Ticker}: {NoLongerTheLeader}");
                open.Remove(holding);
            }

            foreach (var sector in sectors)
            {
                foreach (var leader in sector.Leaders.Where(leader => open.All(holding => holding.Ticker != leader)))
                {
                    if (CloseOn(leader, night) is not { } close)
                    {
                        continue;
                    }

                    var ranked = sector.Largest.Single(one => one.Ticker == leader);

                    written += await ExecuteAsync(connection, transaction, Enter,
                    [
                        ("$ticker", leader),
                        ("$night", Stamp(night)),
                        ("$sector", sector.Sector),
                        ("$company", ranked.Company),
                        ("$entry_close", Money.ToStorage(close)),
                        ("$cut", Cut([.. sector.Largest.Select(one => new CutMember(one.Ticker, 1.0, night))])),
                    ], cancellation);
                    entered.Add(leader);
                }
            }
        }

        var outcome = new HeavyweightBookOutcome(night, rebalanced, valued, entered, ended, open.Count + entered.Count);

        await AppendAsync(connection, transaction, runId, startedAt, outcome, written + ended.Count, cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // A holding carried to a night: its own growth by its close tonight over its close on the session it was carried
    // to, and each of its size cut's the same way, a stock with no close on either session kept where it stood, which
    // is a member that left the index sold at its last close.
    static Holding Carried(Holding holding, DateOnly night, Func<string, DateOnly, decimal?> closeOn)
    {
        (double Growth, DateOnly On) Grown(string ticker, double growth, DateOnly on) =>
            on < night && closeOn(ticker, on) is { } then && then > 0m && closeOn(ticker, night) is { } now
                ? (growth * Statistic.FromRatio(now / then), night)
                : (growth, on);

        var own = Grown(holding.Ticker, holding.Growth, holding.Through);

        return holding with
        {
            Growth = own.Growth,
            Through = own.On,
            Cut = [.. holding.Cut.Select(member => Grown(member.Ticker, member.Growth, member.On) is var grown ? member with { Growth = grown.Growth, On = grown.On } : member)],
        };
    }

    // A holding ended on a session at a close: its result its growth less one, and its size cut's return the mean of
    // its members' growths less one.
    static async Task EndAsync(SqliteConnection connection, SqliteTransaction transaction, Holding holding, DateOnly on, decimal close, string reason, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, End,
        [
            ("$ended_on", Stamp(on)),
            ("$exit_close", Money.ToStorage(close)),
            ("$reason", reason),
            ("$result", holding.Growth - 1.0),
            ("$cut_return", holding.Cut.Count > 0 ? holding.Cut.Average(member => member.Growth) - 1.0 : (object)DBNull.Value),
            ("$ticker", holding.Ticker),
            ("$entered_on", Stamp(holding.EnteredOn)),
        ], cancellation);

    // Every member on a rebalance session as the rule reads it: its company and sector as its newest fetch answered,
    // its value from that fetch's counts, the dollars it traded over the fifty sessions to the session, its return
    // over the look-back and its close with its averages.
    // see: The night values a member from its newest fetch, tonight's close brought to the count's basis by the fetch's own close
    // see: A company's sector on a session is the GICS sector the provider files, with the fourteen moves of 2023-03-17 read by date
    // see: Companies are ranked by CIK with one listing held, the class that traded the more dollars over fifty sessions
    async Task<IReadOnlyList<HeavyweightMember>> ReadMembersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly night,
        IReadOnlySet<string> members,
        IReadOnlyDictionary<string, SortedDictionary<DateOnly, Bar>> bars,
        IReadOnlyDictionary<(string Ticker, string Name), double> averages,
        CancellationToken cancellation)
    {
        var companies = new Dictionary<string, (string? Cik, string? Sector)>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, Companies, [], cancellation))
        {
            companies[row.GetString(0)] = (row.IsDBNull(1) ? null : row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2));
        }

        var counts = new Dictionary<string, (List<FiledCount> Counts, DateOnly Basis, decimal BasisClose)>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, Counts, [], cancellation))
        {
            var ticker = row.GetString(0);
            var basis = Day(row.GetString(4));

            if (!counts.TryGetValue(ticker, out var held))
            {
                counts[ticker] = held = ([], basis, Money.FromStorage(row.GetString(5)));
            }

            held.Counts.Add(new FiledCount(Day(row.GetString(1)), Day(row.GetString(2)), Money.FromStorage(row.GetString(3)), basis));
        }

        var read = new List<HeavyweightMember>();

        foreach (var ticker in members.Order(StringComparer.Ordinal))
        {
            if (!bars.TryGetValue(ticker, out var series) || !series.TryGetValue(night, out var tonight))
            {
                continue;
            }

            var company = companies.TryGetValue(ticker, out var filed) ? filed : (null, null);
            decimal? value = counts.TryGetValue(ticker, out var held)
                ? CompanyValue.Tonight(night, tonight.Close, held.Counts, held.BasisClose, series.TryGetValue(held.Basis, out var basisBar) ? basisBar.Close : null)
                : null;

            var upTo = series.Values.Where(bar => bar.Session <= night).ToArray();
            double? lookBack = upTo.Length > HeavyweightRule.LookBack && upTo[^(HeavyweightRule.LookBack + 1)].Close is var then && then > 0m
                ? Statistic.FromRatio(tonight.Close / then) - 1.0
                : null;

            read.Add(new HeavyweightMember(
                ticker,
                CompanyRank.CompanyOf(ticker, company.Cik),
                GicsSectors.On(ticker, company.Sector, night),
                value,
                CompanyRank.DollarVolume(upTo.Select(bar => (bar.RawClose, bar.Volume))),
                lookBack,
                Statistic.FromPrice(tonight.Close),
                averages.TryGetValue((ticker, IndicatorSeries.Sma50), out var fifty) ? fifty : null,
                averages.TryGetValue((ticker, IndicatorSeries.Sma200), out var twoHundred) ? twoHundred : null));
        }

        return read;
    }

    static async Task<IReadOnlyDictionary<string, SortedDictionary<DateOnly, Bar>>> BarsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellation)
    {
        var bars = new Dictionary<string, SortedDictionary<DateOnly, Bar>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, Bars, [], cancellation))
        {
            if (!bars.TryGetValue(row.GetString(0), out var series))
            {
                bars[row.GetString(0)] = series = [];
            }

            var session = Day(row.GetString(1));

            series[session] = new Bar(session, Money.FromStorage(row.GetString(2)), row.IsDBNull(3) ? Money.FromStorage(row.GetString(2)) : Money.FromStorage(row.GetString(3)), row.GetInt64(4));
        }

        return bars;
    }

    static async Task<IReadOnlyDictionary<(string Ticker, string Name), double>> AveragesAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        var averages = new Dictionary<(string, string), double>();

        await foreach (var row in RowsAsync(connection, transaction, Averages, [("$night", Stamp(night)), ("$fifty", IndicatorSeries.Sma50), ("$twoHundred", IndicatorSeries.Sma200)], cancellation))
        {
            averages[(row.GetString(0), row.GetString(1))] = row.GetDouble(2);
        }

        return averages;
    }

    static async Task<IReadOnlyList<Holding>> OpenAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellation)
    {
        var holdings = new List<Holding>();

        await foreach (var row in RowsAsync(connection, transaction, Open, [], cancellation))
        {
            holdings.Add(new Holding(
                row.GetString(0),
                Day(row.GetString(1)),
                row.GetString(2),
                row.GetString(3),
                Money.FromStorage(row.GetString(4)),
                row.GetDouble(5),
                CutOf(row.GetString(6)),
                Day(row.GetString(7))));
        }

        return holdings;
    }

    // A size cut as the book stores it: each member's ticker, growth and the session it was carried to.
    public static string Cut(IReadOnlyList<CutMember> cut) =>
        JsonSerializer.Serialize(cut.Select(member => new { ticker = member.Ticker, growth = member.Growth, on = Stamp(member.On) }));

    public static IReadOnlyList<CutMember> CutOf(string stored)
    {
        using var document = JsonDocument.Parse(stored);

        return
        [
            .. document.RootElement.EnumerateArray().Select(member => new CutMember(
                member.GetProperty("ticker").GetString()!,
                member.GetProperty("growth").GetDouble(),
                Day(member.GetProperty("on").GetString()!))),
        ];
    }

    async Task<HeavyweightBookOutcome> RecordAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, HeavyweightBookOutcome outcome, CancellationToken cancellation)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await AppendAsync(connection, transaction, runId, startedAt, outcome, 0, cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction transaction, string runId, DateTimeOffset startedAt, HeavyweightBookOutcome outcome, int rows, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Stage),
            ("$started_at", startedAt.ToString("O", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ("$rows_written", rows),
            ("$detail", Detail(outcome)),
        ], cancellation);

    // The book's line for the night, as its run log row holds it.
    public static string Detail(HeavyweightBookOutcome outcome) =>
        outcome.ReadNothing is { } why
            ? why
            : (outcome.Rebalanced
                ? FormattableString.Invariant($"a rebalance, {outcome.Valued} member(s) valued, {outcome.Entered.Count} bought")
                : "no rebalance")
              + FormattableString.Invariant($", {outcome.Ended.Count} ended, {outcome.Held} held")
              + (outcome.Entered.Count > 0 ? "; bought: " + string.Join(", ", outcome.Entered) : string.Empty)
              + (outcome.Ended.Count > 0 ? "; ended: " + string.Join(", ", outcome.Ended) : string.Empty);

    static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var value1 = await command.ExecuteScalarAsync(cancellation);

        return value1 is DBNull ? null : value1;
    }

    static async Task<IReadOnlyList<string>> ColumnAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        var values = new List<string>();

        await foreach (var row in RowsAsync(connection, transaction, sql, parameters, cancellation))
        {
            values.Add(row.GetString(0));
        }

        return values;
    }

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            yield return reader;
        }
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
