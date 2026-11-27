using System.Text.Json;

namespace EquityBrief.Core.Loop;

// A trade's plan as its night set it: the combination's words, the price a level buys at (none for the next open and the first
// pullback), the sessions it waits, the stop's words with what each kind of stop is placed from, and the sale read off the
// stock's own levels plan or the rule's own, with the sessions it is held at most. Stored on the trade as its night set it,
// since the levels it was read from move every night.
public sealed record PlanOrder(
    string Words,
    string Entry,
    double? Level,
    int Window,
    string Stop,
    double RuleStop,
    double? ZoneStop,
    double TypicalDay,
    double? Target,
    IReadOnlyList<double> Staged,
    double? Trail,
    double? RsiAbove,
    int Cap)
{
    // From the operator's order of 2026-11-14 the multiple of the risk the take-profit price falls back to, and the prices the
    // stock turned down at before its night above the lowest its stop could stand, which the take-profit is read from at the
    // fill; none for an order placed before, which sells as it did.
    // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
    public double? TakeProfit { get; init; }

    public IReadOnlyList<TurnedDown> TurnedDown { get; init; } = [];

    // On the operator's ruling of 2026-11-15 on the shapes how the take-profit is placed and whether it sells half; today's
    // full take-profit for an order placed before, which sells as it did.
    public TakeProfitPlacing Placing { get; init; }

    public bool Half { get; init; }

    // On the operator's ruling of 2026-11-14, clause 5, the rules restart a take-profit was added at to a trade or an order placed
    // before it, which counts from the session after; none for an order placed with it.
    public DateOnly? TakeProfitAddedOn { get; init; }

    // From the January pin move the range a buy at the next open is placed in, in prices from its night's close: bought at an open
    // inside it, at its ceiling where the price comes back to it within its window, and not bought under its floor; none for an
    // order buying at whatever price it opens.
    // see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
    public BuyRange? Range { get; init; }

    static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Json() => JsonSerializer.Serialize(this, Options);

    public static PlanOrder? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PlanOrder>(json, Options);

    public PlanSale Sale => new(Target, Trail, Staged) { RsiAbove = RsiAbove };

    // On the operator's ruling of 2026-11-14 an order buying at a level its stock's levels placed no price for is not placed:
    // it ends on its own night unfilled and buys nothing.
    public bool PlacesNoPrice() => Level is null && Entry != PlanPoints.NextOpen && Entry != PlanPoints.FirstPullback;

    // The sale walked from a fill: its own, with the take-profit price beside it as its shape places it where the order takes
    // profit.
    public PlanSale SaleFrom(double fill, double stop) =>
        TakeProfit is { } multiple ? Loop.TakeProfit.Shaped(Sale, fill, stop, TurnedDown, multiple, Placing, Half) : Sale;

    // The sale a walk sells by from a fill: where the take-profit was added at the rules restart, it counts from the bar the walk
    // names, the half's price and the target where the take-profit set it.
    public PlanSale SaleWalked(double fill, double stop, int takeProfitFrom) =>
        TakeProfitAddedOn is null || takeProfitFrom <= 0
            ? SaleFrom(fill, stop)
            : SaleFrom(fill, stop) is var sale && sale.Target != Sale.Target
                ? sale with { TakeProfitFromBar = takeProfitFrom, TargetIsTakeProfit = true }
                : sale with { TakeProfitFromBar = takeProfitFrom };

    // The take-profit price a card draws from a fill, as the walk above places it; none where the order takes no profit.
    public TakeProfitPrice? TakeProfitFrom(double fill, double stop) =>
        TakeProfit is { } multiple ? Loop.TakeProfit.PlacedShaped(Sale, fill, stop, TurnedDown, multiple, Placing, Half) : null;

    // From the January pin move the order's own reward over its own risk, the traded plan's: from the price it buys at, its
    // level or for an order at the next open or the first pullback the night's close, to the one price its sale takes the whole
    // at above it, over the distance down to the stop that price places; none where its stop is not placed under the buy, or
    // its sale names no such price, selling part first, by staged levels, on a trail or at its time limit alone.
    // see: The reward check on the traded plan rides the first pin move it is ready for and registers in the background once it lands
    public double? TradedReward(double close)
    {
        var buy = Level ?? close;

        if (PlanPoints.StopOf(Stop, buy, RuleStop, ZoneStop, TypicalDay) is not { } stop || !(buy > stop))
        {
            return null;
        }

        var sale = SaleFrom(buy, stop);

        return sale.Target is { } whole && whole > buy && sale.HalfAt is null && sale.Trail is null && sale.RsiAbove is null && !sale.Staged.Any(level => level > buy && level < whole)
            ? Math.Round((whole - buy) / (buy - stop), 6, MidpointRounding.AwayFromZero)
            : null;
    }

    // The order at the scale the stock's closes are stored at now, an adjustment since its night moving every price by the
    // same factor, the night's close over the close it was set at.
    public PlanOrder Scaled(double factor) =>
        factor == 1 || !(factor > 0)
            ? this
            : this with
            {
                Level = Level * factor,
                RuleStop = RuleStop * factor,
                ZoneStop = ZoneStop * factor,
                TypicalDay = TypicalDay * factor,
                Target = Target * factor,
                Staged = [.. Staged.Select(level => level * factor)],
                Trail = Trail * factor,
                TurnedDown = [.. TurnedDown.Select(turned => turned with { Price = turned.Price * factor })],
                Range = Range is { } range ? new BuyRange(range.Ceiling * factor, range.Floor * factor) : null,
            };
}

