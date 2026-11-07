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
}
