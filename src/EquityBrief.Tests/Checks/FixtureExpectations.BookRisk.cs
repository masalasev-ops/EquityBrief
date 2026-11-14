using EquityBrief.Core.Families;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// On the operator's answer of 2026-11-14 to item 15 a book's picks per dollar risked against the index's other members bought
// the same way, worked by hand over a constructed tape: a member past its take-profit or under its stop by the pick's sale sold
// there, one past neither sold on the pick's session less its gap at the open, one past either only after the pick's sale sold
// on the pick's session, and a member the index does not hold on the buy left out; then the line's years and the box's
// sentence.
// see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
public partial class FixtureExpectations
{
    // Six sessions in March 2016. P is the pick, A reaches its take-profit at the third session, B closes under its stop at the
    // third, C joins the index after the buy, and D is past neither.
    static HeavyweightTape BookRiskTape()
    {
        double[][] close =
        [
            [10, 10.5, 11, 11.5, 12, 12],
            [20, 21, 23, 22, 22, 22],
            [50, 49, 47, 46, 45, 44],
            [5, 5, 5, 5, 5, 5],
            [30, 30.3, 30.6, 30.9, 31.2, 31.5],
        ];
        bool[][] member = [.. close.Select((row, name) => row.Select((_, session) => name != 3 || session > 0).ToArray())];

        return new HeavyweightTape
        {
            Tickers = ["P", "A", "B", "C", "D"],
            Calendar = [new(2016, 3, 1), new(2016, 3, 2), new(2016, 3, 3), new(2016, 3, 4), new(2016, 3, 7), new(2016, 3, 8)],
            Close = close,
            Average200 = [.. close.Select(row => row.Select(_ => 0.0).ToArray())],
            Member = member,
            LastMemberClose = [.. close.Select((row, name) => HeavyweightTape.LastMemberCloseOf(member[name], row))],
            Index = [.. Enumerable.Repeat(double.NaN, 6)],
            First = 0,
        };
    }

    // Each name's risk over its close, stop, take-profit and fill over its close at the buy; D's open gaps up 2 per cent.
    static (double Risk, double Stop, double? TakeProfit, double Fill)? BookRiskPlaced(int name, int entry) => name switch
    {
        0 => (0.1, 9, null, 1.0),
        1 => (0.05, 19, 22.9, 1.0),
        2 => (0.05, 47.5, 60, 1.0),
        3 => (0.1, 4, 6, 1.0),
        _ => (2.0 / 30, 28, 40, 1.02),
    };

    [Fact]
    public void ABooksPickIsReadPerDollarRiskedAgainstTheIndexsOtherMembersBoughtTheSameWay()
    {
        HeavyweightTrade Pick(int name, int? end, double result) => new(name, QualityRule.Name, 0, end, end is null ? null : "sold", result, null, null, null, 0);

        var edges = BookRisk.Edges(BookRiskTape(), [Pick(0, 4, 0.18), Pick(4, 1, 0.03), Pick(3, 4, 0.1), Pick(1, null, 0.2)], BookRiskPlaced);

        // P: 0.18 over 0.1 is 1.8, against A 0.15 / 0.05 = 3.0, B -0.06 / 0.05 = -1.2 and D (0.04 - 0.02) / (2 / 30) = 0.3, a mean
        // of 0.7. D sold at the second session: 0.03 over 2 / 30 is 0.45, against P 0.05 / 0.1 = 0.5, A 0.05 / 0.05 = 1.0, its
        // take-profit coming after, and B -0.02 / 0.05 = -0.4, a mean of 1.1 / 3. C no member on the buy, so no edge; A still held.
        Assert.Equal(3, edges.Count);
        Assert.All(edges, one => Assert.Equal(0, one.Entry));
        Assert.Equal(1.1, edges[0].Edge!.Value, 9);
        Assert.Equal(0.45 - (1.1 / 3), edges[1].Edge!.Value, 9);
        Assert.Null(edges[2].Edge);

        Assert.Equal(
            "From 2016 to 2016, picks like this made $0.59 more for every $1 risked than buying the index's other stocks the same way, over 2 picks.",
            BookRisk.Box(edges, BookRiskTape().Calendar, 2016));
    }

    // On the S&P 400 Quality is judged from 2016, so X's 2014 pick does not choose it; Y is chosen on its 2016 pick and its box
    // reads both of its picks from 2016.
    [Fact]
    public void ABooksStopAndTakeProfitAreChosenPerDollarRiskedFromItsFirstTestYearTo2018()
    {
        DateOnly[] calendar = [new(2014, 6, 2), new(2016, 6, 1), new(2019, 6, 3)];
        (string, IReadOnlyList<(int Entry, double? Edge)>)[] choices =
        [
            ("X", [(0, 5.0), (1, 0.2), (2, 0.1)]),
            ("Y", [(0, -5.0), (1, 0.3), (2, -0.2)]),
        ];

        Assert.Equal(
            $"book risk: {QualityRule.Name}, the live rule, per $1 risked against the index's other stocks bought the same way: "
            + "X +0.200 over 1 from 2016 to 2018, +0.100 over 1 from 2019, +0.150 over 2 from 2016; "
            + "Y +0.300 over 1 from 2016 to 2018, -0.200 over 1 from 2019, +0.050 over 2 from 2016; chosen from 2016 to 2018: Y; "
            + "From 2016 to 2019, picks like this made $0.05 more for every $1 risked than buying the index's other stocks the same way, over 2 picks.",
            BookRisk.Line(QualityRule.Name, "the live rule", "MID", calendar, choices));
        Assert.EndsWith("no choice holds a pick before 2019, its family's first test year on the index being 2023", BookRisk.Line(HeavyweightRule.Name, "the live rule", "GSPC", calendar, choices), StringComparison.Ordinal);
    }
}
