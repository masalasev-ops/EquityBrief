using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Charts;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// One code the long history asks for: its code, the names the funds and the provider file it under, and the filer's number the
// provider files it under where the pull holds one.
public sealed record LongCode(string Ticker, IReadOnlyList<string> Names, string? Cik);

// The filer a code was read to, the names it files under, whether one of them is the code's company's, and how it was found.
public sealed record LongFiler(string Ticker, string? Cik, IReadOnlyList<string> Entity, bool IsTheCompany, string By);

// A departed code's end: the session its history ends on where its filings give one, what was read, and whether a mark was written
// for an answer running on past it.
public sealed record LongEnd(string Ticker, DateOnly? Session, DateOnly LastClose, DateOnly? LastTraded, string Says, string? Accession, bool Marked);

// The overlap of a code's prices with the pull's at the store's scale: the sessions both hold and those apart.
public sealed record LongOverlap(string Ticker, int Shared, int ApartByACent, int ApartByOnePerCent, int TradedApart, DateOnly? FirstApart, decimal Largest);

// What a write read and wrote, or why it stopped.
public sealed record LongPriceOutcome(
    int Codes,
    int Priced,
    IReadOnlyList<string> Nothing,
    long Bars,
    int MarketBars,
    int Prints,
    int Splits,
    int Dividends,
    int Filings,
    IReadOnlyList<LongSpanCoverage> Coverage,
    IReadOnlyList<LongSeriesCount> Counts,
    DateOnly Cutover,
    IReadOnlyList<LongFiler> Refused,
    IReadOnlyList<LongEnd> Ends,
    IReadOnlyList<LongOverlap> Overlaps,
    IReadOnlyList<string> Stops,
    bool Written,
    IReadOnlyList<string> Unreadable);

