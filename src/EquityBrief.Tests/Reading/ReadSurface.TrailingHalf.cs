using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// read-surface, the operator's review of PR 699, item 2: a take-profit on half whose plan trails draws its other half as "Sell
// the other half" "on a close under" its give-up price once the first half sells, with the operator's line beneath it and no
// separate trail line, and a plan that trails with no take-profit draws the rising give-up price and never "No fixed target".
// Worked by hand: bought at 50.00 with the stop at 46.00 and the trail 4.00 behind its highest close, half at 3 times the risk
// is 62.00, and the give-up price once it sells is 62.00 less 4.00, 58.00; half at 52.00 less 4.00 is 48.00, under the buy,
// so the give-up price is the buy, 50.00.
// see: A plan selling on a rising give-up price draws that price and the price at which it can no longer lose, and no card says it has no fixed target
public partial class ReadSurface
{
    static PlanSaleView TrailingSale(decimal? takeProfit) => new(PlanPoints.RuleSale, 50m, 46m, null, [], 4m, null, 63)
    {
        TakeProfit = takeProfit is { } price ? new TakeProfitView(price, TakeProfitSource.Risk, Multiple: 3, Buy: 50m, Stop: 46m) { Half = true } : null,
    };

    [Fact]
    public void ATrailingHalfSaysItSellsOnACloseUnderItsGiveUpPriceNeverUnderTheBuy()
    {
        var lines = MarkRenderer.PlanSaleLines(TrailingSale(62m));
        var other = Assert.Single(lines, line => line.Key == "other-half");

        Assert.Equal(new PlanSaleLine("other-half", "Sell the other half", "on a close under $58.00", "A give-up price that rises behind its highest close. Its stop sits at the buy until then."), other);
        Assert.DoesNotContain(lines, line => line.Key == "trail");
        Assert.Equal(["target", "other-half", "limit"], lines.Select(line => line.Key));

        Assert.Equal("on a close under $50.00", Assert.Single(MarkRenderer.PlanSaleLines(TrailingSale(52m)), line => line.Key == "other-half").Value);

        Assert.Equal(TakeProfitView.TrailingHalfLine, new TakeProfitView(62m, TakeProfitSource.Risk, Buy: 50m) { Half = true, OtherHalfTrails = true }.OtherHalfLine);
        Assert.Equal("Its stop moves up to the buy, $50.00, once the first half sells.", new TakeProfitView(62m, TakeProfitSource.Risk, Buy: 50m) { Half = true }.OtherHalfLine);

        var whole = MarkRenderer.PlanSaleLines(TrailingSale(null));

        Assert.Equal(new PlanSaleLine("trail", "Sell on a close under", "a give-up price that rises", "$4.00 behind its highest close."), whole[0]);
        Assert.DoesNotContain(whole, line => line.Value.Contains("No fixed target", StringComparison.Ordinal) || line.Label.Contains("Aim to sell", StringComparison.Ordinal));
    }
}
