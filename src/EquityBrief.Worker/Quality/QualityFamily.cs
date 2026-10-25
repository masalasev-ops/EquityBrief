using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Quality;

// What Quality at a fair price's step did on one index: the members it read, how many held a scorecard read and how many
// qualified, how many it holds after the night, whether it reshuffled, what it chose and sold, how many of its buys wait
// for their next session, or the failure that stopped its part.
public sealed record QualityIndexOutcome(string Index, int Members, int Scored, int Qualified, int Held, bool Reshuffled, IReadOnlyList<string> Chosen, IReadOnlyList<string> Sold, int Waiting, string? Fault = null);

// What the step did on the night, an index at a time.
public sealed record QualityOutcome(DateOnly? Night, IReadOnlyList<QualityIndexOutcome> Indices);

// Quality at a fair price's part of the night, a step of its own after the filings refresh has scored every member again,
// over every member of the S&P 500, 400 and 600 holding a bar on the night: every member's answer stored with the
// figures that decided it, its scorecard as the night's last scoring stored it and its price against its own earnings as
// the ledger's valuation reading stored it, read and never worked out here; on the first night it reads and on the first
// night of each month after, the holdings that no longer qualify sold at the night's close and the places left filled
// down the ranking; a holding whose stock left the index sold at its last close as a member; each choice bought at its
// next session's open through the core's one pricing function; and each holding's growth carried night by night by the
// closes as the store holds them, with each member's beside it on its answer, so a holding's benchmark is an
// equal-weighted hold of the members answered on the night it was chosen. It reads no pulled history, makes no request,
// calls no model, reads no market check and writes only its own tables. Each index's part is one transaction, so a
// failure in it leaves none of its writes and is named on its row while the other indices go on.
// see: Quality at a fair price holds the companies the scorecard rates strongest that are not dear against their own history, reshuffled on each month's first session
// see: A quality holding is sold at a reshuffle where it no longer qualifies and at its last close as a member, and is bought at the next open
// see: A quality holding's edge is its return less an equal-weighted hold of its index's members over the same sessions, after its round trip
// see: The nightly run is arithmetic only
public sealed class QualityFamily : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.FundamentalScore, Touch.Read),
            new StoreTouch(Store.LedgerReading, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.QualityNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.QualityResult, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.QualityHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "quality";

    public const string Ok = "ok";

    public const string NotComputed = "not computed";

    // The rule the holdings are kept under before a freeze registers one.
    public const string ProvisionalRule = "provisional";

    // Why a holding ended, as the step stores it and the card draws it.
    public const string NoLongerQualifies = "no longer qualifies";

    public const string LeftTheIndex = "left the index";

    // The indices the step reads, in the page's order of indices.
    public static IReadOnlyList<string> AllIndices { get; } = [IndexFamilies.LargeIndex, .. IndexFamilies.Indices];

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string MembersOn = @"
        SELECT DISTINCT ticker FROM membership
        WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string Bars = "SELECT ticker, session_date, open, close, raw_close FROM bar WHERE session_date <= $night ORDER BY ticker, session_date;";

    const string ScoresOn = "SELECT ticker, passed, applied FROM fundamental_score WHERE index_code = $index AND session_date = $night;";

    const string ReadingsOn = "SELECT ticker, readings FROM ledger_reading WHERE index_code = $index AND session_date = $night;";

    const string ValueOn = "SELECT company_value FROM member_reading WHERE index_code = $index AND session_date = $session AND ticker = $ticker;";

    // The newest night before this one the step read without a failure, and its newest reshuffle before it.
    const string NightBefore = "SELECT MAX(session_date) FROM quality_night WHERE rule = $rule AND index_code = $index AND session_date < $night AND fault IS NULL;";

    const string ReshuffleBefore = "SELECT MAX(session_date) FROM quality_night WHERE rule = $rule AND index_code = $index AND session_date < $night AND reshuffle = 1 AND fault IS NULL;";

    // Every member's carried growth on a night.
    const string CarriedOn = "SELECT ticker, carried FROM quality_result WHERE rule = $rule AND index_code = $index AND session_date = $session;";

    // A member's newest answer on or before a night, for a member the night before holds none for.
    const string NewestCarried = @"
        SELECT session_date, carried FROM quality_result
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND session_date <= $session
        ORDER BY session_date DESC LIMIT 1;
    ";

    const string ClearTheNight = @"
        DELETE FROM quality_night WHERE rule = $rule AND index_code = $index AND session_date = $night;
        DELETE FROM quality_result WHERE rule = $rule AND index_code = $index AND session_date = $night;
        DELETE FROM quality_holding WHERE rule = $rule AND index_code = $index AND chosen_on = $night;
        UPDATE quality_holding SET ended_on = NULL, sale = NULL, end_reason = 'open', result = NULL, benchmark = NULL, cost = NULL, edge = NULL
        WHERE rule = $rule AND index_code = $index AND ended_on = $night;
    ";

    const string OpenHoldings = @"
        SELECT ticker, chosen_on, buy_close, bought_on, fill, gap, bench_gap, growth, through
        FROM quality_holding WHERE rule = $rule AND index_code = $index AND end_reason = 'open'
        ORDER BY chosen_on, ticker;
    ";

    const string InsertNight = @"
        INSERT INTO quality_night (rule, index_code, session_date, members, scored, qualified, held, reshuffle, settings, fault)
        VALUES ($rule, $index, $night, $members, $scored, $qualified, $held, $reshuffle, $settings, NULL);
    ";

    const string InsertNotComputed = @"
        INSERT OR IGNORE INTO quality_night (rule, index_code, session_date, members, scored, qualified, held, reshuffle, settings, fault)
        VALUES ($rule, $index, $night, 0, 0, 0, 0, 0, $settings, $fault);
    ";

    const string InsertResult = @"
        INSERT INTO quality_result (rule, index_code, session_date, ticker, passed, place, passed_checks, applied, value_place, reason, carried)
        VALUES ($rule, $index, $night, $ticker, $passed, $place, $passed_checks, $applied, $value_place, $reason, $carried);
    ";

    const string InsertHolding = @"
        INSERT INTO quality_holding (rule, index_code, ticker, chosen_on, buy_close, growth, through, end_reason)
        VALUES ($rule, $index, $ticker, $night, $buy_close, 1.0, $night, 'open');
    ";

    const string CarryHolding = @"
        UPDATE quality_holding SET growth = $growth, through = $through
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND chosen_on = $chosen_on;
    ";

    const string FillHolding = @"
        UPDATE quality_holding SET bought_on = $bought_on, fill = $fill, gap = $gap, bench_gap = $bench_gap, members = $members
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND chosen_on = $chosen_on;
    ";

    const string EndHolding = @"
        UPDATE quality_holding SET ended_on = $ended_on, sale = $sale, end_reason = $end, result = $result, benchmark = $benchmark, cost = $cost, edge = $edge
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND chosen_on = $chosen_on;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    // One stored bar as the step reads it: its open, its close and its close as traded, the first two at the store's scale.
    sealed record Daily(decimal Open, decimal Close, decimal Raw);

    // One member's answer on the night, with the growth carried to it.
    sealed record Answered(string Ticker, int Passed, int Applied, double? Place, QualityAnswer Answer, double Carried);

    // One open holding as stored.
    sealed record Holding(string Ticker, DateOnly ChosenOn, decimal BuyClose, DateOnly? BoughtOn, decimal? Fill, double? Gap, double? BenchGap, double Growth, DateOnly Through);

    readonly IClock clock;
    readonly string databaseFile;

    public QualityFamily(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The step over the three indices, each index's failure named on its own row while the others go on, and a failure
    // outside every index's part named on the step's row: the family never stops the night.
    public async Task<QualityOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var outcomes = new List<QualityIndexOutcome>();
        DateOnly? read = null;
        var rows = 0;

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            if (await ScalarAsync(connection, null, NewestSession, [], cancellation) is not string newest)
            {
                await AppendAsync(connection, runId, startedAt, 0, Ok, "no session: the store holds no bar", cancellation);

                return new QualityOutcome(null, []);
            }

            var night = Date(newest);
            var bars = await BarsAsync(connection, night, cancellation);

            read = night;

            foreach (var index in AllIndices)
            {
                try
                {
                    var (outcome, written) = await IndexNightAsync(connection, index, night, bars, cancellation);

                    outcomes.Add(outcome);
                    rows += written;
                }
                catch (Exception failure) when (!cancellation.IsCancellationRequested)
                {
                    var fault = IndexFamilies.Cause(failure);

                    rows += await NotComputedAsync(connection, index, night, fault, cancellation);
                    outcomes.Add(new QualityIndexOutcome(index, 0, 0, 0, 0, false, [], [], 0, fault));
                }
            }

            await AppendAsync(connection, runId, startedAt, rows, outcomes.Any(outcome => outcome.Fault is not null) ? NotComputed : Ok, Detail(outcomes), cancellation);

            return new QualityOutcome(night, outcomes);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            var fault = IndexFamilies.Cause(failure);
            QualityIndexOutcome[] all =
            [
                .. outcomes,
                .. AllIndices.Where(index => outcomes.All(outcome => outcome.Index != index)).Select(index => new QualityIndexOutcome(index, 0, 0, 0, 0, false, [], [], 0, fault)),
            ];

            await AppendAfterAFailureAsync(runId, startedAt, rows, Detail(all) + "; the step stopped on " + fault, cancellation);

            return new QualityOutcome(read, all);
        }
    }

    // What the step's row says for each index.
    public static string Detail(IReadOnlyList<QualityIndexOutcome> outcomes) =>
        string.Join("; ", outcomes.Select(outcome => outcome.Fault is { } fault
            ? $"{outcome.Index}: not computed tonight, {fault}"
            : FormattableString.Invariant(
                $"{outcome.Index}: {outcome.Members} member(s) read, {outcome.Scored} scored, {outcome.Qualified} qualified, {(outcome.Reshuffled ? "reshuffled" : "no reshuffle")}, {outcome.Chosen.Count} chosen, {outcome.Sold.Count} sold, {outcome.Held} held, {outcome.Waiting} waiting for their next session")));

    // The settings the night read the family by, and the words its card states the rule in.
    public static string Settings(QualitySettings settings) => JsonSerializer.Serialize(new
    {
        rule = ProvisionalRule,
        floor = settings.ScoreFloorInNine,
        valuation = settings.Valuation.ToString(),
        trend = settings.Trend,
        holdings = settings.Holdings,
        sellOnFiling = settings.SellOnFiling,
        words = settings.Words,
    });

    // One index's part of the night, in one transaction: every member's answer with its carried growth, the holdings
    // carried, bought, sold and chosen, and the night's row.
    async Task<(QualityIndexOutcome Outcome, int Rows)> IndexNightAsync(
        SqliteConnection connection,
        string index,
        DateOnly night,
        IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars,
        CancellationToken cancellation)
    {
        var settings = QualityRule.Starting;
        var members = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, MembersOn, [("$index", index), ("$session", Stamp(night))], cancellation))
        {
            members.Add(row.GetString(0));
        }

        var scores = new Dictionary<string, (int Passed, int Applied)>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, ScoresOn, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            scores[row.GetString(0)] = (row.GetInt32(1), row.GetInt32(2));
        }

        var places = new Dictionary<string, double?>(StringComparer.Ordinal);
        var at = LedgerReadings.IndexOf("value_place");

        await foreach (var row in RowsAsync(connection, null, ReadingsOn, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            places[row.GetString(0)] = !row.IsDBNull(1) && JsonSerializer.Deserialize<double?[]>(row.GetString(1)) is { } readings && readings.Length > at ? readings[at] : null;
        }

        // Each member's growth carried from its answer the night before, or from its own newest answer where the night
        // before holds none for it, by tonight's close over its close on that answer's night as the store holds both
        // tonight; and from one where it has none.
        var before = await ScalarAsync(connection, null, NightBefore, [("$rule", ProvisionalRule), ("$index", index), ("$night", Stamp(night))], cancellation) is string stored ? Date(stored) : (DateOnly?)null;
        var last = new Dictionary<string, (DateOnly Session, double Carried)>(StringComparer.Ordinal);

        if (before is { } previous)
        {
            await foreach (var row in RowsAsync(connection, null, CarriedOn, [("$rule", ProvisionalRule), ("$index", index), ("$session", Stamp(previous))], cancellation))
            {
                last[row.GetString(0)] = (previous, row.GetDouble(1));
            }

            foreach (var ticker in members.Where(ticker => !last.ContainsKey(ticker)).ToArray())
            {
                await foreach (var row in RowsAsync(connection, null, NewestCarried, [("$rule", ProvisionalRule), ("$index", index), ("$ticker", ticker), ("$session", Stamp(previous))], cancellation))
                {
                    last[ticker] = (Date(row.GetString(0)), row.GetDouble(1));
                }
            }
        }

        var answered = new List<Answered>();

        foreach (var ticker in members.Order(StringComparer.Ordinal))
        {
            if (Close(bars, ticker, night) is not { } close)
            {
                continue;
            }

            var (passed, applied) = scores.GetValueOrDefault(ticker);
            var place = places.GetValueOrDefault(ticker);
            var carried = last.TryGetValue(ticker, out var then)
                ? Close(bars, ticker, then.Session) is { } earlier && earlier > 0m ? then.Carried * Statistic.FromRatio(close / earlier) : then.Carried
                : 1.0;

            answered.Add(new Answered(ticker, passed, applied, place, QualityRule.Answer(passed, applied, place, null, settings), carried));
        }

        var ranked = QualityRule.Ranked(answered.Where(one => one.Answer.Passed), one => (one.Passed, one.Applied, one.Place, one.Ticker));
        var rank = ranked.Select((one, place) => (one.Ticker, Place: place + 1)).ToDictionary(pair => pair.Ticker, pair => pair.Place, StringComparer.Ordinal);
        var tonight = answered.ToDictionary(one => one.Ticker, StringComparer.Ordinal);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheNight, [("$rule", ProvisionalRule), ("$index", index), ("$night", Stamp(night))], cancellation);

        var lastReshuffle = await ScalarAsync(connection, transaction, ReshuffleBefore, [("$rule", ProvisionalRule), ("$index", index), ("$night", Stamp(night))], cancellation) is string reshuffled ? Date(reshuffled) : (DateOnly?)null;
        var reshuffle = QualityRule.Reshuffles(night, lastReshuffle);
        var holdings = new List<Holding>();

        await foreach (var row in RowsAsync(connection, transaction, OpenHoldings, [("$rule", ProvisionalRule), ("$index", index)], cancellation))
        {
            holdings.Add(new Holding(
                row.GetString(0),
                Date(row.GetString(1)),
                Money.FromStorage(row.GetString(2)),
                row.IsDBNull(3) ? null : Date(row.GetString(3)),
                row.IsDBNull(4) ? null : Money.FromStorage(row.GetString(4)),
                row.IsDBNull(5) ? null : row.GetDouble(5),
                row.IsDBNull(6) ? null : row.GetDouble(6),
                row.GetDouble(7),
                Date(row.GetString(8))));
        }

        var sold = new List<string>();
        var waiting = 0;
        var written = 0;
        var kept = 0;

        foreach (var held in holdings)
        {
            var holding = held;

            // A stock no longer a member is carried no further: it is sold at its last close as one, the close it was
            // carried to.
            var gone = !members.Contains(holding.Ticker);

            // Carried to tonight by tonight's close over its close on the session it was carried to.
            if (!gone && holding.Through < night && Close(bars, holding.Ticker, holding.Through) is { } then && then > 0m && Close(bars, holding.Ticker, night) is { } now)
            {
                holding = holding with { Growth = holding.Growth * Statistic.FromRatio(now / then), Through = night };
                await ExecuteAsync(connection, transaction, CarryHolding,
                [
                    ("$rule", ProvisionalRule), ("$index", index), ("$ticker", holding.Ticker), ("$chosen_on", Stamp(holding.ChosenOn)),
                    ("$growth", holding.Growth), ("$through", Stamp(holding.Through)),
                ], cancellation);
                written++;
            }

            // Bought at the open of the first session after its choice the store holds, through the core's one pricing
            // function; one whose next session is not stored yet waits for it.
            if (!gone && holding.BoughtOn is null)
            {
                if (await FillAsync(connection, transaction, index, holding, bars, cancellation) is { } filled)
                {
                    holding = filled;
                    written++;
                }
                else
                {
                    waiting++;
                }
            }

            // On a reshuffle one that no longer qualifies is sold at tonight's close; one with no close stored tonight was
            // not answered and is kept.
            var failing = reshuffle && !gone && tonight.TryGetValue(holding.Ticker, out var answer) && !answer.Answer.Passed;

            if (gone || failing)
            {
                await EndAsync(connection, transaction, index, holding, gone ? holding.Through : night, gone ? LeftTheIndex : NoLongerQualifies, bars, tonight, night, cancellation);
                sold.Add(holding.Ticker);
                written++;

                continue;
            }

            kept++;
        }

        // The places left filled down the ranking, each chosen at tonight's close.
        var chosen = new List<string>();

        if (reshuffle)
        {
            var heldNow = holdings.Select(holding => holding.Ticker).Where(ticker => !sold.Contains(ticker, StringComparer.Ordinal)).ToHashSet(StringComparer.Ordinal);

            foreach (var one in ranked.Where(one => !heldNow.Contains(one.Ticker)).Take(Math.Max(0, settings.Holdings - kept)))
            {
                await ExecuteAsync(connection, transaction, InsertHolding,
                [
                    ("$rule", ProvisionalRule), ("$index", index), ("$ticker", one.Ticker), ("$night", Stamp(night)),
                    ("$buy_close", Money.ToStorage(Close(bars, one.Ticker, night)!.Value)),
                ], cancellation);
                chosen.Add(one.Ticker);
                written++;
            }
        }

        foreach (var one in answered)
        {
            await ExecuteAsync(connection, transaction, InsertResult,
            [
                ("$rule", ProvisionalRule), ("$index", index), ("$night", Stamp(night)), ("$ticker", one.Ticker),
                ("$passed", one.Answer.Passed ? 1 : 0), ("$place", rank.TryGetValue(one.Ticker, out var place) ? place : DBNull.Value),
                ("$passed_checks", one.Passed), ("$applied", one.Applied), ("$value_place", (object?)one.Place ?? DBNull.Value),
                ("$reason", (object?)one.Answer.Reason ?? DBNull.Value), ("$carried", one.Carried),
            ], cancellation);
        }

        var scored = answered.Count(one => one.Applied > 0);

        await ExecuteAsync(connection, transaction, InsertNight,
        [
            ("$rule", ProvisionalRule), ("$index", index), ("$night", Stamp(night)), ("$members", members.Count),
            ("$scored", scored), ("$qualified", ranked.Count), ("$held", kept + chosen.Count),
            ("$reshuffle", reshuffle ? 1 : 0), ("$settings", Settings(settings)),
        ], cancellation);

        await transaction.CommitAsync(cancellation);

        return (
            new QualityIndexOutcome(index, members.Count, scored, ranked.Count, kept + chosen.Count, reshuffle, chosen, sold, waiting),
            1 + answered.Count + written);
    }

    // A holding's buy at the open of the first session after its choice the store holds, and its benchmark's gap over the
    // members answered on the night it was chosen, each at the open of its own first session after that night, both
    // through the core's one pricing function; none where the store holds no later session of the stock's yet.
    async Task<Holding?> FillAsync(SqliteConnection connection, SqliteTransaction transaction, string index, Holding holding, IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, CancellationToken cancellation)
    {
        if (After(bars, holding.Ticker, holding.ChosenOn) is not { } next || Close(bars, holding.Ticker, holding.ChosenOn) is not { } close)
        {
            return null;
        }

        var members = new List<FillMember>();

        await foreach (var row in RowsAsync(connection, transaction, CarriedOn, [("$rule", ProvisionalRule), ("$index", index), ("$session", Stamp(holding.ChosenOn))], cancellation))
        {
            var ticker = row.GetString(0);

            if (Close(bars, ticker, holding.ChosenOn) is { } then && then > 0m)
            {
                var price = Statistic.FromPrice(then);

                members.Add(new FillMember(true, price, price, After(bars, ticker, holding.ChosenOn) is { } opened ? Statistic.FromPrice(opened.Day.Open) : null));
            }
        }

        if (TradeFills.Holding(next.Session, Statistic.FromPrice(close), Statistic.FromPrice(next.Day.Open), 0.0, 0.0, members) is not { } fill)
        {
            return null;
        }

        var filled = holding with
        {
            BoughtOn = next.Session,
            Fill = next.Day.Open,
            Gap = fill.Result is { } result ? -result : null,
            BenchGap = fill.Benchmark is { } benchmark ? -benchmark : null,
        };

        await ExecuteAsync(connection, transaction, FillHolding,
        [
            ("$rule", ProvisionalRule), ("$index", index), ("$ticker", holding.Ticker), ("$chosen_on", Stamp(holding.ChosenOn)),
            ("$bought_on", Stamp(next.Session)), ("$fill", Money.ToStorage(next.Day.Open)),
            ("$gap", (object?)filled.Gap ?? DBNull.Value), ("$bench_gap", (object?)filled.BenchGap ?? DBNull.Value), ("$members", fill.Members),
        ], cancellation);

        return filled;
    }

    // A holding sold at a close: its result its growth less one less its gap at the open; its benchmark the mean, over the
    // members answered on the night it was chosen, of each one's carried growth on the night it was sold over its growth on
    // that night, less one less their gap; its round trip at its company's value on the night it was chosen and its prices
    // as traded; and its edge its result less its benchmark, less its round trip, in percentage points. One sold before it
    // was bought holds none of them.
    async Task EndAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        Holding holding,
        DateOnly on,
        string reason,
        IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars,
        IReadOnlyDictionary<string, Answered> tonight,
        DateOnly night,
        CancellationToken cancellation)
    {
        var sale = DayOf(bars, holding.Ticker, on);
        double? result = null;
        double? benchmark = null;
        double? cost = null;
        double? edge = null;

        if (holding is { BoughtOn: { } bought, Fill: { } fill, Gap: { } gap } && sale is { } sold)
        {
            result = holding.Growth - 1.0 - gap;

            var growths = new List<double>();

            await foreach (var row in RowsAsync(connection, transaction, CarriedOn, [("$rule", ProvisionalRule), ("$index", index), ("$session", Stamp(holding.ChosenOn))], cancellation))
            {
                var ticker = row.GetString(0);
                var start = row.GetDouble(1);
                double? end = on == night && tonight.TryGetValue(ticker, out var now) ? now.Carried : null;

                if (end is null)
                {
                    await foreach (var newest in RowsAsync(connection, transaction, NewestCarried, [("$rule", ProvisionalRule), ("$index", index), ("$ticker", ticker), ("$session", Stamp(on))], cancellation))
                    {
                        end = newest.GetDouble(1);
                    }
                }

                if (start > 0 && end is { } reached)
                {
                    growths.Add(reached / start);
                }
            }

            benchmark = growths.Count > 0 && holding.BenchGap is { } benchGap ? growths.Average() - 1.0 - benchGap : null;

            decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", index), ("$session", Stamp(holding.ChosenOn)), ("$ticker", holding.Ticker)], cancellation) is string stored ? Money.FromStorage(stored) : null;
            var entry = DayOf(bars, holding.Ticker, bought) is { Close: > 0m } boughtDay ? fill * boughtDay.Raw / boughtDay.Close : fill;
            var exit = sold.Raw > 0m ? sold.Raw : sold.Close;
            var trip = TradeCost.InPercent(value, entry, exit);

            cost = double.IsNaN(trip) ? null : trip;
            edge = benchmark is { } against && cost is { } paid ? ((result.Value - against) * 100.0) - paid : null;
        }

        await ExecuteAsync(connection, transaction, EndHolding,
        [
            ("$rule", ProvisionalRule), ("$index", index), ("$ticker", holding.Ticker), ("$chosen_on", Stamp(holding.ChosenOn)),
            ("$ended_on", Stamp(on)), ("$sale", sale is { } day ? Money.ToStorage(day.Close) : DBNull.Value), ("$end", reason),
            ("$result", (object?)result ?? DBNull.Value), ("$benchmark", (object?)benchmark ?? DBNull.Value),
            ("$cost", (object?)cost ?? DBNull.Value), ("$edge", (object?)edge ?? DBNull.Value),
        ], cancellation);
    }

    // Every stored bar to the night, each stock's by session.
    static async Task<IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>>> BarsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        var bars = new Dictionary<string, SortedDictionary<DateOnly, Daily>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, Bars, [("$night", Stamp(night))], cancellation))
        {
            var ticker = row.GetString(0);

            if (!bars.TryGetValue(ticker, out var days))
            {
                days = [];
                bars[ticker] = days;
            }

            var close = Money.FromStorage(row.GetString(3));

            days[Date(row.GetString(1))] = new Daily(Money.FromStorage(row.GetString(2)), close, row.IsDBNull(4) ? close : Money.FromStorage(row.GetString(4)));
        }

        return bars;
    }

    static Daily? DayOf(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker, DateOnly session) =>
        bars.TryGetValue(ticker, out var days) && days.TryGetValue(session, out var day) ? day : null;

    static decimal? Close(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker, DateOnly session) =>
        DayOf(bars, ticker, session)?.Close;

    // The first session after one the store holds a bar of the stock for, none where it holds no later one.
    static (DateOnly Session, Daily Day)? After(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker, DateOnly session)
    {
        if (!bars.TryGetValue(ticker, out var days))
        {
            return null;
        }

        foreach (var (day, bar) in days)
        {
            if (day > session)
            {
                return (day, bar);
            }
        }

        return null;
    }

    // An index whose part of the night failed: a night row naming the failure where no earlier try of the night wrote one,
    // and none where the store will not take it, since recording the failure is never what stops the night.
    async Task<int> NotComputedAsync(SqliteConnection connection, string index, DateOnly night, string fault, CancellationToken cancellation)
    {
        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText = InsertNotComputed;
            command.Parameters.AddWithValue("$rule", ProvisionalRule);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$night", Stamp(night));
            command.Parameters.AddWithValue("$settings", Settings(QualityRule.Starting));
            command.Parameters.AddWithValue("$fault", fault);

            return await command.ExecuteNonQueryAsync(cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            return 0;
        }
    }

    // The step's row after a failure outside any index's part, on a connection of its own, and none where the store will
    // not take it.
    async Task AppendAfterAFailureAsync(string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await AppendAsync(connection, runId, startedAt, rows, NotComputed, detail, cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
        }
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int rows, string outcome, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
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

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
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

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
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
