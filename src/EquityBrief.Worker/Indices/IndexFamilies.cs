using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Indices;

// What the index families did for one index on the night: the members read, the breadth and the market check, how many
// each family passed, how many of the index's list were listed and how many held back by a trade still open, and the
// trades kept tonight and ended tonight.
public sealed record IndexNightOutcome(string Index, int Members, double? Breadth, bool MarketOpen, IReadOnlyDictionary<string, int> Passed, int Listed, int HeldByATrade, int TradesKept, int TradesEnded, IndexHeavyweightsOutcome Heavyweights);

// The night the index families read, none where the store holds no bar, and each index's outcome.
public sealed record IndexFamiliesOutcome(DateOnly? Session, IReadOnlyList<IndexNightOutcome> Nights);

// The index families. After the S&P 500's families have drawn their list, they read the S&P 400's and 600's
// provisional swing rules for the night with the sweep's own code over the members' year of bars, each member's answer
// stored under each family whether it passed or not, and draw each index's list by the S&P 500's own rule: the
// families in the page's order, five a family, a stock listed once, and a stock whose trade on any card of any index is
// still open listed by none. They walk each index's open trades over the closes since and keep tonight's listed rows as
// trades. A night run again replaces its own rows; they evaluate no rule of the S&P 500's, make no request and call
// no model.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
// see: The nightly run is arithmetic only
// see: Every computed table's writer is its own deleter
public sealed class IndexFamilies : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.ListRule, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.IndexFamilyNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.IndexFamilyResult, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.IndexFamilyTrade, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "index-families";

    // The indices read, in the order the page offers them after the S&P 500.
    public static IReadOnlyList<string> Indices { get; } = [FundHoldings.MidCapIndex, FundHoldings.SmallCapIndex];

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // An index's members on the night with every span the index held each for.
    const string MembersOf = @"
        SELECT ticker, joined, ""left"" FROM membership
        WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string SpansOf = @"SELECT joined, ""left"" FROM membership WHERE index_code = $index AND ticker = $ticker;";

    const string BarsOf = @"
        SELECT session_date, high, low, close, volume, open, raw_close FROM bar
        WHERE ticker = $ticker AND session_date <= $night ORDER BY session_date;
    ";

    const string PrintsOf = "SELECT event_date, timing, detail FROM calendar WHERE ticker = $ticker AND kind = 'earnings' ORDER BY event_date;";

    // Each quarter as filed, its net and operating income, the profit gate's reading.
    const string QuartersOf = @"
        SELECT period_end, filing_date, net_income, operating_income FROM reported_quarter
        WHERE ticker = $ticker AND filing_date IS NOT NULL ORDER BY period_end;
    ";

    // Every trade an index's list kept, all indices together, which the open trade rule reads.
    const string IndexTrades = @"
        SELECT index_code, family, ticker, session_date, ended_on, cap FROM index_family_trade
        WHERE session_date < $night;
    ";

    const string OpenIndexTrades = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap FROM index_family_trade
        WHERE index_code = $index AND ended_on IS NULL AND session_date < $night;
    ";

    const string ClearTheNight = @"
        DELETE FROM index_family_night WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_result WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_pick WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_trade WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_result WHERE index_code = $index AND passed = 0 AND session_date < (SELECT MIN(session_date) FROM bar);
    ";

    const string InsertNight = @"
        INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced)
        VALUES ($index, $night, $members, $breadth, $open, $settings, $rebalanced);
    ";

    const string InsertResult = @"
        INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason)
        VALUES ($index, $night, $ticker, $family, $passed, $place, $entry, $stop, $target, $trail, $cap, $order_by, $reason);
    ";

    const string InsertPick = @"
        INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night)
        VALUES ($index, $night, $ticker, $family, $state, $place, $also, $held_index, $held_family, $held_night);
    ";

    const string InsertTrade = @"
        INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap)
        VALUES ($index, $family, $ticker, $night, $place, $entry, $stop, $target, $trail, $cap);
    ";

    const string EndTrade = @"
        UPDATE index_family_trade SET ended_on = $ended_on, result = $result, cost = $cost
        WHERE index_code = $index AND family = $family AND ticker = $ticker AND session_date = $session;
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

    public IndexFamilies(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<IndexFamiliesOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var night = await ScalarAsync(connection, NewestSession, [], cancellation) is string newest ? Date(newest) : (DateOnly?)null;

        if (night is not { } session)
        {
            await AppendAsync(connection, runId, startedAt, 0, "no session: the store holds no bar", cancellation);

            return new IndexFamiliesOutcome(null, []);
        }

        var outcomes = new List<IndexNightOutcome>();
        var rows = 0;

        foreach (var index in Indices)
        {
            var (names, income) = await InputsAsync(connection, index, session, cancellation);
            var read = IndexNightRead.Read(index, session, names, income);
            var qualifying = IndexNightRead.Families
                .Select(family => new FamilyQualifiers(family, [.. read.Answers.Where(answer => answer.Family == family && answer.Passed).OrderBy(answer => answer.Place).Select(answer => answer.Ticker)]))
                .ToArray();
            var passing = qualifying.SelectMany(family => family.Tickers).Distinct(StringComparer.Ordinal).ToArray();
            var (open, heldIn) = await OpenAsync(connection, session, passing, cancellation);
            var picks = FamilyList.Draw(qualifying, open);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            await ExecuteAsync(connection, transaction, ClearTheNight, [("$index", index), ("$night", Stamp(session))], cancellation);

            var ended = await WalkAsync(connection, transaction, index, session, cancellation);
            var heavyweights = await IndexHeavyweights.RunAsync(connection, transaction, index, session, names, income, cancellation);

            await ExecuteAsync(connection, transaction, InsertNight, [("$index", index), ("$night", Stamp(session)), ("$members", read.Members), ("$breadth", (object?)read.Breadth ?? DBNull.Value), ("$open", read.MarketOpen ? 1 : 0), ("$settings", Settings(index)), ("$rebalanced", heavyweights.Rebalanced ? 1 : 0)], cancellation);

            foreach (var answer in read.Answers)
            {
                await ExecuteAsync(connection, transaction, InsertResult,
                [
                    ("$index", index), ("$night", Stamp(session)), ("$ticker", answer.Ticker), ("$family", answer.Family), ("$passed", answer.Passed ? 1 : 0),
                    ("$place", (object?)answer.Place ?? DBNull.Value), ("$entry", Stored(answer.Entry)), ("$stop", Stored(answer.Stop)), ("$target", Stored(answer.Target)), ("$trail", Stored(answer.Trail)),
                    ("$cap", (object?)answer.Cap ?? DBNull.Value), ("$order_by", (object?)answer.OrderBy ?? DBNull.Value), ("$reason", (object?)answer.Reason ?? DBNull.Value),
                ], cancellation);
            }

            var answers = read.Answers.Where(answer => answer.Passed).ToDictionary(answer => (answer.Ticker, answer.Family));
            var kept = 0;

            foreach (var pick in picks)
            {
                await ExecuteAsync(connection, transaction, InsertPick,
                [
                    ("$index", index), ("$night", Stamp(session)), ("$ticker", pick.Ticker), ("$family", pick.Family), ("$state", pick.State),
                    ("$place", (object?)pick.Place ?? DBNull.Value), ("$also", JsonSerializer.Serialize(pick.Also)),
                    ("$held_index", pick.HeldBy is not null && heldIn.TryGetValue(pick.Ticker, out var heldIndex) ? heldIndex : DBNull.Value),
                    ("$held_family", (object?)pick.HeldBy?.Family ?? DBNull.Value),
                    ("$held_night", pick.HeldBy is { } held ? Stamp(held.Listed) : DBNull.Value),
                ], cancellation);

                if (pick.State == FamilyList.Listed && answers.TryGetValue((pick.Ticker, pick.Family), out var trade))
                {
                    kept++;
                    await ExecuteAsync(connection, transaction, InsertTrade,
                    [
                        ("$index", index), ("$family", pick.Family), ("$ticker", pick.Ticker), ("$night", Stamp(session)), ("$place", pick.Place!.Value),
                        ("$entry", Stored(trade.Entry)), ("$stop", Stored(trade.Stop)), ("$target", Stored(trade.Target)), ("$trail", Stored(trade.Trail)), ("$cap", trade.Cap!.Value),
                    ], cancellation);
                }
            }

            await transaction.CommitAsync(cancellation);

            var outcome = new IndexNightOutcome(
                index,
                read.Members,
                read.Breadth,
                read.MarketOpen,
                IndexNightRead.Families.ToDictionary(family => family, family => read.Answers.Count(answer => answer.Family == family && answer.Passed), StringComparer.Ordinal),
                picks.Count(pick => pick.State == FamilyList.Listed),
                picks.Count(pick => pick.State == FamilyList.OpenTrade),
                kept,
                ended,
                heavyweights);

            outcomes.Add(outcome);
            rows += 1 + read.Answers.Count + picks.Count + kept;
        }

        await AppendAsync(connection, runId, startedAt, rows, Detail(outcomes), cancellation);

        return new IndexFamiliesOutcome(session, outcomes);
    }

    // What the stage's row says for each index.
    public static string Detail(IReadOnlyList<IndexNightOutcome> outcomes) =>
        string.Join("; ", outcomes.Select(outcome => FormattableString.Invariant(
            $"{outcome.Index}: {outcome.Members} member(s) read, breadth {(outcome.Breadth is { } breadth ? breadth.ToString("0.00", CultureInfo.InvariantCulture) : "not read")}, the market check {(outcome.MarketOpen ? "open" : "closed")}, {string.Join(", ", outcome.Passed.Select(pair => $"{pair.Value} passed by the {pair.Key}"))}, {outcome.Listed} listed, {outcome.HeldByATrade} held back by a trade still open, {outcome.TradesKept} trade(s) kept, {outcome.TradesEnded} ended; the sector heavyweights {(outcome.Heavyweights.Rebalanced ? "rebalanced" : "carried")}, {outcome.Heavyweights.Entered} bought, {outcome.Heavyweights.Ended} sold, {outcome.Heavyweights.Held} held")));

    // The rule each family runs on in the index, the words a card's description is written from: its settings, its
    // floors and its gate.
    public static string Settings(string index) => JsonSerializer.Serialize(new
    {
        floors = new { price = MemberReadings.LowestPrice, dollarVolume = MemberReadings.DollarVolumeFloor(index) },
        profitGate = true,
        marketFloor = FamilySweep.MarketFloor,
        pullback = new { setting = SweepIdeas.BaseRule.Setting.Describe(SweepGrid.Extended), cap = IndexNightRead.PullbackCap },
        breakout = BreakoutSweep.Grid.Key(IndexNightRead.BreakoutAsFrozen),
        drift = DriftSweep.Grid.Key(IndexNightRead.DriftAsFrozen),
    });

    // Each member's year of bars, every span the index held it for, its prints and surprises off the calendar, and its
    // quarters as filed.
    static async Task<(IReadOnlyList<SweepName> Names, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income)> InputsAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        var tickers = new List<string>();

        await foreach (var row in RowsAsync(connection, MembersOf, [("$index", index), ("$session", Stamp(night))], cancellation))
        {
            if (!tickers.Contains(row.GetString(0), StringComparer.Ordinal))
            {
                tickers.Add(row.GetString(0));
            }
        }

        var names = new List<SweepName>();
        var income = new Dictionary<string, IReadOnlyList<FiledIncome>>(StringComparer.Ordinal);

        foreach (var ticker in tickers)
        {
            var spans = new List<(DateOnly?, DateOnly?)>();
            var bars = new List<SweepBar>();
            var prints = new List<DateOnly>();
            var surprises = new List<SweepSurprise>();
            var quarters = new List<FiledIncome>();

            await foreach (var row in RowsAsync(connection, SpansOf, [("$index", index), ("$ticker", ticker)], cancellation))
            {
                spans.Add((row.IsDBNull(0) ? null : Date(row.GetString(0)), row.IsDBNull(1) ? null : Date(row.GetString(1))));
            }

            await foreach (var row in RowsAsync(connection, BarsOf, [("$ticker", ticker), ("$night", Stamp(night))], cancellation))
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

            await foreach (var row in RowsAsync(connection, PrintsOf, [("$ticker", ticker)], cancellation))
            {
                var date = Date(row.GetString(0));

                prints.Add(date);

                if (SweepHistory.SurpriseIn(row.GetString(2)) is { } percent)
                {
                    surprises.Add(new SweepSurprise(date, row.GetString(1) == "after", percent));
                }
            }

            await foreach (var row in RowsAsync(connection, QuartersOf, [("$ticker", ticker)], cancellation))
            {
                quarters.Add(new FiledIncome(
                    Date(row.GetString(0)),
                    Date(row.GetString(1)),
                    row.IsDBNull(2) ? null : Money.FromStorage(row.GetString(2)),
                    row.IsDBNull(3) ? null : Money.FromStorage(row.GetString(3)),
                    null));
            }

            names.Add(new SweepName(ticker, [.. bars], spans, [.. prints], null, surprises));
            income[ticker] = quarters;
        }

        return (names, income);
    }

    // The trade each of the names still holds on the night on any card of any index, with the index it is held in: the
    // S&P 500's by its own list's rule, and the S&P 400's and 600's while not ended or ended on the night itself.
    static async Task<(IReadOnlyDictionary<string, HeldTrade> Open, IReadOnlyDictionary<string, string> Index)> OpenAsync(SqliteConnection connection, DateOnly night, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        var open = new Dictionary<string, HeldTrade>(StringComparer.Ordinal);
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (ticker, held) in await FamilyLister.OpenTradesAsync(connection, night, tickers, cancellation))
        {
            open[ticker] = held;
            index[ticker] = LargeIndex;
        }

        var (elsewhere, heldIn) = await OpenIndexTradesAsync(connection, night, tickers, cancellation);

        foreach (var (ticker, held) in elsewhere)
        {
            if (open.TryAdd(ticker, held))
            {
                index[ticker] = heldIn[ticker];
            }
        }

        return (open, index);
    }

    // The code an S&P 500 card's trade is named by where a list of the S&P 400 or 600 holds a stock back for it.
    public const string LargeIndex = "GSPC";

    // The trade each of the names still holds on the night on an S&P 400 or 600 list, with that list's index: a trade
    // listed before the night that has not ended, or ended on the night itself, since a stock is free the night after
    // its trade ends. The S&P 500's list reads it to hold such a stock back too.
    // see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
    public static async Task<(IReadOnlyDictionary<string, HeldTrade> Open, IReadOnlyDictionary<string, string> Index)> OpenIndexTradesAsync(SqliteConnection connection, DateOnly night, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        var open = new Dictionary<string, HeldTrade>(StringComparer.Ordinal);
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        var wanted = tickers.ToHashSet(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, IndexTrades, [("$night", Stamp(night))], cancellation))
        {
            var ticker = row.GetString(2);
            var ended = row.IsDBNull(4) ? (DateOnly?)null : Date(row.GetString(4));

            if (!wanted.Contains(ticker) || open.ContainsKey(ticker) || ended is { } on && on < night)
            {
                continue;
            }

            open[ticker] = new HeldTrade(row.GetString(1), Date(row.GetString(3)));
            index[ticker] = row.GetString(0);
        }

        return (open, index);
    }

    // Each of the index's trades not yet ended, walked over the member's closes since its night at the scale the
    // series has now, written where it ended with its result and its cost; the count ended.
    async Task<int> WalkAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        var open = new List<(string Family, string Ticker, DateOnly Session, decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap)>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = OpenIndexTrades;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$night", Stamp(night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                open.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    Date(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3)),
                    Money.FromStorage(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : Money.FromStorage(reader.GetString(6)),
                    reader.GetInt32(7)));
            }
        }

        var ended = 0;

        foreach (var trade in open)
        {
            var bars = new List<(DateOnly Session, decimal Close, decimal Raw)>();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT session_date, close, raw_close FROM bar WHERE ticker = $ticker AND session_date >= $from AND session_date <= $night ORDER BY session_date;";
                command.Parameters.AddWithValue("$ticker", trade.Ticker);
                command.Parameters.AddWithValue("$from", Stamp(trade.Session));
                command.Parameters.AddWithValue("$night", Stamp(night));

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    bars.Add((Date(reader.GetString(0)), Money.FromStorage(reader.GetString(1)), reader.IsDBNull(2) ? 0m : Money.FromStorage(reader.GetString(2))));
                }
            }

            if (bars.Count == 0 || bars[0].Session != trade.Session || bars[0].Close <= 0m)
            {
                continue;
            }

            // The series' scale now over the scale its night stored the trade at.
            var scale = Statistic.FromRatio(bars[0].Close / trade.Entry);
            var closes = bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray();
            var entry = Statistic.FromPrice(trade.Entry) * scale;
            var stop = Statistic.FromPrice(trade.Stop) * scale;
            int sessions;
            double? result;

            if (trade.Trail is { } trail)
            {
                result = FamilyWalks.Trailing(closes, 0, entry, stop, Statistic.FromPrice(trail) * scale, trade.Cap, out sessions);
            }
            else
            {
                result = FamilyWalks.Fixed(closes, 0, entry, stop, Statistic.FromPrice(trade.Target ?? trade.Entry) * scale, trade.Cap, out sessions);
            }

            if (result is not { } multiple)
            {
                continue;
            }

            var sale = bars[sessions];
            var cost = TradeCost.InPercent(null, bars[0].Raw > 0m ? bars[0].Raw : bars[0].Close, sale.Raw > 0m ? sale.Raw : sale.Close, 1) / 100.0 / ((entry - stop) / entry);

            await ExecuteAsync(connection, transaction, EndTrade,
            [
                ("$index", index), ("$family", trade.Family), ("$ticker", trade.Ticker), ("$session", Stamp(trade.Session)),
                ("$ended_on", Stamp(sale.Session)), ("$result", multiple), ("$cost", cost),
            ], cancellation);
            ended++;
        }

        return ended;
    }

    static object Stored(decimal? value) => value is { } price ? Money.ToStorage(price) : DBNull.Value;

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

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellation);
    }

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

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

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// What an index's sector heavyweights did on the night: whether the book rebalanced, and the holdings bought, ended
// and held at the close.
public sealed record IndexHeavyweightsOutcome(bool Rebalanced, int Entered, int Ended, int Held);

// The S&P 400's and 600's sector heavyweights on their provisional settings, design (a) read within each index with the
// sweep's own code, kept by the index families in their step: on the first night of a month the book reads, each
// sector's ten largest members of the index by value as it stood, its return its members' mean and a leader's beta at
// least one against the index's fund, the two leading their sector above nothing bought among the members clearing the
// floors and the profit gate, and a holding the rule would no longer buy sold at the close; every night each holding and
// its size cut carried by the night's closes, and a holding whose stock left the index sold at its last close as a
// member. A holding's result is its growth less one, beside its size cut's, and its cost the published table's round
// trip.
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
        [FundHoldings.MidCapIndex] = "IJH",
        [FundHoldings.SmallCapIndex] = "IJR",
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
        double? cutReturn = cut.Count > 0 ? cut.Average(one => one.Growth) - 1.0 : null;
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
