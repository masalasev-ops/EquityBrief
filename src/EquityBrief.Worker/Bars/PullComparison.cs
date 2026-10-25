using System.Globalization;
using EquityBrief.Data;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// What the comparison of the pulls made from a day on with the pulls made before it read: the stocks each side holds and
// those the older hold that no newer pull does; the sessions both hold, those compared at the store's scale, those whose
// closes stand more than a cent apart and more than one per cent apart, those whose closes as traded differ, and the
// largest differences; the sessions the older hold inside a newer series' span that it does not; and the prints the
// older hold and those no newer pull holds.
public sealed record PullComparisonOutcome(
    int OlderStocks,
    int NewerStocks,
    IReadOnlyList<string> NotPulledAgain,
    int Shared,
    int AtTheStoreScale,
    int ApartByACent,
    int ApartByOnePerCent,
    int TradedApart,
    IReadOnlyList<string> Largest,
    int Missing,
    IReadOnlyList<string> MissingNamed,
    int OlderPrints,
    int PrintsMissing);

// The comparison of two pulls before the older is removed. A pull's closes are adjusted as the provider sent them on its
// own day, so each side is brought to the store's scale by the ratio of the store's close to its own on the stock's first
// stored session, as every reader brings it; a stock the store holds no session of, or one either side holds no close on
// that session for, is compared with the newer brought to the older by the median of their closes' ratios over the
// sessions they share, as the sweeps' history brings a pull to the store. Each side reads a session from its newest pull
// holding it. It reads the store and writes nothing.
// see: A pull keeps its whole span beside an earlier pull's rows, and every reader reads the newest pull holding what it reads
public static class PullComparison
{
    // The largest differences, and the stocks with sessions missing, named in full; the rest are counted.
    public const int Named = 10;

    // A cent, the smallest apart the comparison counts, and one per cent of the older close.
    public const decimal Cent = 0.01m;

    public const decimal OnePerCent = 0.01m;

    const string Stocks = "SELECT DISTINCT ticker FROM pulled_bar ORDER BY ticker;";

    const string Closes = "SELECT session_date, close, raw_close, pull FROM pulled_bar WHERE ticker = $ticker ORDER BY session_date, pull;";

    const string FirstStored = "SELECT session_date, close FROM bar WHERE ticker = $ticker ORDER BY session_date LIMIT 1;";

    const string Prints = "SELECT ticker, event_date, pull FROM pulled_earnings;";

