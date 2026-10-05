using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Sweep;

// One earnings surprise as filed: the report date, whether it was reported after the close, and the surprise
// in per cent as the provider states it.
public sealed record SweepSurprise(DateOnly EventDate, bool After, double Percent);

// One member of the index on any session of the history, with its series, the spans the index held it for, the
// earnings dates on file for it, the sector it carries as filed today, and its surprises as filed. A survivor is a name
// the wider universe adds from today's S&P 400 and 600 members alone, read as a member on every session since nothing
// says when it joined, which flatters the wider universe.
public sealed record SweepName(
    string Ticker,
    SweepBar[] Bars,
    IReadOnlyList<(DateOnly? Joined, DateOnly? Left)> Spans,
    DateOnly[] Prints,
    string? Sector = null,
    IReadOnlyList<SweepSurprise>? SurprisesFiled = null,
    bool Survivor = false)
{
    public IReadOnlyList<SweepSurprise> Surprises => SurprisesFiled ?? [];

    public bool MemberOn(DateOnly session) =>
        Spans.Any(span => (span.Joined is not { } joined || joined <= session) && (span.Left is not { } left || left > session));
}

// The history the sweep replays, read once from the live store and held for the whole run: the sessions, every
// name the index held on one of them with its series, the index-nights no bar was served for, and where the
// surprises came from: the pulled surprises where a pull stored any with a percent, the calendar's own year
// otherwise, which holds today's members alone.
public sealed record SweepHistoryInputs(
    DateOnly Through,
    DateOnly[] Sessions,
    IReadOnlyList<SweepName> Names,
    long IndexNights,
    long IndexNightsWithoutABar,
    int NamesWithoutBars,
    int NamesMissingSomeSessions,
    string Fingerprint,
    string SurpriseSource = SweepHistory.NoSurprises,
    int Surprises = 0,
    int NamesWithoutASector = 0,
    IReadOnlyList<(string Ticker, DateOnly Session)>? LiveListedNameSessions = null,
    int Survivors = 0,
    int WiderMembers = 0)
{
    // The name-sessions the live list listed through the history's end, which the point-in-time check adds to
    // its sample.
    public IReadOnlyList<(string Ticker, DateOnly Session)> LiveListed => LiveListedNameSessions ?? [];
}

// One market series a pull stored, GSPC, VIX or a sector fund: its closes in session order through the history's
// end, and the pull that wrote them with the first and last session it holds.
public sealed record SweepMarketSeries(string Series, IReadOnlyList<(DateOnly Session, double Close)> Closes, string Pull)
{
    public DateOnly? First => Closes.Count > 0 ? Closes[0].Session : null;

    public DateOnly? Last => Closes.Count > 0 ? Closes[^1].Session : null;
}

// One row of a rebalance the night's book stored: the session, the sector, the company's place by value, the listing
// held and its value, its lead over the sector, and whether it passed the trend gate and was a leader bought.
public sealed record StoredHeavyweightRow(DateOnly Session, string Sector, int Place, string Ticker, decimal Value, double? Lead, bool Trend, bool Leader);