// The long history's prices. Every code the long history's membership matched and every code the pull holds is asked once for its
// prices from 2004-01-02, its splits and its dividends, one request each; the earnings calendar once a month before the pull's
// first; the market series once each; a company record for each code the pull holds no filer's number for; and each code's
// filer's filing list with its removals' documents from the SEC. Each answer is kept as sent in a folder before anything reads
// it, an answer already kept asked nothing again, and the write reads the folder alone: it counts the provider's S&P 500 from 2012
// against IVV's holdings and moves the cutover where they stand apart, holds each holding to its span's sessions, compares the
// overlap with the pull at the store's scale, reads each departed code's end from its filings, and writes the six tables whole
// with the ends' marks and its row on the run log, or nothing where a stop stands. No bar, pulled row or night moves.
// see: The long history's prices are asked once a code from 2004-01-02 and kept apart from the bars, the pull's tables and every night
// see: Every pull states its provider calls before it runs and stops for the operator at the day's cap
public sealed class LongPricePull : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongBar, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongMarketBar, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongPrint, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongSplit, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongDividend, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongFiling, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongSnapshot, Touch.Read),
            new StoreTouch(Store.LongHolding, Touch.Read),
            new StoreTouch(Store.LongSpan, Touch.Read),
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.HistoricalPrice, Feed.SplitsAndDividends, Feed.EarningsCalendar, Feed.CompanyFinancials, Feed.FilingsArchive]);

    public const string Stage = "long-prices";

    // The operator's word that the data check's counts over the long history have been read, which the long test and the searches
    // wait on.
    public const string CountsStage = "long-counts-read";

    public const string CodesFile = "codes.json";
    public const string PricesFolder = "prices";
    public const string SplitsFolder = "splits";
    public const string DividendsFolder = "dividends";
    public const string PrintsFolder = "prints";
    public const string MarketFolder = "market";
    public const string CompaniesFolder = "companies";
    public const string FilersFolder = "filers";
    public const string SearchFolder = "search";
    public const string FilersFile = "filers.json";
    public const string RefusedFile = "refused.txt";

    // The series asked beside the codes: the index, the VIX and the funds the pull holds, HYG from its own start in 2007.
    public static IReadOnlyList<string> MarketSeries { get; } = ["GSPC", "VIX", "SPY", "IJH", "IJR", "HYG", .. EquityBrief.Worker.Bars.HistoryPull.SectorFunds];

    const string SelectHoldings = "SELECT index_code, period, ticker, name, value_usd FROM long_holding;";
    const string SelectSpans = "SELECT index_code, ticker, joined, left, read_from FROM long_span;";
    const string SelectMembers = @"SELECT ticker, name, joined, ""left"" FROM membership WHERE index_code = 'GSPC';";
    const string SelectNames = "SELECT ticker, name FROM membership WHERE name IS NOT NULL;";
    const string SelectPulled = "SELECT DISTINCT ticker FROM pulled_bar;";
    const string SelectCiks = "SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;";
    const string SelectFundCounts = "SELECT period, holdings FROM long_snapshot WHERE index_code = 'GSPC';";
    const string SelectPulledCloses = @"
        SELECT session_date, close, raw_close FROM pulled_bar AS row
        WHERE ticker = $ticker AND pull = (SELECT MAX(pull) FROM pulled_bar AS newer WHERE newer.ticker = row.ticker AND newer.session_date = row.session_date)
        ORDER BY session_date;";
    const string SelectFirstStored = "SELECT session_date, close FROM bar WHERE ticker = $ticker ORDER BY session_date LIMIT 1;";

    const string Clear = "DELETE FROM long_bar; DELETE FROM long_market_bar; DELETE FROM long_print; DELETE FROM long_split; DELETE FROM long_dividend; DELETE FROM long_filing;";

    const string InsertBar = @"
        INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id)
        VALUES ($ticker, $session_date, $open, $high, $low, $close, $raw_close, $volume, $run_id)
        ON CONFLICT (ticker, session_date) DO NOTHING;";

    const string InsertMarketBar = @"
        INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id)
        VALUES ($series, $session_date, $open, $high, $low, $close, $run_id)
        ON CONFLICT (series, session_date) DO NOTHING;";

    const string InsertPrint = @"
        INSERT INTO long_print (ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, read_from, run_id)
        VALUES ($ticker, $event_date, $timing, $eps_actual, $eps_estimate, $surprise_percent, $read_from, $run_id)
        ON CONFLICT (ticker, event_date, read_from) DO NOTHING;";

    const string InsertSplit = @"
        INSERT INTO long_split (ticker, ex_date, new_shares, old_shares, run_id)
        VALUES ($ticker, $ex_date, $new_shares, $old_shares, $run_id)
        ON CONFLICT (ticker, ex_date) DO NOTHING;";

    const string InsertDividend = @"
        INSERT INTO long_dividend (ticker, ex_date, amount, raw_amount, run_id)
        VALUES ($ticker, $ex_date, $amount, $raw_amount, $run_id)
        ON CONFLICT (ticker, ex_date) DO NOTHING;";

    const string InsertFiling = @"
        INSERT INTO long_filing (ticker, cik, accession, form, items, reported, filed, run_id)
        VALUES ($ticker, $cik, $accession, $form, $items, $reported, $filed, $run_id)
        ON CONFLICT (ticker, accession) DO NOTHING;";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    const string SelectCountsWord = "SELECT MAX(started_at) FROM run_log WHERE stage = 'long-counts-read';";
    const string SelectLongCheck = "SELECT MAX(ended_at) FROM run_log WHERE stage = 'data-check-long';";

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly? Day(object value) =>
        value is string text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    static string Asked(string code) => code + "." + LongDayFiles.Exchange;

    static bool TryMoney(string text, out decimal value)
    {
        try
        {
            value = Money.FromStorage(text);

            return true;
        }
        catch (FormatException)
        {
            value = 0m;

            return false;
        }
    }

    static string FileOf(string folder, string kind, string name) => Path.Combine(folder, kind, name + ".json");

    // Every code to ask: each the membership matched, each its spans hold and each the pull holds, with the names the funds and the
    // provider file it under and the filer's number the pull holds.
    public static async Task<IReadOnlyList<LongCode>> CodesAsync(string databaseFile, CancellationToken cancellation = default)
    {
        var builder = StoreConnection.Builder(databaseFile);
        builder.Mode = SqliteOpenMode.ReadOnly;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);

        var names = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var ciks = new Dictionary<string, string>(StringComparer.Ordinal);

        void Name(string ticker, string? name)
        {
            if (!names.TryGetValue(ticker, out var held))
            {
                names[ticker] = held = new SortedSet<string>(StringComparer.Ordinal);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                held.Add(name);
            }
        }

        await ReadAsync(connection, SelectHoldings, reader =>
        {
            if (!reader.IsDBNull(2))
            {
                Name(reader.GetString(2), reader.GetString(3));
            }
        }, cancellation);
        await ReadAsync(connection, SelectSpans, reader => Name(reader.GetString(1), null), cancellation);
        await ReadAsync(connection, SelectPulled, reader => Name(reader.GetString(0), null), cancellation);
        await ReadAsync(connection, SelectNames, reader =>
        {
            if (names.ContainsKey(reader.GetString(0)))
            {
                Name(reader.GetString(0), reader.GetString(1));
            }
        }, cancellation);
        await ReadAsync(connection, SelectCiks, reader => ciks[reader.GetString(0)] = reader.GetString(1), cancellation);

        return [.. names.Select(pair => new LongCode(pair.Key, [.. pair.Value], ciks.GetValueOrDefault(pair.Key)))];
    }

    // The statement before the first request, from the codes and what the folder already keeps.
    public static string Statement(string folder, IReadOnlyList<LongCode> codes)
    {
        var codesToAsk = codes.Count(code => !File.Exists(FileOf(folder, DividendsFolder, code.Ticker)));
        var companies = codes.Count(code => code.Cik is null && !File.Exists(FileOf(folder, CompaniesFolder, code.Ticker)));
        var months = LongPrices.Months().Count(month => !File.Exists(FileOf(folder, PrintsFolder, Month(month.First))));
        var series = MarketSeries.Count(one => !File.Exists(FileOf(folder, MarketFolder, one)));

        return LongPrices.Stated(codesToAsk, companies, months, series);
    }

    static string Month(DateOnly first) => first.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // Asks the provider for everything not yet kept, each answer written as sent before the next is asked; the requests made. A
    // statement past the day's stop is refused before any request unless the operator said to go past it.
    public static async Task<int> CaptureAsync(string folder, IReadOnlyList<LongCode> codes, ILongHistoryFeed feed, DateOnly through, bool pastTheStop, Action<string> progress, CancellationToken cancellation = default)
    {
        var codesToAsk = codes.Count(code => !File.Exists(FileOf(folder, DividendsFolder, code.Ticker)));
        var companies = codes.Count(code => code.Cik is null && !File.Exists(FileOf(folder, CompaniesFolder, code.Ticker)));
        var monthsToAsk = LongPrices.Months().Count(month => !File.Exists(FileOf(folder, PrintsFolder, Month(month.First))));
        var seriesToAsk = MarketSeries.Count(one => !File.Exists(FileOf(folder, MarketFolder, one)));
        var weighted = LongPrices.Weighted(codesToAsk, companies, monthsToAsk, seriesToAsk);
        var stated = LongPrices.Stated(codesToAsk, companies, monthsToAsk, seriesToAsk);

        progress(ProviderStop.Stated(stated, weighted));
        ProviderStop.Hold(stated, weighted, pastTheStop);

        foreach (var kind in new[] { PricesFolder, SplitsFolder, DividendsFolder, PrintsFolder, MarketFolder, CompaniesFolder })
        {
            Directory.CreateDirectory(Path.Combine(folder, kind));
        }

        await File.WriteAllTextAsync(Path.Combine(folder, CodesFile), JsonSerializer.Serialize(codes), cancellation);

        var before = feed.Requests;

        async Task KeepAsync(string file, Func<Task<string>> ask)
        {
            if (File.Exists(file))
            {
                return;
            }

            string answer;

            // A refusal is not kept, so the next capture asks again; it is named in the folder's list of refusals.
            try
            {
                answer = await ask();
            }
            catch (ProviderRefusal refusal)
            {
                await File.AppendAllTextAsync(Path.Combine(folder, RefusedFile), Path.GetRelativePath(folder, file) + " " + refusal.Message + Environment.NewLine, cancellation);

                return;
            }

            await File.WriteAllTextAsync(file + ".part", answer, cancellation);
            File.Move(file + ".part", file, overwrite: true);
        }

        foreach (var series in MarketSeries)
        {
            await KeepAsync(FileOf(folder, MarketFolder, series), () => feed.PricesAsync(series + EodhdMarketSeriesFeed.SuffixOf(series), LongPrices.From, through, cancellation));
        }

        foreach (var (first, last) in LongPrices.Months())
        {
            await KeepAsync(FileOf(folder, PrintsFolder, Month(first)), () => feed.CalendarAsync(first, last, cancellation));
        }

        var asked = 0;

        foreach (var code in codes)
        {
            // The dividends are asked last, so a code whose dividends are kept has all three kept.
            await KeepAsync(FileOf(folder, PricesFolder, code.Ticker), () => feed.PricesAsync(Asked(code.Ticker), LongPrices.From, through, cancellation));
            await KeepAsync(FileOf(folder, SplitsFolder, code.Ticker), () => feed.SplitsAsync(Asked(code.Ticker), LongPrices.From, cancellation));
            await KeepAsync(FileOf(folder, DividendsFolder, code.Ticker), () => feed.DividendsAsync(Asked(code.Ticker), LongPrices.From, cancellation));

            if (code.Cik is null)
            {
                await KeepAsync(FileOf(folder, CompaniesFolder, code.Ticker), () => feed.CompanyAsync(Asked(code.Ticker), cancellation));
            }

            if (++asked % 100 == 0)
            {
                progress(FormattableString.Invariant($"{asked:N0} of {codes.Count:N0} code(s) kept, {feed.Requests - before:N0} request(s)"));
            }
        }

        return feed.Requests - before;
    }

    // The filer's number a company record states, none where it states none.
    public static string? CikOf(string? company)
    {
        if (company is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(company);

            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("CIK", out var cik) && cik.ValueKind is JsonValueKind.String or JsonValueKind.Number
                && cik.ToString() is { Length: > 0 } text && text.Any(char.IsDigit) ? text.TrimStart('0') : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Reads each code's filer from the SEC: the provider's number first, the pull's or the company record's, else the archive's
    // company search by the names the funds filed it under; each filer's filing list, its older pages and its removals' documents
    // kept under its number, a filer kept asked nothing again; the filer kept only where one of its names is the code's company's.
    public static async Task<IReadOnlyList<LongFiler>> FilersAsync(string folder, IReadOnlyList<LongCode> codes, IFilerFilingsFeed filings, ITrustScheduleFeed search, Action<string> progress, CancellationToken cancellation = default)
    {
        var filersFolder = Path.Combine(folder, FilersFolder);
        var searchFolder = Path.Combine(folder, SearchFolder);

        Directory.CreateDirectory(filersFolder);
        Directory.CreateDirectory(searchFolder);

        var reader = new HistoryEndFilings(filings);
        var filers = new List<LongFiler>();
        var done = 0;

        foreach (var code in codes)
        {
            var companyFile = FileOf(folder, CompaniesFolder, code.Ticker);
            var cik = code.Cik?.TrimStart('0') ?? CikOf(File.Exists(companyFile) ? await File.ReadAllTextAsync(companyFile, cancellation) : null);
            var by = code.Cik is not null ? "the pull's filer" : cik is not null ? "the provider's company record" : "the archive's company search";

            if (cik is null)
            {
                foreach (var name in code.Names)
                {
                    try
                    {
                        if (await LongMembers.FilerAsync(searchFolder, search, name, cancellation) is { } found)
                        {
                            cik = found.Cik.TrimStart('0');

                            break;
                        }
                    }
                    catch (HttpRequestException)
                    {
                    }
                }
            }

            if (cik is null)
            {
                filers.Add(new LongFiler(code.Ticker, null, [], false, "no filer found"));

                continue;
            }

            var kept = Path.Combine(filersFolder, cik);

            if (!File.Exists(Path.Combine(kept, HistoryEndFilings.IndexFile)))
            {
                try
                {
                    await reader.CaptureAsync(cik, kept, cancellation);
                }
                catch (HttpRequestException failure)
                {
                    filers.Add(new LongFiler(code.Ticker, cik, [], false, "the archive refused its filing list: " + failure.Message));

                    continue;
                }
            }

            var entity = LongMembers.NamesOf(await File.ReadAllTextAsync(Path.Combine(kept, HistoryEndFilings.IndexFile), cancellation));

            filers.Add(new LongFiler(code.Ticker, cik, entity, code.Names.Count == 0 || LongPrices.IsTheCompany(entity, code.Names), by));

            if (++done % 100 == 0)
            {
                progress(FormattableString.Invariant($"{done:N0} filer(s) read, {filings.Requests + search.Requests:N0} request(s) to the SEC"));
            }
        }

        await File.WriteAllTextAsync(Path.Combine(folder, FilersFile), JsonSerializer.Serialize(filers), cancellation);

        return filers;
    }

    // A kept answer of prices as sessions, none where the provider refused it or sent nothing readable; the rows no session is read
    // from skipped, as the night's bulk reader skips them. A row the reader refuses, a session that could not have traded among them,
    // is skipped too and named in the list given, so one bad old row leaves a session missing, which the data check reads, and
    // stops no write.
    // see: A row of the long prices the bar reader refuses is skipped and listed, and stops no write
    public static IReadOnlyList<ProviderBar> BarsOf(string answer, string ticker, ICollection<string>? unreadable = null)
    {
        using var document = JsonDocument.Parse(answer);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bars = new List<ProviderBar>();

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            try
            {
                if (ProviderBarReader.TryRead(entry, ticker, out var bar) && bar is not null)
                {
                    bars.Add(bar);
                }
            }
            catch (FormatException refused)
            {
                unreadable?.Add(refused.Message);
            }
        }

        return [.. bars.DistinctBy(bar => bar.SessionDate).OrderBy(bar => bar.SessionDate)];
    }

    static IReadOnlyList<T> ArrayOr<T>(string answer, Func<string, IReadOnlyList<T>> parse)
    {
        try
        {
            using var document = JsonDocument.Parse(answer);

            return document.RootElement.ValueKind == JsonValueKind.Array ? parse(answer) : [];
        }
        catch (Exception failure) when (failure is JsonException or FormatException)
        {
            return [];
        }
    }

    // Writes the folder's answers whole, or nothing where a stop stands; asks nothing of anyone.
    public static async Task<LongPriceOutcome> WriteAsync(string databaseFile, string folder, string runId, IClock clock, bool write, Action<string> progress, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var codes = JsonSerializer.Deserialize<List<LongCode>>(await File.ReadAllTextAsync(Path.Combine(folder, CodesFile), cancellation)) ?? [];
        var filers = File.Exists(Path.Combine(folder, FilersFile))
            ? JsonSerializer.Deserialize<List<LongFiler>>(await File.ReadAllTextAsync(Path.Combine(folder, FilersFile), cancellation)) ?? []
            : [];
        var filerOf = filers.ToDictionary(filer => filer.Ticker, StringComparer.Ordinal);

        var opened = StoreConnection.Builder(databaseFile);

        if (!write)
        {
            opened.Mode = SqliteOpenMode.ReadOnly;
        }

        await using var connection = new SqliteConnection(opened.ConnectionString);
        await connection.OpenAsync(cancellation);

        // The index's sessions are the calendar every span and end is read on.
        var index = BarsOf(await File.ReadAllTextAsync(FileOf(folder, MarketFolder, "GSPC"), cancellation), "GSPC");
        var calendar = new PulledCalendar(index.ToDictionary(bar => bar.SessionDate, _ => 1), [(index[0].SessionDate, index[^1].SessionDate)]);
        DateOnly[] sessions = [.. index.Select(bar => bar.SessionDate)];
        var newest = sessions[^1];

        // The provider's S&P 500 from 2012 against IVV's holdings, its codes' closes read first.
        var members = new List<ProviderSpan>();
        var fund = new Dictionary<DateOnly, int>();

        await ReadAsync(connection, SelectMembers, reader => members.Add(new ProviderSpan(reader.GetString(0), Day(reader.GetValue(2)), Day(reader.GetValue(3)), reader.IsDBNull(1) ? null : reader.GetString(1))), cancellation);
        await ReadAsync(connection, SelectFundCounts, reader =>
        {
            if (Day(reader.GetString(0)) is { } period)
            {
                fund[period] = reader.GetInt32(1);
            }
        }, cancellation);

        var countedCloses = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.Ordinal);

        IReadOnlyDictionary<DateOnly, decimal> CountedCloses(string code)
        {
            if (!countedCloses.TryGetValue(code, out var closes))
            {
                var file = FileOf(folder, PricesFolder, code);

                countedCloses[code] = closes = File.Exists(file)
                    ? BarsOf(File.ReadAllText(file), code).Where(bar => bar.SessionDate >= LongSpans.ProviderFrom.AddDays(-92) && bar.SessionDate <= LongPrices.FallbackFrom).ToDictionary(bar => bar.SessionDate, bar => bar.Close)
                    : new Dictionary<DateOnly, decimal>();
            }

            return closes;
        }

        var counts = LongPrices.Counted(members, fund.Where(pair => pair.Key < LongPrices.FallbackFrom).ToDictionary(), CountedCloses);
        var cutover = LongPrices.Cutover(counts);

        countedCloses.Clear();
        progress(FormattableString.Invariant($"the S&P 500 from 2012: {counts.Count} quarter end(s), the most apart {counts.Select(count => Math.Abs(count.Apart)).DefaultIfEmpty().Max()}, read from {cutover:yyyy-MM-dd}"));

        await using var transaction = write ? await connection.BeginTransactionAsync(cancellation) : null;
        var spans = await LongMembers.SpansAsync(connection, (SqliteTransaction?)transaction, cutover, cancellation);

        var holdings = new List<(string Index, DateOnly Period, string? Ticker, decimal Value)>();

        await ReadAsync(connection, SelectHoldings, reader =>
        {
            if (Day(reader.GetString(1)) is { } period)
            {
                holdings.Add((reader.GetString(0), period, reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(4) ? 0m : decimal.Parse(reader.GetString(4), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)));
            }
        }, cancellation, (SqliteTransaction?)transaction);

        async Task<int> ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction?)transaction;
            command.CommandText = sql;

            return await command.ExecuteNonQueryAsync(cancellation);
        }

        if (write)
        {
            await ExecuteAsync(Clear);
        }

        await using var insertBar = connection.CreateCommand();
        insertBar.Transaction = (SqliteTransaction?)transaction;
        insertBar.CommandText = InsertBar;

        var barParameters = new[] { "$ticker", "$session_date", "$open", "$high", "$low", "$close", "$raw_close", "$volume", "$run_id" }.Select(name => insertBar.Parameters.Add(name, name == "$volume" ? SqliteType.Integer : SqliteType.Text)).ToArray();

        var heldBy = new Dictionary<string, (HashSet<DateOnly> Held, DateOnly? Last)>(StringComparer.Ordinal);
        var nothing = new List<string>();
        var unreadable = new List<string>();
        var ends = new List<LongEnd>();
        var overlaps = new List<LongOverlap>();
        var marks = new List<(string Ticker, DateOnly Session, string Says)>();
        var (bars, priced, done) = (0L, 0, 0);
        var spanCodes = spans.Select(span => span.Ticker).Concat(holdings.Select(holding => holding.Ticker).OfType<string>()).ToHashSet(StringComparer.Ordinal);

        foreach (var code in codes)
        {
            var file = FileOf(folder, PricesFolder, code.Ticker);
            var series = File.Exists(file) ? BarsOf(await File.ReadAllTextAsync(file, cancellation), code.Ticker, unreadable) : [];

            if (++done % 250 == 0)
            {
                progress(FormattableString.Invariant($"{done:N0} of {codes.Count:N0} code(s) read, {bars:N0} session(s)"));
            }

            if (series.Count == 0)
            {
                var failed = filerOf.TryGetValue(code.Ticker, out var filer) && filer.Cik is { } cik && Directory.Exists(Path.Combine(folder, FilersFolder, cik))
                    && FilerFilings.Read(Path.Combine(folder, FilersFolder, cik)).FirstOrDefault(one => one.Form == "8-K" && one.Items.Contains("1.03")) is { } bankruptcy
                    ? FormattableString.Invariant($", failed: its 8-K of {bankruptcy.Filed:yyyy-MM-dd} ({bankruptcy.Accession}) reports a bankruptcy")
                    : string.Empty;

                nothing.Add(code.Ticker + (code.Names.Count > 0 ? " (" + string.Join("; ", code.Names) + ")" : string.Empty) + failed);

                continue;
            }

            priced++;

            if (write)
            {
                foreach (var bar in series)
                {
                    barParameters[0].Value = code.Ticker;
                    barParameters[1].Value = Text(bar.SessionDate);
                    barParameters[2].Value = Money.ToStorage(bar.Open);
                    barParameters[3].Value = Money.ToStorage(bar.High);
                    barParameters[4].Value = Money.ToStorage(bar.Low);
                    barParameters[5].Value = Money.ToStorage(bar.Close);
                    barParameters[6].Value = Money.ToStorage(bar.RawClose);
                    barParameters[7].Value = bar.Volume;
                    barParameters[8].Value = runId;
                    bars += await insertBar.ExecuteNonQueryAsync(cancellation);
                }
            }
            else
            {
                bars += series.Count;
            }

            var lastClose = series[^1].SessionDate;
            var lastTraded = series.LastOrDefault(bar => bar.Volume > 0)?.SessionDate;

            if (spanCodes.Contains(code.Ticker))
            {
                heldBy[code.Ticker] = ([.. series.Select(bar => bar.SessionDate)], lastTraded ?? lastClose);
            }

            overlaps.AddRange(await OverlapAsync(connection, (SqliteTransaction?)transaction, code.Ticker, series, cancellation));

            if (LongPrices.Departed(code.Ticker, lastClose, newest))
            {
                var end = EndOf(folder, code, filerOf.GetValueOrDefault(code.Ticker), series, calendar);

                ends.Add(end);

                if (end.Marked && end.Session is { } session)
                {
                    marks.Add((code.Ticker, session, end.Says));
                }
            }
        }

        var coverage = LongPrices.Coverage(holdings, spans, sessions, ticker => heldBy.TryGetValue(ticker, out var one) ? one.Held : new HashSet<DateOnly>(), ticker => heldBy.TryGetValue(ticker, out var one) ? one.Last : null);
        var stops = LongPrices.Stops(coverage);

        var (marketBars, prints, splits, dividends, filingRows) = (0, 0, 0, 0, 0);

        if (write && stops.Count == 0)
        {
            marketBars = await WriteMarketAsync(connection, (SqliteTransaction)transaction!, folder, runId, unreadable, cancellation);
            prints = await WritePrintsAsync(connection, (SqliteTransaction)transaction!, folder, codes, runId, cancellation);
            (splits, dividends) = await WriteActionsAsync(connection, (SqliteTransaction)transaction!, folder, codes, runId, cancellation);
            filingRows = await WriteFilingsAsync(connection, (SqliteTransaction)transaction!, folder, filers, runId, cancellation);

            if (cutover != LongSpans.ProviderFrom)
            {
                await LongMembers.RewriteSpansAsync(connection, (SqliteTransaction)transaction!, spans, runId, cancellation);
            }

            var marked = await SessionMarks.MarkEndsAsync(connection, (SqliteTransaction)transaction!, marks, startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), runId, cancellation);
            var refusedCount = filers.Count(filer => filer.Cik is not null && !filer.IsTheCompany);

            await using var run = connection.CreateCommand();
            run.Transaction = (SqliteTransaction)transaction!;
            run.CommandText = AppendRun;
            run.Parameters.AddWithValue("$run_id", runId);
            run.Parameters.AddWithValue("$stage", Stage);
            run.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$rows", bars + marketBars + prints + splits + dividends + filingRows + marked);
            run.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new
            {
                codes = codes.Count,
                priced,
                bars,
                marketBars,
                prints,
                splits,
                dividends,
                filings = filingRows,
                marked,
                cutover = Text(cutover),
                apartAtMost = counts.Select(count => Math.Abs(count.Apart)).DefaultIfEmpty().Max(),
                lowest = coverage.OrderBy(quarter => quarter.HoldingsShare).Take(3).Select(quarter => FormattableString.Invariant($"{quarter.Index} {quarter.Period:yyyy-MM-dd} {quarter.HoldingsShare:P1} of holdings, {quarter.ValueShare:P1} of value")).ToArray(),
                nothing,
                unreadable,
                refused = refusedCount,
                ends = ends.Count(end => end.Session is not null),
                unsettled = ends.Where(end => end.Session is null).Select(end => FormattableString.Invariant($"{end.Ticker} {end.LastTraded ?? end.LastClose:yyyy-MM-dd}")).ToArray(),
                overlapsApart = overlaps.Count(overlap => overlap.ApartByOnePerCent > 0),
            }));
            await run.ExecuteNonQueryAsync(cancellation);
            await transaction!.CommitAsync(cancellation);
        }
        else if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellation);
        }

        return new LongPriceOutcome(
            codes.Count,
            priced,
            nothing,
            bars,
            marketBars,
            prints,
            splits,
            dividends,
            filingRows,
            coverage,
            counts,
            cutover,
            [.. filers.Where(filer => filer.Cik is not null && !filer.IsTheCompany)],
            ends,
            overlaps,
            stops,
            write && stops.Count == 0,
            unreadable);
    }

    // A departed code's end: the filer's filings read by the core's rule where its filer is the code's company, the session before
    // the day the filing names, and a mark where the code's prices run on past it; otherwise its last session with volume, listed.
    // see: A departed company's end no filing settles reads its last session with volume and goes to the operator on one list
    public static LongEnd EndOf(string folder, LongCode code, LongFiler? filer, IReadOnlyList<ProviderBar> series, PulledCalendar calendar)
    {
        var lastClose = series[^1].SessionDate;
        var lastTraded = series.LastOrDefault(bar => bar.Volume > 0)?.SessionDate;

        if (filer is not { IsTheCompany: true, Cik: { } cik } || !File.Exists(Path.Combine(folder, FilersFolder, cik, HistoryEndFilings.IndexFile)))
        {
            return new LongEnd(code.Ticker, null, lastClose, lastTraded, filer is null || filer.Cik is null ? "No filer was found for it, so it reads to its last session with volume and goes to the operator." : "Its filer's names are not its company's, so it reads to its last session with volume and goes to the operator.", null, false);
        }

        var read = HistoryEnd.Of(FilerFilings.Read(Path.Combine(folder, FilersFolder, cik)));

        if (read.EndsBefore is null || HistoryEnd.LastSession(read, calendar) is not { } session)
        {
            return new LongEnd(code.Ticker, null, lastClose, lastTraded, read.Says + " It reads to its last session with volume.", null, false);
        }

        return new LongEnd(code.Ticker, session, lastClose, lastTraded, read.Says, read.ReadFrom?.Accession, lastClose > session);
    }

    // The overlap of a code's prices with the pull's, each side brought to the store's scale by its close on the store's first
    // session, or the long side to the pull by the median of their ratios where the store holds none.
    static async Task<IReadOnlyList<LongOverlap>> OverlapAsync(SqliteConnection connection, SqliteTransaction? transaction, string ticker, IReadOnlyList<ProviderBar> series, CancellationToken cancellation)
    {
        var pulled = new SortedDictionary<DateOnly, (decimal Close, decimal? Raw)>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SelectPulledCloses;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (Day(reader.GetString(0)) is { } day && TryMoney(reader.GetString(1), out var close))
                {
                    pulled[day] = (close, !reader.IsDBNull(2) && TryMoney(reader.GetString(2), out var raw) ? raw : null);
                }
            }
        }

        if (pulled.Count == 0)
        {
            return [];
        }

        (DateOnly Day, decimal Close)? stored = null;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SelectFirstStored;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation) && Day(reader.GetString(0)) is { } day && TryMoney(reader.GetString(1), out var close))
            {
                stored = (day, close);
            }
        }

        return [Overlap(ticker, series.ToDictionary(bar => bar.SessionDate, bar => (bar.Close, new decimal?(bar.RawClose))), pulled, stored)];
    }

    // The comparison itself, over the sessions both sides hold.
    public static LongOverlap Overlap(string ticker, IReadOnlyDictionary<DateOnly, (decimal Close, decimal? Raw)> longer, IReadOnlyDictionary<DateOnly, (decimal Close, decimal? Raw)> pulled, (DateOnly Day, decimal Close)? stored)
    {
        DateOnly[] shared = [.. longer.Keys.Where(pulled.ContainsKey).Order()];

        if (shared.Length == 0)
        {
            return new LongOverlap(ticker, 0, 0, 0, 0, null, 0m);
        }

        decimal longScale, pulledScale;

        if (stored is { } first && longer.TryGetValue(first.Day, out var longFirst) && pulled.TryGetValue(first.Day, out var pulledFirst) && longFirst.Close > 0m && pulledFirst.Close > 0m)
        {
            (longScale, pulledScale) = (first.Close / longFirst.Close, first.Close / pulledFirst.Close);
        }
        else
        {
            decimal[] ratios = [.. shared.Where(day => longer[day].Close > 0m).Select(day => pulled[day].Close / longer[day].Close).Order()];

            (longScale, pulledScale) = (ratios.Length == 0 ? 1m : ratios[ratios.Length / 2], 1m);
        }

        var (cent, percent, traded) = (0, 0, 0);
        DateOnly? firstApart = null;
        var largest = 0m;

        foreach (var day in shared)
        {
            var (a, b) = (longer[day].Close * longScale, pulled[day].Close * pulledScale);
            var apart = Math.Abs(a - b);

            if (apart > 0.01m)
            {
                cent++;
            }

            if (b > 0m && apart > 0.01m * b)
            {
                percent++;
                firstApart ??= day;
                largest = Math.Max(largest, apart / b);
            }

            if (longer[day].Raw is { } rawLong && pulled[day].Raw is { } rawPulled && Math.Abs(rawLong - rawPulled) > 0.01m)
            {
                traded++;
            }
        }

        return new LongOverlap(ticker, shared.Length, cent, percent, traded, firstApart, largest);
    }

    static async Task<int> WriteMarketAsync(SqliteConnection connection, SqliteTransaction transaction, string folder, string runId, ICollection<string> unreadable, CancellationToken cancellation)
    {
        var written = 0;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertMarketBar;

        foreach (var series in MarketSeries)
        {
            var file = FileOf(folder, MarketFolder, series);

            if (!File.Exists(file))
            {
                continue;
            }

            foreach (var bar in BarsOf(await File.ReadAllTextAsync(file, cancellation), series, unreadable))
            {
                insert.Parameters.Clear();
                insert.Parameters.AddWithValue("$series", series);
                insert.Parameters.AddWithValue("$session_date", Text(bar.SessionDate));
                insert.Parameters.AddWithValue("$open", Money.ToStorage(bar.Open));
                insert.Parameters.AddWithValue("$high", Money.ToStorage(bar.High));
                insert.Parameters.AddWithValue("$low", Money.ToStorage(bar.Low));
                insert.Parameters.AddWithValue("$close", Money.ToStorage(bar.Close));
                insert.Parameters.AddWithValue("$run_id", runId);
                written += await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        return written;
    }

    // The calendar's prints of the codes asked, as the provider filed them: the day, the timing, the figures and the surprise.
    static async Task<int> WritePrintsAsync(SqliteConnection connection, SqliteTransaction transaction, string folder, IReadOnlyList<LongCode> codes, string runId, CancellationToken cancellation)
    {
        var asked = codes.Select(code => code.Ticker).ToHashSet(StringComparer.Ordinal);
        var written = 0;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertPrint;

        foreach (var (first, last) in LongPrices.Months())
        {
            var file = FileOf(folder, PrintsFolder, Month(first));

            if (!File.Exists(file))
            {
                continue;
            }

            IReadOnlyList<CalendarEvent> events;

            try
            {
                events = RecordedEarningsCalendarFeed.Parse(await File.ReadAllTextAsync(file, cancellation), first, last);
            }
            catch (Exception failure) when (failure is JsonException or FormatException or KeyNotFoundException)
            {
                continue;
            }

            foreach (var print in events.Where(print => asked.Contains(print.Ticker)))
            {
                insert.Parameters.Clear();
                insert.Parameters.AddWithValue("$ticker", print.Ticker);
                insert.Parameters.AddWithValue("$event_date", Text(print.EventDate));
                insert.Parameters.AddWithValue("$timing", EquityBrief.Worker.Calendar.CalendarFetcher.Filed(print.Timing));
                insert.Parameters.AddWithValue("$eps_actual", (object?)print.Actual ?? DBNull.Value);
                insert.Parameters.AddWithValue("$eps_estimate", (object?)print.Estimate ?? DBNull.Value);
                insert.Parameters.AddWithValue("$surprise_percent", print.Surprise is { } surprise && double.TryParse(surprise, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ? percent : DBNull.Value);
                insert.Parameters.AddWithValue("$read_from", LongPrices.ProviderPrint);
                insert.Parameters.AddWithValue("$run_id", runId);
                written += await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        return written;
    }

    static async Task<(int Splits, int Dividends)> WriteActionsAsync(SqliteConnection connection, SqliteTransaction transaction, string folder, IReadOnlyList<LongCode> codes, string runId, CancellationToken cancellation)
    {
        var (splits, dividends) = (0, 0);

        await using var split = connection.CreateCommand();
        split.Transaction = transaction;
        split.CommandText = InsertSplit;

        await using var dividend = connection.CreateCommand();
        dividend.Transaction = transaction;
        dividend.CommandText = InsertDividend;

        foreach (var code in codes)
        {
            var splitFile = FileOf(folder, SplitsFolder, code.Ticker);
            var dividendFile = FileOf(folder, DividendsFolder, code.Ticker);

            foreach (var one in File.Exists(splitFile) ? ArrayOr(await File.ReadAllTextAsync(splitFile, cancellation), answer => SplitAnswers.Parse(answer, code.Ticker)) : [])
            {
                split.Parameters.Clear();
                split.Parameters.AddWithValue("$ticker", code.Ticker);
                split.Parameters.AddWithValue("$ex_date", Text(one.ExDate));
                split.Parameters.AddWithValue("$new_shares", one.NewShares.ToString(CultureInfo.InvariantCulture));
                split.Parameters.AddWithValue("$old_shares", one.OldShares.ToString(CultureInfo.InvariantCulture));
                split.Parameters.AddWithValue("$run_id", runId);
                splits += await split.ExecuteNonQueryAsync(cancellation);
            }

            foreach (var one in File.Exists(dividendFile) ? ArrayOr(await File.ReadAllTextAsync(dividendFile, cancellation), answer => DividendAnswers.Parse(answer, code.Ticker)) : [])
            {
                dividend.Parameters.Clear();
                dividend.Parameters.AddWithValue("$ticker", code.Ticker);
                dividend.Parameters.AddWithValue("$ex_date", Text(one.ExDate));
                dividend.Parameters.AddWithValue("$amount", Money.ToStorage(one.Amount));
                dividend.Parameters.AddWithValue("$raw_amount", one.Unadjusted is { } raw ? Money.ToStorage(raw) : DBNull.Value);
                dividend.Parameters.AddWithValue("$run_id", runId);
                dividends += await dividend.ExecuteNonQueryAsync(cancellation);
            }
        }

        return (splits, dividends);
    }

    // Each code's filer's filings as its list states them, kept where the filer is the code's company.
    static async Task<int> WriteFilingsAsync(SqliteConnection connection, SqliteTransaction transaction, string folder, IReadOnlyList<LongFiler> filers, string runId, CancellationToken cancellation)
    {
        var written = 0;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertFiling;

        foreach (var filer in filers.Where(filer => filer.IsTheCompany && filer.Cik is not null))
        {
            var kept = Path.Combine(folder, FilersFolder, filer.Cik!);

            if (!File.Exists(Path.Combine(kept, HistoryEndFilings.IndexFile)))
            {
                continue;
            }

            foreach (var filing in FilerFilings.Read(kept))
            {
                insert.Parameters.Clear();
                insert.Parameters.AddWithValue("$ticker", filer.Ticker);
                insert.Parameters.AddWithValue("$cik", filer.Cik!);
                insert.Parameters.AddWithValue("$accession", filing.Accession);
                insert.Parameters.AddWithValue("$form", filing.Form);
                insert.Parameters.AddWithValue("$items", filing.Items.Count == 0 ? DBNull.Value : string.Join(',', filing.Items));
                insert.Parameters.AddWithValue("$reported", filing.Reported is { } reported ? Text(reported) : DBNull.Value);
                insert.Parameters.AddWithValue("$filed", Text(filing.Filed));
                insert.Parameters.AddWithValue("$run_id", runId);
                written += await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        return written;
    }

    // What a person reads of a write: the counts, each quarter end of the S&P 500's count, the lowest coverage and each stop, the
    // codes priced by nothing, the filers refused, the ends unsettled and the overlaps apart, each named with '--each'.
    public static IReadOnlyList<string> Lines(LongPriceOutcome outcome, bool each)
    {
        var lines = new List<string>
        {
            FormattableString.Invariant($"{outcome.Priced:N0} of {outcome.Codes:N0} code(s) priced, {outcome.Bars:N0} session(s), {outcome.MarketBars:N0} market session(s), {outcome.Prints:N0} print(s), {outcome.Splits:N0} split(s), {outcome.Dividends:N0} dividend(s), {outcome.Filings:N0} filing(s), ") + (outcome.Written ? "written" : "nothing written"),
        };

        foreach (var count in outcome.Counts)
        {
            lines.Add(FormattableString.Invariant($"S&P 500 {count.Period:yyyy-MM-dd}: {count.Codes} code(s) in the provider's spans, {count.Companies} once two codes carrying one series count once, IVV {count.Fund}, {count.Apart:+0;-0;0} apart") + (each && count.CountedOnce.Count > 0 ? "; once: " + string.Join("; ", count.CountedOnce) : string.Empty));
        }

        lines.Add(FormattableString.Invariant($"the S&P 500 reads the provider's dates from {outcome.Cutover:yyyy-MM-dd}"));

        foreach (var quarter in outcome.Coverage.OrderBy(quarter => quarter.HoldingsShare).Take(each ? outcome.Coverage.Count : 6))
        {
            lines.Add(FormattableString.Invariant($"{quarter.Index} {quarter.Period:yyyy-MM-dd}: {quarter.Priced} of {quarter.Holdings} holdings priced over their spans, {quarter.HoldingsShare:P1}, {quarter.ValueShare:P1} of value"));
        }

        lines.AddRange(outcome.Stops.Select(stop => "stop: " + stop));
        lines.Add(FormattableString.Invariant($"{outcome.Nothing.Count} code(s) the provider holds nothing for, left out") + (outcome.Nothing.Count > 0 ? ": " + string.Join("; ", outcome.Nothing) : string.Empty));
        lines.Add(FormattableString.Invariant($"{outcome.Unreadable.Count} row(s) the bar reader refused, skipped and left a missing session") + (outcome.Unreadable.Count > 0 ? ": " + string.Join("; ", each ? outcome.Unreadable : outcome.Unreadable.Take(10)) + (!each && outcome.Unreadable.Count > 10 ? "; the rest with '--each'" : string.Empty) : string.Empty));
        lines.Add(FormattableString.Invariant($"{outcome.Refused.Count} filer(s) refused, their names not the company's"));
        lines.AddRange(each ? outcome.Refused.Select(filer => FormattableString.Invariant($"  refused {filer.Ticker}: {filer.Cik ?? "none"} {string.Join("; ", filer.Entity)} ({filer.By})")) : []);

        var settled = outcome.Ends.Where(end => end.Session is not null).ToArray();
        var unsettled = outcome.Ends.Where(end => end.Session is null).ToArray();

        lines.Add(FormattableString.Invariant($"{outcome.Ends.Count} departed code(s): {settled.Length} ended by a filing, {settled.Count(end => end.Marked)} marked where the answer runs on past it, {unsettled.Length} to the operator"));
        lines.AddRange(each ? settled.Select(end => FormattableString.Invariant($"  ends {end.Ticker} {end.Session:yyyy-MM-dd}{(end.Marked ? " marked" : string.Empty)}, last close {end.LastClose:yyyy-MM-dd}: {end.Says}")) : []);
        lines.AddRange(each ? unsettled.Select(end => FormattableString.Invariant($"  unsettled {end.Ticker}, last traded {end.LastTraded ?? end.LastClose:yyyy-MM-dd}: {end.Says}")) : []);

        var apart = outcome.Overlaps.Where(overlap => overlap.ApartByOnePerCent > 0).ToArray();

        lines.Add(FormattableString.Invariant($"{outcome.Overlaps.Count} code(s) overlap the pull, {outcome.Overlaps.Sum(overlap => overlap.Shared):N0} session(s) shared, {outcome.Overlaps.Sum(overlap => overlap.ApartByACent):N0} apart by a cent, {outcome.Overlaps.Sum(overlap => overlap.ApartByOnePerCent):N0} by one per cent on {apart.Length} code(s), {outcome.Overlaps.Sum(overlap => overlap.TradedApart):N0} traded apart"));
        lines.AddRange(each ? apart.Select(overlap => FormattableString.Invariant($"  apart {overlap.Ticker}: {overlap.ApartByOnePerCent} of {overlap.Shared} session(s) by one per cent, the first {overlap.FirstApart:yyyy-MM-dd}, the most {overlap.Largest:P1}")) : []);

        return lines;
    }

    // The operator's word that the data check's counts over the long history have been read, on the run log.
    public static async Task<int> CountsReadAsync(string databaseFile, string runId, IClock clock, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var run = connection.CreateCommand();
        var now = clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        run.CommandText = AppendRun;
        run.Parameters.AddWithValue("$run_id", runId);
        run.Parameters.AddWithValue("$stage", CountsStage);
        run.Parameters.AddWithValue("$started_at", now);
        run.Parameters.AddWithValue("$ended_at", now);
        run.Parameters.AddWithValue("$rows", 1);
        run.Parameters.AddWithValue("$detail", "The operator has read the data check's counts over the long history.");

        return await run.ExecuteNonQueryAsync(cancellation);
    }

    // Whether the operator's word stands: given after the newest data check over the long history ended, none where no such check
    // has run.
    public static async Task<bool> CountsWordStandsAsync(string databaseFile, CancellationToken cancellation = default)
    {
        var builder = StoreConnection.Builder(databaseFile);
        builder.Mode = SqliteOpenMode.ReadOnly;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);

        string? word = null, check = null;

        await ReadAsync(connection, SelectCountsWord, reader => word = reader.IsDBNull(0) ? null : reader.GetString(0), cancellation);
        await ReadAsync(connection, SelectLongCheck, reader => check = reader.IsDBNull(0) ? null : reader.GetString(0), cancellation);

        return word is not null && check is not null && string.CompareOrdinal(word, check) >= 0;
    }

    static async Task ReadAsync(SqliteConnection connection, string sql, Action<SqliteDataReader> row, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            row(reader);
        }
    }
}
