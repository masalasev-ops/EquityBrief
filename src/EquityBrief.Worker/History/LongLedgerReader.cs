using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// The long ledger's measure, read-only and writing no store row: at its top, whether the older decade's unexplained jumps a
// stock-year are more than twice the newer years', and their count by era; each family on each index by era under the two
// readings, with and without the trades crossing an unexplained jump, and where leaving the stopped members out flatters the
// edge; the long setups whose stock's prices stopped while open, those of a company with an 8-K carrying item 1.03 on file,
// and what those would read at nothing; the holdings the long history leaves out each year by count and share of value,
// failures apart, the failures' combined share at each quarter end they were held, and each failure at nothing in the
// equal-weighted quarter after its last quarter end held beside the quarter as run; and the standing years' setups at their
// last price with the member-sessions the leavers' dates add.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public sealed class LongLedgerReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongReading, Touch.Read),
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.DepartedSetup, Touch.Read),
            new StoreTouch(Store.LongHolding, Touch.Read),
            new StoreTouch(Store.LongSnapshot, Touch.Read),
            new StoreTouch(Store.LongFiling, Touch.Read),
            new StoreTouch(Store.LongSpan, Touch.Read),
            new StoreTouch(Store.LongBar, Touch.Read),
            new StoreTouch(Store.LongMarketBar, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.DataCheck, Touch.Read),
        ],
        Feeds: []);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Shown(double? figure, string format = "0.000") => figure is { } held ? held.ToString(format, CultureInfo.InvariantCulture) : "none";

    public static async Task<IReadOnlyList<string>> MeasureAsync(string databaseFile, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        async Task EachAsync(string sql, Action<SqliteDataReader> row)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                row(reader);
            }
        }

        var lines = new List<string>();

        // The unexplained jumps by era, and a stock-year as a code holding a long price in the year.
        var jumps = new Dictionary<string, int>(StringComparer.Ordinal);
        var jumpsByYear = new Dictionary<int, int>();

        await EachAsync("SELECT session_date FROM data_check WHERE class = 'jump' AND kind = 'unexplained';", row =>
        {
            var year = Day(row.GetString(0)).Year;
            jumpsByYear[year] = jumpsByYear.GetValueOrDefault(year) + 1;
            jumps[LongLedger.EraOf(year)] = jumps.GetValueOrDefault(LongLedger.EraOf(year)) + 1;
        });

        var stockYears = new Dictionary<int, int>();

        await EachAsync("SELECT CAST(substr(session_date, 1, 4) AS INTEGER), COUNT(DISTINCT ticker) FROM long_bar GROUP BY 1;", row => stockYears[row.GetInt32(0)] = row.GetInt32(1));

        var (olderJumps, olderYears) = (jumpsByYear.Where(pair => pair.Key is >= 2005 and <= 2014).Sum(pair => pair.Value), stockYears.Where(pair => pair.Key is >= 2005 and <= 2014).Sum(pair => pair.Value));
        var (newerJumps, newerYears) = (jumpsByYear.Where(pair => pair.Key >= 2015).Sum(pair => pair.Value), stockYears.Where(pair => pair.Key >= 2015).Sum(pair => pair.Value));
        var above = LongLedger.OlderJumpsAbove(olderJumps, olderYears, newerJumps, newerYears);

        lines.Add(above
            ? FormattableString.Invariant($"The unexplained jumps of 2005 to 2014 are more than twice 2015 to 2026's a stock-year: {olderJumps} over {olderYears} stock-years against {newerJumps} over {newerYears}. Every result below is shown with and without the trades crossing one, and nothing waits on it.")
            : FormattableString.Invariant($"The unexplained jumps of 2005 to 2014 are not more than twice 2015 to 2026's a stock-year: {olderJumps} over {olderYears} stock-years against {newerJumps} over {newerYears}."));
        lines.Add("unexplained jumps by era: " + string.Join(", ", LongLedger.Eras.Select(era => FormattableString.Invariant($"{era.Era} {jumps.GetValueOrDefault(era.Era)}"))));

        // Each family on each index by era under the two readings.
        var years = new List<(string Index, string Family, int Year, string Reading, int Settled, int Targets, double? Edge, double? After, int Crossing, double? Without)>();

        await EachAsync("SELECT index_code, family, year, reading, settled, targets, edge_mean, edge_after_cost_mean, crossing, edge_after_cost_without FROM long_reading ORDER BY index_code, family, year, reading;", row =>
            years.Add((row.GetString(0), row.GetString(1), row.GetInt32(2), row.GetString(3), row.GetInt32(4), row.GetInt32(5),
                row.IsDBNull(6) ? null : row.GetDouble(6), row.IsDBNull(7) ? null : row.GetDouble(7), row.GetInt32(8), row.IsDBNull(9) ? null : row.GetDouble(9))));

        static double? Weighted(IEnumerable<(int Count, double? Mean)> parts)
        {
            var held = parts.Where(part => part.Mean is not null && part.Count > 0).ToArray();
            var count = held.Sum(part => part.Count);

            return count == 0 ? null : held.Sum(part => part.Count * part.Mean!.Value) / count;
        }

        foreach (var group in years.GroupBy(year => (year.Index, year.Family)).OrderBy(group => group.Key.Index, StringComparer.Ordinal).ThenBy(group => group.Key.Family, StringComparer.Ordinal))
        {
            foreach (var era in LongLedger.Eras)
            {
                var asRuled = group.Where(year => year.Reading == LongLedger.AsRuled && year.Year >= era.First && year.Year <= era.Last).ToArray();
                var lastPrice = group.Where(year => year.Reading == LongLedger.LastPrice && year.Year >= era.First && year.Year <= era.Last).ToArray();

                if (asRuled.Length == 0 && lastPrice.Length == 0)
                {
                    continue;
                }

                lines.Add(FormattableString.Invariant(
                    $"{group.Key.Index} {group.Key.Family} {era.Era}: as ruled {asRuled.Sum(year => year.Settled)} settled, {asRuled.Sum(year => year.Targets)} at target, edge after costs {Shown(Weighted(asRuled.Select(year => (year.Settled, year.After))))}, without the {asRuled.Sum(year => year.Crossing)} crossing a jump {Shown(Weighted(asRuled.Select(year => (year.Settled - year.Crossing, year.Without))))}; at the last price {lastPrice.Sum(year => year.Settled)} settled, edge after costs {Shown(Weighted(lastPrice.Select(year => (year.Settled, year.After))))}, without {Shown(Weighted(lastPrice.Select(year => (year.Settled - year.Crossing, year.Without))))}"));
            }

            var longRuled = Weighted(group.Where(year => year.Reading == LongLedger.AsRuled && year.Year <= LongLedger.Through.Year).Select(year => (year.Settled, year.After)));
            var longLast = Weighted(group.Where(year => year.Reading == LongLedger.LastPrice && year.Year <= LongLedger.Through.Year).Select(year => (year.Settled, year.After)));

            if (longRuled is { } ruled && longLast is { } last && ruled > last)
            {
                lines.Add(FormattableString.Invariant($"{group.Key.Index} {group.Key.Family}: leaving the stopped members out flatters the edge over 2005 to 2018, {ruled:0.000} as ruled against {last:0.000} at the last price; the fix goes to the operator as a proposal and nothing moves without their word"));
            }
        }

        // The swing setups of the long ledger whose edge as ruled lies past the outlying bound either way, by family and era,
        // and the mean edge without them: a member whose prices stand still sets a risk near nought, and a result read in it
        // runs to hundreds.
        var edges = new List<(string Index, string Family, int Year, double Edge)>();

        await EachAsync("SELECT index_code, family, CAST(substr(session_date, 1, 4) AS INTEGER), edge FROM long_setup WHERE settled = 1 AND edge IS NOT NULL;", row =>
        {
            if (!LikeSetups.IsBook(row.GetString(1)))
            {
                edges.Add((row.GetString(0), row.GetString(1), row.GetInt32(2), row.GetDouble(3)));
            }
        });

        foreach (var group in edges.GroupBy(row => (row.Index, row.Family, Era: LongLedger.EraOf(row.Year))).OrderBy(group => group.Key.Index, StringComparer.Ordinal).ThenBy(group => group.Key.Family, StringComparer.Ordinal).ThenBy(group => group.Key.Era, StringComparer.Ordinal))
        {
            var outlying = group.Count(row => Math.Abs(row.Edge) > LongLedger.OutlyingRisks);

            if (outlying > 0)
            {
                lines.Add(FormattableString.Invariant($"{group.Key.Index} {group.Key.Family} {group.Key.Era}: {outlying} of {group.Count()} settled setup(s) with an edge past {LongLedger.OutlyingRisks} risks either way, the mean edge {group.Average(row => row.Edge):0.000} with them and {group.Where(row => Math.Abs(row.Edge) <= LongLedger.OutlyingRisks).Select(row => row.Edge).DefaultIfEmpty().Average():0.000} without"));
            }
        }

        // The long setups whose stock's prices stopped while open, those of a company that filed item 1.03, and those at nothing.
        var bankrupt = new HashSet<string>(StringComparer.Ordinal);

        await EachAsync("SELECT DISTINCT ticker FROM long_filing WHERE form LIKE '8-K%' AND (',' || items || ',') LIKE '%,1.03,%';", row => bankrupt.Add(row.GetString(0)));

        var stoppedRows = new List<(string Index, string Family, string Ticker, double Entry, double Stop, double? Fill, double? Last, double? LastEdge, double? Benchmark)>();

        await EachAsync("SELECT index_code, family, ticker, entry, stop, fill, last_result, last_edge, last_benchmark FROM long_setup WHERE result IS NULL AND last_result IS NOT NULL;", row =>
            stoppedRows.Add((row.GetString(0), row.GetString(1), row.GetString(2),
                Statistic.FromPrice(Money.FromStorage(row.GetString(3))), Statistic.FromPrice(Money.FromStorage(row.GetString(4))),
                row.IsDBNull(5) ? null : Statistic.FromPrice(Money.FromStorage(row.GetString(5))),
                row.IsDBNull(6) ? null : row.GetDouble(6), row.IsDBNull(7) ? null : row.GetDouble(7), row.IsDBNull(8) ? null : row.GetDouble(8))));

        foreach (var index in stoppedRows.GroupBy(row => row.Index).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var filed = index.Where(row => bankrupt.Contains(row.Ticker)).ToArray();
            var atNothing = filed.Select(row => LongLedger.AtLastPrice(LikeSetups.IsBook(row.Family), row.Fill ?? row.Entry, row.Entry, row.Stop, double.Epsilon)).ToArray();

            lines.Add(FormattableString.Invariant(
                $"{index.Key}: {index.Count()} long setup(s) on {index.Select(row => row.Ticker).Distinct(StringComparer.Ordinal).Count()} stock(s) whose prices stopped while open, read at the last price; {filed.Length} of them on {filed.Select(row => row.Ticker).Distinct(StringComparer.Ordinal).Count()} stock(s) with an 8-K carrying item 1.03 on file, their mean result at the last price {Shown(filed.Length == 0 ? null : filed.Where(row => row.Last is not null).Select(row => row.Last!.Value).DefaultIfEmpty().Average())} and at nothing {Shown(atNothing.Length == 0 ? null : atNothing.Where(made => made is not null).Select(made => made!.Value).DefaultIfEmpty().Average())}, the rule as ruled reading none of them"));
        }

        // The holdings the long history leaves out each year, failures apart, by count and share of value.
        var snapshots = new Dictionary<(string, string), decimal>();

        await EachAsync("SELECT index_code, period, value_usd FROM long_snapshot;", row => snapshots[(row.GetString(0), row.GetString(1))] = Money.FromStorage(row.GetString(2)));

        var leftOut = new List<(string Index, string Period, string Name, string? Left, decimal Value)>();

        await EachAsync("SELECT index_code, period, name, left_as, value_usd FROM long_holding WHERE ticker IS NULL;", row =>
            leftOut.Add((row.GetString(0), row.GetString(1), row.GetString(2), row.IsDBNull(3) ? null : row.GetString(3), row.IsDBNull(4) ? 0m : Money.FromStorage(row.GetString(4)))));

        foreach (var index in leftOut.GroupBy(row => row.Index).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            foreach (var year in index.GroupBy(row => row.Period[..4]).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var quarters = year.Select(row => row.Period).Distinct(StringComparer.Ordinal).ToArray();
                var value = quarters.Sum(period => snapshots.GetValueOrDefault((index.Key, period)));
                var failed = year.Where(row => row.Left == "failed").ToArray();
                var others = year.Where(row => row.Left != "failed").ToArray();

                lines.Add(FormattableString.Invariant(
                    $"{index.Key} {year.Key} left out: {others.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count()} holding(s), {Share(others.Sum(row => row.Value), value)} of value a quarter on average; failures apart {failed.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count()}, {Share(failed.Sum(row => row.Value), value)}"));
            }

            foreach (var quarter in index.Where(row => row.Left == "failed").GroupBy(row => row.Period).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                lines.Add(FormattableString.Invariant($"{index.Key} {quarter.Key}: the left-out failures held, {string.Join(", ", quarter.Select(row => row.Name).Distinct(StringComparer.Ordinal))}, {Share(quarter.Sum(row => row.Value), snapshots.GetValueOrDefault((index.Key, quarter.Key)))} of the index's value"));
            }
        }

        // Each left-out failure at nothing in the equal-weighted quarter after its last quarter end held.
        var held = new Dictionary<(string, string), List<string>>();

        await EachAsync("SELECT index_code, period, ticker FROM long_holding WHERE ticker IS NOT NULL;", row =>
        {
            if (!held.TryGetValue((row.GetString(0), row.GetString(1)), out var list))
            {
                held[(row.GetString(0), row.GetString(1))] = list = [];
            }

            list.Add(row.GetString(2));
        });

        var periods = snapshots.Keys.GroupBy(key => key.Item1).ToDictionary(group => group.Key, group => group.Select(key => key.Item2).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        foreach (var failure in leftOut.Where(row => row.Left == "failed").GroupBy(row => (row.Index, row.Name)).OrderBy(group => group.Key.Index, StringComparer.Ordinal).ThenBy(group => group.Key.Name, StringComparer.Ordinal))
        {
            var last = failure.Max(row => row.Period)!;
            var next = periods[failure.Key.Index].FirstOrDefault(period => string.CompareOrdinal(period, last) > 0);

            if (next is null || !held.TryGetValue((failure.Key.Index, last), out var members))
            {
                continue;
            }

            var returns = new List<double>();

            foreach (var ticker in members.Distinct(StringComparer.Ordinal))
            {
                double? from = null;
                double? to = null;

                await EachAsync(FormattableString.Invariant($"SELECT close FROM long_bar WHERE ticker = '{ticker.Replace("'", "''", StringComparison.Ordinal)}' AND session_date <= '{last}' ORDER BY session_date DESC LIMIT 1;"), row => from = Statistic.FromPrice(Money.FromStorage(row.GetString(0))));
                await EachAsync(FormattableString.Invariant($"SELECT close FROM long_bar WHERE ticker = '{ticker.Replace("'", "''", StringComparison.Ordinal)}' AND session_date <= '{next}' ORDER BY session_date DESC LIMIT 1;"), row => to = Statistic.FromPrice(Money.FromStorage(row.GetString(0))));

                if (from is > 0 && to is { } end)
                {
                    returns.Add((end / from.Value) - 1);
                }
            }

            if (returns.Count == 0)
            {
                continue;
            }

            var asRun = returns.Average();
            var atNothing = returns.Append(-1).Average();

            lines.Add(FormattableString.Invariant($"{failure.Key.Index} {failure.Key.Name}: last held {last}; the equal-weighted quarter to {next} over {returns.Count} priced member(s) reads {asRun:0.0000} as run and {atNothing:0.0000} with it at nothing"));
        }

        // The standing years: the setups at their last price and the member-sessions the leavers' dates add.
        var departed = new List<(string Index, double? Edge)>();

        await EachAsync("SELECT index_code, last_edge FROM departed_setup;", row => departed.Add((row.GetString(0), row.IsDBNull(1) ? null : row.GetDouble(1))));

        var sessions = new List<DateOnly>();

        await EachAsync("SELECT session_date FROM long_market_bar WHERE series = 'GSPC' ORDER BY session_date;", row => sessions.Add(Day(row.GetString(0))));

        var lastPriced = new Dictionary<string, DateOnly>(StringComparer.Ordinal);

        foreach (var query in new[] { "SELECT ticker, MAX(session_date) FROM long_bar GROUP BY ticker;", "SELECT ticker, MAX(session_date) FROM bar GROUP BY ticker;" })
        {
            await EachAsync(query, row =>
            {
                var day = Day(row.GetString(1));

                if (!lastPriced.TryGetValue(row.GetString(0), out var known) || day > known)
                {
                    lastPriced[row.GetString(0)] = day;
                }
            });
        }

        var added = new Dictionary<string, int>(StringComparer.Ordinal);

        await EachAsync("SELECT index_code, ticker, joined, \"left\" FROM long_span;", row =>
        {
            var index = row.GetString(0);

            if (!lastPriced.TryGetValue(row.GetString(1), out var priced))
            {
                return;
            }

            var left = row.IsDBNull(3) ? sessions[^1].AddDays(1) : Day(row.GetString(3));
            var from = priced > LongLedger.StandingFrom ? priced : LongLedger.StandingFrom;

            added[index] = added.GetValueOrDefault(index) + sessions.Count(session => session > from && session < left && session > priced);
        });

        foreach (var index in departed.GroupBy(row => row.Index).Select(group => group.Key).Union(added.Keys).Order(StringComparer.Ordinal))
        {
            var rows = departed.Where(row => row.Index == index).ToArray();

            lines.Add(FormattableString.Invariant($"{index} standing years: {rows.Length} setup(s) whose prices stopped while open read at the last price, mean edge {Shown(rows.Where(row => row.Edge is not null).Select(row => row.Edge!.Value).DefaultIfEmpty().Average())} over {rows.Count(row => row.Edge is not null)} with a benchmark; the leavers' dates add {added.GetValueOrDefault(index)} member-session(s) from 2019 a benchmark reads as held with no price"));
        }

        return lines;
    }

    static string Share(decimal part, decimal whole) =>
        whole > 0 ? (part / whole).ToString("P2", CultureInfo.InvariantCulture) : "none";
}
