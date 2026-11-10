using System.Collections.Concurrent;
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

    // A listing's target, none for one whose stop trails or whose rule names none, as the dip's does.
    static double? Target(FamilyListing listing) => listing.Trails || double.IsNaN(listing.Target) ? null : listing.Target;

    (double? Result, int Sessions, string End) Walked(FamilyListing listing)
    {
        var series = read.Series[listing.Name];
        var (opens, lows, closes) = prices.GetOrAdd(listing.Name, _ =>
            ([.. series.Bars.Select(bar => Statistic.FromPrice(bar.Open))], [.. series.Bars.Select(bar => Statistic.FromPrice(bar.Low))], [.. series.Bars.Select(bar => Statistic.FromPrice(bar.Close))]));
        var order = PlanTrades.Place(plan, family, series, listing.Bar, listing.Stop, Target(listing), listing.Trails ? listing.Trail : null, listing.Cap)
            ?? PlanTrades.Unplaced(plan, listing.Stop, listing.Cap);
        var state = PlanHook.Walk(order, opens, lows, closes, listing.Bar, order.RsiAbove is null ? default : PlanPoints.RsiOf(closes));
        var rest = Math.Max(1, closes.Length - listing.Bar);

        if (state.Waiting)
        {
            return (null, rest, string.Empty);
        }

        if (state.Unfilled)
        {
            return (null, Math.Max(1, order.Window) + (order.Entry == PlanPoints.FirstPullback ? 1 : 0), PlanEnd.Unfilled);
        }

        if (state.Outcome is not { } outcome || state.FillBar is not { } filled || state.Fill is not { } fill)
        {
            return (null, rest, string.Empty);
        }

        return ((outcome.Sold - fill) / (listing.Entry - listing.Stop), filled - listing.Bar + outcome.Sessions, outcome.End);
    }
}
