using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// see: The wait plan is drawn from the ladder's own bands and stop, is provisional and sets nothing
public sealed partial class ReadSurface
{
    static Level Band(decimal low, decimal high, string role = LevelSeries.Support, bool anchored = true) =>
        new(low, high, role, false, 1, anchored, []);

    // Close 112, a typical day of 2. Support at 96 to 98 and 102 to 104, resistance at 120 to 122 and 140 to 142.
    static readonly IReadOnlyList<Level> WaitBands =
    [
        Band(96m, 98m),
        Band(102m, 104m),
        Band(120m, 122m, LevelSeries.Resistance),
        Band(140m, 142m, LevelSeries.Resistance),
    ];

    [Fact]
    public void AGapDaysZoneIsTheGapWidenedDownToABandWithinOneTypicalDay()
    {
        // A gap from 105 to 110. The top of the band under it, 104, stands 1 under the gap, within the day of 2, so the zone is 104 to 110,
        // its middle 107. The stop is the next band's low, 96, a risk of 11; twice that needs 129, so the exit is 140, 33 over 11, 3 to 1.
        // The zone's top stands 112 - 110 = 2 under the close, one typical day.
        var plan = WaitPlans.Of(112m, 105m, 110m, 2, WaitBands, TrendState.Range, []);

        Assert.Equal((104m, 110m, 96m, 140m), (plan.ZoneLow, plan.ZoneHigh, plan.Stop, plan.FirstExit));
        Assert.Equal(3, plan.RewardToRisk!.Value, 6);
        Assert.Equal(1, plan.ZoneBelowDays!.Value, 6);
        Assert.Null(plan.Why);

        // A band whose top of 106 stands inside the gap, which runs from 105 to 110, is not under it, so the band at 102 to 104 is read and the zone
        // is again 104 to 110.
        Assert.Equal((104m, 110m), ZoneOf(WaitPlans.Of(112m, 105m, 110m, 2, [Band(102m, 104m), Band(105.5m, 106m), Band(96m, 98m)], TrendState.Range, [])));

        // Exactly one typical day under the gap is within it.
        Assert.Equal((104m, 110m), ZoneOf(WaitPlans.Of(112m, 106m, 110m, 2, WaitBands, TrendState.Range, [])));
    }

    static (decimal?, decimal?) ZoneOf(WaitPlan plan) => (plan.ZoneLow, plan.ZoneHigh);

    [Fact]
    public void WithoutAGapOrABandNearItTheZoneIsTheNearestSupportBandUnderTheClose()
    {
        // No gap: the band at 102 to 104, its middle 103. The stop 96, a risk of 7; twice that needs 117, so the exit is 120, 17 over 7.
        // The zone's top stands 8 under the close, four typical days.
        var plain = WaitPlans.Of(112m, null, null, 2, WaitBands, TrendState.Range, []);

        Assert.Equal((102m, 104m, 96m, 120m), (plain.ZoneLow, plain.ZoneHigh, plain.Stop, plain.FirstExit));
        Assert.Equal(17d / 7, plain.RewardToRisk!.Value, 6);
        Assert.Equal(4, plain.ZoneBelowDays!.Value, 6);

        // A gap from 108 to 110 whose band stands 4 under it, past the day of 2: the same zone as no gap at all.
        Assert.Equal((102m, 104m), ZoneOf(WaitPlans.Of(112m, 108m, 110m, 2, WaitBands, TrendState.Range, [])));

        // No typical day reads none of the gap's widening and no distance, and a typical day of nothing reads the same.
        var unread = WaitPlans.Of(112m, 105m, 110m, null, WaitBands, TrendState.Range, []);

        Assert.Equal((102m, 104m), ZoneOf(unread));
        Assert.Null(unread.ZoneBelowDays);
        Assert.Null(WaitPlans.Of(112m, null, null, 0, WaitBands, TrendState.Range, []).ZoneBelowDays);

        // A close inside a support band: that band is not under it, so the next one down is the zone.
        Assert.Equal((96m, 98m), ZoneOf(WaitPlans.Of(103m, null, null, 2, WaitBands, TrendState.Range, [])));

        // The nearest is read by its value, whatever order the bands come in.
        Assert.Equal((102m, 104m), ZoneOf(WaitPlans.Of(112m, null, null, 2, [.. WaitBands.Reverse()], TrendState.Range, [])));
    }

