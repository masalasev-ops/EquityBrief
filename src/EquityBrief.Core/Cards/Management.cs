using System.Globalization;

namespace EquityBrief.Core.Cards;

// How a taken trade is managed, in the rule's own terms and no others, written from the family and the plan the card
// stored: each stop a close below it, since every rule here sells on a close through its stop and never on a price
// touched during a session; a target a close at or above it; a trailing stop raised to the highest close less the plan's
// distance and never lowered; and the cap a count of sessions from the buy. The short-term dip sells at the next
// session's open after its stop's close or its exit's, and at its last session's close. No step a rule lacks is named.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: The dip buys at the next session's open and sells at the next open after its exit's close or its stop's, and at its last session's close
public static class Management
{
    // A short-term dip pick's management: sold at the next session's open after a close under its stop or above its
    // exit's average of closes, and at the close of its last session where neither came.
    public static IReadOnlyList<string> ForDip(decimal? stop, int exitAverage, int? timeLimit)
    {
        var words = new List<string>();

        if (stop is { } floor)
        {
            words.Add($"Sell at the next session's open after a session that closes below {Price(floor)}.");
        }

        words.Add(FormattableString.Invariant($"Sell at the next session's open after a session that closes above its {exitAverage}-session average of closes."));
        words.Add("No target: the trade is ended by its stop, its exit or its time limit.");

        if (timeLimit is { } most)
        {
            words.Add(FormattableString.Invariant($"Otherwise sell at the close {most} sessions after the buy."));
        }

        return words;
    }

    public static IReadOnlyList<string> For(decimal? stop, decimal? target, decimal? trail, int? cap, bool heavyweights)
    {
        if (heavyweights)
        {
            return
            [
                "Held while it leads its sector: sell at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the rule reads, where the rule would no longer buy it.",
                "Sell at its last close as a member if it leaves the index.",
                "No stop and no target: nothing sells it at a price set in advance, so the whole position is at risk.",
            ];
        }

        var words = new List<string>();

        if (stop is { } floor)
        {
            words.Add(trail is { } distance
                ? $"Sell at the close of a session that closes below the stop, {Price(floor)} at the buy, raised after each close to that close less {Price(distance)} and never lowered."
                : $"Sell at the close of a session that closes below {Price(floor)}.");
        }

        words.Add(target is { } goal
            ? $"Sell at the close of a session that closes at or above {Price(goal)}."
            : "No target: the trade is ended by its stop or its cap.");

        if (cap is { } most)
        {
            words.Add(FormattableString.Invariant($"Otherwise sell at the close {most} sessions after the buy."));
        }

        return words;
    }

    static string Price(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);
}
