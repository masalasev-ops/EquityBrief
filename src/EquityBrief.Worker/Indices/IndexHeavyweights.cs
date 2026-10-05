using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Data;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Indices;

// What an index's sector heavyweights did on the night: whether the book rebalanced, and the holdings bought, ended
// and held at the close.
public sealed record IndexHeavyweightsOutcome(bool Rebalanced, int Entered, int Ended, int Held);

// The S&P 400's and 600's sector heavyweights on their provisional settings, design (a) read within each index with the
// sweep's own code: on the first night of a month the book reads, each sector's ten largest members of the index by
// value as it stood, its return its members' mean and a leader's beta at least one against the index's fund, the two
// leading their sector above nothing bought among the members clearing the floors and the profit gate, and a holding
// the rule would no longer buy sold at the close; every night each holding and its size cut carried by the night's
// closes, and a holding whose stock left the index sold at its last close as a member. A holding's result is its
// growth less one, beside its size cut's, and its cost the published table's round trip.
// see: The 400 and 600 each sweep two heavyweight designs and keep the stronger after costs
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
public static class IndexHeavyweights
{
    // Design (a) within an index at the S&P 500's frozen settings, a sector's return its members' mean.
    public static HeavyweightSetting Provisional { get; } = HeavyweightSweep.Frozen with { Sector = HeavyweightSectorReturn.Members };

    public const string NoLongerTheLeader = "no longer the leader";

    public const string LeftTheIndex = "left the index";

    const string LastRebalance = "SELECT MAX(session_date) FROM index_family_night WHERE index_code = $index AND rebalanced = 1 AND session_date < $night;";

    const string Companies = @"
        SELECT c.ticker, c.cik, c.sector FROM company c
        WHERE c.fetched_at = (SELECT MAX(fetched_at) FROM company WHERE ticker = c.ticker);
    ";

    const string Counts = @"
        SELECT r.ticker, r.period_end, r.filing_date, r.shares, r.basis_session FROM reported_quarter r
        WHERE r.shares IS NOT NULL AND r.filing_date IS NOT NULL AND r.basis_session IS NOT NULL;
    ";

    const string FundCloses = "SELECT session_date, close FROM market_bar WHERE series = $series AND session_date <= $night ORDER BY session_date;";

    const string OpenHoldings = @"
        SELECT ticker, entered_on, sector, entry_close, growth, cut, through FROM index_heavyweight_holding
        WHERE index_code = $index AND ended_on IS NULL AND entered_on < $night;
    ";

    const string UndoTheNight = @"
        DELETE FROM index_heavyweight_holding WHERE index_code = $index AND entered_on = $night;
        UPDATE index_heavyweight_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL, cost = NULL
        WHERE index_code = $index AND ended_on = $night;
    ";

    const string Carry = @"
        UPDATE index_heavyweight_holding SET growth = $growth, cut = $cut, through = $through
        WHERE index_code = $index AND ticker = $ticker AND entered_on = $entered_on;
    ";

    const string End = @"
        UPDATE index_heavyweight_holding
        SET ended_on = $ended_on, exit_close = $exit_close, reason = $reason, result = $result, cut_return = $cut_return, cost = $cost
        WHERE index_code = $index AND ticker = $ticker AND entered_on = $entered_on;
    ";

