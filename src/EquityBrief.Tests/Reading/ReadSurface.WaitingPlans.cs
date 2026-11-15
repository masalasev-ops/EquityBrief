using EquityBrief.Core.Families;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The operator's ruling of 2026-11-15 on the review of PR 699, item 1: the drift's and the breakout's lines say their plans buy
// only on a pullback, and their record box says its figure reads high, in the operator's words.
// see: The drift and the breakout buy only on a pullback to their buy price within 20 trading days, and their record says it reads high
public partial class ReadSurface
{
    [Fact]
    public void TheDriftAndTheBreakoutSayTheyBuyOnlyOnAPullbackAndTheirRecordReadsHigh()
    {
        Assert.Equal("A stock jumps after beating earnings. It is bought only if it pulls back to the buy price within 20 trading days, so most signals are never bought.", MarkRenderer.SetupWords[DriftRule.Name].Plain);
        Assert.Equal("A stock breaks above its recent high on heavy volume. It is bought only if it pulls back to the buy price within 20 trading days, so most signals are never bought.", MarkRenderer.SetupWords[BreakoutRule.Name].Plain);
        Assert.All(MarkRenderer.SetupWords.Values, words => Assert.DoesNotContain("no fixed target", words.About, StringComparison.OrdinalIgnoreCase));

        const string High = "The buy price and stop were set partly from these same years, so this figure reads high.";

        // A drift's and a breakout's figure, made or lost, on any index, carries the line; a pullback's does not, nor a record
        // short of the count it needs.
        Assert.Contains(High, MarkRenderer.SetupRecordBox(new SetupLongRecord("GSPC", DriftRule.Name, 0.42, 875, 2019, 2026)), StringComparison.Ordinal);
        Assert.Contains(High, MarkRenderer.SetupRecordBox(new SetupLongRecord("MID", BreakoutRule.Name, -0.1, 424, 2019, 2026)), StringComparison.Ordinal);
        Assert.DoesNotContain(High, MarkRenderer.SetupRecordBox(new SetupLongRecord("GSPC", SetupFamilies.Pullback, 0.42, 875, 2019, 2026)), StringComparison.Ordinal);
        Assert.DoesNotContain(High, MarkRenderer.SetupRecordBox(new SetupLongRecord("GSPC", DriftRule.Name, 0.42, 12, 2019, 2026)), StringComparison.Ordinal);
    }
}