// Where a plan trade stands over the sessions after its night: still waiting for its fill, never filled, or filled at a
// session and price with its stop, and its outcome once its sale is reached, none while it is held.
public sealed record PlanTradeState(bool Waiting, int? FillBar, double? Fill, double? Stop, PlanOutcome? Outcome)
{
    public bool Unfilled => !Waiting && FillBar is null;
}

// From 23.10 the plan a rule's trades are bought and sold by, one way of the test of the buy and sell points: the entry,
// the next open, a zone, the 20-session average, a plain distance under the close or the first pullback, within its window;
// the stop, the rule's own, the zone's own or a number of typical days under the buy; and the sale, the rule's own, the
// first band above, the staged sales or the time limit alone; and from the operator's order of 2026-11-14 the multiple of the
// risk its take-profit falls back to. Six parameters a registration states, all off where none is stated, and stated off
// where they name the next open at the rule's own stop and sale with no take-profit, the rule then buying at the next open at
// its own stop and sale as before.
// see: At phase 23's restart each price family's live rule takes the plan its long test recommends where it beats today's plan from 2019 above the ruled benchmark, today's plan kept beside it as a variant
public static class PlanHook
{
    public const string EntryParameter = "plan_entry";
    public const string DistanceParameter = "plan_distance";
    public const string WindowParameter = "plan_window";
    public const string StopParameter = "plan_stop";
    public const string SaleParameter = "plan_sale";

    // From the operator's order of 2026-11-14 the multiple of the risk the take-profit price falls back to, one of those the
    // test walks; off where none is stated.
    // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
    public const string TakeProfitParameter = "plan_take_profit";

    // On the operator's ruling of 2026-11-15 on the shapes how the take-profit is placed: 0 today's, the nearest price above the
    // buy it turned down at; 1 the multiple of the risk alone; 2 the first price it turned down at that far above the buy or
    // more; 3 half the shares at the multiple, the stop on the other half then moved up to the buy. Off where none is stated.
    public const string TakeProfitShapeParameter = "plan_take_profit_shape";

    public const int AtTheRisk = 1;
    public const int Beyond = 2;
    public const int OnHalf = 3;

    // From the January pin move the buy range of a plan buying at the next open: its ceiling's typical daily moves above the last
    // close and its floor's under it, each one of the widths the range's test walks, nought or none for no floor; no range where
    // no ceiling is stated.
    // see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
    public const string RangeCeilingParameter = "plan_range_ceiling";

    public const string RangeFloorParameter = "plan_range_floor";

    public static IReadOnlyList<string> Parameters { get; } = [EntryParameter, DistanceParameter, WindowParameter, StopParameter, SaleParameter, TakeProfitParameter, TakeProfitShapeParameter, RangeCeilingParameter, RangeFloorParameter];

    // The entries by number: 0 the next open, 1 to 3 the zones, 4 the 20-session average, 5 a distance under the close in
    // typical days and 6 the first pullback.
    public const int NextOpen = 0;
    public const int Average = 4;
    public const int Distance = 5;
    public const int FirstPullback = 6;

