using System.Globalization;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Sweep;
using EquityBrief.Data;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// From the 23.8 correction what the long history's profit check reads on one index: each code's quarters off the SEC's facts,
// each year's members on its first session with how many read the check, and the first year 90 in every 100 do, none where
// no year does.
// see: The profit check over the long history reads the long facts from the first year they cover 90 in every 100 members, the years before it unchecked
public sealed record LongIncome(
    IReadOnlyDictionary<string, IReadOnlyList<Core.Readings.FiledIncome>> Income,
    IReadOnlyList<(int Year, int Members, int Read)> Years,
    int? FirstChecked);

// The long history's reader: the record the sweeps, the ledger build and the tester already take, read over the long
// spans of one index, each code's long prices with the store's own bars after them at the store's scale under its
// marks, the long prints beside the pulled ones and the calendar's, the long calendar's surprises before the pulled
// ones, and each code's sector as the store files it today. It reads the store read-only one statement at a time, as
// the sweep's own read does, writes nothing, and edits no source a rule pins.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public sealed class LongHistoryReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSpan, Touch.Read),
            new StoreTouch(Store.LongBar, Touch.Read),
            new StoreTouch(Store.LongMarketBar, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.SessionMark, Touch.Read),
            new StoreTouch(Store.LongPrint, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.LongFact, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.LongFiling, Touch.Read),
        ],
        Feeds: []);

    readonly string databaseFile;

    public LongHistoryReader(string databaseFile) => this.databaseFile = databaseFile;

    // The newest session the long prices or the store's own bars hold, which a long build reads its history through so
    // every trade it writes replays over the years after its span.
    public async Task<DateOnly> NewestSessionAsync(CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);
        DateOnly? newest = null;

        foreach (var table in new[] { "SELECT MAX(session_date) FROM long_bar;", "SELECT MAX(session_date) FROM bar;" })
        {
            await foreach (var row in RowsAsync(connection, table, [], cancellation))
            {
                if (!row.IsDBNull(0) && Day(row.GetString(0)) is var day && (newest is null || day > newest))
                {
                    newest = day;
                }
            }
        }

        return newest ?? throw new InvalidOperationException("the store holds no long price and no bar");
    }

    // One index's long history through the session given: every code its long spans hold, each over its own spans, none a
    // survivor.
    public async Task<SweepHistoryInputs> ReadAsync(string index, DateOnly through, Action<string>? progress = null, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var stamp = Text(through);
        var spans = new Dictionary<string, List<(DateOnly?, DateOnly?)>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT ticker, joined, \"left\" FROM long_span WHERE index_code = $index ORDER BY ticker, joined;", [("$index", index)], cancellation))
        {
            var ticker = row.GetString(0);

            if (!spans.TryGetValue(ticker, out var held))
            {
                spans[ticker] = held = [];
            }

            held.Add((row.IsDBNull(1) ? null : Day(row.GetString(1)), row.IsDBNull(2) ? null : Day(row.GetString(2))));
        }

        var marks = new Dictionary<string, List<SessionMark>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT ticker, session_date, kind, says FROM session_mark ORDER BY ticker, session_date;", [], cancellation))
        {
            var ticker = row.GetString(0);

            if (!marks.TryGetValue(ticker, out var held))
            {
                marks[ticker] = held = [];
            }

            held.Add(new SessionMark(Day(row.GetString(1)), row.GetString(2), row.GetString(3)));
        }

        var prints = new Dictionary<string, SortedSet<DateOnly>>(StringComparer.Ordinal);

        foreach (var query in new[]
        {
            "SELECT ticker, event_date FROM long_print;",
            "SELECT ticker, event_date FROM pulled_earnings;",
            "SELECT ticker, event_date FROM calendar WHERE kind = 'earnings';",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var ticker = row.GetString(0);

                if (!prints.TryGetValue(ticker, out var dates))
                {
                    prints[ticker] = dates = [];
                }

                dates.Add(Day(row.GetString(1)));
            }
        }

        var surprises = await SurprisesAsync(connection, cancellation);
        var sectors = new Dictionary<string, string>(StringComparer.Ordinal);

        // The sector as the store files it today: the S&P 500's membership's newest, then a company's as its fetch or the
        // companies pull filed it, a code the store files no sector for reading none.
        foreach (var query in new[]
        {
            "SELECT ticker, sector FROM pulled_company WHERE sector IS NOT NULL ORDER BY pull;",
            "SELECT ticker, sector FROM company WHERE sector IS NOT NULL ORDER BY fetched_at;",
            "SELECT ticker, sector FROM membership WHERE index_code = 'GSPC' AND sector IS NOT NULL ORDER BY observed_at;",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                sectors[row.GetString(0)] = row.GetString(1);
            }
        }

        var names = new List<SweepName>();
        var fingerprint = new System.Text.StringBuilder();
        var read = 0;

        foreach (var (ticker, held) in spans.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var longBars = LongLedger.Marked(await BarsAsync(connection, "long_bar", ticker, stamp, cancellation), marks.GetValueOrDefault(ticker) ?? []);
            var stored = LongLedger.Marked(await BarsAsync(connection, "bar", ticker, stamp, cancellation), marks.GetValueOrDefault(ticker) ?? []);
            var series = SweepHistory.Merged(longBars, stored);

            fingerprint.Append(ticker).Append(':').Append(series.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(series.Sum(bar => bar.Close).ToString(CultureInfo.InvariantCulture)).Append(';');

            names.Add(new SweepName(
                ticker,
                series,
                held,
                [.. (prints.GetValueOrDefault(ticker) ?? [])],
                sectors.GetValueOrDefault(ticker),
                [.. (surprises.GetValueOrDefault(ticker) ?? []).OrderBy(surprise => surprise.EventDate)]));

            if (++read % 250 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"long history: read {read} of {spans.Count} code(s) on {index}"));
            }
        }

        var sessions = SweepHistory.Sessions(names);
        var (indexNights, withoutBar, namesWithout, namesMissing) = Missing(names, sessions);

        return new SweepHistoryInputs(
            through,
            sessions,
            names,
            indexNights,
            withoutBar,
            namesWithout,
            namesMissing,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint.ToString())))[..16].ToLowerInvariant(),
            surprises.Count > 0 ? SweepHistory.PulledSurprises : SweepHistory.NoSurprises,
            surprises.Sum(pair => pair.Value.Count),
            names.Count(name => name.Sector is null),
            [],
            0,
            names.Count);
    }

    // From 23.7 the named market series' closes through the session given from the long pull, which holds each from 2004 or
    // its start through the newest session it was asked for, one statement a series, none for a series it holds no session
    // of; the record the sweeps' own reads take, their pull named as the long one.
    public async Task<IReadOnlyList<SweepMarketSeries>> SeriesOfAsync(IReadOnlyList<string> named, DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var found = new List<SweepMarketSeries>();

        foreach (var series in named)
        {
            var closes = new List<(DateOnly, double)>();

            await foreach (var row in RowsAsync(
                connection,
                "SELECT session_date, close FROM long_market_bar WHERE series = $series AND session_date <= $through ORDER BY session_date;",
                [("$series", series), ("$through", Text(through))],
                cancellation))
            {
                closes.Add((Day(row.GetString(0)), Core.Prices.Statistic.FromPrice(Money.FromStorage(row.GetString(1)))));
            }

            if (closes.Count > 0)
            {
                found.Add(new SweepMarketSeries(series, closes, LongPull));
            }
        }

        return found;
    }

    // On the operator's answer of 2026-11-14 to item 15 each filer's facts as first filed by the history's end, the standing
    // table's and the long one's, whose periods meet and do not overlap, which the books' long test scores and values each
    // member from as the quality and Fundamentals families' history tests do from the standing table alone.
    // see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Core.Ledger.FiledFactRow>>> FiledFactsAsync(IEnumerable<string> ciks, DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var facts = new Dictionary<string, IReadOnlyList<Core.Ledger.FiledFactRow>>(StringComparer.Ordinal);

        foreach (var cik in ciks.Distinct(StringComparer.Ordinal))
        {
            var rows = new List<Core.Ledger.FiledFactRow>();

            foreach (var table in new[] { "filed_fact", "long_fact" })
            {
                await foreach (var row in RowsAsync(
                    connection,
                    $"SELECT concept, period_start, period_end, dollars, filed, form, accession FROM {table} WHERE cik = $cik AND filed <= $through;",
                    [("$cik", cik), ("$through", Text(through))],
                    cancellation))
                {
                    rows.Add(new Core.Ledger.FiledFactRow(row.GetString(0), Day(row.GetString(1)), Day(row.GetString(2)), Money.FromStorage(row.GetString(3)), Day(row.GetString(4)), row.GetString(5), row.GetString(6)));
                }
            }

            facts[cik] = rows;
        }

        return facts;
    }

    public const string LongPull = "long";

    // From the 23.8 correction each code's quarters of net income, operating income and interest expense off the SEC's facts
    // as first filed, the long facts and the standing ones, by the filer the store files the code under; and over one index's
    // codes, each year's members on its first session and how many of them read the profit check off the quarters filed
    // before it, with the first year 90 in every 100 do.
    // see: The profit check over the long history reads the long facts from the first year they cover 90 in every 100 members, the years before it unchecked
    public async Task<LongIncome> IncomeAsync(IReadOnlyList<SweepName> names, IReadOnlyList<DateOnly> sessions, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var filerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var query in new[]
        {
            "SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;",
            "SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;",
            "SELECT DISTINCT ticker, cik FROM long_filing;",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                filerOf[row.GetString(0)] = row.GetString(1).TrimStart('0');
            }
        }

        var wanted = names.Select(name => name.Ticker).Where(filerOf.ContainsKey).Select(ticker => filerOf[ticker]).ToHashSet(StringComparer.Ordinal);
        var concepts = string.Join(", ", Core.Ledger.FiledFacts.NetIncome.Concat(Core.Ledger.FiledFacts.OperatingIncome).Concat(Core.Ledger.FiledFacts.InterestExpense).Select(concept => "'" + concept + "'"));
        var facts = new Dictionary<string, List<Core.Ledger.FiledFactRow>>(StringComparer.Ordinal);

        foreach (var query in new[]
        {
            $"SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM long_fact WHERE concept IN ({concepts});",
            $"SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE concept IN ({concepts});",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var cik = row.GetString(0).TrimStart('0');

                if (!wanted.Contains(cik))
                {
                    continue;
                }

                if (!facts.TryGetValue(cik, out var rows))
                {
                    facts[cik] = rows = [];
                }

                rows.Add(new Core.Ledger.FiledFactRow(row.GetString(1), Day(row.GetString(2)), Day(row.GetString(3)), Money.FromStorage(row.GetString(4)), Day(row.GetString(5)), row.GetString(6), row.GetString(7)));
            }
        }

        var quartersOf = facts.ToDictionary(pair => pair.Key, pair => LongProfit.Quarters(pair.Value), StringComparer.Ordinal);
        var income = new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (filerOf.TryGetValue(name.Ticker, out var cik) && quartersOf.TryGetValue(cik, out var quarters))
            {
                income[name.Ticker] = quarters;
            }
        }

        var years = new List<(int Year, int Members, int Read)>();

        foreach (var first in sessions.GroupBy(session => session.Year).Select(year => year.Min()).Order())
        {
            var members = names.Where(name => name.MemberOn(first)).Select(name => name.Ticker).Distinct(StringComparer.Ordinal).ToArray();

            years.Add((first.Year, members.Length, members.Count(ticker => income.TryGetValue(ticker, out var quarters) && LongProfit.Reads(quarters, first))));
        }

        return new LongIncome(income, years, LongProfit.FirstCheckedYear(years));
    }

    // Each code's surprises: the pulled ones where the pull holds the day, and the long calendar's on every other day it
    // holds a percent for.
    static async Task<Dictionary<string, List<SweepSurprise>>> SurprisesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var surprises = new Dictionary<string, List<SweepSurprise>>(StringComparer.Ordinal);
        var held = new HashSet<(string, DateOnly)>();

        foreach (var query in new[]
        {
            "SELECT ticker, event_date, timing, surprise_percent FROM pulled_surprise WHERE surprise_percent IS NOT NULL;",
            "SELECT ticker, event_date, timing, surprise_percent FROM long_print WHERE read_from = 'provider' AND surprise_percent IS NOT NULL;",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var ticker = row.GetString(0);
                var day = Day(row.GetString(1));

                if (!held.Add((ticker, day)))
                {
                    continue;
                }

                if (!surprises.TryGetValue(ticker, out var list))
                {
                    surprises[ticker] = list = [];
                }

                list.Add(new SweepSurprise(day, row.GetString(2) == "after", row.GetDouble(3)));
            }
        }

        return surprises;
    }

    // The index-nights no bar was served for, and the names holding no bar or missing a session inside their own series.
    static (long IndexNights, long WithoutBar, int NamesWithout, int NamesMissing) Missing(IReadOnlyList<SweepName> names, DateOnly[] sessions)
    {
        long nights = 0;
        long without = 0;
        var namesWithout = 0;
        var namesMissing = 0;

        foreach (var name in names)
        {
            var days = name.Bars.Select(bar => bar.Session).ToHashSet();
            var memberNights = sessions.Where(name.MemberOn).ToArray();

            nights += memberNights.Length;
            without += memberNights.Count(session => !days.Contains(session));

            if (name.Bars.Length == 0)
            {
                namesWithout++;
            }
            else if (sessions.Any(session => session >= name.Bars[0].Session && session <= name.Bars[^1].Session && !days.Contains(session)))
            {
                namesMissing++;
            }
        }

        return (nights, without, namesWithout, namesMissing);
    }

    async Task<SqliteConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        return connection;
    }

    static async Task<SweepBar[]> BarsAsync(SqliteConnection connection, string table, string ticker, string through, CancellationToken cancellation)
    {
        var bars = new List<SweepBar>();

        await foreach (var row in RowsAsync(
            connection,
            table == "bar"
                ? "SELECT session_date, high, low, close, volume, open, raw_close FROM bar WHERE ticker = $ticker AND session_date <= $through ORDER BY session_date;"
                : "SELECT session_date, high, low, close, volume, open, raw_close FROM long_bar WHERE ticker = $ticker AND session_date <= $through ORDER BY session_date;",
            [("$ticker", ticker), ("$through", through)],
            cancellation))
        {
            bars.Add(new SweepBar(
                Day(row.GetString(0)),
                Money.FromStorage(row.GetString(1)),
                Money.FromStorage(row.GetString(2)),
                Money.FromStorage(row.GetString(3)),
                row.GetInt64(4),
                Money.FromStorage(row.GetString(5)),
                row.IsDBNull(6) ? 0m : Money.FromStorage(row.GetString(6))));
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

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
