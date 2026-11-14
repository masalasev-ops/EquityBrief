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
    // see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
    public double? TakeProfit { get; init; }

    public IReadOnlyList<TurnedDown> TurnedDown { get; init; } = [];

    static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Json() => JsonSerializer.Serialize(this, Options);

    public static PlanOrder? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PlanOrder>(json, Options);

    public PlanSale Sale => new(Target, Trail, Staged) { RsiAbove = RsiAbove };

    // On the operator's ruling of 2026-11-14 an order buying at a level its stock's levels placed no price for is not placed:
    // it ends on its own night unfilled and buys nothing.
    public bool PlacesNoPrice() => Level is null && Entry != PlanPoints.NextOpen && Entry != PlanPoints.FirstPullback;

    // The sale walked from a fill: its own, with the take-profit price beside it where the order takes profit.
    public PlanSale SaleFrom(double fill, double stop) =>
        TakeProfit is { } multiple ? Loop.TakeProfit.SaleWith(Sale, fill, stop, TurnedDown, multiple) : Sale;

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
    // see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
    public const string TakeProfitParameter = "plan_take_profit";

    public static IReadOnlyList<string> Parameters { get; } = [EntryParameter, DistanceParameter, WindowParameter, StopParameter, SaleParameter, TakeProfitParameter];

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

        // The plan stated off, the next open at the rule's own stop and sale, is the rule's own plan, which its own walk trades:
        // a variant keeping today's plan beside a planned live rule states it so.
        var combination = new PlanCombination(entry, window, stop, Sales[saleNumber]) { TakeProfit = takeProfit > 0 ? takeProfit : null };

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

        return new PlanOrder(combination.Words, combination.Entry, level, combination.Window, combination.Stop, ruleStop, zoneRead?.Stop, typicalDay, sale.Target, [.. sale.Staged.Order()], sale.Trail, sale.RsiAbove, cap)
        {
            TakeProfit = combination.TakeProfit,
            TurnedDown = combination.TakeProfit is null ? [] : [.. (turnedDown ?? []).Where(turned => turned.Price > lowest)],
        };
    }

    // A plan trade over its stock's bars from its night, `pick` the night's own bar: waiting while its window has not passed
    // without a fill, never filled once it has, and filled at the bar and price the test fills at with its stop placed from
    // that price, walked to its sale as the test walks it, the outcome none while it is held. A fill at or under its stop is
    // never filled, as the test leaves it.
    public static PlanTradeState Walk(PlanOrder order, ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, ReadOnlySpan<double> closes, int pick, ReadOnlySpan<double> rsi = default)
    {
        var fill = order.Entry == PlanPoints.FirstPullback
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

        return new PlanTradeState(false, filled.Bar, filled.Price, stop, PlanPoints.Walk(closes, opens, filled.Bar, filled.Price, stop, order.SaleFrom(filled.Price, stop), order.Cap, rsi));
    }
}
