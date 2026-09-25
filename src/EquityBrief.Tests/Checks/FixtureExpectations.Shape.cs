using EquityBrief.Core.Filter;
using EquityBrief.Core.Shortlist;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.3: the shape clock's classifier and its trigger, worked by hand over
// constructed nights, and over nights carrying the operator's stored nights' measured shares.
public partial class FixtureExpectations
{
    static readonly DateOnly ShapeFirst = new(2026, 1, 5);

    // A night of a thousand members: the counts through the four gates after the market, the list, and
    // the volume ratio; and its firing, each reason's share in tenths of a per cent over a thousand
    // rows, a reason absent where the night did not count it.
    static (NightShape Shape, NightFiring Firing) ShapeNight(
        int day,
        int[] through,
        double? ratio = 1.0,
        string version = ShapeClock.NoVersion,
        int listed = 0,
        params (string Reason, int Tenths)[] reasons) =>
    (
        new NightShape(ShapeFirst.AddDays(day), version, 1000, through, listed, ratio),
        new NightFiring(
            ShapeFirst.AddDays(day),
            reasons.ToDictionary(reason => reason.Reason, reason => new ReasonCount(1000, reason.Tenths), StringComparer.Ordinal),
            0,
            0)
    );

    static IReadOnlyList<EventNight> EventsOf(IEnumerable<(NightShape Shape, NightFiring Firing)> nights)
    {
        var held = nights.ToArray();

        return ShapeClock.Events([.. held.Select(night => night.Shape)], [.. held.Select(night => night.Firing)]);
    }

    [Fact]
    public void AUsuallyQuietGateOrReasonAboveAQuarterMarksTheNightAndOneAboveAQuarterEveryNightMarksNone()
    {
        // Five nights. The setup gate passes 5% of the index on four and 26% on the fifth, a median of
        // 5%: worked by hand, the fifth is an event. The trend gate passes 30% every night, a median of
        // 30%, and marks none. Unusual volume fires for 2% on four and 50% on the second, a median of 2%,
        // so the second is an event too; at entry zone fires for 40% every night and marks none.
        var nights = Enumerable.Range(0, 5).Select(day => ShapeNight(
            day,
            [300, day == 4 ? 260 : 50, 10, 5],
            reasons: [(ShortlistSeries.UnusualVolume, day == 1 ? 500 : 20), (ShortlistSeries.AtEntryZone, 400)]));

        var events = EventsOf(nights);

        Assert.Equal([ShapeFirst.AddDays(1), ShapeFirst.AddDays(4)], events.Select(one => one.Session));
        Assert.Equal(new Flood(ShortlistSeries.UnusualVolume, 0.5, 0.02), Assert.Single(events[0].Floods));
        Assert.Equal(new Flood(SwingGates.Setup, 0.26, 0.05), Assert.Single(events[1].Floods));
        Assert.All(events, one => Assert.Null(one.VolumeRatio));

        // At exactly a quarter a gate has not passed more than one.
        Assert.Empty(EventsOf(Enumerable.Range(0, 5).Select(day => ShapeNight(day, [300, day == 4 ? 250 : 50, 10, 5]))));

        // A gate whose median is a quarter or more marks nothing, however high it goes on one night.
        Assert.Empty(EventsOf(Enumerable.Range(0, 5).Select(day => ShapeNight(day, [day == 0 ? 900 : 250, 50, 10, 5]))));
    }

    [Fact]
    public void AVolumeRatioAtTheThresholdMarksTheNightAndOneAHundredthUnderDoesNot()
    {
        Assert.Equal(1.8, ShapeClock.EventVolumeRatio);

        var events = EventsOf([ShapeNight(0, [100, 30, 10, 5], 1.8), ShapeNight(1, [100, 30, 10, 5], 1.79), ShapeNight(2, [100, 30, 10, 5], null)]);

        var marked = Assert.Single(events);

        Assert.Equal((ShapeFirst, 1.8), (marked.Session, marked.VolumeRatio!.Value));
        Assert.Empty(marked.Floods);
    }

