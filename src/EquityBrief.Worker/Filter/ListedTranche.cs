using System.Text.Json;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Prices;
using EquityBrief.Data;

namespace EquityBrief.Worker.Filter;

// The ladder's first tranche as the night's listing kept it, read for the trade gate: the entry the
// plan's arithmetic takes, which is the middle of the tranche's zone, its stop, the first traded
// target and their reward to risk, worked by the ladder's own arithmetic rather than a second
// statement of it, and rounded as the ladder stores it.
// see: Code owns every number
public static class ListedTranche
{
    public static FirstTranche Of(string planAtListing)
    {
        using var document = JsonDocument.Parse(planAtListing);
        var root = document.RootElement;

        decimal? Price(string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? Money.FromStorage(value.GetString()!)
                : null;

        var (low, high, stop, target) = (Price("entryLow"), Price("entryHigh"), Price("stop"), Price("firstTradedTarget"));

        var plan = new Ladder(
            low is { } entryLow && high is { } entryHigh ? [new Tranche(entryLow, entryHigh, TrancheCondition.ReachesTheZone, stop)] : [],
            target is { } exit ? [new Exit(exit, exit, Traded: true, Trailing: false, Fraction: string.Empty, Reason: null)] : [],
            null,
            null,
            []);

        var arithmetic = LadderSeries.ArithmeticFor(plan);
        decimal? entry = plan.Tranches.Count > 0 ? LadderSeries.Midpoint(plan.Tranches[0]) : null;

        return new FirstTranche(
            entry,
            stop,
            target,
            arithmetic.FirstRewardToRisk is { } ratio ? Math.Round(ratio, PriceForm.Places, MidpointRounding.AwayFromZero) : null,
            arithmetic.Absent);
    }
}