    // The stop by number: a number of typical days under the buy above nought, 0 the rule's own and -1 the zone's own.
    public const int RuleStop = 0;
    public const int ZoneStop = -1;

    // The sales by number, in the order the test lists them.
    public static IReadOnlyList<string> Sales => PlanPoints.Sales;

    // The combination a registration's parameters state, none where they state none; a number naming no entry, stop or
    // sale, a window outside the test's, a distance without its entry and a zone's stop on an entry that is no zone are each
    // refused rather than read as the rule's own.
    public static PlanCombination? Of(IReadOnlyDictionary<string, double> parameters)
    {
        if (!Parameters.Any(parameters.ContainsKey))
        {
            return null;
        }

        var entryNumber = (int)Math.Round(parameters.GetValueOrDefault(EntryParameter));
        var window = (int)Math.Round(parameters.GetValueOrDefault(WindowParameter));
        var stopNumber = parameters.GetValueOrDefault(StopParameter);
        var saleNumber = (int)Math.Round(parameters.GetValueOrDefault(SaleParameter));

        string entry = entryNumber switch
        {
            NextOpen => PlanPoints.NextOpen,
            1 or 2 or 3 => PlanPoints.Zones[entryNumber - 1],
            Average => PlanPoints.Average,
            Distance when parameters.GetValueOrDefault(DistanceParameter) is var days && days > 0 => PlanPoints.DistanceEntry(days),
            FirstPullback => PlanPoints.FirstPullback,
            _ => throw new ArgumentException(FormattableString.Invariant($"The plan's entry states {entryNumber}, and it states 0 to {FirstPullback}, a distance with its typical days over nought."), nameof(parameters)),
        };

        if (entryNumber != Distance && parameters.GetValueOrDefault(DistanceParameter) != 0)
        {
            throw new ArgumentException("The plan states a distance and its entry is not the distance under the close.", nameof(parameters));
        }

        var waits = entryNumber == FirstPullback ? [PlanPoints.FirstPullbackSessions] : PlanPoints.Windows;

        if (entryNumber == NextOpen ? window != 0 : !waits.Contains(window))
        {
            throw new ArgumentException(FormattableString.Invariant($"The plan's window states {window}, and the next open waits none and a level waits {string.Join(", ", PlanPoints.Windows)} sessions, the first pullback {PlanPoints.FirstPullbackSessions}."), nameof(parameters));
        }

        string stop = stopNumber switch
        {
            RuleStop => PlanPoints.RuleStop,
            ZoneStop when entryNumber is 1 or 2 or 3 => PlanPoints.ZoneStop,
            > 0 when PlanPoints.TypicalDays.Concat(PlanPoints.WiderDays).Contains(stopNumber) => PlanPoints.TypicalStop(stopNumber),
            _ => throw new ArgumentException(FormattableString.Invariant($"The plan's stop states {stopNumber}, and it states 0 for the rule's own, -1 for a zone's own or one of {string.Join(", ", PlanPoints.TypicalDays.Concat(PlanPoints.WiderDays))} typical days."), nameof(parameters)),
        };

        if (saleNumber < 0 || saleNumber >= Sales.Count)
        {
            throw new ArgumentException(FormattableString.Invariant($"The plan's sale states {saleNumber}, and it states 0 to {Sales.Count - 1}."), nameof(parameters));
        }

        // From the operator's order of 2026-11-14 the take-profit's multiple, one of those the test walks, none where it is off.
        var takeProfit = parameters.GetValueOrDefault(TakeProfitParameter);

        if (takeProfit != 0 && !TakeProfit.Multiples.Contains(takeProfit))
        {
            throw new ArgumentException(FormattableString.Invariant($"The plan's take-profit states {takeProfit} times the risk, and it states 0 for none or one of {string.Join(", ", TakeProfit.Multiples)}."), nameof(parameters));
        }

        // On the operator's ruling of 2026-11-15 on the shapes how the take-profit is placed, stated only beside a multiple.
        var shape = parameters.GetValueOrDefault(TakeProfitShapeParameter);

        if (shape is not (0 or AtTheRisk or Beyond or OnHalf) || (shape != 0 && takeProfit == 0))
        {
            throw new ArgumentException(FormattableString.Invariant($"The plan's take-profit shape states {shape}, and it states 0 for today's, {AtTheRisk} for the multiple of the risk alone, {Beyond} for the first price it turned down at that far above the buy or {OnHalf} for half, beside a multiple."), nameof(parameters));
        }

        // From the January pin move the buy range, stated only on a buy at the next open, at a ceiling and a floor the range's test
        // walks.
        var ceiling = parameters.GetValueOrDefault(RangeCeilingParameter);
        var floorMoves = parameters.GetValueOrDefault(RangeFloorParameter);
        RangeWidths? range = null;

        if (ceiling != 0 || floorMoves != 0)
        {
            if (entryNumber != NextOpen || !BuyRanges.Ceilings.Contains(ceiling) || (floorMoves != 0 && !BuyRanges.Floors.Contains(floorMoves)))
            {
                throw new ArgumentException(FormattableString.Invariant($"The plan's buy range states a ceiling of {ceiling} and a floor of {floorMoves}, and it states a ceiling of {string.Join(", ", BuyRanges.Ceilings)} typical days and a floor of {string.Join(", ", BuyRanges.Floors.OfType<double>())} or nought for none, on a buy at the next open."), nameof(parameters));
            }

            range = new RangeWidths(ceiling, floorMoves > 0 ? floorMoves : null);
        }

        // The plan stated off, the next open at the rule's own stop and sale, is the rule's own plan, which its own walk trades:
        // a variant keeping today's plan beside a planned live rule states it so.
        var combination = new PlanCombination(entry, window, stop, Sales[saleNumber])
        {
            TakeProfit = takeProfit > 0 ? takeProfit : null,
            Half = shape == OnHalf,
            Placing = shape switch { AtTheRisk => TakeProfitPlacing.Risk, Beyond => TakeProfitPlacing.Beyond, _ => TakeProfitPlacing.Turned },
            Range = range,
        };

        return PlanPrices.Traded(combination) ? null : combination;
    }

