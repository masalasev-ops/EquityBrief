using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Cards;

// How a card's number is written: in typical moves, as a multiple, as a share of the members, as a count of sessions or
// as a plain ratio.
public enum CardNumberForm
{
    Moves,
    Times,
    Share,
    Sessions,
    Ratio,
}

// One number a card's rows draw in place of a sentence: the ledger's reading it is read from, its column's heading, what
// the heading's tooltip says, and how it is written.
public sealed record CardNumber(string Column, string Heading, string Says, CardNumberForm Form);

// The three numbers each swing family's rows draw in place of a sentence, a variant's picks and the rows close to a buy
// point among them, each read from the ledger's readings the night stored for the member: the pullback's depth, dry-up
// and strength, the breakout's sessions since its high, volume and ranges, and the drift's reaction, volume and sessions
// since its report. The business-first family buys at the pullback's buy point and draws the pullback's. It computes no
// reading.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
public static class CardNumbers
{
    public static IReadOnlyList<CardNumber> Of(string family) => family switch
    {
        Families.SetupFamilies.Pullback or Families.FundamentalsRule.Name =>
        [
            new("depth", "Depth", "How far it came down from its high, in its own typical day's moves.", CardNumberForm.Moves),
            new("dry_up", "Dry-up", "Its volume while it came down, as a multiple of its 50-session average.", CardNumberForm.Times),
            new("strength", "Strength", "Its place among the index's members by return over 63 and 126 sessions.", CardNumberForm.Share),
        ],
        Families.BreakoutRule.Name =>
        [
            new("since_high", "Since its high", "The sessions since the highest high of the 20 sessions before.", CardNumberForm.Sessions),
            new("volume_multiple", "Volume", "The session's volume as a multiple of its 50-session average.", CardNumberForm.Times),
            new("range_ratio", "Ranges", "The mean daily range of the 20 sessions before over that of the 20 before them.", CardNumberForm.Ratio),
        ],
        Families.DriftRule.Name =>
        [
            new("reaction_moves", "Reaction", "The reaction session's rise, in its own typical day's moves.", CardNumberForm.Moves),
            new("volume_multiple", "Volume", "The reaction session's volume as a multiple of its 50-session average.", CardNumberForm.Times),
            new("freshness", "Since the report", "The sessions since its reaction session.", CardNumberForm.Sessions),
        ],
        _ => [],
    };

    // A member's three numbers for a family, none where the night stored no reading of it.
    public static IReadOnlyList<double?> Values(string family, IReadOnlyList<double?> readings) =>
        [.. Of(family).Select(number => LedgerReadings.IndexOf(number.Column) is var at && at >= 0 && at < readings.Count ? readings[at] : null)];
}
