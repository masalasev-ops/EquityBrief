using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 22.34: the chart page's scale fits the candles, the plan's lines and the levels shown, and an average never widens it.
// see: The chart page's scale fits the candles and the plan's lines and the levels shown, and an average never widens it
public class ChartScaleTests
{
    static readonly DateOnly First = new(2026, 9, 1);

    static IReadOnlyList<ChartCandle> Candles(decimal low, decimal high, double? sma200 = null, double? sma20 = null) =>
    [
        .. Enumerable.Range(0, 20).Select(at => new ChartCandle(
            First.AddDays(at),
            First.AddDays(at),
            (low + high) / 2,
            high,
            low,
            (low + high) / 2,
            1_000,
            at == 0 ? sma20 : (double)((low + high) / 2),
            null,
            at == 0 ? sma200 : null)),
    ];

    static (double Low, double High) Axis(string svg)
    {
        var read = Regex.Match(svg, "data-axis-low=\"([0-9.]+)\" data-axis-high=\"([0-9.]+)\"");

        return (double.Parse(read.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(read.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    static string Legend(string svg, string name) =>
        Regex.Match(svg, "<text class=\"m-legend-t\" data-legend=\"" + name + "\"[^>]*>([^<]*)</text>").Groups[1].Value;

    [Fact]
    public void TheFitLeavesASmallMarginAndAFlatSeriesStillHasARange()
    {
        // Worked by hand: 100 to 200 spans 100, so 3 either side.
        var axis = ChartScale.Fit([100, 200, 150]);

        Assert.Equal(97, axis.Low, 6);
        Assert.Equal(203, axis.High, 6);

        // 50 flat takes 3% of itself, 1.5, either side, and nothing at all takes 1.
        Assert.Equal((48.5, 51.5), (ChartScale.Fit([50, 50]).Low, ChartScale.Fit([50, 50]).High));
        Assert.Equal((-1, 1), (ChartScale.Fit([0]).Low, ChartScale.Fit([0]).High));
    }

    [Theory]
    // FISV's candles at their margin: 5 labels 45 to 70, six lines, and 2.5 would label twelve.
    [InlineData(43.07, 71.21, 5)]
    // 40 to 75: 5 labels eight, past the seven, so 10.
    [InlineData(40, 75, 10)]
    [InlineData(4.1, 18, 2)]
    [InlineData(0.8, 1.3, 0.1)]
    [InlineData(1000, 3000, 500)]
    public void TheStepIsTheSmallestRoundOneKeepingWithinTheLines(double low, double high, double expected)
    {
        var step = ChartScale.Step(low, high);

        Assert.Equal(expected, step, 9);
        Assert.InRange(ChartScale.Lines(low, high, step).Count, 1, ChartScale.MostLines);
    }

    [Fact]
    public void TheLinesAreWholeMultiplesInsideTheScaleAndTheLabelHoldsThePlacesItsStepNeeds()
    {
        Assert.Equal([45d, 50d, 55d, 60d, 65d, 70d], ChartScale.Lines(43.07, 71.21, 5));
        Assert.Equal("45", ChartScale.Label(45, 5));
        Assert.Equal("1,250", ChartScale.Label(1250, 50));
        Assert.Equal("2.5", ChartScale.Label(2.5, 0.5));
        Assert.Equal("0.25", ChartScale.Label(0.25, 0.25));
    }

    [Fact]
    public void AnAverageFarAboveTheCandlesIsClippedAndNamedAndTheScaleStaysAtTheCandles()
    {
        // FISV's shape: candles from 43.87 to 70.41 and a 200-session average of 171.32 at the left edge.
        var svg = new MarkRenderer().CandleChart("FISV", "daily", Candles(43.87m, 70.41m, sma200: 171.32), [], [], []);
        var (low, high) = Axis(svg);

        Assert.Equal(43.07, low, 1);
        Assert.Equal(71.21, high, 1);
        Assert.Equal("200-session average 171.32 at the left edge, off the chart above", Legend(svg, "sma200"));
        Assert.Contains("data-sma200=\"200-session average 171.32, off the chart above\"", svg, StringComparison.Ordinal);
        Assert.Matches("<g class=\"moving-average\" data-average=\"sma200\"[^>]* clip-path=\"url\\(#chart-pane-FISV-daily-plain\\)\">", svg);
        Assert.Contains("<clipPath id=\"chart-pane-FISV-daily-plain\">", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAverageBelowTheCandlesIsNamedAsBelowAndOneInsideReadsItsNewestValue()
    {
        var svg = new MarkRenderer().CandleChart("T", "weekly", Candles(100m, 120m, sma200: 50), [], [], []);

        Assert.Equal("200-session average 50.00 at the left edge, off the chart below", Legend(svg, "sma200"));
        Assert.Equal("20-session average 110.00", Legend(svg, "sma20"));
        Assert.DoesNotContain("data-sma20=\"20-session average 110.00, off", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void APlansLineWidensTheScaleAndALevelOnlyInTheDrawingTheToggleShows()
    {
        var marks = new MarkRenderer();
        var planned = marks.CandleChart("T", "daily", Candles(100m, 120m), [], [new ChartPlan("Rule", 110m, 90m, 150m)], []);

        Assert.True(Axis(planned).High > 150 && Axis(planned).Low < 90, "A plan's stop and target stand on the pane.");

        // A level inside the scale draws one chart; one past it draws two, the second reaching it.
        var inside = marks.CandleChart("T", "daily", Candles(100m, 120m), [new ChartBand(105m, 106m, "support", true, 1)], [], []);

        Assert.Single(Regex.Matches(inside, "<svg "));
        Assert.Contains("class=\"candle-chart\"", inside, StringComparison.Ordinal);

        var outside = marks.CandleChart("T", "daily", Candles(100m, 120m), [new ChartBand(60m, 62m, "support", true, 1)], [], []);
        var drawings = Regex.Matches(outside, "<svg [^>]*class=\"(candle-chart candle-chart-(?:plain|levels))\"[^>]*data-axis-low=\"([0-9.]+)\"");

        Assert.Equal(["candle-chart candle-chart-plain", "candle-chart candle-chart-levels"], drawings.Select(one => one.Groups[1].Value));
        Assert.True(double.Parse(drawings[0].Groups[2].Value, CultureInfo.InvariantCulture) > 90);
        Assert.True(double.Parse(drawings[1].Groups[2].Value, CultureInfo.InvariantCulture) < 60);
        Assert.Contains("chart-pane-T-daily-plain", outside, StringComparison.Ordinal);
        Assert.Contains("chart-pane-T-daily-levels", outside, StringComparison.Ordinal);

        // And the page swaps them with the toggle.
        var sheet = Stylesheet.Css;

        Assert.Contains(".chart-fig .candle-chart-levels{display:none}", sheet, StringComparison.Ordinal);
        Assert.Contains(".chart-fig.bands-on .candle-chart-plain{display:none}", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void TheThreeAveragesDifferAndEachSwatchIsDrawnAsItsLine()
    {
        var candles = Enumerable.Range(0, 5).Select(at => new ChartCandle(First.AddDays(at), First.AddDays(at), 10m, 11m, 9m, 10m, 1, 10, 10, 10)).ToArray();
        var svg = new MarkRenderer().CandleChart("T", "daily", candles, [], [], []);
        var swatches = Regex.Matches(svg, "<line class=\"m-legend-swatch\"[^>]* (stroke=[^/]*)/>").Select(one => one.Groups[1].Value.Trim()).ToArray();
        var lines = Regex.Matches(svg, "<path d=\"[^\"]*\" fill=\"none\" (stroke=[^/]*)/>").Select(one => one.Groups[1].Value.Trim()).ToArray();

        Assert.Equal(3, swatches.Length);
        Assert.Equal(3, swatches.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(swatches, lines);

        // Told apart without colour: the first solid, and the other two by dashes of their own.
        Assert.DoesNotContain("stroke-dasharray", swatches[0], StringComparison.Ordinal);
        Assert.Contains("stroke-dasharray=\"8 4\"", swatches[1], StringComparison.Ordinal);
        Assert.Contains("stroke-dasharray=\"1.5 3.5\"", swatches[2], StringComparison.Ordinal);
    }

    [Fact]
    public void TheGridlinesAreLabelledInTheColumnAndALabelANamedPriceStandsOnIsLeftOut()
    {
        var svg = new MarkRenderer().CandleChart("T", "daily", Candles(43.87m, 70.41m), [], [], []);
        var drawn = Regex.Matches(svg, "<line class=\"m-gridline\" data-price=\"([0-9.]+)\"").Select(one => one.Groups[1].Value).ToArray();
        var labelled = Regex.Matches(svg, "<text class=\"m-tick price-grid-t\" data-price=\"[0-9.]+\"[^>]*>([^<]*)</text>").Select(one => one.Groups[1].Value).ToArray();

        Assert.Equal(["45", "50", "55", "60", "65", "70"], drawn);

        // The newest close, 57.14, has its tag in the column, so the 55 and the 60 either side are worked against it by hand:
        // the pane's 324 pixels over a scale of 28.15 is 11.5 a dollar, 55 stands 24.6 below the tag and 60 32.9 above it.
        Assert.Equal(["45", "50", "55", "60", "65", "70"], labelled);

        var planned = new MarkRenderer().CandleChart("T", "daily", Candles(43.87m, 70.41m), [], [new ChartPlan("Rule", 57.14m, 45m, 70m)], []);
        var kept = Regex.Matches(planned, "<text class=\"m-tick price-grid-t\" data-price=\"[0-9.]+\"[^>]*>([^<]*)</text>").Select(one => one.Groups[1].Value).ToArray();

        Assert.DoesNotContain("45", kept);
        Assert.DoesNotContain("70", kept);
        Assert.Contains("55", kept);
    }

    [Fact]
    public void ALegendTooLongForOneRowTakesASecondAndThePictureGrowsToHoldIt()
    {
        var candles = new[]
        {
            new ChartCandle(First, First, 10m, 11m, 9m, 10m, 1, 5000, 5000, 5000),
            new ChartCandle(First.AddDays(1), First.AddDays(1), 10m, 11m, 9m, 10m, 1, null, null, null),
        };
        var svg = new MarkRenderer().CandleChart("T", "daily", candles, [], [], []);
        var rows = Regex.Matches(svg, "<text class=\"m-legend-t\"[^>]* y=\"([0-9.]+)\">").Select(one => one.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        var height = Regex.Match(svg, "viewBox=\"0 0 [0-9.]+ ([0-9.]+)\"").Groups[1].Value;
        var plain = Regex.Match(new MarkRenderer().CandleChart("T", "daily", Candles(10m, 11m), [], [], []), "viewBox=\"0 0 [0-9.]+ ([0-9.]+)\"").Groups[1].Value;

        Assert.Equal(["16", "36"], rows);
        Assert.Equal(int.Parse(plain, CultureInfo.InvariantCulture) + 20, int.Parse(height, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TheShellsScriptPutsTheLegendBackWhenThePointerLeaves() =>
        Assert.Contains("label.textContent = label.getAttribute('data-rest')", new SinglePageApp().Shell("T"), StringComparison.Ordinal);
}
