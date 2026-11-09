using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.8 correction: the buy and sell points sell the dip at the next open after a close whose
// 2-session RSI stands above 65, worked by hand over a constructed path, its stop still read first and its time limit last;
// the RSI read off the closes as the dip's own rule reads it; and a member of its benchmark sold the same way.
// see: The buy and sell points sell the dip at the next open after its 2-session RSI closes above 65
public partial class FixtureExpectations
{
    // Bought at 10 on the session after the pick with its stop at 9: the RSI reads 30, 50 and then 70 on the third session
    // held, whose close of 10.6 sells at the next open, 10.8, 0.8 risks; an RSI never above 65 holds it to its time limit of 5.
    [Fact]
    public void TheDipSellsAtTheNextOpenAfterItsRsiClosesAbove65()
    {
        double[] closes = [10, 9.8, 10.1, 10.6, 10.9, 11.0, 11.1, 11.2];
        double[] opens = [10, 10.0, 9.9, 10.2, 10.8, 11.0, 11.1, 11.2];
        double[] rsi = [double.NaN, 30, 50, 70, 80, 60, 50, 40];
        double[] calm = [double.NaN, 30, 50, 60, 64, 60, 50, 40];

        var sold = PlanPoints.Walk(closes, opens, 1, 10.0, 9.0, PlanPoints.DipSale, 5, rsi)!;
        var held = PlanPoints.Walk(closes, opens, 1, 10.0, 9.0, PlanPoints.DipSale, 5, calm)!;

        Assert.Equal((10.8, 3, PlanPoints.Reached), (sold.Sold, sold.Sessions, sold.End));
        Assert.Equal(0.8, sold.Result, 9);
        Assert.Equal((11.1, 5, PlanPoints.Timed), (held.Sold, held.Sessions, held.End));

        // The stop is read before the RSI: a close under it sells as a stop on a session the RSI also stands above 65.
        var stopped = PlanPoints.Walk([10, 8.9, 9.5], [10, 10.0, 8.8], 1, 10.0, 9.0, PlanPoints.DipSale, 5, [double.NaN, 90, 90])!;

        Assert.Equal((8.8, PlanPoints.Stopped), (stopped.Sold, stopped.End));

        // A member of its benchmark bought at its next open, 10.0, at a risk of 1, sells the same way, 0.8 risks.
        Assert.Equal(0.8, PlanPoints.MemberResult(closes, opens, 0, 1, _ => 9, _ => PlanPoints.DipSale, 5, rsi)!.Value, 9);

        Assert.Equal(DipRule.RsiExit, PlanPoints.DipSale.RsiAbove);
        Assert.Null(PlanPoints.DipSale.Target);
        Assert.Null(PlanPoints.DipSale.Trail);
    }

    // The RSI the walk reads is the dip rule's own over the same closes, NaN where it reads none.
    [Fact]
    public void TheDipsRsiIsReadOffTheClosesAsItsRuleReadsIt()
    {
        double[] closes = [10, 10.5, 10.2, 9.8, 9.9, 10.4, 10.9];
        var rule = DipRule.Rsi(closes, DipRule.RsiSessions);
        var read = PlanPoints.RsiOf(closes);

        Assert.Equal(closes.Length, read.Length);

        for (var at = 0; at < closes.Length; at++)
        {
            if (rule[at] is { } value)
            {
                Assert.Equal(value, read[at], 12);
            }
            else
            {
                Assert.True(double.IsNaN(read[at]));
            }
        }

        Assert.Contains(read, value => double.IsFinite(value));
    }
}
