using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Volume;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// One stock's bars as a plan trade reads them, oldest first at the scale they are stored at now.
public sealed record PlanBars(DateOnly[] Sessions, double[] Opens, double[] Lows, double[] Closes)
{
    public int IndexOf(DateOnly session) => Array.BinarySearch(Sessions, session);

    public static PlanBars Of(IReadOnlyList<SweepBar> bars) =>
        new(
            [.. bars.Select(bar => bar.Session)],
            [.. bars.Select(bar => Statistic.FromPrice(bar.Open))],
            [.. bars.Select(bar => Statistic.FromPrice(bar.Low))],
            [.. bars.Select(bar => Statistic.FromPrice(bar.Close))]);
}

// Where a plan trade ended: the session its sale's signal stood on, or the last session of its window where it never filled;
// its result in the risk the rule's own plan set at its night's close, none where it never filled; and its fill and sale.
public sealed record PlanEnd(DateOnly On, double? Result, DateOnly? FilledOn, double? Fill, DateOnly? SoldOn, double? Sold, string End)
{
    // How a trade its plan never filled ended, beside the plan's own ends.
    public const string Unfilled = "unfilled";
}

// Where a plan trade stands once it is no longer waiting: its fill's session and price at its night's scale, none where it
// never filled, and its end once it has one.
public sealed record PlanWhere(DateOnly? FilledOn, double? Fill, PlanEnd? End);

// The levels plan as of a setup's session: each zone's top and its own stop, the staged sales' levels and the low edges
// of the resistance bands with real touches.
public sealed record LevelsAsOf(IReadOnlyList<(double Top, double? Stop)> Zones, IReadOnlyList<double> Staged, IReadOnlyList<double> Bands, string? Why);

// From 23.10 a rule's trades under the plan its registration states: the order placed on its night from the stock's own bars,
// as the test of the buy and sell points and a watched card place it; each walked over the bars since, filled where the test
// fills and sold where it sells; and benchmarked on every member that night under the same plan, each bought at its own next
// open, as the test's benchmark is.
// see: At phase 23's restart each price family's live rule takes the plan its long test recommends where it beats today's plan from 2019 above the ruled benchmark, today's plan kept beside it as a variant
public static class PlanTrades
{
    const string BarsOf = @"
        SELECT session_date, open, high, low, close, volume FROM bar
        WHERE ticker = $ticker AND session_date <= $through
        ORDER BY session_date;
    ";

    const string EveryBarThrough = @"
        SELECT ticker, session_date, open, high, low, close, volume FROM bar
        WHERE session_date <= $through
        ORDER BY ticker, session_date;
    ";

    // From the operator's order of 2026-11-14 the newest pull holding a stock's first stored session and its close there, and
    // that pull's highs and lows before it over the take-profit's years, as marked, which the take-profit's turned-down prices
    // are read from at the store's scale.
    // see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
    const string PullHoldingFirst = "SELECT pull, close FROM pulled_bar WHERE ticker = $ticker AND session_date = $first ORDER BY pull DESC LIMIT 1;";

    const string PulledHighsBefore = @"
        SELECT session_date, high, low FROM pulled_bar
        WHERE ticker = $ticker AND pull = $pull AND session_date < $first AND session_date >= $from AND " + Core.Chart.SessionMarking.AsMarked + @"
        ORDER BY session_date;
    ";

