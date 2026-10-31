using System.Globalization;

namespace EquityBrief.Core.Cards;

// How a taken trade is managed, in the rule's own terms and no others, written from the family and the plan the card
// stored: each stop a close below it, since every rule here sells on a close through its stop and never on a price
// touched during a session; a target a close at or above it; a trailing stop raised to the highest close less the plan's
// distance and never lowered; and the cap a count of sessions from the buy. From 21.14 a swing trade a close through its
// stop, at its target or under its trail ends is sold at the next session's open, as the short-term dip sells after its
// stop's close or its exit's; each sells at its last session's close. No step a rule lacks is named.
// The terms are said in the reader's words: a session a trading day, the cap a time limit and a rebalance a reshuffle.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: The dip buys at the next session's open and sells at the next open after its exit's close or its stop's, and at its last session's close
// see: A swing trade sold on a close's signal is sold at the next session's open by the fill step, and no rule's version moves because of it
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public static class Management
{
    // A short-term dip pick's management: sold at the next session's open after a close under its stop or one its exit
    // reads, by its dial's level, above its exit's average of closes, from 22.3 its 2-session RSI closing above its level
    // or a close above the close before, and at the close of its last session where neither came.
    public static IReadOnlyList<string> ForDip(decimal? stop, int exitLevel, int? timeLimit)
    {
        var words = new List<string>();

        if (stop is { } floor)
        {
            words.Add($"Sell at the next trading day's open after a day that closes below {Price(floor)}.");
        }

        words.Add(exitLevel switch
        {
            Families.DipRule.RsiExit => FormattableString.Invariant($"Sell at the next trading day's open after a day its {Families.DipRule.RsiSessions}-day RSI closes above {exitLevel}."),
            Families.DipRule.UpCloseExit => "Sell at the next trading day's open after a day that closes above the day before.",
            _ => FormattableString.Invariant($"Sell at the next trading day's open after a day that closes above its {exitLevel}-day average close."),
        });
        words.Add("No target: the trade is ended by its stop, its exit or its time limit.");

        if (timeLimit is { } most)
        {
            words.Add(FormattableString.Invariant($"Otherwise sell at the close {most} trading days after the buy."));
        }

        return words;
    }

    public static IReadOnlyList<string> For(decimal? stop, decimal? target, decimal? trail, int? cap, bool heavyweights)
    {
        if (heavyweights)
        {
            return
            [
                "Kept while it leads its sector: sell at the close of a month's reshuffle, its first trading day or the first after it whose stored year holds the closes the rule reads, where the rule would no longer buy it.",
                "Sell at its last close as a member if it leaves the index.",
                "No stop and no target: nothing sells it at a price set in advance, so the whole position is at risk.",
            ];
        }

        var words = new List<string>();

        if (stop is { } floor)
        {
            words.Add(trail is { } distance
                ? $"Sell at the next trading day's open after a day that closes below the stop, {Price(floor)} at the buy, raised after each close to that close less {Price(distance)} and never lowered."
                : $"Sell at the next trading day's open after a day that closes below {Price(floor)}.");
        }

        words.Add(target is { } goal
            ? $"Sell at the next trading day's open after a day that closes at or above {Price(goal)}."
            : "No target: the trade is ended by its stop or its time limit.");

        if (cap is { } most)
        {
            words.Add(FormattableString.Invariant($"Otherwise sell at the close {most} trading days after the buy."));
        }

        return words;
    }

    static string Price(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);
}