// What the heavyweights' sweep reads beside the history: each name's filer and sector as the companies pull filed them,
// its share counts with the days they were filed and the session whose basis they are stated on, and its splits; each
// sector fund's closes; and every reading the night's book stored, which the sweep's replay is held to.
public sealed record HeavyweightHistory(
    IReadOnlyDictionary<string, (string? Cik, string? Sector)> Companies,
    IReadOnlyDictionary<string, IReadOnlyList<FiledCount>> Counts,
    IReadOnlyDictionary<string, IReadOnlyList<FiledSplit>> Splits,
    IReadOnlyList<SweepMarketSeries> Funds,
    IReadOnlyList<StoredHeavyweightRow> Stored);

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
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.PulledMarketBar, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.PulledShares, Touch.Read),
            new StoreTouch(Store.PulledSplit, Touch.Read),
            new StoreTouch(Store.PulledRevenue, Touch.Read),
            new StoreTouch(Store.PulledMember, Touch.Read),
            new StoreTouch(Store.PulledIncome, Touch.Read),
            new StoreTouch(Store.PulledSnapshot, Touch.Read),
            new StoreTouch(Store.PulledHolding, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read),
        ],
        Feeds: []);

    const string Index = "GSPC";

    // Where the surprises the fifth condition reads came from.
    public const string PulledSurprises = "pulled";
    public const string CalendarSurprises = "calendar";
    public const string NoSurprises = "none";

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

    // The history the sweep replays: the S&P 500's own, and with the wider universe asked for, today's members of the S&P
    // 400 and 600 as a members pull stored them beside it, each read as a member on every session, a name the S&P 500
    // held at some point read over its spans and every session besides, and one it never held marked a survivor.
    // see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
    //
    // Given one wider index alone, the history is that index's members today as a members pull stored them, each a
    // survivor read as a member on every session, and none of the S&P 500's: the sweeps of each index's own rules read it.
    // see: Each index runs every family as rules of its own, ranked and benchmarked on that index's members alone
    public async Task<SweepHistoryInputs> ReadAsync(DateOnly through, Action<string>? progress = null, CancellationToken cancellation = default, bool wider = false, string? index = null)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var spans = new Dictionary<string, List<(DateOnly?, DateOnly?)>>(StringComparer.Ordinal);

        if (index is null)
        {
            await foreach (var row in RowsAsync(connection, "SELECT ticker, joined, \"left\" FROM membership WHERE index_code = $index ORDER BY ticker;", [("$index", Index)], cancellation))
            {
                var ticker = row.GetString(0);

                if (!spans.TryGetValue(ticker, out var held))
                {
                    spans[ticker] = held = [];
                }

                held.Add((row.IsDBNull(1) ? null : Date(row.GetString(1)), row.IsDBNull(2) ? null : Date(row.GetString(2))));
            }
        }

        var survivors = new HashSet<string>(StringComparer.Ordinal);
        var widerMembers = 0;

        if (index is not null && await TableHeldAsync(connection, "pulled_member", cancellation))
        {
            await foreach (var row in RowsAsync(connection, "SELECT DISTINCT ticker FROM pulled_member WHERE index_code = $index ORDER BY ticker;", [("$index", index)], cancellation))
            {
                var ticker = row.GetString(0);

                widerMembers++;
                spans[ticker] = [(null, null)];
                survivors.Add(ticker);
            }
        }

        if (wider && await TableHeldAsync(connection, "pulled_member", cancellation))
        {
            await foreach (var row in RowsAsync(connection, "SELECT DISTINCT ticker FROM pulled_member ORDER BY ticker;", [], cancellation))
            {
                var ticker = row.GetString(0);

                widerMembers++;

                if (!spans.TryGetValue(ticker, out var held))
                {
                    spans[ticker] = held = [];
                    survivors.Add(ticker);
                }

                held.Add((null, null));
            }
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

        // The sector as filed today, the newest observation of a ticker's span winning.
        var sectors = new Dictionary<string, string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT ticker, sector FROM membership WHERE index_code = $index AND sector IS NOT NULL ORDER BY observed_at;", [("$index", Index)], cancellation))
        {
            sectors[row.GetString(0)] = row.GetString(1);
        }

        var (surprises, surpriseSource) = await SurprisesAsync(connection, cancellation);

        // The name-sessions the live list listed, through the history's end.
        var liveListed = new List<(string Ticker, DateOnly Session)>();

        await foreach (var row in RowsAsync(connection, "SELECT ticker, session_date FROM gate_result WHERE passed = 1 AND session_date <= $through ORDER BY session_date, ticker;", [("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))], cancellation))
        {
            liveListed.Add((row.GetString(0), Date(row.GetString(1))));
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

            names.Add(new SweepName(
                ticker,
                series,
                held,
                [.. (prints.GetValueOrDefault(ticker) ?? [])],
                sectors.GetValueOrDefault(ticker),
                [.. (surprises.GetValueOrDefault(ticker) ?? []).OrderBy(surprise => surprise.EventDate)],
                survivors.Contains(ticker)));

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
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint.ToString())))[..16].ToLowerInvariant(),
            surpriseSource,
            surprises.Sum(pair => pair.Value.Count),
            names.Count(name => name.Sector is null && name.Spans.Count > 0 && !name.Survivor),
            liveListed,
            survivors.Count,
            widerMembers);
    }

    // A wider index's membership as it stood, from its fund's snapshots as the holdings pull stored them: each code a
    // snapshot matched, held from the first holding it to the last, with today's members as a members pull stored them
    // telling a name the newest snapshot holds that is still a member from one since let go. None where the store holds
    // no snapshot of the index, which leaves a sweep reading survivors alone and saying so.
    // see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
    public async Task<IReadOnlyDictionary<string, (DateOnly? Joined, DateOnly? Left)>> AsItStoodAsync(string indexCode, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        if (!await TableHeldAsync(connection, "pulled_snapshot", cancellation))
        {
            return new Dictionary<string, (DateOnly?, DateOnly?)>(StringComparer.Ordinal);
        }

        var periods = new List<DateOnly>();
        var held = new List<(DateOnly, string)>();
        var today = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT period FROM pulled_snapshot WHERE index_code = $index;", [("$index", indexCode)], cancellation))
        {
            periods.Add(Date(row.GetString(0)));
        }

        await foreach (var row in RowsAsync(connection, "SELECT period, ticker FROM pulled_holding WHERE index_code = $index AND ticker IS NOT NULL;", [("$index", indexCode)], cancellation))
        {
            held.Add((Date(row.GetString(0)), row.GetString(1)));
        }

        if (await TableHeldAsync(connection, "pulled_member", cancellation))
        {
            await foreach (var row in RowsAsync(connection, "SELECT ticker FROM pulled_member WHERE index_code = $index;", [("$index", indexCode)], cancellation))
            {
                today.Add(row.GetString(0));
            }
        }

        return HoldingSpans.From(periods, held, today);
    }

    // Each pulled company's quarters of income filed by the history's end, none where the store holds no table for them.
    // see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<EquityBrief.Core.Readings.FiledIncome>>> IncomeAsync(DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var income = new Dictionary<string, List<EquityBrief.Core.Readings.FiledIncome>>(StringComparer.Ordinal);

        if (!await TableHeldAsync(connection, "pulled_income", cancellation))
        {
            return new Dictionary<string, IReadOnlyList<EquityBrief.Core.Readings.FiledIncome>>(StringComparer.Ordinal);
        }

        static decimal? Figure(SqliteDataReader row, int at) => row.IsDBNull(at) ? null : Money.FromStorage(row.GetString(at));

        await foreach (var row in RowsAsync(
            connection,
            "SELECT ticker, period_end, filing_date, net_income, operating_income, interest_expense FROM pulled_income WHERE filing_date <= $through;",
            [("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))],
            cancellation))
        {
            if (!income.TryGetValue(row.GetString(0), out var held))
            {
                income[row.GetString(0)] = held = [];
            }

            held.Add(new EquityBrief.Core.Readings.FiledIncome(Date(row.GetString(1)), Date(row.GetString(2)), Figure(row, 3), Figure(row, 4), Figure(row, 5)));
        }

        return income.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EquityBrief.Core.Readings.FiledIncome>)pair.Value, StringComparer.Ordinal);
    }

    // Whether the store holds a table, so a store migrated before it read none rather than failing.
    static async Task<bool> TableHeldAsync(SqliteConnection connection, string table, CancellationToken cancellation)
    {
        await foreach (var _ in RowsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $table;", [("$table", table)], cancellation))
        {
            return true;
        }

        return false;
    }

    // The market series the ideas' run reads, each series' closes through the history's end with the pull that
    // wrote them, one statement a series; none where the store holds no market pull, or was migrated before the
    // table existed.
    // see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
    public async Task<IReadOnlyList<SweepMarketSeries>> MarketAsync(DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var held = false;

        await foreach (var row in RowsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'pulled_market_bar';", [], cancellation))
        {
            held = true;
        }

        if (!held)
        {
            return [];
        }

        return await SeriesAsync(connection, ["GSPC", "VIX"], through, cancellation);
    }

    // The named series' closes through the history's end, none for a series the store holds no session of: the fund an
    // index's heavyweights read their beta against.
    public async Task<IReadOnlyList<SweepMarketSeries>> SeriesOfAsync(IReadOnlyList<string> named, DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        return await SeriesAsync(connection, named, through, cancellation);
    }

    // Each named series' closes through the history's end with the pull that wrote them, one statement a series, and
    // none for a series the store holds no session of.
    static async Task<IReadOnlyList<SweepMarketSeries>> SeriesAsync(SqliteConnection connection, IReadOnlyList<string> named, DateOnly through, CancellationToken cancellation)
    {
        var found = new List<SweepMarketSeries>();
        var stamp = through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var series in named)
        {
            var closes = new List<(DateOnly, double)>();
            var pull = string.Empty;

            await foreach (var row in RowsAsync(
                connection,
                "SELECT session_date, close, pull FROM pulled_market_bar WHERE series = $series AND session_date <= $through ORDER BY session_date;",
                [("$series", series), ("$through", stamp)],
                cancellation))
            {
                closes.Add((Date(row.GetString(0)), Core.Prices.Statistic.FromPrice(Money.FromStorage(row.GetString(1)))));
                pull = row.GetString(2);
            }

            if (closes.Count > 0)
            {
                found.Add(new SweepMarketSeries(series, closes, pull));
            }
        }

        return found;
    }

    // What the heavyweights' sweep reads beside the history: each name's filer and sector as the companies pull filed
    // them, its counts filed by the history's end, every split, since a count is stated on the basis of the day the
    // pull asked for it, each sector fund's closes through the history's end, and the readings the night's book stored
    // through it; each none where the store holds no table for it.
    // see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
    // see: A company's value on a session is the newest share count filed before it times the session's close on the count's split basis
    public async Task<HeavyweightHistory> HeavyweightAsync(DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var tables = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('pulled_company', 'pulled_shares', 'pulled_split', 'pulled_market_bar', 'heavyweight_night');", [], cancellation))
        {
            tables.Add(row.GetString(0));
        }

        var stamp = through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var companies = new Dictionary<string, (string? Cik, string? Sector)>(StringComparer.Ordinal);
        var counts = new Dictionary<string, List<FiledCount>>(StringComparer.Ordinal);
        var splits = new Dictionary<string, List<FiledSplit>>(StringComparer.Ordinal);
        var stored = new List<StoredHeavyweightRow>();

        static List<T> Of<T>(Dictionary<string, List<T>> held, string ticker) =>
            held.TryGetValue(ticker, out var list) ? list : held[ticker] = [];

        if (tables.Contains("pulled_company"))
        {
            await foreach (var row in RowsAsync(connection, "SELECT ticker, cik, sector FROM pulled_company;", [], cancellation))
            {
                companies[row.GetString(0)] = (row.IsDBNull(1) ? null : row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2));
            }
        }

        if (tables.Contains("pulled_shares"))
        {
            await foreach (var row in RowsAsync(connection, "SELECT ticker, period_end, filing_date, shares, basis_session FROM pulled_shares WHERE filing_date <= $through;", [("$through", stamp)], cancellation))
            {
                Of(counts, row.GetString(0)).Add(new FiledCount(Date(row.GetString(1)), Date(row.GetString(2)), Money.FromStorage(row.GetString(3)), Date(row.GetString(4))));
            }
        }

        if (tables.Contains("pulled_split"))
        {
            await foreach (var row in RowsAsync(connection, "SELECT ticker, ex_date, new_shares, old_shares FROM pulled_split;", [], cancellation))
            {
                Of(splits, row.GetString(0)).Add(new FiledSplit(Date(row.GetString(1)), Money.FromStorage(row.GetString(2)), Money.FromStorage(row.GetString(3))));
            }
        }

        var funds = tables.Contains("pulled_market_bar")
            ? await SeriesAsync(connection, [.. GicsSectors.Funds.Values.Order(StringComparer.Ordinal)], through, cancellation)
            : [];

        if (tables.Contains("heavyweight_night"))
        {
            await foreach (var row in RowsAsync(
                connection,
                "SELECT session_date, sector, place, ticker, company_value, lead, trend, leader FROM heavyweight_night WHERE session_date <= $through ORDER BY session_date, sector, place;",
                [("$through", stamp)],
                cancellation))
            {
                stored.Add(new StoredHeavyweightRow(
                    Date(row.GetString(0)),
                    row.GetString(1),
                    row.GetInt32(2),
                    row.GetString(3),
                    Money.FromStorage(row.GetString(4)),
                    row.IsDBNull(5) ? null : row.GetDouble(5),
                    row.GetInt32(6) == 1,
                    row.GetInt32(7) == 1));
            }
        }

        return new HeavyweightHistory(
            companies,
            counts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FiledCount>)pair.Value, StringComparer.Ordinal),
            splits.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FiledSplit>)pair.Value, StringComparer.Ordinal),
            funds,
            stored);
    }

    // Each name's revenue as its filer's facts state it, every figure filed by the history's end under the revenue
    // concepts, found through the filer the companies pull names for the name; none for a name the pull names no
    // filer for, and none at all where the store holds no revenue pull.
    // see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FiledRevenue>>> RevenueAsync(DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var tables = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('pulled_company', 'pulled_revenue');", [], cancellation))
        {
            tables.Add(row.GetString(0));
        }

        var byTicker = new Dictionary<string, IReadOnlyList<FiledRevenue>>(StringComparer.Ordinal);

        if (tables.Count < 2)
        {
            return byTicker;
        }

        var byFiler = new Dictionary<string, List<FiledRevenue>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(
            connection,
            "SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM pulled_revenue WHERE filed <= $through;",
            [("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))],
            cancellation))
        {
            if (!byFiler.TryGetValue(row.GetString(0), out var facts))
            {
                byFiler[row.GetString(0)] = facts = [];
            }

            facts.Add(new FiledRevenue(row.GetString(1), Date(row.GetString(2)), Date(row.GetString(3)), Money.FromStorage(row.GetString(4)), Date(row.GetString(5)), row.GetString(6), row.GetString(7)));
        }

        await foreach (var row in RowsAsync(connection, "SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;", [], cancellation))
        {
            if (byFiler.TryGetValue(row.GetString(1), out var facts))
            {
                byTicker[row.GetString(0)] = facts;
            }
        }

        return byTicker;
    }

    // The surprises the fifth condition reads: every pulled surprise carrying a percent where a pull stored any,
    // which reaches the years before the store's own, and the calendar's own prints otherwise, whose detail
    // carries the provider's surprise for today's members over the year the calendar holds. The pulled table is
    // read only where the store holds it, since a store migrated before it exists holds none.
    static async Task<(Dictionary<string, List<SweepSurprise>> Surprises, string Source)> SurprisesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var surprises = new Dictionary<string, List<SweepSurprise>>(StringComparer.Ordinal);

        void Add(string ticker, DateOnly date, string timing, double percent)
        {
            if (!surprises.TryGetValue(ticker, out var held))
            {
                surprises[ticker] = held = [];
            }

            held.Add(new SweepSurprise(date, timing == "after", percent));
        }

        var pulledTableHeld = false;

        await foreach (var row in RowsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'pulled_surprise';", [], cancellation))
        {
            pulledTableHeld = true;
        }

        if (pulledTableHeld)
        {
            await foreach (var row in RowsAsync(connection, "SELECT ticker, event_date, timing, surprise_percent FROM pulled_surprise WHERE surprise_percent IS NOT NULL;", [], cancellation))
            {
                Add(row.GetString(0), Date(row.GetString(1)), row.GetString(2), row.GetDouble(3));
            }
        }

        if (surprises.Count > 0)
        {
            return (surprises, PulledSurprises);
        }

        await foreach (var row in RowsAsync(connection, "SELECT ticker, event_date, timing, detail FROM calendar WHERE kind = 'earnings';", [], cancellation))
        {
            if (SurpriseIn(row.GetString(3)) is { } percent)
            {
                Add(row.GetString(0), Date(row.GetString(1)), row.GetString(2), percent);
            }
        }

        return (surprises, surprises.Count > 0 ? CalendarSurprises : NoSurprises);
    }

    // The provider's surprise as the calendar's detail carries it, a number kept as text, and none where the
    // detail carries no number there.
    public static double? SurpriseIn(string detail)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(detail);

            if (!document.RootElement.TryGetProperty("surprise", out var value) || value.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }

            return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ? percent : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
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
                    Open = Core.Prices.PriceForm.Round(bar.Open * factor, Core.Prices.Statistic.Places),
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
            $"SELECT session_date, high, low, close, volume, open, raw_close FROM {table} WHERE ticker = $ticker AND session_date <= $through ORDER BY session_date;",
            [("$ticker", ticker), ("$through", through)],
            cancellation))
        {
            bars.Add(new SweepBar(
                Date(row.GetString(0)),
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

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
