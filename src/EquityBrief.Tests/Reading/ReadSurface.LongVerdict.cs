using System.Net;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 23.7's scorecard part: a rule's verdict over the long history from its family's first test year, the years its setting
// never saw apart from those it was chosen on with each side's mean edge after costs, the rules summed across the three
// over the same history, a rule the long walk does not reach saying why, and a rule no long test has read saying so.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public partial class ReadSurface
{
    internal static string[] LongVerdictRows => [CheckReach.Key("15.21 Paper trading", "An account's verdict over the long history")];

    [Fact]
    public void TheScorecardDrawsTheRuleOverTheLongHistoryWithTheYearsItsSettingNeverSawApart()
    {
        // The pullback from 2008: 400 trades over 2008 to 2018 totalling 40 risks, 0.100 each, and 150 from 2019 totalling
        // -15, -0.100 each.
        var own = new PaperLongVerdict("pullback", "live", false, 0.0123, 9, 18, 550, null, 2008, 400, 40.0, 150, -15.0, false);
        var pooled = new LoopPooledRow("loop-pooled-long-1", "2026-11", "pullback", PooledJudge.Own, ["GSPC", "MID"], ["a", "b"], "risks", 900, 60, 0.2, false, true, 18, 10, 1.5, true, false, null, null);
        var drawn = WebUtility.HtmlDecode(MarkRenderer.LongPart(own, pooled));

        Assert.Contains($"<h4>{MarkRenderer.LongHeading}</h4>", drawn, StringComparison.Ordinal);
        Assert.Contains("data-long=\"did not pass\"", drawn, StringComparison.Ordinal);
        Assert.Contains("did not pass</span> from 2008: p 0.0123, better in 9 of 18 years, 550 trades.", drawn, StringComparison.Ordinal);
        Assert.Contains("<p data-long-years=\"never seen\">The years its setting never saw, 2008 to 2018: 400 trades, +0.100 risks each after costs.</p>", drawn, StringComparison.Ordinal);
        Assert.Contains("<p data-long-years=\"chosen on\">The years it was chosen on, 2019 on: 150 trades, -0.100 risks each after costs.</p>", drawn, StringComparison.Ordinal);
        Assert.Contains($"<p data-long-pooled=\"did not pass\">{MarkRenderer.PooledHeading}:", drawn, StringComparison.Ordinal);

        var why = WebUtility.HtmlDecode(MarkRenderer.LongPart(own with { Why = "read on the standing years alone, the long history's walk not reaching it: its book" }, null));

        Assert.Contains("data-long=\"not walked\"", why, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-long-line=\"why\">Read on the standing years alone, the long history's walk not reaching it: its book.</p>", why, StringComparison.Ordinal);
        Assert.DoesNotContain("data-long-years", why, StringComparison.Ordinal);

        Assert.Contains("No monthly run has tested this rule over the long history yet.", MarkRenderer.LongPart(null, null), StringComparison.Ordinal);
    }
}