    [Fact]
    public void AWaitPlansStopIsTheLaddersOwnAndABandOnlyAnAverageAnchorsCarriesNoZone()
    {
        // In an uptrend the stop trails: of the lows under the band at 102, the last in session order is 99, not the higher 100.5 before
        // it, and 103 is not under the band; 99 stands above the band beneath's 96 and inside no band.
        Assert.Equal(99m, WaitPlans.Of(112m, null, null, 2, WaitBands, TrendState.Uptrend, [95m, 100.5m, 99m, 103m]).Stop);

        // A low inside the band beneath moves to that band's low edge, as the ladder moves it; a range reads no low at all.
        Assert.Equal(96m, WaitPlans.Of(112m, null, null, 2, WaitBands, TrendState.Uptrend, [97m]).Stop);
        Assert.Equal(96m, WaitPlans.Of(112m, null, null, 2, WaitBands, TrendState.Range, [100.5m]).Stop);

        // A band only an average anchors is skipped for the zone and still stands as the level beneath.
        IReadOnlyList<Level> averaged = [Band(96m, 98m), Band(99m, 100m, anchored: false), Band(102m, 104m), Band(120m, 122m, LevelSeries.Resistance)];

        Assert.Equal(99m, WaitPlans.Of(112m, null, null, 2, averaged, TrendState.Range, []).Stop);
        Assert.Equal((96m, 98m), ZoneOf(WaitPlans.Of(101m, null, null, 2, averaged, TrendState.Range, [])));

        // Nothing beneath the band: a zone with no stop, so no exit and no reward to risk.
        var none = WaitPlans.Of(99m, null, null, 2, [Band(96m, 98m), Band(120m, 122m, LevelSeries.Resistance)], TrendState.Range, []);

        Assert.Equal((96m, 98m, null, null, null), (none.ZoneLow, none.ZoneHigh, none.Stop, none.FirstExit, none.RewardToRisk));
        Assert.Null(none.Why);

        // A stop no resistance stands at twice the risk from: no exit and no reward to risk, the stop still said.
        var open = WaitPlans.Of(112m, null, null, 2, [Band(96m, 98m), Band(102m, 104m), Band(110m, 111m, LevelSeries.Resistance)], TrendState.Range, []);

        Assert.Equal((96m, null, null), (open.Stop, open.FirstExit, open.RewardToRisk));

        // Exactly twice the risk is far enough: the zone's middle 103, a risk of 7, a band at 117 standing 14 above it, 2 to 1.
        var twice = WaitPlans.Of(112m, null, null, 2, [Band(96m, 98m), Band(102m, 104m), Band(117m, 118m, LevelSeries.Resistance)], TrendState.Range, []);

        Assert.Equal(117m, twice.FirstExit);
        Assert.Equal(2, twice.RewardToRisk!.Value, 6);
    }

    [Theory]
    [InlineData(TrendState.Downtrend, WaitPlans.TrendDown)]
    [InlineData(TrendState.NotClassified, WaitPlans.NotClassified)]
    public void ATrendTheLadderPlacesNothingOnDrawsNoWaitPlan(string state, string why)
    {
        var plan = WaitPlans.Of(112m, 105m, 110m, 2, WaitBands, state, []);

        Assert.Equal(why, plan.Why);
        Assert.Equal((null, null, null, null), (plan.ZoneLow, plan.ZoneHigh, plan.Stop, plan.FirstExit));
    }

    [Fact]
    public void NoAnchoredSupportBandUnderTheCloseDrawsNoWaitPlan()
    {
        Assert.Equal(WaitPlans.NoBand, WaitPlans.Of(112m, null, null, 2, [], TrendState.Range, []).Why);
        Assert.Equal(WaitPlans.NoBand, WaitPlans.Of(112m, null, null, 2, [Band(102m, 104m, anchored: false), Band(120m, 122m, LevelSeries.Resistance)], TrendState.Range, []).Why);
    }

    [Fact]
    public void AWaitPlanReadsAsOneProvisionalSentence()
    {
        var reading = EntryWorked() with { WaitLow = 104m, WaitHigh = 110m, WaitStop = 96m, WaitExit = 140m, WaitReward = 3, ZoneDays = 1 };

        Assert.Equal("Provisional, and it sets nothing. Wait to buy between $104.00 and $110.00, 1.0 typical days below tonight's close. Stop $96.00. First exit $140.00, about 3.0 to 1. It is worth taking if it reaches its first exit before its stop more than 25.0% of the time.", EntryWords.Wait(reading));
        Assert.Equal("Provisional, and it sets nothing. Wait to buy between $104.00 and $110.00, 1.0 typical days below tonight's close. Stop $96.00. No resistance band in the year stands at twice the risk, so it draws no first exit.", EntryWords.Wait(reading with { WaitExit = null, WaitReward = null }));
        Assert.Equal("Provisional, and it sets nothing. Wait to buy between $104.00 and $110.00. The ladder places no stop under this zone, so it draws no exit.", EntryWords.Wait(reading with { WaitStop = null, WaitExit = null, WaitReward = null, ZoneDays = null }));
        Assert.Equal("Provisional, and it sets nothing. No wait plan: the trend is down, so the ladder places nothing.", EntryWords.Wait(EntryWorked() with { WaitWhy = WaitPlans.TrendDown }));
        Assert.Null(EntryWords.Wait(EntryWorked()));
    }

    [Fact]
    public void TheRegionDrawsTheWaitPlanAndTheCardsLineDoesNot()
    {
        var reading = EntryWorked() with { WaitLow = 104m, WaitHigh = 110m, WaitStop = 96m, WaitExit = 140m, WaitReward = 3, ZoneDays = 1 };
        var drawn = new MarkRenderer().EntryCheck(reading, 110m, 100m, null);

        Assert.Contains("data-entry=\"wait\">Provisional, and it sets nothing. Wait to buy between $104.00 and $110.00", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("Wait to buy", MarkRenderer.EntryCheckLine(reading, "HAND"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-entry=\"wait\"", new MarkRenderer().EntryCheck(EntryWorked(), 110m, 100m, null), StringComparison.Ordinal);
    }
}
