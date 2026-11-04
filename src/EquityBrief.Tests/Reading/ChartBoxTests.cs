using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 22.35: the box beside the candle under the pointer, a tap or the arrow keys.
// see: The chart page draws a box beside the candle under the pointer or a tap or the arrow keys, and rests on the newest candle's line
public class ChartBoxTests
{
    static readonly DateOnly Monday = new(2026, 10, 26);

    static readonly ChartCandle[] Candles =
    [
        new(Monday, Monday, 100m, 104.5m, 99.25m, 100m, 1_234_567, 101.25, null, 99.5),
        new(Monday.AddDays(1), Monday.AddDays(1), 100m, 111m, 100m, 110m, 7_654_321, 102, null, null),
        new(Monday.AddDays(2), Monday.AddDays(4), 110m, 112m, 90m, 99m, 12, null, null, null),
    ];

    [Fact]
    public void TheBoxStatesTheSessionThePricesTheSignedChangeTheVolumeAndTheAverages()
    {
        // Worked by hand: 110 on 100 is +10.00, +10.00%, in the gain's colour; volume in full; the 50's not stored.
        var box = MarkRenderer.ChartBox(Candles, 1);

        Assert.Contains("<div class=\"chart-box-when\">2026-10-27</div>", box, StringComparison.Ordinal);
        Assert.Contains("<dt>Open</dt><dd>100.00</dd><dt>High</dt><dd>111.00</dd><dt>Low</dt><dd>100.00</dd><dt>Close</dt><dd>110.00</dd>", box, StringComparison.Ordinal);
        Assert.Contains("<dt>Change</dt><dd><span class=\"gain\">+10.00 (+10.00%)</span></dd>", box, StringComparison.Ordinal);
        Assert.Contains("<dt>Volume</dt><dd>7,654,321</dd>", box, StringComparison.Ordinal);
        Assert.Contains("<dt>20-session average</dt><dd>102.00</dd><dt>50-session average</dt><dd>not stored</dd><dt>200-session average</dt><dd>not stored</dd>", box, StringComparison.Ordinal);
    }

    [Fact]
    public void AWeeksCandleNamesItsFirstAndLastAndAFallIsInTheLossColourWithItsSign()
    {
        // 99 on 110 is -11.00, -10.00%.
        var box = MarkRenderer.ChartBox(Candles, 2);

        Assert.Contains("<div class=\"chart-box-when\">2026-10-28 to 2026-10-30</div>", box, StringComparison.Ordinal);
        Assert.Contains("<dt>Change</dt><dd><span class=\"loss\">-11.00 (-10.00%)</span></dd>", box, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstCandleSaysNoCandleStandsBeforeIt() =>
        Assert.Contains("<dt>Change</dt><dd>no candle before</dd>", MarkRenderer.ChartBox(Candles, 0), StringComparison.Ordinal);

    [Fact]
    public void EachCandleCarriesItsBoxWhereToStandAndItsCloseAndThePictureTakesTheFocus()
    {
        var svg = new MarkRenderer().CandleChart("T", "weekly", Candles, [], [], []);
        var carried = Regex.Matches(svg, "<g class=\"candle\"[^>]* data-box=\"([^\"]*)\" data-x=\"([0-9.]+)\" data-close-y=\"([0-9.]+)\" data-close-shown=\"([^\"]*)\"");

        Assert.Equal(3, carried.Count);
        Assert.Equal(MarkRenderer.ChartBox(Candles, 2), WebUtility.HtmlDecode(carried[2].Groups[1].Value));
        Assert.Equal("99.00", carried[2].Groups[4].Value);
        Assert.Matches("<svg [^>]*tabindex=\"0\" aria-label=\"T's weekly chart: the arrow keys step from candle to candle\"", svg);

        // The line runs from the pane's top through the volume pane: the hit rectangle and the line are the same height.
        var line = Regex.Match(svg, "<line class=\"chart-cross-line\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"([0-9]+)\"/>").Groups[1].Value;
        var hit = Regex.Match(svg, "<rect class=\"candle-hit\"[^>]* height=\"([0-9]+)\"").Groups[1].Value;

        Assert.Equal("448", line);
        Assert.Equal(line, hit);
        Assert.Contains("<rect class=\"chart-cross-tag\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScriptAnswersTheMouseTheTapAndTheKeysAndRestsOnTheNewestCandle()
    {
        var shell = new SinglePageApp().Shell("T");

        Assert.Contains("box.innerHTML = candle.getAttribute('data-box');", shell, StringComparison.Ordinal);
        Assert.Contains("if (event.pointerType === 'touch') { return; }", shell, StringComparison.Ordinal);
        Assert.Contains("if (event.pointerType !== 'touch') { return; }", shell, StringComparison.Ordinal);
        Assert.Contains("['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)", shell, StringComparison.Ordinal);
        Assert.Contains("if (candles.length > 0) { readCandle(candles[candles.length - 1]); }", shell, StringComparison.Ordinal);
        Assert.Contains("box.style.left = Math.max(4, Math.min(left, room)) + 'px';", shell, StringComparison.Ordinal);

        // A chart the keys reached keeps its box when the mouse leaves; any other chart's goes down.
        Assert.Contains("if (!chart.hasAttribute('data-keyed')) { pointAway(chart); }", shell, StringComparison.Ordinal);
        Assert.Contains("event.target.matches('.candle-chart[data-keyed]')", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCrossIsHiddenAtRestAndNothingOverTheChartTakesThePointerFromACandle()
    {
        Assert.Contains(".chart-cross{display:none} .chart-cross.on{display:inline}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".candle-chart .volume-pane,.candle-chart .chart-events,.candle-chart .chart-cross{pointer-events:none}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".chart-box{position:absolute;", Stylesheet.Css, StringComparison.Ordinal);
    }
}