    const string Buy = @"
        INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through)
        VALUES ($index, $ticker, $night, $sector, $entry_close, 1.0, $cut, $night);
    ";

    // One stock of a size cut, its growth carried as a holding's is and the session it was carried to.
    sealed record CutMember(string Ticker, double Growth, string Through);

    public static async Task<IndexHeavyweightsOutcome> RunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        DateOnly night,
        IReadOnlyList<SweepName> names,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        CancellationToken cancellation)
    {
        await ExecuteAsync(connection, transaction, UndoTheNight, [("$index", index), ("$night", Stamp(night))], cancellation);

        var bars = names.ToDictionary(name => name.Ticker, name => name.Bars.ToDictionary(bar => bar.Session), StringComparer.Ordinal);
        var members = names.Where(name => name.MemberOn(night)).Select(name => name.Ticker).ToHashSet(StringComparer.Ordinal);
        var (entered, ended) = (0, 0);

        decimal? CloseOn(string ticker, DateOnly session) => bars.TryGetValue(ticker, out var held) && held.TryGetValue(session, out var bar) ? bar.Close : null;

        // Each open holding and its size cut carried by tonight's closes, and one whose stock left the index sold at the
        // session it was last carried to, its last close as a member.
        var open = new List<(string Ticker, DateOnly Entered, string Sector, decimal Entry, double Growth, List<CutMember> Cut, DateOnly Through)>();

        await foreach (var row in RowsAsync(connection, transaction, OpenHoldings, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            open.Add((row.GetString(0), Date(row.GetString(1)), row.GetString(2), Money.FromStorage(row.GetString(3)), row.GetDouble(4), JsonSerializer.Deserialize<List<CutMember>>(row.GetString(5)) ?? [], Date(row.GetString(6))));
        }

        var held = new Dictionary<string, (DateOnly Entered, string Sector, decimal Entry, double Growth, List<CutMember> Cut)>(StringComparer.Ordinal);

        foreach (var holding in open)
        {
            if (!members.Contains(holding.Ticker))
            {
                await EndAsync(connection, transaction, index, holding.Ticker, holding.Entered, holding.Through, CloseOn(holding.Ticker, holding.Through) ?? holding.Entry, LeftTheIndex, holding.Growth, holding.Cut, holding.Entry, cancellation);
                ended++;

                continue;
            }

            var growth = CloseOn(holding.Ticker, holding.Through) is { } then && then > 0m && CloseOn(holding.Ticker, night) is { } now
                ? holding.Growth * Statistic.FromRatio(now / then)
                : holding.Growth;
            var cut = holding.Cut
                .Select(one => CloseOn(one.Ticker, Date(one.Through)) is { } then && then > 0m && CloseOn(one.Ticker, night) is { } now
                    ? one with { Growth = one.Growth * Statistic.FromRatio(now / then), Through = Stamp(night) }
                    : one)
                .ToList();

            await ExecuteAsync(connection, transaction, Carry, [("$index", index), ("$ticker", holding.Ticker), ("$entered_on", Stamp(holding.Entered)), ("$growth", growth), ("$cut", JsonSerializer.Serialize(cut)), ("$through", Stamp(night))], cancellation);
            held[holding.Ticker] = (holding.Entered, holding.Sector, holding.Entry, growth, cut);
        }

        var last = await ScalarAsync(connection, transaction, LastRebalance, [("$index", index), ("$night", Stamp(night))], cancellation) is string stored ? Date(stored) : (DateOnly?)null;

        if (!HeavyweightRule.Rebalances(night, last))
        {
            return new IndexHeavyweightsOutcome(false, 0, ended, held.Count);
        }

        // The rebalance: the sweep's own reading of the night's session within the index, the members clearing the floors
        // and the gate.
        var rebalance = await ReadAsync(connection, transaction, index, night, names, income, cancellation);
        var leaders = rebalance.Leaders.ToDictionary(one => one.Ticker, StringComparer.Ordinal);

        foreach (var (ticker, holding) in held.ToArray())
        {
            if (!leaders.ContainsKey(ticker))
            {
                await EndAsync(connection, transaction, index, ticker, holding.Entered, night, CloseOn(ticker, night) ?? holding.Entry, NoLongerTheLeader, holding.Growth, holding.Cut, holding.Entry, cancellation);
                held.Remove(ticker);
                ended++;
            }
        }

        foreach (var leader in rebalance.Leaders.Where(leader => !held.ContainsKey(leader.Ticker)))
        {
            if (CloseOn(leader.Ticker, night) is not { } close)
            {
                continue;
            }

            var cut = leader.Cut.Select(ticker => new CutMember(ticker, 1.0, Stamp(night))).ToList();

            await ExecuteAsync(connection, transaction, Buy, [("$index", index), ("$ticker", leader.Ticker), ("$night", Stamp(night)), ("$sector", leader.Sector), ("$entry_close", Money.ToStorage(close)), ("$cut", JsonSerializer.Serialize(cut))], cancellation);
            held[leader.Ticker] = (night, leader.Sector, close, 1.0, cut);
            entered++;
        }

        return new IndexHeavyweightsOutcome(true, entered, ended, held.Count);
    }

    // The leaders a rebalance on the night buys within the index, each with its sector and the size cut it was chosen
    // from: the sweep's tape laid over the members' year with each company's value from the newest count filed before
    // the session, a beta against the index's fund, and the members failing the floors or the profit gate read by none.
    static async Task<(IReadOnlyList<(string Ticker, string Sector, IReadOnlyList<string> Cut)> Leaders, int Read)> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        DateOnly night,
        IReadOnlyList<SweepName> names,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        CancellationToken cancellation)
    {
        var companies = new Dictionary<string, (string? Cik, string? Sector)>(StringComparer.Ordinal);
        var counts = new Dictionary<string, List<FiledCount>>(StringComparer.Ordinal);
        var fund = new List<(DateOnly Session, double Close)>();

        await foreach (var row in RowsAsync(connection, transaction, Companies, [], cancellation))
        {
            companies[row.GetString(0)] = (row.IsDBNull(1) ? null : row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2));
        }

        await foreach (var row in RowsAsync(connection, transaction, Counts, [], cancellation))
        {
            if (!counts.TryGetValue(row.GetString(0), out var list))
            {
                counts[row.GetString(0)] = list = [];
            }

            list.Add(new FiledCount(Date(row.GetString(1)), Date(row.GetString(2)), Money.FromStorage(row.GetString(3)), Date(row.GetString(4))));
        }

        await foreach (var row in RowsAsync(connection, transaction, FundCloses, [("$series", IndexSweepFunds[index]), ("$night", Stamp(night))], cancellation))
        {
            fund.Add((Date(row.GetString(0)), Statistic.FromPrice(Money.FromStorage(row.GetString(1)))));
        }

        var calendar = names.SelectMany(name => name.Bars.Select(bar => bar.Session)).Where(session => session <= night).Distinct().Order().ToArray();
        var at = Array.IndexOf(calendar, night);

        if (at < 0)
        {
            return ([], 0);
        }

        var inputs = new SweepHistoryInputs(night, calendar, names, 0, 0, 0, 0, string.Empty);
        var history = new HeavyweightHistory(
            companies,
            counts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FiledCount>)pair.Value, StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<FiledSplit>>(StringComparer.Ordinal),
            [],
            []);
        var (tape, sessions) = HeavyweightSweep.Lay(inputs, history, fund.Count > 0 ? new SweepMarketSeries(IndexSweepFunds[index], fund, string.Empty) : null, new HashSet<int> { at }, 0);

        if (!sessions.TryGetValue(at, out var session))
        {
            return ([], 0);
        }

        var kept = session.Members
            .Where(candidate => Clears(index, names[candidate.Name], night, income.GetValueOrDefault(names[candidate.Name].Ticker) ?? []))
            .ToArray();
        var lookup = tape.Tickers.Select((ticker, place) => (ticker, place)).ToDictionary(pair => pair.ticker, pair => pair.place, StringComparer.Ordinal);
        var rebalance = HeavyweightSweep.Read(session with { Members = kept }, Provisional, lookup);

        return ([.. rebalance.Sectors.SelectMany(sector => sector.Leaders.Select(leader => (tape.Tickers[leader], sector.Sector, (IReadOnlyList<string>)[.. sector.Cut.Select(cut => tape.Tickers[cut])])))], kept.Length);
    }

    // The fund each index's beta is read against.
    static IReadOnlyDictionary<string, string> IndexSweepFunds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Core.Providers.FundHoldings.MidCapIndex] = "IJH",
        [Core.Providers.FundHoldings.SmallCapIndex] = "IJR",
    };

    // Whether a member clears the price and dollar volume floors and the profit gate on the night.
    static bool Clears(string index, SweepName name, DateOnly night, IReadOnlyList<FiledIncome> income)
    {
        var upTo = name.Bars.Where(bar => bar.Session <= night).ToArray();

        if (upTo.Length == 0 || upTo[^1].Session != night)
        {
            return false;
        }

        var close = upTo[^1];
        var window = upTo[Math.Max(0, upTo.Length - MemberReadings.DollarVolumeSessions)..].Select(bar => (bar.Close, bar.Volume)).ToArray();

        return MemberReadings.ClearsTheFloors(index, close.RawClose > 0m ? close.RawClose : close.Close, MemberReadings.DollarVolume(window))
            && MemberReadings.Profit(income, night);
    }

    static async Task EndAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string ticker, DateOnly entered, DateOnly on, decimal exit, string reason, double growth, List<CutMember> cut, decimal entry, CancellationToken cancellation)
    {
        var cutReturn = cut.Count > 0 ? cut.Average(one => one.Growth) - 1.0 : (double?)null;
        var cost = TradeCost.InPercent(null, entry, exit) / 100.0;

        await ExecuteAsync(connection, transaction, End,
        [
            ("$index", index), ("$ticker", ticker), ("$entered_on", Stamp(entered)), ("$ended_on", Stamp(on)), ("$exit_close", Money.ToStorage(exit)),
            ("$reason", reason), ("$result", growth - 1.0), ("$cut_return", (object?)cutReturn ?? DBNull.Value), ("$cost", cost),
        ], cancellation);
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellation);
    }

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
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

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