    public static async Task<PullComparisonOutcome> CompareAsync(string databaseFile, DateOnly since, CancellationToken cancellation = default)
    {
        // A pull's run id begins with its instant, so the pulls made from the day on sort at or after its date.
        var cut = HistoryPull.RunPrefix + since.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var tickers = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Stocks;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                tickers.Add(reader.GetString(0));
            }
        }

        var (olderStocks, newerStocks, shared, atTheStore, cent, percent, traded, missing) = (0, 0, 0, 0, 0, 0, 0, 0);
        var notPulled = new List<string>();
        var missingNamed = new List<string>();
        var largest = new PriorityQueue<string, decimal>();
        var spans = new Dictionary<string, (DateOnly First, DateOnly Last)>(StringComparer.Ordinal);

        foreach (var ticker in tickers)
        {
            var older = new SortedDictionary<DateOnly, (decimal Close, decimal Traded)>();
            var newer = new SortedDictionary<DateOnly, (decimal Close, decimal Traded)>();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Closes;
                command.Parameters.AddWithValue("$ticker", ticker);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                // Ordered by pull within a session, so each side keeps its newest pull's row.
                while (await reader.ReadAsync(cancellation))
                {
                    var side = string.CompareOrdinal(reader.GetString(3), cut) >= 0 ? newer : older;

                    side[Date(reader.GetString(0))] = (Money.FromStorage(reader.GetString(1)), Money.FromStorage(reader.GetString(2)));
                }
            }

            if (older.Count > 0)
            {
                olderStocks++;
            }

            if (newer.Count > 0)
            {
                newerStocks++;
                spans[ticker] = (newer.Keys.First(), newer.Keys.Last());
            }

            if (older.Count == 0)
            {
                continue;
            }

            if (newer.Count == 0)
            {
                notPulled.Add(ticker);

                continue;
            }

            var both = older.Keys.Where(newer.ContainsKey).ToArray();
            var (olderFactor, newerFactor) = (1m, 1m);
            var stored = await FirstStoredAsync(connection, ticker, cancellation);

            if (stored is { } first && first.Close > 0m && older.TryGetValue(first.Session, out var o) && o.Close > 0m && newer.TryGetValue(first.Session, out var n) && n.Close > 0m)
            {
                (olderFactor, newerFactor) = (first.Close / o.Close, first.Close / n.Close);
                atTheStore += both.Length;
            }
            else
            {
                newerFactor = Median([.. both.Where(day => newer[day].Close > 0m).Select(day => older[day].Close / newer[day].Close)]);
            }

            foreach (var day in both)
            {
                var (was, now) = (older[day].Close * olderFactor, newer[day].Close * newerFactor);
                var apart = Math.Abs(was - now);

                shared++;

                if (apart > Cent)
                {
                    cent++;

                    var share = was > 0m ? apart / was : decimal.MaxValue;

                    if (share > OnePerCent)
                    {
                        percent++;
                    }

                    largest.Enqueue(FormattableString.Invariant($"{ticker} {day:yyyy-MM-dd} {Math.Round(was, 4)} against {Math.Round(now, 4)}"), share);

                    if (largest.Count > Named)
                    {
                        largest.Dequeue();
                    }
                }

                if (older[day].Traded > 0m && newer[day].Traded > 0m && older[day].Traded != newer[day].Traded)
                {
                    traded++;
                }
            }

            var (from, through) = spans[ticker];
            var lacking = older.Keys.Where(day => day >= from && day <= through && !newer.ContainsKey(day)).ToArray();

            if (lacking.Length > 0)
            {
                missing += lacking.Length;

                if (missingNamed.Count < Named)
                {
                    missingNamed.Add(FormattableString.Invariant($"{ticker} {lacking.Length} session(s), the first {lacking[0]:yyyy-MM-dd}"));
                }
            }
        }

        var (olderPrints, printsMissing) = await PrintsAsync(connection, cut, spans, cancellation);
        var named = new List<string>();

        while (largest.TryDequeue(out var one, out _))
        {
            named.Insert(0, one);
        }

        return new PullComparisonOutcome(olderStocks, newerStocks, notPulled, shared, atTheStore, cent, percent, traded, named, missing, missingNamed, olderPrints, printsMissing);
    }

    // What the comparison found, a line each part.
    public static IReadOnlyList<string> Lines(PullComparisonOutcome compared, DateOnly since) =>
    [
        FormattableString.Invariant($"compared the pulls made from {since:yyyy-MM-dd} with those made before it: the older hold {compared.OlderStocks} stock(s) and the newer {compared.NewerStocks}; {compared.NotPulledAgain.Count} the older hold and no newer pull does{(compared.NotPulledAgain.Count > 0 ? ": " + string.Join(", ", compared.NotPulledAgain) : string.Empty)}"),
        FormattableString.Invariant($"{compared.Shared} session(s) both hold, {compared.AtTheStoreScale} of them at the store's scale and the rest with the newer brought to the older; {compared.ApartByACent} more than a cent apart, {compared.ApartByOnePerCent} of them more than one per cent apart; {compared.TradedApart} whose closes as traded differ"),
        "the largest: " + (compared.Largest.Count > 0 ? string.Join("; ", compared.Largest) : "none"),
        FormattableString.Invariant($"{compared.Missing} session(s) the older hold inside a newer series' span that it does not{(compared.MissingNamed.Count > 0 ? ": " + string.Join("; ", compared.MissingNamed) : string.Empty)}"),
        FormattableString.Invariant($"{compared.OlderPrints} print(s) the older hold, {compared.PrintsMissing} of them inside a newer series' span and held by no newer pull"),
    ];

    static async Task<(DateOnly Session, decimal Close)?> FirstStoredAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = FirstStored;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) ? (Date(reader.GetString(0)), Money.FromStorage(reader.GetString(1))) : null;
    }

    // The prints the older pulls hold, and those of them, inside the span of a newer series of the same stock, that no
    // newer pull holds.
    static async Task<(int Older, int Missing)> PrintsAsync(SqliteConnection connection, string cut, IReadOnlyDictionary<string, (DateOnly First, DateOnly Last)> spans, CancellationToken cancellation)
    {
        var older = new HashSet<(string, DateOnly)>();
        var newer = new HashSet<(string, DateOnly)>();

        await using var command = connection.CreateCommand();
        command.CommandText = Prints;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            (string.CompareOrdinal(reader.GetString(2), cut) >= 0 ? newer : older).Add((reader.GetString(0), Date(reader.GetString(1))));
        }

        return (
            older.Count,
            older.Count(print => spans.TryGetValue(print.Item1, out var span) && print.Item2 >= span.First && print.Item2 <= span.Last && !newer.Contains(print)));
    }

    static decimal Median(decimal[] values)
    {
        if (values.Length == 0)
        {
            return 1m;
        }

        Array.Sort(values);

        return values.Length % 2 == 1 ? values[values.Length / 2] : (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2;
    }

    static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