    [Fact]
    public void TheStoredNightsMeasuredSharesMarkTheExpiryAndNoOtherNight()
    {
        // The operator's twelve stored nights, 2026-09-09 to 2026-09-24, each carrying the shares the counts
        // measured: the four gates through the funnel with the market held open, the six reasons where the
        // night counted them under their current rules, in tenths of a per cent, and the volume ratio.
        // Worked by hand: unusual volume's median is 1.6% and it fires for 50.1% on 2026-09-18, which also
        // trades at 2.00; crossed a level's median is 27.2%, so its nights above a quarter mark nothing;
        // no gate passes a quarter of the index on any night.
        string[] reasons = [ShortlistSeries.AtEntryZone, ShortlistSeries.CrossedALevel, ShortlistSeries.BreakoutOnVolume, ShortlistSeries.TrendStateChanged, ShortlistSeries.UnusualVolume, ShortlistSeries.EarningsSoon];
        var measured = new (string Night, int[] Gates, int?[] Reasons, double Ratio)[]
        {
            ("2026-09-09", [167, 34, 0, 0], [423, 347, null, 0, 16, null], 0.87),
            ("2026-09-10", [145, 32, 6, 0], [404, 292, null, 45, 8, null], 0.89),
            ("2026-09-11", [155, 32, 14, 0], [456, 236, null, 56, 16, null], 0.81),
            ("2026-09-14", [147, 22, 8, 2], [472, 317, null, 85, 8, null], 0.92),
            ("2026-09-15", [135, 30, 4, 0], [442, 258, null, 63, 16, null], 0.95),
            ("2026-09-16", [123, 34, 4, 0], [393, 331, 10, 52, 34, null], 0.96),
            ("2026-09-17", [113, 26, 2, 0], [435, 222, 32, 65, 16, null], 0.94),
            ("2026-09-18", [97, 24, 4, 0], [447, 243, 14, 50, 501, null], 2.00),
            ("2026-09-21", [85, 16, 2, 0], [416, 282, 20, 62, 10, null], 0.95),
            ("2026-09-22", [93, 22, 4, 0], [370, 328, 32, 40, 38, null], 0.98),
            ("2026-09-23", [93, 22, 4, 2], [344, 262, 12, 32, 30, null], 0.94),
            ("2026-09-24", [99, 12, 2, 0], [342, 239, 6, 52, 18, 217], 0.90),
        };

        var nights = measured.Select(night =>
        {
            var session = DateOnly.ParseExact(night.Night, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            return (
                new NightShape(session, ShapeClock.NoVersion, 1000, night.Gates, 0, night.Ratio),
                new NightFiring(
                    session,
                    reasons.Zip(night.Reasons).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First, pair => new ReasonCount(1000, pair.Second!.Value), StringComparer.Ordinal),
                    0,
                    0));
        });

        var marked = Assert.Single(EventsOf(nights));

        Assert.Equal(new DateOnly(2026, 9, 18), marked.Session);
        Assert.Equal(new Flood(ShortlistSeries.UnusualVolume, 0.501, 0.016), Assert.Single(marked.Floods));
        Assert.Equal(2.00, marked.VolumeRatio!.Value);
    }

