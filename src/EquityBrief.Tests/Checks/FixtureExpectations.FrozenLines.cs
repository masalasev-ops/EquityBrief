using EquityBrief.Core.Cards;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11: a family's freezes read off its live rule at each instant its register rows were written,
// the same rule registered again no freeze; the line kept at its freeze naming the last two years the setting frozen
// there lost; the freeze that made the live rule and the answer it followed, whose figures a frozen card states; and the
// drift's revenue part named on the funnel as the revenue.
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
// see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
public partial class FixtureExpectations
{
    static DateTimeOffset Hour(int hour, int minute = 0) => new(2026, 10, 21, hour, minute, 0, TimeSpan.Zero);

    static RecordedAnswer Swept(string run, string? design, string answer, DateTimeOffset at, AnswerFigures? figures = null) =>
        new(run, "MID", "breakout", design, answer, at, figures);

    // A run's figures over the eight years 2019 to 2026, the last two given, the six before them as stated.
    static AnswerFigures FiguresWith(double? twentyFive, double? twentySix, params double?[] before) =>
        new(
            "high=251|volume=1.5|ceiling=1|stop=3|market=5",
            1033,
            0.023,
            Enumerable.Range(2019, 6).Select((year, at) => (year, edge: before.Length > at ? before[at] : 0.01))
                .Append((2025, twentyFive))
                .Append((2026, twentySix))
                .ToDictionary(one => one.Item1, one => one.Item2),
            -0.010,
            378,
            0,
            54.6,
            "20261020T062626Z",
            0.1798,
            false);

    [Fact]
    public void AFamilysFreezesAreTheInstantsItsLiveRuleNamedARuleOtherThanTheOneStandingJustBefore()
    {
        // Seven instants of the family's rows, handed out of order. By hand: none stands at 09:00; X stands from 10:00, the
        // first freeze; X registered again at 10:30, as a moved evaluator's is, is no freeze; at 11:00 X is retired and
        // none stands; X registered again at 12:00 after that stands again, a freeze; Y in X's place at 13:00 a freeze;
        // Y again at 13:30 none. Three freezes, X at 10:00, X at 12:00 and Y at 13:00.
        var freezes = SweepLine.Freezes(
        [
            (Hour(13, 30), "Y"),
            (Hour(9), null),
            (Hour(10), "X"),
            (Hour(12), "X"),
            (Hour(10, 30), "X"),
            (Hour(11), null),
            (Hour(13), "Y"),
        ]);

        Assert.Equal([new SweepFreeze("X", Hour(10)), new SweepFreeze("X", Hour(12)), new SweepFreeze("Y", Hour(13))], freezes);
        Assert.Empty(SweepLine.Freezes([(Hour(9), null), (Hour(10), null)]));
    }

    [Fact]
    public void ALineIsKeptAtItsFreezeAndAddsTheLastYearsTheSettingFrozenThereLost()
    {
        var none = Swept("r1", null, SweepAnswer.NonePassedWord, Hour(8), FiguresWith(-0.071, -0.096));

        // Its freeze at 09:00 made X, which reads the card: the line is kept there. A second freeze, Y at 10:00, ends it, so
        // nothing is kept; a card no live rule reads draws it kept at nothing; and a freeze before the answer keeps
        // nothing, the answer read as recorded after it.
        Assert.Equal(new SweepFreeze("X", Hour(9)), SweepLine.KeptAt([none], [new SweepFreeze("X", Hour(9))], "X"));
        Assert.Null(SweepLine.KeptAt([none], [new SweepFreeze("X", Hour(9)), new SweepFreeze("Y", Hour(10))], "Y"));
        Assert.Null(SweepLine.KeptAt([none], [new SweepFreeze("X", Hour(9))], null));
        Assert.Null(SweepLine.KeptAt([none], [new SweepFreeze("X", Hour(7))], "X"));
        Assert.True(SweepLine.Drawn([none], [new SweepFreeze("X", Hour(7))], "X"));

        // The years it lost of the last two it stored, 2025 and 2026: both below nothing, then 2025 above and 2026 below,
        // then both above, then 2026 holding no edge with 2025 below.
        Assert.Equal([2025, 2026], SweepLine.LostIn(FiguresWith(-0.071, -0.096)));
        Assert.Equal([2026], SweepLine.LostIn(FiguresWith(0.424, -0.096)));
        Assert.Empty(SweepLine.LostIn(FiguresWith(0.424, 0.093)));
        Assert.Equal([2025], SweepLine.LostIn(FiguresWith(-0.071, null)));
        Assert.Equal([(2025, (double?)-0.071), (2026, (double?)null)], SweepLine.Last(FiguresWith(-0.071, null)));

        // The words each adds to the line, and none for none.
        Assert.Equal(", and the setting frozen here lost in 2025 and 2026", SweepLine.LostLine([2025, 2026]));
        Assert.Equal(", and the setting frozen here lost in 2026", SweepLine.LostLine([2026]));
        Assert.Equal(string.Empty, SweepLine.LostLine([]));

        // Years above nothing, of the eight stored: 0.05, 0.1, -0.02, 0.03, -0.04 and 0.08 before them, then -0.071 and
        // -0.096, four above; a year holding no edge is above nothing in none.
        Assert.Equal(4, SweepLine.YearsAbove(FiguresWith(-0.071, -0.096, 0.05, 0.1, -0.02, 0.03, -0.04, 0.08)));
        Assert.Equal(5, SweepLine.YearsAbove(FiguresWith(0.424, null, 0.05, 0.1, -0.02, 0.03, -0.04, 0.08)));
    }

