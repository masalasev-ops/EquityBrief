using EquityBrief.Api.Reading;
using EquityBrief.Core.Ladders;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 18.3 correction of 2026-10-11: the ladder figure says under its heading where no band stands on a side
// of the price, where PEP's page drew "Buy into these" over nothing; the reward to risk bars are as tall as the bars they
// draw, where a tranche stating no risk left a blank row beneath them; and the technicals name the provider's multiples as
// the provider's, where CVX's page drew a price to earnings of 20.4 beside the valuation's 19.6 with nothing saying the two
// are struck on other bases. Worked by hand.
public partial class ReadSurface
{
    [Fact]
    public void ALadderSideHoldingNoBandSaysSoTheRewardBarsAreAsTallAsTheBarsTheyDrawAndTheProvidersMultiplesAreNamedItsOwn()
    {
        // The provider's trailing and forward multiples, named as the provider's in the grid and in the sentence beneath it.
        var strip = NameScreen.FactStrip(
            "XYZ",
            110m,
            null,
            null,
            [new FilingRow("XYZ", new DateOnly(2026, 8, 1), "{\"valuation\":{\"trailingPe\":\"20.4\",\"forwardPe\":\"18.1\"}}", "provider")],
            []);

        Assert.Contains("<dt>Price to earnings, the provider's</dt><dd>20.4 <small>forward 18.1</small></dd>", strip, StringComparison.Ordinal);
        Assert.Contains("the provider's trailing multiple 20.4, its forward multiple 18.1</p>", strip, StringComparison.Ordinal);

        var marks = new MarkRenderer();

        // PEP at 125.97 under every band it holds: nothing beneath the price, the three bands above it.
        var under = marks.LadderFigure("PEP", PepBands, 125.97m, live: false);

        Assert.Contains("data-side=\"below\">No band stands below the price, so the plan has nowhere beneath it to buy.</text>", under, StringComparison.Ordinal);
        Assert.DoesNotContain("data-side=\"above\">No band", under, StringComparison.Ordinal);

        // A name above every band it holds: nothing above the price to sell into.
        var over = marks.LadderFigure("XYZ", [new LadderBand(100m, 102m, "support", 1.2, 2, new DateOnly(2026, 9, 1), "tranche 1 buys here")], 110m, live: false);

        Assert.Contains("data-side=\"above\">No band stands above the price, so the plan has nothing above it to sell into.</text>", over, StringComparison.Ordinal);
        Assert.DoesNotContain("data-side=\"below\">No band", over, StringComparison.Ordinal);

        // Two tranches stating a risk and a third stating none: 26 above the bars, 20 a bar, 8 between the two tranches'
        // bars and a foot of 10, 84 in all, the third tranche taking no room.
        var bars = marks.RewardToRiskBars(
            "XYZ",
            [
                new TrancheShare(10m, 11m, 10.5m, 3, 9.5m, 1m, [new TargetReward(12.5m, 2m, 2m, true)]),
                new TrancheShare(8m, 9m, 8.5m, 3, 7.5m, 1m, [new TargetReward(12.5m, 4m, 4m, true)]),
                new TrancheShare(6m, 7m, 6.5m, 3, null, null, [new TargetReward(12.5m, 6m, null, true)]),
            ]);

        Assert.Contains("data-mark=\"reward-to-risk\" data-bars=\"2\"", bars, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 640 84\"", bars, StringComparison.Ordinal);
    }
}
