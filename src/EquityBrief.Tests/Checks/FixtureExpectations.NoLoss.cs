using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Returns;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.18: the close at which a breakout's rising give-up price first reaches its buy, worked by hand
// over a constructed plan in the fixture's expectation, against the rule's own trailing exit walked over the same closes.
// see: A breakout idea says it sells on a rising give-up price with no fixed target and names the price at which it can no longer lose
public partial class FixtureExpectations
{
    [Fact]
    public void ABreakoutCanNoLongerLoseOnceACloseReachesItsBuyAndItsTrailAsTheRulesTrailingExitWalksIt()
    {
        var expected = Expected("breakout-no-loss");

        static decimal Price(JsonElement value) => decimal.Parse(value.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.StartsWith("22.18's ruling", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);
        Assert.Equal(["gate_result"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        // The plan: the give-up price the rule's own number of typical moves beneath the buy, the distance it trails.
        var buy = Price(expected.GetProperty("buy"));
        var moves = expected.GetProperty("stopMoves").GetDecimal();

        Assert.Equal(BreakoutRule.StopMoves, expected.GetProperty("stopMoves").GetDouble());

        var stop = buy - Price(expected.GetProperty("typicalMove")) * moves;

        Assert.Equal(Price(expected.GetProperty("stop")), stop);
        Assert.Equal(Price(expected.GetProperty("noLossAt")), TrailingExit.NoLossAt(buy, buy - stop));
        Assert.Equal("The trade can no longer lose once the stock reaches about $187.20.", MarkRenderer.NoLossLine(buy, buy - stop));

        // The rule's trailing exit over the same closes: never lowered, a cent under the buy a cent short, at the buy at it.
        ReturnBar[] bars = [.. expected.GetProperty("closes").EnumerateArray().Select((close, at) => new ReturnBar(new DateOnly(2026, 11, 2).AddDays(at), Price(close)))];
        decimal[] stops = [.. expected.GetProperty("stops").EnumerateArray().Select(Price)];

        Assert.Equal(stops, bars.Select((_, at) => TrailingExit.StopAfter(bars[..(at + 1)], buy, stop)));
        Assert.True(TrailingExit.StopAfter(bars[..4], buy, stop) < buy);
        Assert.Equal(buy, TrailingExit.StopAfter(bars, buy, stop));
    }
}