    // The prices a stock turned down at before a night over the take-profit's years: the swing highs of its stored bars and of
    // the newest pull's sessions before them, brought to the store's scale by the two closes of the first stored session, each
    // confirmed by the night; the stored bars alone where no pull holds that session.
    public static async Task<IReadOnlyList<TurnedDown>> TurnedDownAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, string ticker, IReadOnlyList<SweepBar> stored, DateOnly night, CancellationToken cancellation)
    {
        var bars = new List<SwingBar>();

        if (stored.Count > 0 && stored[0].Close > 0m)
        {
            string? pull = null;
            var pulledClose = 0m;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = PullHoldingFirst;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$first", Day(stored[0].Session));

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                if (await reader.ReadAsync(cancellation))
                {
                    pull = reader.GetString(0);
                    pulledClose = global::EquityBrief.Data.Money.FromStorage(reader.GetString(1));
                }
            }

            if (pull is not null && pulledClose > 0m)
            {
                var ratio = stored[0].Close / pulledClose;

                await using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = PulledHighsBefore;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$pull", pull);
                command.Parameters.AddWithValue("$first", Day(stored[0].Session));
                command.Parameters.AddWithValue("$from", Day(night.AddYears(-TakeProfit.HistoryYears)));

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    bars.Add(new SwingBar(
                        DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                        global::EquityBrief.Data.Money.FromStorage(reader.GetString(1)) * ratio,
                        global::EquityBrief.Data.Money.FromStorage(reader.GetString(2)) * ratio));
                }
            }
        }

        bars.AddRange(stored.Where(bar => bar.Session <= night).Select(bar => new SwingBar(bar.Session, bar.High, bar.Low)));

        return TakeProfit.Before(SwingSeries.For(bars), night);
    }

    const string TypicalOn = "SELECT value FROM indicator WHERE ticker = $ticker AND session_date = $session AND name = $typical;";

    // From the operator's order of 2026-11-14 a book's holding's or a Fundamentals trade's stop and take-profit placed from its
    // buy on a session: the stock's typical daily move as the night's indicators stored it on the session and the prices it
    // turned down at before it; none where no typical move is stored.
    // see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
    public static async Task<(double Stop, TakeProfitPrice? TakeProfit)?> BookExitAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, string ticker, DateOnly session, decimal buy, CancellationToken cancellation)
    {
        double typical;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = TypicalOn;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", Day(session));
            command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);

            if (await command.ExecuteScalarAsync(cancellation) is not double read || !(read > 0))
            {
                return null;
            }

            typical = read;
        }

        var bars = await BarsAsync(connection, transaction, ticker, session, cancellation);

        return TakeProfit.ForBook(Statistic.FromPrice(buy), typical, await TurnedDownAsync(connection, transaction, ticker, bars, session, cancellation));
    }

    // The prices a series' stock turned down at before a bar's session, from the swings the series confirmed by it.
    public static IReadOnlyList<TurnedDown> TurnedDownAt(SweepSeries series, int bar) =>
        bar < 0 || bar >= series.Bars.Length ? [] : TakeProfit.Before([.. series.Swings.Take(series.Confirmed[bar])], series.Bars[bar].Session);

    // One stock's stored bars through a session, oldest first.
    public static async Task<IReadOnlyList<SweepBar>> BarsAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, string ticker, DateOnly through, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = BarsOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$through", Day(through));

        var bars = new List<SweepBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(BarOf(reader, 0));
        }

        return bars;
    }

    // The order a rule stating a plan keeps on a pick, from its stock's stored bars through its night; where the bars place
    // none, a zone the levels plan does not draw among them, an order holding no level, which never fills and ends with no
    // result once its window has passed, as the test counts a pick its plan cannot place.
    public static async Task<PlanOrder> OrderAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, PlanCombination combination, string family, string ticker, DateOnly night, decimal ruleStop, decimal? target, decimal? trail, int cap, CancellationToken cancellation)
    {
        var bars = await BarsAsync(connection, transaction, ticker, night, cancellation);
        var stop = Statistic.FromPrice(ruleStop);
        var turnedDown = combination.TakeProfit is null ? null : await TurnedDownAsync(connection, transaction, ticker, bars, night, cancellation);

        return Place(combination, family, ticker, bars, night, stop, target is { } aim ? Statistic.FromPrice(aim) : null, trail is { } distance ? Statistic.FromPrice(distance) : null, cap, turnedDown)
            ?? Unplaced(combination, stop, cap);
    }

    // Every named stock's stored bars through a session, oldest first, a stock holding none left out.
    public static async Task<IReadOnlyList<(string Ticker, IReadOnlyList<SweepBar> Bars)>> MembersBarsAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, IReadOnlySet<string> tickers, DateOnly through, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = EveryBarThrough;
        command.Parameters.AddWithValue("$through", Day(through));

        var held = new Dictionary<string, List<SweepBar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!tickers.Contains(ticker))
            {
                continue;
            }

            if (!held.TryGetValue(ticker, out var bars))
            {
                held[ticker] = bars = [];
            }

            bars.Add(BarOf(reader, 1));
        }

        return [.. held.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, (IReadOnlyList<SweepBar>)pair.Value))];
    }

    static SweepBar BarOf(Microsoft.Data.Sqlite.SqliteDataReader reader, int at) =>
        new(
            DateOnly.ParseExact(reader.GetString(at), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            global::EquityBrief.Data.Money.FromStorage(reader.GetString(at + 2)),
            global::EquityBrief.Data.Money.FromStorage(reader.GetString(at + 3)),
            global::EquityBrief.Data.Money.FromStorage(reader.GetString(at + 4)),
            reader.GetInt64(at + 5),
            global::EquityBrief.Data.Money.FromStorage(reader.GetString(at + 1)));

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // The levels plan as of a bar, from the bands and the trend the night's own functions read over the year it held: each
    // zone's top with its own stop, the staged sales and the bands above with real touches. A plan trade's order is placed
    // from it, so a rule stating a plan pins this file.
    public static LevelsAsOf LevelsAt(SweepSeries series, int bar)
    {
        var from = Math.Max(0, bar - VolumeProfileSeries.Window + 1);
        var bars = series.Bars[from..(bar + 1)].Select(one => new ReplayBar(one.Session, one.High, one.Low, one.Close, one.Volume)).ToArray();
        var indicators = new Dictionary<string, double>(StringComparer.Ordinal);

        void Hold(string key, double value)
        {
            if (double.IsFinite(value) && value > 0)
            {
                indicators[key] = value;
            }
        }

        Hold(IndicatorSeries.Atr14, series.Atr[bar]);
        Hold(IndicatorSeries.Sma20, series.Sma20[bar]);
        Hold(IndicatorSeries.Sma50, series.Sma50[bar]);
        Hold(IndicatorSeries.Sma200, series.Sma200[bar]);

        if (!indicators.TryGetValue(IndicatorSeries.Atr14, out var atr))
        {
            return new LevelsAsOf([], [], [], PlanPointTest.NoTypicalMove);
        }

        var confirmed = series.Swings.Take(series.Confirmed[bar]).ToArray();
        var close = series.Bars[bar].Close;
        decimal? Average(string key) => indicators.TryGetValue(key, out var value) ? Statistic.ToPrice(value) : null;
        var trend = TrendSeries.For(close, Average(IndicatorSeries.Sma50), Average(IndicatorSeries.Sma200), confirmed);
        var bands = SessionReplay.BandsOver(bars, indicators, confirmed);
        var recent = bars.Skip(Math.Max(0, bars.Length - LadderSeries.ConditionLookback)).Select(one => new LadderBar(one.SessionDate, one.High, one.Low, one.Close)).ToArray();
        var lows = confirmed.Where(swing => swing.Direction == SwingSeries.Low).OrderBy(swing => swing.SessionDate).Select(swing => swing.Price).ToArray();
        var ladder = LadderSeries.For(bands, close, Statistic.ToPrice(atr), recent, trend.State, lows);
        var touched = bands
            .Where(band => band.Role == LevelSeries.Resistance && band.HasNonAverageAnchor && band.Members.Any(member => member.Source == MemberSource.Touch))
            .Select(band => Statistic.FromPrice(band.LowEdge))
            .ToArray();

        return new LevelsAsOf(
            [.. ladder.Tranches.Take(LadderSeries.MostTranches).Select(tranche => (Statistic.FromPrice(tranche.HighEdge), tranche.Stop is { } stop ? Statistic.FromPrice(stop) : default(double?)))],
            [.. ladder.Exits.Where(exit => exit.Traded && !exit.Trailing).Select(exit => Statistic.FromPrice(exit.LowEdge))],
            touched,
            ladder.Reason);
    }

    // The order a combination places on a pick from its stock's bars through its night, the rule's own stop and sale at the
    // night's close; none where the bars do not reach the night, hold no typical daily move there or place no order.
    public static PlanOrder? Place(PlanCombination combination, string family, string ticker, IReadOnlyList<SweepBar> bars, DateOnly night, double ruleStop, double? target, double? trail, int cap, IReadOnlyList<TurnedDown>? turnedDown = null)
    {
        if (bars.Count == 0 || bars[^1].Session != night)
        {
            return null;
        }

        var sessionAt = bars.Select((bar, at) => (bar.Session, at)).ToDictionary(pair => pair.Session, pair => pair.at);
        var series = SweepColumns.Series(new SweepName(ticker, [.. bars], [(null, null)], []), sessionAt);
        var last = series.Bars.Length - 1;

        return last < 0 || series.Bars[last].Session != night ? null : Place(combination, family, series, last, ruleStop, target, trail, cap, turnedDown);
    }

    // The same over a series the step already holds, at its bar on the night; from the operator's order of 2026-11-14 the
    // prices its stock turned down at read from the series' own swings where none are handed.
    public static PlanOrder? Place(PlanCombination combination, string family, SweepSeries series, int bar, double ruleStop, double? target, double? trail, int cap, IReadOnlyList<TurnedDown>? turnedDown = null)
    {
        if (bar < 0 || bar >= series.Bars.Length || !(series.Atr[bar] is var typical && typical > 0))
        {
            return null;
        }

        var levels = LevelsAt(series, bar);
        var close = Statistic.FromPrice(series.Bars[bar].Close);
        var ruleSale = family == PlanPoints.DipName ? PlanPoints.DipSale : new PlanSale(target, trail, []);
        var average = series.Sma20[bar];

        return PlanHook.OrderOf(combination, close, new PlanLevels(levels.Zones, levels.Staged, levels.Bands), double.IsNaN(average) ? null : average, typical, ruleStop, ruleSale, cap, combination.TakeProfit is null ? null : turnedDown ?? TurnedDownAt(series, bar));
    }

    // An order holding no level, which never fills and ends with no result once its window has passed.
    public static PlanOrder Unplaced(PlanCombination combination, double ruleStop, int cap) =>
        new(combination.Words, combination.Entry, null, combination.Window, combination.Stop, ruleStop, null, 0, null, [], null, null, cap);

    // Where a plan trade stands over its stock's bars: ended, with its result in the rule's risk at its night's close; ended
    // with none on its window's last session where it never filled; and none while it waits or is held. The order is read at
    // the scale the bars are stored at now, its night's close over the close it was set at.
    public static PlanEnd? Ended(PlanOrder order, decimal entry, decimal ruleStop, DateOnly night, PlanBars bars) =>
        Where(order, entry, ruleStop, night, bars)?.End;

    // Where a plan trade stands: none while it waits for its fill; filled, with its session and its price at its night's scale,
    // and its end once it has one.
    public static PlanWhere? Where(PlanOrder order, decimal entry, decimal ruleStop, DateOnly night, PlanBars bars)
    {
        var pick = bars.IndexOf(night);

        if (pick < 0 || !(entry > 0m))
        {
            return null;
        }

        if (order.PlacesNoPrice())
        {
            return new PlanWhere(null, null, new PlanEnd(night, null, null, null, null, null, PlanEnd.Unfilled));
        }

        var factor = bars.Closes[pick] / Statistic.FromPrice(entry);
        var scaled = order.Scaled(factor);
        var rulesRisk = bars.Closes[pick] - (Statistic.FromPrice(ruleStop) * factor);
        var rsi = scaled.RsiAbove is null ? default : PlanPoints.RsiOf(bars.Closes);
        var state = PlanHook.Walk(scaled, bars.Opens, bars.Lows, bars.Closes, pick, rsi);

        if (state.Waiting)
        {
            return null;
        }

        if (state.Unfilled)
        {
            var through = Math.Min(bars.Sessions.Length - 1, pick + Math.Max(1, order.Window) + (order.Entry == PlanPoints.FirstPullback ? 1 : 0));

            return new PlanWhere(null, null, new PlanEnd(bars.Sessions[through], null, null, null, null, null, PlanEnd.Unfilled));
        }

        if (state.FillBar is not { } filled || state.Fill is not { } fill || !(rulesRisk > 0))
        {
            return null;
        }

        if (state.Outcome is not { } outcome)
        {
            return new PlanWhere(bars.Sessions[filled], fill / factor, null);
        }

        var signal = filled + outcome.Sessions - 1;

        return new PlanWhere(bars.Sessions[filled], fill / factor, new PlanEnd(
            bars.Sessions[signal],
            (outcome.Sold - fill) / rulesRisk,
            bars.Sessions[filled],
            fill / factor,
            signal + 1 < bars.Sessions.Length ? bars.Sessions[signal + 1] : null,
            outcome.Sold / factor,
            outcome.End));
    }

    // The same plan on every member that night: each bought at its own next open in its own typical moves at the rule's
    // distance, its stop and its sale placed as the plan places them on the pick, a zone's stop, the first band and the staged
    // sales off the member's own levels plan; the mean of the members' results in the risk the rule's plan sets on each, and
    // how many that is. A member whose result lies past 20 risks either way is left out, as the test leaves it.
    public static (double? Benchmark, int Members) Benchmark(PlanCombination combination, double stopMoves, double? rewardToRisk, double? trailMoves, int cap, IReadOnlyList<(string Ticker, IReadOnlyList<SweepBar> Bars)> members, DateOnly night)
    {
        var placed = new List<(SweepSeries Series, int Bar)>();

        foreach (var (ticker, held) in members)
        {
            var sessionAt = held.Select((bar, at) => (bar.Session, at)).ToDictionary(pair => pair.Session, pair => pair.at);

            if (!sessionAt.TryGetValue(night, out var bar))
            {
                continue;
            }

            var series = SweepColumns.Series(new SweepName(ticker, [.. held], [(null, null)], []), sessionAt);

            if (series.Bars.Length == held.Count)
            {
                placed.Add((series, bar));
            }
        }

        return Benchmark(combination, stopMoves, rewardToRisk, trailMoves, cap, placed);
    }

    // The same over series the step already holds, each at its bar on the night.
    public static (double? Benchmark, int Members) Benchmark(PlanCombination combination, double stopMoves, double? rewardToRisk, double? trailMoves, int cap, IEnumerable<(SweepSeries Series, int Bar)> members)
    {
        var zone = combination.Stop == PlanPoints.ZoneStop ? PlanPoints.Zones.ToList().IndexOf(combination.Entry) : -1;
        var results = new List<double?>();

        foreach (var (series, bar) in members)
        {
            if (bar < 0 || bar >= series.Bars.Length || !(series.Atr[bar] is var typical && typical > 0))
            {
                continue;
            }

            var closes = series.Bars.Select(one => Statistic.FromPrice(one.Close)).ToArray();
            var opens = series.Bars.Select(one => Statistic.FromPrice(one.Open)).ToArray();
            var close = closes[bar];
            var risk = stopMoves * typical;
            var levels = zone >= 0 || combination.Exit == PlanPoints.FirstBand || combination.Exit == PlanPoints.StagedSales ? LevelsAt(series, bar) : null;

            double? StopAt(double fill) => combination.Stop == PlanPoints.RuleStop ? close - risk
                : zone >= 0 ? (levels!.Zones.Count > zone ? levels.Zones[zone].Stop : null)
                : PlanPoints.StopOf(combination.Stop, fill, close - risk, null, typical);

            PlanSale? Own(double fill) => combination.Exit switch
            {
                PlanPoints.RuleSale => trailMoves is { } moves ? new PlanSale(null, moves * typical, [])
                    : rewardToRisk is { } reward ? new PlanSale(close + (reward * risk), null, [])
                    : PlanPoints.DipSale,
                PlanPoints.FirstBand => new PlanSale(levels!.Bands.Where(edge => edge > fill).Order().Cast<double?>().FirstOrDefault(), null, []),
                PlanPoints.StagedSales => new PlanSale(null, null, levels!.Staged),
                _ => new PlanSale(null, null, []),
            };

            // From the operator's order of 2026-11-14 each member takes profit as the plan places it on the pick, from its own
            // swings.
            var swings = series.Swings;
            var confirmed = series.Confirmed[bar];
            var session = series.Bars[bar].Session;
            TurnedDown? turnedDown(double price) => TakeProfit.NearestAbove(price, swings, confirmed, session);

            PlanSale? SaleAt(double fill) =>
                Own(fill) is { } sale && combination.TakeProfit is { } multiple && StopAt(fill) is { } stop
                    ? TakeProfit.SaleWith(sale, fill, stop, turnedDown, multiple)
                    : Own(fill);

            results.Add(PlanPoints.MemberResult(closes, opens, bar, risk, StopAt, SaleAt, cap, PlanPoints.RsiOf(closes)));
        }

        var mean = PlanPoints.BenchmarkOf(results);

        return (mean, results.Count(result => result is { } value && Math.Abs(value) <= Core.History.LongLedger.OutlyingRisks));
    }
}
