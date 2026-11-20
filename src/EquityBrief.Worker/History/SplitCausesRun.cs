using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// From 24.11, read-only: every step and entry the split rule sets aside over the long history and over the store's own
// year, each with its cause, counted by cause. It opens the store for reading alone and writes nothing.
// see: A split table's entry is read only where the closes step by its own ratio, and an entry of another size is set aside and listed
public static class SplitCausesRun
{
    public const string Verb = "split-causes";

    public const string LongSpan = "the long history";

    public const string StoreSpan = "the store's year";

    public sealed record Found(string Span, string Ticker, SplitSetAside SetAside);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static async Task<Dictionary<string, List<T>>> ByTickerAsync<T>(SqliteConnection connection, IEnumerable<string> queries, Func<SqliteDataReader, T?> read, CancellationToken cancellation)
        where T : class
    {
        var by = new Dictionary<string, List<T>>(StringComparer.Ordinal);

        foreach (var query in queries)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = query;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (read(reader) is not { } one)
                {
                    continue;
                }

                var ticker = reader.GetString(0);

                if (!by.TryGetValue(ticker, out var list))
                {
                    by[ticker] = list = [];
                }

                list.Add(one);
            }
        }

        return by;
    }

    static SplitEntry? Entry(SqliteDataReader reader)
    {
        var newShares = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
        var oldShares = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture);

        return newShares > 0m && oldShares > 0m ? new SplitEntry(Day(reader.GetString(1)), newShares, oldShares) : null;
    }

    static SplitDividend? Dividend(SqliteDataReader reader) => new(Day(reader.GetString(1)), Money.FromStorage(reader.GetString(2)));

    // Each stock's closes in a bar table read in turn, with the entries and the dividends the split rule reads beside them.
    static async Task ReadSpanAsync(
        SqliteConnection connection,
        string span,
        string bars,
        IReadOnlyDictionary<string, List<SplitEntry>> entries,
        IReadOnlyDictionary<string, List<SplitDividend>> dividends,
        Action<Found> found,
        CancellationToken cancellation)
    {
        var current = string.Empty;
        var held = new List<SweepBar>();

        void Flush()
        {
            if (held.Count > 0)
            {
                foreach (var one in SplitCauses.Of(held, (IReadOnlyList<SplitEntry>?)entries.GetValueOrDefault(current) ?? [], (IReadOnlyList<SplitDividend>?)dividends.GetValueOrDefault(current) ?? []))
                {
                    found(new Found(span, current, one));
                }
            }

            held.Clear();
        }

        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT ticker, session_date, close, raw_close FROM {bars} ORDER BY ticker, session_date;";

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (ticker != current)
            {
                Flush();
                current = ticker;
            }

            held.Add(new SweepBar(Day(reader.GetString(1)), 0m, 0m, Money.FromStorage(reader.GetString(2)), 0, RawClose: reader.IsDBNull(3) ? 0m : Money.FromStorage(reader.GetString(3))));
        }

        Flush();
    }

    // The long history read against the three split tables and both dividend tables, and the store's year against the
    // tables the night reads, as the rule reads each.
    public static async Task<IReadOnlyList<Found>> ReadAsync(string databaseFile, CancellationToken cancellation = default)
    {
        var builder = StoreConnection.Builder(databaseFile);

        builder.Mode = SqliteOpenMode.ReadOnly;
        builder.Pooling = false;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);

        const string Events = "SELECT ticker, ex_date, new_shares, old_shares FROM split_event;";
        const string Pulled = "SELECT ticker, ex_date, new_shares, old_shares FROM pulled_split;";
        const string Long = "SELECT ticker, ex_date, new_shares, old_shares FROM long_split;";
        const string Paid = "SELECT ticker, ex_date, COALESCE(unadjusted, amount) FROM dividend_event;";
        const string LongPaid = "SELECT ticker, ex_date, COALESCE(raw_amount, amount) FROM long_dividend;";

        var night = await ByTickerAsync(connection, [Events, Pulled], Entry, cancellation);
        var all = await ByTickerAsync(connection, [Events, Pulled, Long], Entry, cancellation);
        var paid = await ByTickerAsync(connection, [Paid], Dividend, cancellation);
        var allPaid = await ByTickerAsync(connection, [Paid, LongPaid], Dividend, cancellation);
        var found = new List<Found>();

        await ReadSpanAsync(connection, LongSpan, "long_bar", all, allPaid, found.Add, cancellation);
        await ReadSpanAsync(connection, StoreSpan, "bar", night, paid, found.Add, cancellation);

        return found;
    }

    // Each span's count by cause, the steps' causes before the entries', in the order the causes are read.
    public static IReadOnlyList<string> Counts(IReadOnlyList<Found> found) =>
    [
        .. new[] { LongSpan, StoreSpan }.Select(span =>
        {
            var inSpan = found.Where(one => one.Span == span).ToList();
            var steps = inSpan.Count(one => one.SetAside.Step is not null);

            return FormattableString.Invariant($"{span}: {steps} step(s) and {inSpan.Count - steps} entr(ies) set aside; ")
                + string.Join("; ", Enum.GetValues<SplitCause>().Select(cause => (Cause: cause, Count: inSpan.Count(one => one.SetAside.Cause == cause))).Where(one => one.Count > 0)
                    .Select(one => FormattableString.Invariant($"{SplitCauses.Words(one.Cause)}: {one.Count}")));
        }),
    ];

    public static string Line(Found one) =>
        FormattableString.Invariant($"{one.Span}: {one.Ticker} {one.SetAside.Day:yyyy-MM-dd}, ")
        + (one.SetAside.Step is { } step
            ? FormattableString.Invariant($"a step of {step:0.###}")
            : FormattableString.Invariant($"an entry of {one.SetAside.Entry!.NewShares:0.####} for {one.SetAside.Entry.OldShares:0.####}"))
        + ": " + SplitCauses.Words(one.SetAside.Cause);
}
