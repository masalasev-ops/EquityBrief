using EquityBrief.Core.Families;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// Home and the combined account over the operator's answers of 2026-11-06, each part held to its new words against the page
// as it read before.
public partial class ReadSurface
{
    // On a night the monthly books reshuffled, Home's ideas were the night's new picks alone and nothing on it named what the
    // books bought; Tonight said the reshuffle added them apart.
    // see: On a night the monthly books reshuffle Home names their buys apart from the new picks and says they come from the reshuffle
    [Fact]
    public void OnAReshuffleNightHomeNamesTheBooksBuysAndSaysTheyComeFromTheReshuffle()
    {
        var marks = new MarkRenderer();
        var app = new SinglePageApp();
        (string, string)[] bought = [(HeavyweightRule.Name, "XOM"), (HeavyweightRule.Name, "CVX"), (QualityRule.Name, "AAPL")];

        var drawn = app.HomeRegion(marks, Day() with { Reshuffled = bought }, string.Empty);

        Assert.Contains(
            "<p class=\"home-more home-reshuffle\" data-reshuffled=\"3\">The monthly books reshuffled tonight and also buy 3: XOM and CVX (Leader of its sector), AAPL (Strong business at a fair price). These come from the reshuffle, not from tonight's new picks.</p>",
            drawn,
            StringComparison.Ordinal);

        // The new picks' count stands as it was, the books' buys said apart from it.
        Assert.Contains("data-ideas=\"none\">No new ideas tonight.", drawn, StringComparison.Ordinal);

        Assert.Equal(
            "The monthly books reshuffled tonight and also buy 1: AAPL (Strong business at a fair price). It comes from the reshuffle, not from tonight's new picks.",
            SinglePageApp.ReshuffleLine([(QualityRule.Name, "AAPL")]));

        // A night no book buys draws no line.
        Assert.Null(SinglePageApp.ReshuffleLine([]));
        Assert.DoesNotContain("home-reshuffle", app.HomeRegion(marks, Day(), string.Empty), StringComparison.Ordinal);
    }
}
