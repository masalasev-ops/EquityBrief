using System.Collections.Concurrent;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// From 23.10 a family's listings under the plan its rule states, for the loop's walks: each listing's order placed from its
// stock's series on its session as the night places it and walked once, filled where its level trades within its window and
// sold where the plan sells, its result in the rule's own risk at its close; one never filled holding its stock through its
// window with no result, and one the history has not ended holding it to the history's end; and each session's benchmark of
// the same plan on every member read once.
// see: At phase 23's restart each price family's live rule takes the plan its long test recommends where it beats today's plan from 2019 above the ruled benchmark, today's plan kept beside it as a variant
public sealed class PlannedListings(LoopRead read, PlanCombination plan, string family)
{
    readonly ConcurrentDictionary<(int Name, int Bar), (double? Result, int Sessions, string End)> walked = new();

    readonly ConcurrentDictionary<(int Session, double StopMoves, double? Reward, double? Trail, int Cap), double> benchmarks = new();

    readonly ConcurrentDictionary<int, (double[] Opens, double[] Lows, double[] Closes)> prices = new();

    public PlanCombination Plan => plan;

    public (double? Result, int Sessions, string End) Of(FamilyListing listing) =>
        walked.GetOrAdd((listing.Name, listing.Bar), _ => Walked(listing));

    public (double? Result, int Sessions) Exit(FamilyListing listing)
    {
        var (result, sessions, _) = Of(listing);

        return (result, sessions);
    }

    public double Benchmark(FamilyListing listing)
    {
        if (!(listing.Move > 0) || !(listing.Entry > listing.Stop))
        {
            return double.NaN;
        }

        var risk = listing.Entry - listing.Stop;
        double? reward = Target(listing) is { } aimed ? (aimed - listing.Entry) / risk : null;
        double? trail = listing.Trails ? listing.Trail / listing.Move : null;
        var key = (listing.Session, risk / listing.Move, reward, trail, listing.Cap);

        return benchmarks.GetOrAdd(key, held =>
            PlanTrades.Benchmark(
                plan,
                held.StopMoves,
                held.Reward,
                held.Trail,
                held.Cap,
                read.Members.Names[held.Session].Select((name, at) => (read.Series[name], read.Members.Bars[held.Session][at]))).Benchmark ?? double.NaN);
    }

    // On the operator's ruling of 2026-11-14 a listing bought as its plan buys it and sold by an exit of the menu in place of
    // everything the plan says about selling, its stop, its take-profit, its staged sales and its time limit: the family's own
    // stop, target and trail set as far from the fill as from the listing's entry, the exit's path read from the fill's
    // session and sold at the open after the close that ends it where its end calls for one, a half's at half weight; none
    // where the plan never filled. It is the change the loop puts to the operator for a rule standing at a plan.
    public (double? Result, int Sessions, string End) Under(FamilyListing listing, ExitChoice exit) =>
        menus.GetOrAdd((listing.Name, listing.Bar, exit.Number), _ => WalkedUnder(listing, exit));

    readonly ConcurrentDictionary<(int Name, int Bar, int Exit), (double? Result, int Sessions, string End)> menus = new();

    // A listing's target, none for one whose stop trails or whose rule names none, as the dip's does.
    static double? Target(FamilyListing listing) => listing.Trails || double.IsNaN(listing.Target) ? null : listing.Target;

    (double[] Opens, double[] Lows, double[] Closes) PricesOf(int name) =>
        prices.GetOrAdd(name, _ =>
            ([.. read.Series[name].Bars.Select(bar => Statistic.FromPrice(bar.Open))], [.. read.Series[name].Bars.Select(bar => Statistic.FromPrice(bar.Low))], [.. read.Series[name].Bars.Select(bar => Statistic.FromPrice(bar.Close))]));

    (PlanOrder Order, PlanTradeState State) Placed(FamilyListing listing)
    {
        var series = read.Series[listing.Name];
        var (opens, lows, closes) = PricesOf(listing.Name);
        var order = PlanTrades.Place(plan, family, series, listing.Bar, listing.Stop, Target(listing), listing.Trails ? listing.Trail : null, listing.Cap)
            ?? PlanTrades.Unplaced(plan, listing.Stop, listing.Cap);

        return (order, PlanHook.Walk(order, opens, lows, closes, listing.Bar, order.RsiAbove is null ? default : PlanPoints.RsiOf(closes)));
    }

    (double? Result, int Sessions, string End)? NotFilled(FamilyListing listing, PlanOrder order, PlanTradeState state)
    {
        var rest = Math.Max(1, PricesOf(listing.Name).Closes.Length - listing.Bar);

        if (state.Waiting)
        {
            return (null, rest, string.Empty);
        }

        if (state.Unfilled)
        {
            return (null, Math.Max(1, order.Window) + (order.Entry == PlanPoints.FirstPullback ? 1 : 0), PlanEnd.Unfilled);
        }

        return state.FillBar is null || state.Fill is null ? (null, rest, string.Empty) : null;
    }

    (double? Result, int Sessions, string End) Walked(FamilyListing listing)
    {
        var (order, state) = Placed(listing);

        if (NotFilled(listing, order, state) is { } none)
        {
            return none;
        }

        if (state.Outcome is not { } outcome)
        {
            return (null, Math.Max(1, PricesOf(listing.Name).Closes.Length - listing.Bar), string.Empty);
        }

        return ((outcome.Sold - state.Fill!.Value) / (listing.Entry - listing.Stop), state.FillBar!.Value - listing.Bar + outcome.Sessions, outcome.End);
    }

    (double? Result, int Sessions, string End) WalkedUnder(FamilyListing listing, ExitChoice exit)
    {
        var (order, state) = Placed(listing);

        if (NotFilled(listing, order, state) is { } none)
        {
            return none;
        }

        var (filled, fill) = (state.FillBar!.Value, state.Fill!.Value);
        var risk = listing.Entry - listing.Stop;
        var (opens, _, closes) = PricesOf(listing.Name);

        if (!(risk > 0))
        {
            return (null, Math.Max(1, closes.Length - listing.Bar), string.Empty);
        }

        var anchor = new SetupAnchor(
            DateOnly.MinValue,
            fill,
            fill - risk,
            Target(listing) is { } aimed ? fill + (aimed - listing.Entry) : null,
            listing.Trails ? listing.Trail : null,
            listing.Cap,
            listing.Move > 0 ? risk / listing.Move : null);
        var outcome = ExitMenu.Replay(closes, filled, anchor, listing.Move, exit, opens, SeriesPrices.Highs(read.Series)[listing.Name]);
        var sessions = filled - listing.Bar + outcome.Sessions;

        if (outcome.Result is null)
        {
            return (null, Math.Max(1, sessions), outcome.End);
        }

        var halfOn = ExitFills.HalfSoldOn(bar => closes[bar], filled, anchor, exit, outcome);

        return (OpenFills.Sold(read.Series[listing.Name], outcome, filled, risk, halfOn), sessions, outcome.End);
    }
}
