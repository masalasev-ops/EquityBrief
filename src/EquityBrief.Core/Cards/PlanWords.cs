using System.Globalization;

namespace EquityBrief.Core.Cards;

// The words a pick's page draws for a way in it does not trade: the heading of each fold beneath the rule's own plan, and the
// line naming that way's stops as its own beside the stop the pick's trade is sold on.
// see: A pick's page draws its rule's plan as the plan and folds every other way in beneath it as not traded, each stop named as its own
public static class PlanWords
{
    public const string AnotherWay = "Not traded: another way in, for comparison";

    // That way's stops as its own: "Its stop, 54.70, is its own" or "Its stops, 57.91, 54.70 and 52.49, are its own", then
    // the stop the pick's trade is sold below, or that the pick's trade sells by its own exits where its rule states no stop.
    public static string OwnStops(IReadOnlyList<decimal> stops, decimal? traded, string setup)
    {
        var drawn = stops.Distinct().Select(stop => stop.ToString("0.00", CultureInfo.InvariantCulture)).ToArray();
        var named = drawn.Length switch
        {
            0 => "It places no stop of its own",
            1 => $"Its stop, {drawn[0]}, is its own",
            _ => $"Its stops, {string.Join(", ", drawn[..^1])} and {drawn[^1]}, are its own",
        };
        var sold = traded is { } stop
            ? $"the {setup}'s trade is sold on a close below {stop.ToString("0.00", CultureInfo.InvariantCulture)}"
            : $"the {setup}'s trade is sold by its own exits";

        return $"{named}: {sold}.";
    }

    // The share of plans like it that must reach their exit before their stop for the whole set to neither make nor lose
    // money, given how far the two sit from the buy: the risk over the risk and the reward together, one over one plus the
    // reward to risk.
    // see: Each plan states the share of the time it must reach its exit before its stop to break even, and whether its shares are the whole position
    public static decimal BreakEven(decimal rewardToRisk) => 1m / (1m + rewardToRisk);

    // That share in the words the staged plan's arithmetic has used since 4.8, the exit named as the plan names it.
    public static string BreakEvenSaid(decimal rewardToRisk, string exit) =>
        $"It is worth taking if it reaches {exit} before its stop more than {(BreakEven(rewardToRisk) * 100).ToString("0.0", CultureInfo.InvariantCulture)}% of the time.";

    // The example's shares named as the whole position for a plan buying it at once.
    public static string WholePosition(long shares) =>
        $"The {shares.ToString("N0", CultureInfo.InvariantCulture)} shares are the whole position, bought at once.";
}
