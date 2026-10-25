using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// One trade a list or a book kept, in the one shape every paper account reads: the account it belongs to, by the
// registered rule's name or the page's own list, its family, its stock, the night it was listed on, its place that night,
// the close its plan was set at and its stop, none for a book's holding, the session it ended on, its result at its fill,
// in multiples of the risk its close put up for a swing trade and as a fraction of that close for a book's holding, its
// round trip as its table stores it, in multiples of its risk or as a fraction of its buy, and from 19.10 its fill at the
// next session's open, the session of that fill, the open the bar store holds there now, and why it holds no fill. Paper
// trading's page reads besides its target, the distance its stop trails where its table stores one, the sessions it is
// given, which end of its plan its fill opened past and its edge as the fill stored them, a holding's sector and the
// reason its book sold it, and a swing trade's exit of the menu where its table names one, with its stop's distance in
// the night's typical moves.
public sealed record PaperRow(
    string Account,
    string Family,
    string Ticker,
    DateOnly Bought,
    int Place,
    decimal Entry,
    decimal? Stop,
    DateOnly? Ended,
    double? Result,
    bool Book,
    double? CostRisk,
    double? CostFraction,
    decimal? Fill = null,
    DateOnly? FilledOn = null,
    decimal? MarkedFrom = null,
    string? Unfilled = null,
    decimal? Target = null,
    decimal? Trail = null,
    int? Cap = null,
    string? Past = null,
    double? Edge = null,
    string? Sector = null,
    string? Reason = null,
    int? BookSize = null,
    int? Exit = null,
    double? RiskMoves = null);

// What paper trading reads of a stock on the night: its company as the membership names it, its typical move and its
// 200-session average as the night's indicators stored them, each none where none is stored.
public sealed record PaperStockRow(string Ticker, string? Company, double? TypicalMove, double? Average200);

// One rule's verdict in an index's newest monthly per-rule test, as the tester stored it.
public sealed record RuleTestRow(
    string RunId,
    string Month,
    string Family,
    string Rule,
    string Standing,
    string Words,
    string Against,
    string Unit,
    int? Units,
    int? Blocks,
    double? Adjusted,
    bool? Gate,
    bool? Stable,
    int? Counted,
    int? Better,
    double? Trimmed,
    bool? Counts,
    bool Passed,
    string? Why);

// One test year of one rule in that test: its units and total, and the side it was read against's.
public sealed record RuleTestYearRow(string Family, string Rule, int Year, bool Complete, int Units, double Total, int ComparedUnits, double ComparedTotal);

