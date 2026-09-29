using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Sweep;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Sweep;

// One member of the index on any session of the history, with its series, the spans the index held it for, and
// the earnings dates on file for it.
public sealed record SweepName(
    string Ticker,
    SweepBar[] Bars,
    IReadOnlyList<(DateOnly? Joined, DateOnly? Left)> Spans,
    DateOnly[] Prints)
{
    public bool MemberOn(DateOnly session) =>
        Spans.Any(span => (span.Joined is not { } joined || joined <= session) && (span.Left is not { } left || left > session));
}

// The history the sweep replays, read once from the live store and held for the whole run: the sessions, every
// name the index held on one of them with its series, and the index-nights no bar was served for.
public sealed record SweepHistoryInputs(
    DateOnly Through,
    DateOnly[] Sessions,
    IReadOnlyList<SweepName> Names,
    long IndexNights,
    long IndexNightsWithoutABar,
    int NamesWithoutBars,
    int NamesMissingSomeSessions,
    string Fingerprint);

// The sweep's one read of the store.
//
// It reads the live store directly and writes nothing to it, opened read-only and never immutable, since a night
// writes to it between the sweep's reads. Every read is one statement over one name's rows or one small table,
// its transaction closed before anything is computed over what it returned, so a writer waits at most for one
// such read and never for the sweep's arithmetic. The history ends at the newest session the store's own bars
// hold when the run starts, fixed for the whole run, so the nights added meanwhile change nothing it reads.
//
// The pulled history before the store's year is scaled to the store's own bars over the sessions both hold, by
// the median ratio of their closes, since the provider's adjusted prices move by a factor after each corporate
// action and the store's bars are the newer adjustment; the ratios and the distances in typical moves every
// gate reads are unchanged by the factor. A session is a day at least half the names whose series span it hold,
// as the history pull reads its own calendar.
// see: The history pulled before the store's year sits apart from its bars, marked by the pull that wrote it, read by no night and removed whole by that pull
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class SweepHistory : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
        ],
        Feeds: []);

    const string Index = "GSPC";

    readonly string databaseFile;

    public SweepHistory(string databaseFile) => this.databaseFile = databaseFile;

    // The connection every read goes through: read-only and never immutable, waiting on a writer's lock for as
    // long as the store's other readers do.
    public static string ConnectionString(string databaseFile)
    {
        var builder = StoreConnection.Builder(databaseFile);

        builder.Mode = SqliteOpenMode.ReadOnly;

        return builder.ConnectionString;
    }

    // The newest session the store's own bars hold, which a run fixes as its end when it starts.
    public async Task<DateOnly> NewestSessionAsync(CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        return Date((string)(await command.ExecuteScalarAsync(cancellation))!);
    }

    public async Task<SweepHistoryInputs> ReadAsync(DateOnly through, Action<string>? progress = null, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var spans = new Dictionary<string, List<(DateOnly?, DateOnly?)>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT ticker, joined, \"left\" FROM membership WHERE index_code = $index ORDER BY ticker;", [("$index", Index)], cancellation))
        {
            var ticker = row.GetString(0);

            if (!spans.TryGetValue(ticker, out var held))
            {
                spans[ticker] = held = [];
            }

            held.Add((row.IsDBNull(1) ? null : Date(row.GetString(1)), row.IsDBNull(2) ? null : Date(row.GetString(2))));
        }

        var prints = new Dictionary<string, SortedSet<DateOnly>>(StringComparer.Ordinal);

        foreach (var query in new[] { "SELECT ticker, event_date FROM pulled_earnings;", "SELECT ticker, event_date FROM calendar WHERE kind = 'earnings';" })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var ticker = row.GetString(0);

                if (!prints.TryGetValue(ticker, out var dates))
                {
                    prints[ticker] = dates = [];
                }

                dates.Add(Date(row.GetString(1)));
            }
        }

        var names = new List<SweepName>();
        var stamp = through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fingerprint = new System.Text.StringBuilder();
        var read = 0;

        foreach (var (ticker, held) in spans.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var pulled = await BarsAsync(connection, "pulled_bar", ticker, stamp, cancellation);
            var stored = await BarsAsync(connection, "bar", ticker, stamp, cancellation);
            var series = Merged(pulled, stored);

            fingerprint.Append(ticker).Append(':').Append(series.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(series.Sum(bar => bar.Close).ToString(CultureInfo.InvariantCulture)).Append(';');

            names.Add(new SweepName(ticker, series, held, [.. (prints.GetValueOrDefault(ticker) ?? [])]));

            if (++read % 100 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"read {read} of {spans.Count} name(s)"));
            }
        }

        var sessions = Sessions(names);
        var (indexNights, withoutBar, namesWithout, namesMissing) = Missing(names, sessions);

        return new SweepHistoryInputs(
            through,
            sessions,
            names,
            indexNights,
            withoutBar,
            namesWithout,
            namesMissing,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint.ToString())))[..16].ToLowerInvariant());
    }

    // The pulled series scaled by the median of the stored closes over the pulled ones across the sessions both
    // hold, then the stored bars from the store's first session on. A name the store holds no bar for keeps its
    // pulled series as served, and one the pull holds none for is the store's series alone.
    public static SweepBar[] Merged(IReadOnlyList<SweepBar> pulled, IReadOnlyList<SweepBar> stored)
    {
        if (stored.Count == 0)
        {
            return [.. pulled];
        }

        if (pulled.Count == 0)
        {
            return [.. stored];
        }

        var byDay = stored.ToDictionary(bar => bar.Session);
        var ratios = pulled
            .Where(bar => bar.Close > 0 && byDay.ContainsKey(bar.Session))
            .Select(bar => byDay[bar.Session].Close / bar.Close)
            .Order()
            .ToArray();
        var factor = ratios.Length == 0 ? 1m : ratios.Length % 2 == 1 ? ratios[ratios.Length / 2] : (ratios[(ratios.Length / 2) - 1] + ratios[ratios.Length / 2]) / 2;
        var first = stored[0].Session;

        return
        [
            .. pulled
                .Where(bar => bar.Session < first)
                .Select(bar => bar with
                {
                    High = Core.Prices.PriceForm.Round(bar.High * factor, Core.Prices.Statistic.Places),
                    Low = Core.Prices.PriceForm.Round(bar.Low * factor, Core.Prices.Statistic.Places),
                    Close = Core.Prices.PriceForm.Round(bar.Close * factor, Core.Prices.Statistic.Places),
                }),
            .. stored,
        ];
    }

    // The days at least half the names whose series span each hold.
    public static DateOnly[] Sessions(IReadOnlyList<SweepName> names)
    {
        var holders = new SortedDictionary<DateOnly, int>();
        var spans = names.Where(name => name.Bars.Length > 0).Select(name => (First: name.Bars[0].Session, Last: name.Bars[^1].Session)).ToArray();

        foreach (var name in names)
        {
            foreach (var bar in name.Bars)
            {
                holders[bar.Session] = holders.GetValueOrDefault(bar.Session) + 1;
            }
        }

        return [.. holders.Where(pair => pair.Value * 2 >= spans.Count(span => span.First <= pair.Key && pair.Key <= span.Last)).Select(pair => pair.Key)];
    }

    // The index-nights over the sessions, and those a member of the index on the night has no bar for, being a
    // name the provider no longer serves or a session its series misses.
    static (long IndexNights, long WithoutBar, int NamesWithout, int NamesMissing) Missing(IReadOnlyList<SweepName> names, DateOnly[] sessions)
    {
        long nights = 0;
        long without = 0;
        var namesWithout = 0;
        var namesMissing = 0;

        foreach (var name in names)
        {
            var held = name.Bars.Select(bar => bar.Session).ToHashSet();
            var memberNights = sessions.Where(name.MemberOn).ToArray();
            var missing = memberNights.Count(session => !held.Contains(session));

            nights += memberNights.Length;
            without += missing;
            namesWithout += memberNights.Length > 0 && name.Bars.Length == 0 ? 1 : 0;
            namesMissing += missing > 0 && name.Bars.Length > 0 ? 1 : 0;
        }

        return (nights, without, namesWithout, namesMissing);
    }

    static async Task<SweepBar[]> BarsAsync(SqliteConnection connection, string table, string ticker, string through, CancellationToken cancellation)
    {
        var bars = new List<SweepBar>();

        await foreach (var row in RowsAsync(
            connection,
            $"SELECT session_date, high, low, close, volume FROM {table} WHERE ticker = $ticker AND session_date <= $through ORDER BY session_date;",
            [("$ticker", ticker), ("$through", through)],
            cancellation))
        {
            bars.Add(new SweepBar(
                Date(row.GetString(0)),
                Money.FromStorage(row.GetString(1)),
                Money.FromStorage(row.GetString(2)),
                Money.FromStorage(row.GetString(3)),
                row.GetInt64(4)));
        }

        return [.. bars];
    }

    // One statement, read to its end and closed before the caller computes anything over what it returned.
    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(
        SqliteConnection connection,
        string query,
        IReadOnlyList<(string Name, string Value)> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;

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

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