    [Fact]
    public void TheLiveRulesFreezeReadsTheAnswerItFollowedOfItsOwnDesign()
    {
        // X frozen at 09:00 and Y at 11:00: Y, reading the card, its freeze the last; X reading it would be no live freeze.
        IReadOnlyList<SweepFreeze> freezes = [new SweepFreeze("X", Hour(9)), new SweepFreeze("Y", Hour(11))];

        Assert.Equal(new SweepFreeze("Y", Hour(11)), SweepLine.LiveFreeze(freezes, "Y"));
        Assert.Null(SweepLine.LiveFreeze(freezes, "X"));
        Assert.Null(SweepLine.LiveFreeze(freezes, null));

        // The answer a freeze followed is the newest at or before its instant: r1 at 08:00 for X's; r2 recorded at 11:00,
        // Y's own instant, for Y's; and r3 after it for neither.
        var r1 = Swept("r1", null, SweepAnswer.NonePassedWord, Hour(8));
        var r2 = Swept("r2", null, SweepAnswer.PassedWord, Hour(11));
        var r3 = Swept("r3", null, SweepAnswer.NonePassedWord, Hour(12));

        Assert.Equal(r1, SweepLine.Followed([r3, r2, r1], freezes[0]));
        Assert.Equal(r2, SweepLine.Followed([r3, r2, r1], freezes[1]));

        // The sector heavyweights' two designs: design (a)'s answer at 08:00 and design (b)'s at 10:00, each before Y's
        // freeze; design (a)'s rule reads its own design's answer and not design (b)'s newer one.
        var a = Swept("a1", "a", SweepAnswer.NonePassedWord, Hour(8));
        var b = Swept("b1", "b", SweepAnswer.NonePassedWord, Hour(10));

        Assert.Equal(a, SweepLine.Followed([a, b], freezes[1], "a"));
        Assert.Equal(b, SweepLine.Followed([a, b], freezes[1], "b"));
        Assert.Equal(b, SweepLine.Followed([a, b], freezes[1]));
    }

    [Fact]
    public void TheDriftsRevenuePartIsNamedOnTheFunnelAsTheRevenue()
    {
        // The reason the index families' step stores for a member the revenue level keeps off is the part named on the
        // funnel as the revenue, as the fundamentals-first family's revenue is.
        Assert.Equal(IndexRules.RevenueNotAbove, RuleRows.RevenueNotAbove);
        Assert.Equal(RuleRows.Revenue, RuleRows.PartOf(IndexRules.RevenueNotAbove));
        Assert.Equal([(RuleRows.Market, 9), (RuleRows.Setup, 4), (RuleRows.Revenue, 1)], RuleRows.ReadGates(RuleRows.GatesJson([("market", 9), ("setup", 4), (IndexRules.RevenueNotAbove, 1)])));
    }
}
