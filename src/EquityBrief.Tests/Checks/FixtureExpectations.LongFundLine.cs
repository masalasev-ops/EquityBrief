using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.7 correction: the long test's verdict stays against the benchmark as ruled, and beside it the
// second line, a rule's trades from its family's first test year against its index's own fund held over each trade's
// sessions, worked by hand; a walk handing no trade's last session, and a book's months, have none and say so.
// see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
public partial class FixtureExpectations
{
    // One trade a month from 2005, listed at 20 with its stop at 18 and a result of 0.5, ended 10 sessions after it lists,
    // the fund rising 0.2 per cent a session: over its 10 sessions the fund gains 1.002 to the tenth less one, which on a buy
    // of 20 at a risk of 2 is ten times that in risks, so each trade reads 0.5 less it. The pullback reads its trades from
    // 2008, the verdict against its benchmark as before; the same trades handed as a list carry no fund line; and a book
    // has none.
    [Fact]
    public void TheLongTestsFundLineIsWorkedByHandBesideItsVerdict()
    {
        var calendar = LongCalendar();
        var fund = Enumerable.Range(0, calendar.Length).Select(at => 100 * Math.Pow(1.002, at)).ToArray();
        var read = new LoopRead("GSPC", null!, null!, null!, null!, null!, null!, calendar, 0, LoopFolds.Of(calendar, calendar[^1], LongTest.PriceFirstTestYear), Long: true, Fund: fund);
        ExitProcedures.Walked[] walked =
        [
            .. Enumerable.Range(0, calendar.Length - 10).Where(at => at % 21 == 0)
                .Select(at => new ExitProcedures.Walked(at, at + 10, new FamilyTrade(new FamilyListing(0, at, at, 0, 0, 20, 18, double.NaN, double.NaN, 10), 0, 0.5, 0.1), "target")),
        ];

        var units = new RuleTests.WalkedUnits(walked);
        var plain = units.ToArray();
        var tested = RuleTests.JudgeLong(read,
        [
            new RuleUnderTest(SetupFamilies.Pullback, "pullback live", "live", "the pullback", LoopProcedures.Risks, 10, units),
            new RuleUnderTest(BreakoutRule.Name, "breakout live", "live", "the breakout", LoopProcedures.Risks, 10, plain),
        ]);

        var pullback = tested.Single(one => one.Rule.Family == SetupFamilies.Pullback);
        var breakout = tested.Single(one => one.Rule.Family == BreakoutRule.Name);
        var each = 0.5 - (10 * (Math.Pow(1.002, 10) - 1));

        Assert.Equal(walked.Count(one => calendar[one.Entry].Year >= 2008), pullback.FundUnits);
        Assert.Equal(each, pullback.FundEdge!.Value, 9);
        Assert.Equal(walked.Select(one => one.Edge), units.Select(unit => unit.Edge));
        Assert.Null(breakout.FundUnits);
        Assert.Contains("; against the index's own fund held over the same sessions +0.298 a trade over", WalkForwardTester.RuleLine(pullback, longRun: true), StringComparison.Ordinal);
        Assert.EndsWith("; no fund line, its walk handing no trade's last session", WalkForwardTester.RuleLine(breakout, longRun: true), StringComparison.Ordinal);
        Assert.DoesNotContain("fund", WalkForwardTester.RuleLine(pullback), StringComparison.Ordinal);
        Assert.Null(RuleTests.FundLineOf(read with { Fund = null }, pullback.Rule, 2008));
    }
}