    [Fact]
    public void SixtyOrdinaryNightsUnderAnOpenVersionCrossTheTriggerAndNoNightCountsBeforeAVersionIsOpen()
    {
        Assert.Equal(60, ShapeClock.CalibrationNights);

        NightShape Night(int day, string version) => new(ShapeFirst.AddDays(day), version, 1000, [100, 30, 10, 6], 6, 1.0);

        IReadOnlyList<NightShape> Run(int nights, string version) => [.. Enumerable.Range(0, nights).Select(day => Night(day, version))];

        var night = ShapeFirst.AddDays(59);

        // Sixty under an open version cross; fifty-nine do not.
        Assert.True(ShapeClock.For(Run(60, "v1"), [], "v1", night).Crossed);
        Assert.False(ShapeClock.For(Run(59, "v1"), [], "v1", night).Crossed);

        // Sixty under no version count toward nothing, and the state says no version is open.
        var none = ShapeClock.For(Run(60, ShapeClock.NoVersion), [], ShapeClock.NoVersion, night);

        Assert.False(none.Crossed);
        Assert.False(none.VersionOpen);
        Assert.Equal((60, 60), (none.WindowNights, none.Ordinary));

        // Sixty under the open version with the first ten of them events are fifty ordinary nights and do
        // not cross; seventy with the same ten do.
        IReadOnlyList<NightShape> WithEvents(int nights) =>
            [.. Enumerable.Range(0, nights).Select(day => Night(day, "v1") with { MedianVolumeRatio = day < 10 ? 2.0 : 1.0 })];

        var fifty = ShapeClock.For(WithEvents(60), [], "v1", night);

        Assert.Equal((60, 10, 50, false), (fifty.WindowNights, fifty.Events.Count, fifty.Ordinary, fifty.Crossed));
        Assert.True(ShapeClock.For(WithEvents(70), [], "v1", ShapeFirst.AddDays(69)).Crossed);

        // A night under another version is outside the window.
        Assert.Equal(59, ShapeClock.For([.. Run(59, "v1"), Night(59, "v0")], [], "v1", night).WindowNights);
    }

    [Fact]
    public void AnEventNightIsReadByNoMedianAndEachMedianIsHeldAgainstItsBand()
    {
        // Four ordinary nights and one flooded by volume, which carries counts far above the others. Worked
        // by hand, each median over the four ordinary nights: through trend 80, 90, 100, 110 gives 95;
        // through setup 30 each; through trigger 10, 12, 14, 16 gives 13; through trade 5 each; the list 4,
        // 6, 8, 10 gives 7. Unusual volume's median over the ordinary nights is 1%.
        var nights = new[]
        {
            ShapeNight(0, [80, 30, 10, 5], listed: 4, reasons: [(ShortlistSeries.UnusualVolume, 10)]),
            ShapeNight(1, [90, 30, 12, 5], listed: 6, reasons: [(ShortlistSeries.UnusualVolume, 10)]),
            ShapeNight(2, [100, 30, 14, 5], listed: 8, reasons: [(ShortlistSeries.UnusualVolume, 10)]),
            ShapeNight(3, [110, 30, 16, 5], listed: 10, reasons: [(ShortlistSeries.UnusualVolume, 10)]),
            ShapeNight(4, [900, 500, 400, 300], ratio: 2.5, listed: 250, reasons: [(ShortlistSeries.UnusualVolume, 600)]),
        };

        var state = ShapeClock.For([.. nights.Select(night => night.Shape)], [.. nights.Select(night => night.Firing)], ShapeClock.NoVersion, ShapeFirst.AddDays(4));

        Assert.Equal(4, state.Ordinary);
        Assert.Equal([95.0, 30.0, 13.0, 5.0], state.Gates.Select(gate => gate.Median!.Value));
        Assert.Equal(7.0, state.List.Median!.Value);
        Assert.Equal(
            [(SwingGates.Trend, 50, 100), (SwingGates.Setup, 20, 60), (SwingGates.Trigger, 8, 40), (SwingGates.Trade, 5, 35)],
            state.Gates.Select(gate => (gate.Measure, gate.Low, gate.High)));
        Assert.Equal((5, 30), (state.List.Low, state.List.High));

        var unusual = state.Reasons.Single(reason => reason.Reason == ShortlistSeries.UnusualVolume);

        Assert.Equal((0.6, 0.01), (unusual.Tonight!.Value, unusual.Median!.Value));
        Assert.NotNull(state.Tonight);
    }
}