// What paper trading reads: every list's and book's trades on an index, the newest per-rule test's rows, each member's
// cost and value on a session for the round trip a trade pays where its table stores none, the closes a position is
// marked by, and the index fund's closes. It computes none of the accounts.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public sealed partial class ReadApi
{
    // The page's own list on an index is named by this word before its family.
    public const string PageList = "list";

    // From 19.10 every trade is read with its fill at the next session's open: its result is the fill's, and the last six
    // columns are the fill's price, its session, the open the bar store holds now on that session, which an open position
    // is marked from, why a trade holds no fill, which end of its plan it opened past and its edge; a trade with no row
    // waits on its next session.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    static string Filled(string source, string alias, string index, string owner, string session) =>
        $"LEFT JOIN trade_fill f ON f.source = '{source}' AND f.index_code = {index} AND f.owner = {owner} AND f.ticker = {alias}.ticker AND f.session_date = {alias}.{session}";

    const string FillColumns = "f.fill, f.filled_on, (SELECT b.open FROM bar b WHERE b.ticker = f.ticker AND b.session_date = f.filled_on), f.reason, f.past, f.edge";

    const int FillWidth = 6;

    // Each swing trade in one column order after its account and family: its stock, night, place, close, stop, end, result
    // at its fill, target, the distance it trails, cap, round trip, the exit of the menu it is walked under and its stop's
    // distance in typical moves, a column a table keeps none of read as none.
    static readonly string FamilyTradesOn = $"SELECT t.candidate, t.family, t.ticker, t.session_date, t.place, t.entry, t.stop, t.ended_on, f.result, t.target, NULL, t.cap, t.cost, t.exit, t.risk_moves, {FillColumns} FROM family_trade t {Filled("family_trade", "t", "'GSPC'", "t.candidate", "session_date")} ORDER BY t.session_date, t.place;";

    static readonly string RulePicksKept = $"SELECT t.rule, t.family, t.ticker, t.session_date, t.place, t.entry, t.stop, t.ended_on, f.result, t.target, NULL, t.cap, NULL, NULL, NULL, {FillColumns} FROM rule_pick t {Filled("rule_pick", "t", "t.index_code", "t.rule", "session_date")} WHERE t.index_code = $index ORDER BY t.session_date, t.place;";

    static readonly string IndexFamilyTradesOn = $"SELECT t.family, t.family, t.ticker, t.session_date, t.place, t.entry, t.stop, t.ended_on, f.result, t.target, t.trail, t.cap, t.cost, t.exit, t.risk_moves, {FillColumns} FROM index_family_trade t {Filled("index_family_trade", "t", "t.index_code", "t.family", "session_date")} WHERE t.index_code = $index ORDER BY t.session_date, t.place;";

    static readonly string IndexRuleTradesOn = $"SELECT t.candidate, t.family, t.ticker, t.session_date, t.place, t.entry, t.stop, t.ended_on, f.result, t.target, t.trail, t.cap, t.cost, t.exit, t.risk_moves, {FillColumns} FROM index_rule_trade t {Filled("index_rule_trade", "t", "t.index_code", "t.candidate", "session_date")} WHERE t.index_code = $index ORDER BY t.session_date, t.place;";

    // Each holding in one column order after its account: its stock, rebalance, close, end, result at its fill, round
    // trip, sector and the reason its book sold it, a book keeping no round trip read as none.
    static readonly string PageBookOn = $"SELECT '', h.ticker, h.entered_on, h.entry_close, h.ended_on, f.result, NULL, h.sector, h.reason, {FillColumns} FROM heavyweight_holding h {Filled("heavyweight_holding", "h", "'GSPC'", "''", "entered_on")} ORDER BY h.entered_on, h.sector, h.ticker;";

    static readonly string RuleBooksOn = $"SELECT h.candidate, h.ticker, h.entered_on, h.entry_close, h.ended_on, f.result, NULL, h.sector, h.reason, {FillColumns} FROM heavyweight_rule_holding h {Filled("heavyweight_rule_holding", "h", "'GSPC'", "h.candidate", "entered_on")} ORDER BY h.entered_on, h.sector, h.ticker;";

    static readonly string IndexBookOn = $"SELECT '', h.ticker, h.entered_on, h.entry_close, h.ended_on, f.result, h.cost, h.sector, h.reason, {FillColumns} FROM index_heavyweight_holding h {Filled("index_heavyweight_holding", "h", "h.index_code", "''", "entered_on")} WHERE h.index_code = $index ORDER BY h.entered_on, h.sector, h.ticker;";

    static readonly string IndexRuleBooksOn = $"SELECT h.candidate, h.ticker, h.entered_on, h.entry_close, h.ended_on, f.result, h.cost, h.sector, h.reason, {FillColumns} FROM index_heavyweight_rule_holding h {Filled("index_heavyweight_rule_holding", "h", "h.index_code", "h.candidate", "entered_on")} WHERE h.index_code = $index ORDER BY h.entered_on, h.sector, h.ticker;";

    // From 22.11 every holding of the quality family on an index, under its rule's name and the provisional rule's under
    // the page's list, in the holdings' column order with the book its rule keeps and the fill its own step stored at the
    // next open in the fills' place, its round trip read as a fraction of its buy and its edge in percentage points as one.
    // see: A quality rule is registered as the family on its index and kept by the family's own step alone
    static readonly string QualityBooksOn =
        $"SELECT CASE WHEN h.rule = '{QualityProvisional}' THEN '{PageList} {QualityRule.Name}' ELSE h.rule END, h.ticker, h.chosen_on, h.buy_close, h.ended_on, h.result, h.cost / 100.0, NULL, " +
        "CASE WHEN h.end_reason = 'open' THEN NULL ELSE h.end_reason END, " +
        "(SELECT json_extract(n.settings, '$.holdings') FROM quality_night n WHERE n.rule = h.rule AND n.index_code = h.index_code AND n.session_date = h.chosen_on), " +
        "h.fill, h.bought_on, (SELECT b.open FROM bar b WHERE b.ticker = h.ticker AND b.session_date = h.bought_on), NULL, NULL, h.edge / 100.0 " +
        "FROM quality_holding h WHERE h.index_code = $index ORDER BY h.chosen_on, h.ticker;";

    // A stock's company as its newest membership row names it, and its typical move and 200-session average on the night.
    const string PaperStocksOn = @"
        SELECT t.value,
               (SELECT m.name FROM membership m WHERE m.ticker = t.value ORDER BY m.observed_at DESC, m.rowid DESC LIMIT 1),
               (SELECT i.value FROM indicator i WHERE i.ticker = t.value AND i.session_date = $night AND i.name = $typical),
               (SELECT i.value FROM indicator i WHERE i.ticker = t.value AND i.session_date = $night AND i.name = $average)
        FROM json_each($names) t;";

    // The newest per-rule test on an index, by the order the runs were written.
    const string RuleTestsOn = @"
        SELECT run_id, month, family, rule, standing, words, against, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, why
        FROM loop_rule WHERE index_code = $index AND run_id = (SELECT run_id FROM loop_rule WHERE index_code = $index ORDER BY rowid DESC LIMIT 1)
        ORDER BY rowid;";

    const string RuleTestYearsOn = @"
        SELECT family, rule, year, complete, units, total, compared_units, compared_total
        FROM loop_rule_year WHERE index_code = $index AND run_id = $run_id ORDER BY family, rule, year;";

    const string MemberCostsOn = "SELECT ticker, session_date, cost, company_value FROM member_reading WHERE index_code = $index AND ticker IN (SELECT value FROM json_each($names));";

    // The counts a member reader would have read on a session: its newest fetch on or before the session's night.
    const string CountsBefore = @"
        SELECT period_end, filing_date, shares, basis_session, basis_close
        FROM reported_quarter
        WHERE ticker = $ticker AND fetched_at = (
            SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = $ticker AND session_date <= $session);";

    const string ClosesFrom = "SELECT ticker, session_date, close FROM bar WHERE session_date >= $from AND ticker IN (SELECT value FROM json_each($names));";

    const string SessionsFrom = "SELECT DISTINCT session_date FROM bar WHERE session_date >= $from ORDER BY session_date;";

    const string FundClosesFrom = "SELECT session_date, close FROM market_bar WHERE series = $series AND session_date >= $from;";

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static decimal? PriceAt(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

    static DateOnly? DayAt(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : Day(reader.GetString(at));

    // Every trade the registered rules' lists and books on an index kept, and the page's own lists on every index but
    // the S&P 500's swing families, whose page list Past picks reads: an S&P 400's or 600's families and the
    // fundamentals-first family on every index, and each index's own book of sector heavyweights.
    public async Task<IReadOnlyList<PaperRow>> PaperRowsAsync(string index)
    {
        var rows = new List<PaperRow>();
        var large = index == "GSPC";

        await using var connection = Open();

        async Task ReadAsync(string sql, Func<SqliteDataReader, PaperRow?> read)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = sql;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (read(reader) is { } row)
                {
                    rows.Add(row);
                }
            }
        }

        static PaperRow Fill(SqliteDataReader reader, PaperRow row)
        {
            var at = reader.FieldCount - FillWidth;

            return row with
            {
                Fill = PriceAt(reader, at),
                FilledOn = DayAt(reader, at + 1),
                MarkedFrom = PriceAt(reader, at + 2),
                Unfilled = reader.IsDBNull(at + 3) ? null : reader.GetString(at + 3),
                Past = reader.IsDBNull(at + 4) ? null : reader.GetString(at + 4),
                Edge = Optional(reader, at + 5),
            };
        }

        static PaperRow Swing(SqliteDataReader reader, string account) =>
            Fill(reader, new(account, reader.GetString(1), reader.GetString(2), Day(reader.GetString(3)), reader.GetInt32(4), Money.FromStorage(reader.GetString(5)), PriceAt(reader, 6), DayAt(reader, 7), Optional(reader, 8), false, Optional(reader, 12), null)
            {
                Target = PriceAt(reader, 9),
                Trail = PriceAt(reader, 10),
                Cap = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                Exit = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                RiskMoves = Optional(reader, 14),
            });

        static PaperRow Holding(SqliteDataReader reader, string account) =>
            Fill(reader, new(account, HeavyweightRule.Name, reader.GetString(1), Day(reader.GetString(2)), 0, Money.FromStorage(reader.GetString(3)), null, DayAt(reader, 4), Optional(reader, 5), true, null, Optional(reader, 6))
            {
                Sector = reader.IsDBNull(7) ? null : reader.GetString(7),
                Reason = reader.IsDBNull(8) ? null : reader.GetString(8),
            });

        if (large)
        {
            await ReadAsync(FamilyTradesOn, reader => Swing(reader, reader.GetString(0)));
            await ReadAsync(RulePicksKept, reader => Swing(reader, reader.GetString(0)));
            await ReadAsync(PageBookOn, reader => Holding(reader, PageList + " " + HeavyweightRule.Name));
            await ReadAsync(RuleBooksOn, reader => Holding(reader, reader.GetString(0)));
        }
        else
        {
            await ReadAsync(IndexRuleTradesOn, reader => Swing(reader, reader.GetString(0)));
            await ReadAsync(IndexBookOn, reader => Holding(reader, PageList + " " + HeavyweightRule.Name));
            await ReadAsync(IndexRuleBooksOn, reader => Holding(reader, reader.GetString(0)));
        }

        await ReadAsync(IndexFamilyTradesOn, reader =>
            large && reader.GetString(0) != FundamentalsRule.Name
                ? null
                : Swing(reader, PageList + " " + reader.GetString(0)));

        await ReadAsync(QualityBooksOn, reader => Fill(reader, new(reader.GetString(0), QualityRule.Name, reader.GetString(1), Day(reader.GetString(2)), 0, Money.FromStorage(reader.GetString(3)), null, DayAt(reader, 4), Optional(reader, 5), true, null, Optional(reader, 6))
        {
            Reason = reader.IsDBNull(8) ? null : reader.GetString(8),
            BookSize = reader.IsDBNull(9) ? null : reader.GetInt32(9),
        }));

        return rows;
    }

    // Each stock's company, typical move and 200-session average on the night, keyed by the stock; a stock the store holds
    // none of reads none of each.
    public async Task<IReadOnlyDictionary<string, PaperStockRow>> PaperStocksAsync(IReadOnlyList<string> tickers, DateOnly night)
    {
        var read = new Dictionary<string, PaperStockRow>(StringComparer.Ordinal);

        if (tickers.Count == 0)
        {
            return read;
        }

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = PaperStocksOn;
        command.Parameters.AddWithValue("$names", System.Text.Json.JsonSerializer.Serialize(tickers.Distinct(StringComparer.Ordinal).ToArray()));
        command.Parameters.AddWithValue("$night", Stamp(night));
        command.Parameters.AddWithValue("$typical", EquityBrief.Core.Indicators.IndicatorSeries.Atr14);
        command.Parameters.AddWithValue("$average", EquityBrief.Core.Indicators.IndicatorSeries.Sma200);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var ticker = reader.GetString(0);

            read[ticker] = new PaperStockRow(ticker, reader.IsDBNull(1) ? null : reader.GetString(1), Optional(reader, 2), Optional(reader, 3));
        }

        return read;
    }

    // An index's newest per-rule test and its test years, none where no test has run on it.
    public async Task<(IReadOnlyList<RuleTestRow> Rules, IReadOnlyList<RuleTestYearRow> Years)> RuleTestsAsync(string index)
    {
        var rules = new List<RuleTestRow>();
        var years = new List<RuleTestYearRow>();

        await using var connection = Open();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RuleTestsOn;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            static bool? Flag(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : reader.GetInt64(at) == 1;

            static int? Whole(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : reader.GetInt32(at);

            while (await reader.ReadAsync())
            {
                rules.Add(new RuleTestRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    Whole(reader, 8),
                    Whole(reader, 9),
                    Optional(reader, 10),
                    Flag(reader, 11),
                    Flag(reader, 12),
                    Whole(reader, 13),
                    Whole(reader, 14),
                    Optional(reader, 15),
                    Flag(reader, 16),
                    reader.GetInt64(17) == 1,
                    reader.IsDBNull(18) ? null : reader.GetString(18)));
            }
        }

        if (rules.Count == 0)
        {
            return (rules, years);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RuleTestYearsOn;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$run_id", rules[0].RunId);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                years.Add(new RuleTestYearRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3) == 1, reader.GetInt32(4), reader.GetDouble(5), reader.GetInt32(6), reader.GetDouble(7)));
            }
        }

        return (rules, years);
    }

    // Each member's cost in per cent of the buy and its company value on each session a trade was bought on, from the
    // member readings where the night kept them, and otherwise the value as the member reader reads it, from the count
    // its newest fetch on the session's night filed and the close that count is stated against, with no cost; none
    // where neither is read. Keyed by the stock and the session.
    public async Task<IReadOnlyDictionary<(string Ticker, DateOnly Session), (double? Cost, decimal? Value)>> MemberCostsAsync(string index, IReadOnlyList<(string Ticker, DateOnly Session)> bought)
    {
        var read = new Dictionary<(string, DateOnly), (double?, decimal?)>();

        if (bought.Count == 0)
        {
            return read;
        }

        var names = bought.Select(one => one.Ticker).Distinct(StringComparer.Ordinal).ToArray();

        await using var connection = Open();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MemberCostsOn;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$names", System.Text.Json.JsonSerializer.Serialize(names));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                read[(reader.GetString(0), Day(reader.GetString(1)))] = (Optional(reader, 2), PriceAt(reader, 3));
            }
        }

        var closes = await ClosesAsync(names, bought.Min(one => one.Session).AddYears(-2));

        foreach (var (ticker, session) in bought.Distinct())
        {
            if (read.ContainsKey((ticker, session)) || !closes.TryGetValue((ticker, session), out var close))
            {
                continue;
            }

            var counts = new List<FiledCount>();
            (decimal Close, DateOnly Session)? basis = null;

            await using var command = connection.CreateCommand();

            command.CommandText = CountsBefore;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", Stamp(session));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
                {
                    basis = (Money.FromStorage(reader.GetString(4)), Day(reader.GetString(3)));
                }

                if (!reader.IsDBNull(1) && !reader.IsDBNull(2) && !reader.IsDBNull(3))
                {
                    counts.Add(new FiledCount(Day(reader.GetString(0)), Day(reader.GetString(1)), Money.FromStorage(reader.GetString(2)), Day(reader.GetString(3))));
                }
            }

            read[(ticker, session)] = (null, basis is { } held
                ? CompanyValue.Tonight(session, close, counts, held.Close, closes.TryGetValue((ticker, held.Session), out var then) ? then : null)
                : null);
        }

        return read;
    }

    // Each stock's closes on the bar store's sessions from a day on, keyed by the stock and the session.
    public async Task<IReadOnlyDictionary<(string Ticker, DateOnly Session), decimal>> ClosesAsync(IReadOnlyList<string> tickers, DateOnly from)
    {
        var closes = new Dictionary<(string, DateOnly), decimal>();

        if (tickers.Count == 0)
        {
            return closes;
        }

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ClosesFrom;
        command.Parameters.AddWithValue("$from", Stamp(from));
        command.Parameters.AddWithValue("$names", System.Text.Json.JsonSerializer.Serialize(tickers));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            closes[(reader.GetString(0), Day(reader.GetString(1)))] = Money.FromStorage(reader.GetString(2));
        }

        return closes;
    }

    // The bar store's sessions from a day on, in order.
    public async Task<IReadOnlyList<DateOnly>> SessionsFromAsync(DateOnly from)
    {
        var sessions = new List<DateOnly>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SessionsFrom;
        command.Parameters.AddWithValue("$from", Stamp(from));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            sessions.Add(Day(reader.GetString(0)));
        }

        return sessions;
    }

    // A fund's closes from a day on, by session.
    public async Task<IReadOnlyDictionary<DateOnly, decimal>> FundClosesAsync(string series, DateOnly from)
    {
        var closes = new Dictionary<DateOnly, decimal>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FundClosesFrom;
        command.Parameters.AddWithValue("$series", series);
        command.Parameters.AddWithValue("$from", Stamp(from));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            closes[Day(reader.GetString(0))] = Money.FromStorage(reader.GetString(1));
        }

        return closes;
    }
}