    // The parameters that state a combination, the inverse of the reading above.
    public static IReadOnlyDictionary<string, double> ParametersOf(PlanCombination combination)
    {
        var stated = new Dictionary<string, double>(StringComparer.Ordinal);
        var zone = PlanPoints.Zones.ToList().IndexOf(combination.Entry);

        stated[EntryParameter] = combination.Entry == PlanPoints.NextOpen ? NextOpen
            : zone >= 0 ? zone + 1
            : combination.Entry == PlanPoints.Average ? Average
            : combination.Entry == PlanPoints.FirstPullback ? FirstPullback
            : Distance;

        if (PlanPoints.DistanceOf(combination.Entry) is { } days)
        {
            stated[DistanceParameter] = days;
        }

        stated[WindowParameter] = combination.Window;
        stated[StopParameter] = combination.Stop == PlanPoints.RuleStop ? RuleStop
            : combination.Stop == PlanPoints.ZoneStop ? ZoneStop
            : PlanPoints.TypicalDays.Concat(PlanPoints.WiderDays).First(one => PlanPoints.NamesStop(combination.Stop, one));
        stated[SaleParameter] = Sales.ToList().IndexOf(combination.Exit);

        if (combination.TakeProfit is { } multiple)
        {
            stated[TakeProfitParameter] = multiple;

            if (combination.Half || combination.Placing != TakeProfitPlacing.Turned)
            {
                stated[TakeProfitShapeParameter] = combination.Half ? OnHalf : combination.Placing == TakeProfitPlacing.Risk ? AtTheRisk : Beyond;
            }
        }

        if (combination.Range is { } range)
        {
            stated[RangeCeilingParameter] = range.Ceiling;

            if (range.Floor is { } floor)
            {
                stated[RangeFloorParameter] = floor;
            }
        }

        return stated;
    }

