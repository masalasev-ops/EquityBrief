using System.Globalization;
using EquityBrief.Core.Filter;
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

// One number a card's rows draw in place of a sentence: the ledger's reading it is read as, its column's heading, what
// the heading's tooltip says, how it is written, and the gate and the value a family's stored answer holds it under where
// the answer holds it.
public sealed record CardNumber(string Column, string Heading, string Says, CardNumberForm Form, string? Gate = null, string? Value = null);

// A number as a card draws it: its figure, or none with why.
public sealed record CardCell(double? Value, string? None);

// The three numbers each swing family's rows draw in place of a sentence, a variant's picks and the rows close to a buy
// point among them: the pullback's depth, dry-up and strength, the breakout's sessions since its high, volume and ranges,
// and the drift's reaction, volume and sessions since its report. The business-first family buys at the pullback's buy
// point and draws the pullback's. It computes no reading but a drift's sessions since the report day its setup is read
// at, counted on the exchange's calendar.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
public static class CardNumbers
{
    public const string NotReached = "not reached";

    public const string NotStored = "not stored";

    public const string NoSetup = "no setup stored";

    public const string NotRead = "not read";

    public static IReadOnlyList<CardNumber> Of(string family) => family switch
    {
        Families.SetupFamilies.Pullback or Families.FundamentalsRule.Name =>
        [
            new("depth", "Depth", "How far it came down from its high, in its own typical day's moves.", CardNumberForm.Moves, SwingGates.Setup, "depth"),
            new("dry_up", "Dry-up", "Its volume while it came down, as a multiple of its 50-session average.", CardNumberForm.Times, SwingGates.Setup, "dry-up"),
            new("strength", "Strength", "Its place among the index's members by return over 63 and 126 sessions.", CardNumberForm.Share, SwingGates.Trend, "strength"),
        ],
        Families.BreakoutRule.Name =>
        [
            new("since_high", "Since its high", "The sessions since the highest high of the 20 sessions before.", CardNumberForm.Sessions),
            new("volume_multiple", "Volume", "The session's volume as a multiple of its 50-session average.", CardNumberForm.Times, Families.BreakoutRule.Volume, "multiple"),
            new("range_ratio", "Ranges", "The mean daily range of the 20 sessions before over that of the 20 before them.", CardNumberForm.Ratio, Families.BreakoutRule.Tightened, "ratio"),
        ],
        Families.DriftRule.Name =>
        [
            new("reaction_moves", "Reaction", "The reaction session's rise, in its own typical day's moves.", CardNumberForm.Moves, Families.DriftRule.Reaction, "moves"),
            new("volume_multiple", "Volume", "The reaction session's volume as a multiple of its 50-session average.", CardNumberForm.Times, Families.DriftRule.Volume, "multiple"),
            new("freshness", "Since the report", "The sessions since its reaction session.", CardNumberForm.Sessions, Families.DriftRule.Print, "back"),
        ],
        _ => [],
    };

    // A member's numbers for a family as its card draws them. Where the family's stored answer for the member is handed
    // in, a number the answer holds under a gate is read from that gate's values, saying where the answer never reached
    // the gate or the gate stored no such value. Otherwise a family's own reading, which only the member's setup of the
    // family carries, is read from that setup, saying where the night stored none, and every other reading from the
    // member's readings on the night, saying where it read none.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public static IReadOnlyList<CardCell> Cells(string family, IReadOnlyList<Gate>? answer, IReadOnlyList<double?>? setup, IReadOnlyList<double?>? member) =>
    [
        .. Of(family).Select(number =>
        {
            if (answer is not null && number.Gate is { } named)
            {
                return answer.FirstOrDefault(gate => gate.Name == named) is not { } gate
                    ? new CardCell(null, NotReached)
                    : gate.Values.TryGetValue(number.Value!, out var stored) && double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                        ? new CardCell(value, null)
                        : new CardCell(null, NotStored);
            }

            var at = LedgerReadings.IndexOf(number.Column);

            if (Loop.RidgeScore.FamilyOwn.Contains(number.Column))
            {
                return setup is null
                    ? new CardCell(null, NoSetup)
                    : at < setup.Count && setup[at] is { } own ? new CardCell(own, null) : new CardCell(null, NotRead);
            }

            return member is not null && at < member.Count && member[at] is { } read ? new CardCell(read, null) : new CardCell(null, NotRead);
        }),
    ];

    // A drift's setup as a night after its report day reads it, on an index where only the ledger's setups carry the
    // family's own readings and the ledger stores a drift's on its report day alone: its readings as stored there, with its
    // sessions since the report counted on the exchange's calendar from that day to the night; none where the day is
    // after the night, the calendar's table does not reach either, or the day lies further back than the window the
    // rule states, its last session included.
    public static IReadOnlyList<double?>? AtReportDay(DateOnly reportDay, IReadOnlyList<double?> readings, DateOnly night, int window)
    {
        if (reportDay > night || Bars.ExchangeClosures.SessionsUntil(reportDay, night) is not { } since || since > window)
        {
            return null;
        }

        var held = readings.ToArray();
        var at = LedgerReadings.IndexOf("freshness");

        if (at >= 0 && at < held.Length)
        {
            held[at] = since;
        }

        return held;
    }
}
