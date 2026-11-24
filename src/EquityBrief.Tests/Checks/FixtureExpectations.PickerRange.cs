using EquityBrief.Core.Judge;
using EquityBrief.Core.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: every AI stock picker version buys within the approved range from the night of 2026-11-24, worked
// by hand over constructed sessions: a pick of that night opening inside its range bought at the open, one opening above it
// bought at the ceiling where the price comes back within the order's sessions and not bought above where it does not, one
// opening under the floor not bought below, one whose order is still standing neither; and a pick of an earlier night bought
// at the open as it was.
// see: Every picker version buys within the approved range from the night of 2026-11-24, the picks of earlier nights bought at the open as they were
public partial class FixtureExpectations
{
    static readonly DateOnly RangeNight = new(2026, 11, 24);

    static JudgeBars RangeBars(double[] opens, double[] closes, double[] lows) =>
        new([.. Enumerable.Range(0, opens.Length).Select(at => RangeNight.AddDays(at))], opens, closes, lows);

    [Fact]
    public void APickerPickFromTheRangesFirstNightBuysWithinItsRangeAndOneOutsideItNamesItsSideAsWorkedByHand()
    {
        // The width approved: a ceiling half a typical move above the close and a floor 2 under it. A close of 100 and a move
        // of 2 set a range of 96 to 101; a pick of the night before sets none, and buys at the open.
        Assert.Equal(new RangeWidths(0.5, 2), JudgeAccounts.Widths);
        Assert.Equal(new BuyRange(101, 96), JudgeAccounts.RangeOf(RangeNight, 100m, 2.0));
        Assert.Null(JudgeAccounts.RangeOf(RangeNight.AddDays(-1), 100m, 2.0));
        Assert.Null(JudgeAccounts.WidthsOn(RangeNight.AddDays(-1)));
        Assert.Equal(JudgeAccounts.Widths, JudgeAccounts.WidthsOn(RangeNight));

        var widths = JudgeAccounts.Widths;

        // Inside: an open of 100.5 buys at 100.5, a week's stop 2 under it at 98.5 and its target 4 over it at 104.5.
        var inside = JudgeAccounts.Placed("AAA", 1, "1 week", JudgeMenu.Whole, 2.0, RangeBars([100, 100.5, 101], [100, 101, 101], [100, 100, 100.5]), _ => null, widths);

        Assert.Null(inside.NotBought);
        Assert.Equal((RangeNight.AddDays(1), 100.5m, 98.5m, 104.5m), (inside.Trade!.Bought, inside.Trade.Buy, inside.Trade.Stop!.Value, inside.Trade.Target!.Value));

        // Above and back: an open of 103 over the ceiling of 101 places an order there; the next session opens at 102 and
        // trades down to 100.8, so it fills at 101 on that session, its stop 99 and its target 105 from the price paid. A close
        // of 106 on the session after sells at the next open, 107: 3 risks.
        var back = JudgeAccounts.Placed("BBB", 1, "1 week", JudgeMenu.Whole, 2.0, RangeBars([100, 103, 102, 104, 107], [100, 103, 102, 106, 107], [100, 102, 100.8, 103, 107]), _ => null, widths);

        Assert.Equal((RangeNight.AddDays(2), 101m, 99m, 105m, RangeNight.AddDays(4), 107m), (back.Trade!.Bought, back.Trade.Buy, back.Trade.Stop!.Value, back.Trade.Target!.Value, back.Trade.Ended!.Value, back.Trade.Sale!.Value));
        Assert.Equal(3.0, JudgeAccounts.ResultOf(back.Trade));

        // Above and not back within the 2 sessions: not bought, above; under the floor of 96: not bought, below.
        var above = JudgeAccounts.Placed("CCC", 1, "1 week", JudgeMenu.Whole, 2.0, RangeBars([100, 103, 104, 105], [100, 103, 104, 105], [100, 102, 103, 104]), _ => null, widths);
        var below = JudgeAccounts.Placed("DDD", 1, "1 week", JudgeMenu.Whole, 2.0, RangeBars([100, 95, 95], [100, 95, 95], [100, 94, 94]), _ => null, widths);

        Assert.Equal((null, BuyRanges.Above), (above.Trade, above.NotBought));
        Assert.Equal((null, BuyRanges.Below), (below.Trade, below.NotBought));

        // An order still standing on the last session held is neither bought nor not bought.
        var standing = JudgeAccounts.Placed("EEE", 1, "1 week", JudgeMenu.Whole, 2.0, RangeBars([100, 103], [100, 103], [100, 102]), _ => null, widths);

        Assert.Equal((null, null), (standing.Trade, standing.NotBought));

        // With no widths, the same bars as the pick above its range buy at the open, 103, as a pick of an earlier night does.
        var open = JudgeAccounts.Placed("CCC", 1, "1 week", JudgeMenu.Whole, 2.0, RangeBars([100, 103, 104, 105], [100, 103, 104, 105], [100, 102, 103, 104]), _ => null);

        Assert.Equal((RangeNight.AddDays(1), 103m, (string?)null), (open.Trade!.Bought, open.Trade.Buy, open.NotBought));
    }
}