    // The order a combination places on a pick at its night's close: the level it buys at, the stop's parts and the sale read
    // off the stock's levels plan or the rule's own; none where the level is not read.
    public static PlanOrder? OrderOf(PlanCombination combination, double close, PlanLevels levels, double? average20, double typicalDay, double ruleStop, PlanSale ruleSale, int cap, IReadOnlyList<TurnedDown>? turnedDown = null)
    {
        var zone = PlanPoints.Zones.ToList().IndexOf(combination.Entry);
        (double Top, double? Stop)? zoneRead = zone >= 0 && zone < levels.Zones.Count ? levels.Zones[zone] : null;
        double? level = combination.Entry == PlanPoints.NextOpen || combination.Entry == PlanPoints.FirstPullback ? null
            : combination.Entry == PlanPoints.Average ? (average20 is { } average && average > 0 ? average : null)
            : PlanPoints.DistanceOf(combination.Entry) is { } days ? (typicalDay > 0 ? close - (days * typicalDay) : null)
            : zoneRead?.Top;

        if (level is null && combination.Entry != PlanPoints.NextOpen && combination.Entry != PlanPoints.FirstPullback || cap <= 0)
        {
            return null;
        }

        var sale = combination.Exit == PlanPoints.RuleSale ? ruleSale
            : combination.Exit == PlanPoints.FirstBand ? new PlanSale(levels.Bands.Where(edge => edge > (level ?? close)).Order().Cast<double?>().FirstOrDefault(), null, [])
            : combination.Exit == PlanPoints.StagedSales ? new PlanSale(null, null, levels.Staged)
            : new PlanSale(null, null, []);

        // The prices it turned down at above the lowest its stop could stand from any fill the order takes, which the take-profit
        // reads at the fill.
        var lowest = new[] { ruleStop, zoneRead?.Stop ?? ruleStop, (level ?? close) - (PlanPoints.WiderDays.Max() * typicalDay) }.Min();

        // A buy at the next open in a range waits the sessions its order at the ceiling stands.
        var range = combination.Range is { } widths && combination.Entry == PlanPoints.NextOpen ? BuyRanges.Of(close, typicalDay, widths) : null;

        return new PlanOrder(combination.Words, combination.Entry, level, range is null ? combination.Window : BuyRanges.OrderSessions, combination.Stop, ruleStop, zoneRead?.Stop, typicalDay, sale.Target, [.. sale.Staged.Order()], sale.Trail, sale.RsiAbove, cap)
        {
            TakeProfit = combination.TakeProfit,
            TurnedDown = combination.TakeProfit is null || combination.Half ? [] : [.. (turnedDown ?? []).Where(turned => turned.Price > lowest)],
            Placing = combination.Placing,
            Half = combination.Half,
            Range = range,
        };
    }

    // A plan trade over its stock's bars from its night, `pick` the night's own bar: waiting while its window has not passed
    // without a fill, never filled once it has, and filled at the bar and price the test fills at with its stop placed from
    // that price, walked to its sale as the test walks it, the outcome none while it is held. A fill at or under its stop is
    // never filled, as the test leaves it.
    public static PlanTradeState Walk(PlanOrder order, ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, ReadOnlySpan<double> closes, int pick, ReadOnlySpan<double> rsi = default, int takeProfitFrom = 0)
    {
        // From the January pin move an order in a range fills as the range fills: waiting while its sessions have not passed, and
        // never filled where it opened under its floor or stayed above its ceiling.
        if (order.Range is { } range && BuyRanges.FillOf(opens, lows, pick, range) is { Fill: null } open)
        {
            return new PlanTradeState(open.Waiting, null, null, null, null);
        }

        var fill = order.Range is { } bought ? BuyRanges.FillOf(opens, lows, pick, bought).Fill
            : order.Entry == PlanPoints.FirstPullback
            ? PlanPoints.PullbackFillOf(closes, opens, pick, order.Window)
            : PlanPoints.FillOf(opens, lows, pick, order.Level, order.Window);

        if (fill is not { } filled)
        {
            var through = pick + Math.Max(1, order.Window) + (order.Entry == PlanPoints.FirstPullback ? 1 : 0);

            return new PlanTradeState(through >= closes.Length, null, null, null, null);
        }

        if (PlanPoints.StopOf(order.Stop, filled.Price, order.RuleStop, order.ZoneStop, order.TypicalDay) is not { } stop || !(filled.Price > stop))
        {
            return new PlanTradeState(false, null, null, null, null);
        }

        return new PlanTradeState(false, filled.Bar, filled.Price, stop, PlanPoints.Walk(closes, opens, filled.Bar, filled.Price, stop, order.SaleWalked(filled.Price, stop, takeProfitFrom), order.Cap, rsi));
    }
}
