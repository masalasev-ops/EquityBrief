using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.2: the swing filter's five gates, its trigger, its exclusions and its
// order, each worked by hand over constructed members on both sides of its threshold and at it.
public partial class FixtureExpectations
{
    static readonly DateOnly GateNight = new(2026, 9, 8);
    static readonly DateOnly GateSessionBefore = new(2026, 9, 4);

    // A member passing every gate, worked by hand: breadth 0.75 against 0.5; an uptrend with a mean
    // place of 0.8 against two thirds; a close of 102 three typical moves of 4 below a high of 114, on a
    // dry-up of 0.8 against 1, inside the anchored support band 100 to 104; a close above the previous
    // session's high of 101 with no event on the session before; the ladder's first tranche entered at
    // 102 with its stop at 96 and its target at 120, a reward to risk of 3 against 2 and a stop 1.5
    // typical moves below the entry, inside 1 to 2.5; no print on file and nothing else excluding it.
    static GateInputs Passing() => new(
        "ZZA",
        new Breadth(4, 4, 3, 0.75),
        "uptrend",
        0.8,
        new SwingReading(GateNight, 252, 5, 10, 114m, GateNight.AddDays(-7), 5, 3.0, 0.8, 0.9),
        null,
        102m,
        100m,
        101m,
        1000,
        1000,
        4,
        [
            new FilterBand(100m, 104m, "support", 10, true),
            new FilterBand(120m, 121m, "resistance", 5, true),
        ],
        new FirstTranche(102m, 96m, 120m, 3m, null),
        null,
        null,
        false,
        null,
        GateSessionBefore,
        false);

    static GateResult Gates(GateInputs inputs, FilterSettings? settings = null) => SwingGates.Evaluate(inputs, settings ?? FilterSettings.Proposed);

    static bool Passed(GateResult result, string gate) => result.Gates.Single(one => one.Name == gate).Passed;

    static string Reason(GateResult result, string gate) => result.Gates.Single(one => one.Name == gate).Reason;

    [Fact]
    public void AMemberPassingEveryGateIsWorkedByHandAndPassesTheFilter()
    {
        var result = Gates(Passing());

        Assert.Equal(SwingGates.Order, result.Gates.Select(gate => gate.Name));
        Assert.All(result.Gates, gate => Assert.True(gate.Passed, gate.Name + ": " + gate.Reason));
        Assert.Equal(SwingGates.Pullback, result.Family);
        Assert.True(result.TriggerEvent);
        Assert.Empty(result.Exclusions);
        Assert.True(result.Passed);

        // The ladder's reading as the listing kept it, and the swing trade's own worked by hand: in at 102,
        // the stop at the band's low edge of 100, the target at the lowest band above the close, 120, so
        // (120 - 102) / (102 - 100) = 9 with the stop half a typical move below.
        Assert.Equal((3m, 1.5), (result.LadderTrade.RewardToRisk!.Value, result.LadderTrade.StopInMoves!.Value));
        Assert.Equal((102m, 100m, 120m, 9m), (result.SwingTrade.Entry!.Value, result.SwingTrade.Stop!.Value, result.SwingTrade.Target!.Value, result.SwingTrade.RewardToRisk!.Value));
        Assert.Equal(0.5, result.SwingTrade.StopInMoves!.Value, 9);
        Assert.Equal(10, result.BandStrength);
        Assert.Equal(["no earnings date is on file, so none is excluded for one"], result.Notes);
    }

    [Fact]
    public void TheMarketGatePassesAtItsFloorAndNotAStepBelowItAndFailsOnABreadthNotRead()
    {
        Assert.Equal(0.5, FilterSettings.ProposedBreadthFloor);
        Assert.True(Passed(Gates(Passing() with { Breadth = new Breadth(4, 4, 2, 0.5) }), SwingGates.Market));
        Assert.False(Passed(Gates(Passing() with { Breadth = new Breadth(4, 4, 2, 0.4999) }), SwingGates.Market));

        // Counted at the two floors the operator rules between: 0.47 passes at 45% and not at 50%.
        var night = Passing() with { Breadth = new Breadth(100, 100, 47, 0.47) };

        Assert.True(Passed(Gates(night, FilterSettings.Proposed with { BreadthFloor = 0.45 }), SwingGates.Market));
        Assert.False(Passed(Gates(night), SwingGates.Market));

        // A breadth held by too few is not available, and the gate fails and says how many held it; every
        // other gate is still evaluated.
        var unread = Gates(Passing() with { Breadth = new Breadth(4, 1, 1, null) });

        Assert.False(Passed(unread, SwingGates.Market));
        Assert.Equal("breadth is not available: 1 of the 4 members hold a close and a 200-day average, fewer than half", Reason(unread, SwingGates.Market));
        Assert.True(Passed(unread, SwingGates.Trend) && Passed(unread, SwingGates.Setup) && Passed(unread, SwingGates.Trigger) && Passed(unread, SwingGates.Trade));
        Assert.False(unread.Passed);

        Assert.Equal("no market reading is stored for the night", Reason(Gates(Passing() with { Breadth = null }), SwingGates.Market));
    }

    [Fact]
    public void TheTrendGatePassesAnUptrendAtTwoThirdsAndNothingElse()
    {
        Assert.True(Passed(Gates(Passing() with { Strength = 2.0 / 3.0 }), SwingGates.Trend));
        Assert.False(Passed(Gates(Passing() with { Strength = (2.0 / 3.0) - 1e-9 }), SwingGates.Trend));
        Assert.False(Passed(Gates(Passing() with { TrendState = "range" }), SwingGates.Trend));
        Assert.Equal("range, not an uptrend; strength 0.800 at or above its floor of 0.667", Reason(Gates(Passing() with { TrendState = "range" }), SwingGates.Trend));

        // An absent reading fails with its reason rather than passing on the absence.
        Assert.Equal("no trend state is stored for the night", Reason(Gates(Passing() with { TrendState = null }), SwingGates.Trend));
        Assert.Equal("no bar for this session", Reason(Gates(Passing() with { Strength = null, Reading = null, ReadingNote = "no bar for this session" }), SwingGates.Trend));
        Assert.False(Passed(Gates(Passing() with { Strength = null }), SwingGates.Trend));
    }

    [Fact]
    public void ThePullbackPassesAtEitherEndOfItsDepthAndBelowTheDryUpCeilingAndInsideAnAnchoredSupportBand()
    {
        SwingReading Reading(double depth, double? dryUp) => Passing().Reading! with { Depth = depth, DryUp = dryUp };

        // Depth at 2 and at 5 passes, a hundredth outside either end fails.
        Assert.Equal(SwingGates.Pullback, Gates(Passing() with { Reading = Reading(2.0, 0.8) }).Family);
        Assert.Equal(SwingGates.Pullback, Gates(Passing() with { Reading = Reading(5.0, 0.8) }).Family);
        Assert.Null(Gates(Passing() with { Reading = Reading(1.99, 0.8) }).Family);
        Assert.Null(Gates(Passing() with { Reading = Reading(5.01, 0.8) }).Family);

        // A dry-up a hundredth under 1 passes, one at 1 fails, and a high made on the night has none.
        Assert.Equal(SwingGates.Pullback, Gates(Passing() with { Reading = Reading(3.0, 0.99) }).Family);
        Assert.Null(Gates(Passing() with { Reading = Reading(3.0, 1.0) }).Family);
        Assert.Contains("the high was made on the night", Reason(Gates(Passing() with { Reading = Reading(3.0, null) with { PullbackSessions = 0 } }), SwingGates.Setup), StringComparison.Ordinal);

        // Inside the band is above its low edge and at or below its high: 104 is inside, 100 is not.
        Assert.Equal(SwingGates.Pullback, Gates(Passing() with { Close = 104m }).Family);
        Assert.Null(Gates(Passing() with { Close = 100m }).Family);

        // A band with no member that is not a moving average, or one the close sits above as resistance, is not one.
        Assert.Null(Gates(Passing() with { Bands = [new FilterBand(100m, 104m, "support", 10, false)] }).Family);
        Assert.Null(Gates(Passing() with { Bands = [new FilterBand(100m, 104m, "resistance", 10, true)] }).Family);
    }

    [Fact]
    public void TheBreakoutPassesATightBaseClearingABandAboveThePreviousCloseOnOneAndAHalfTimesItsVolume()
    {
        // No pullback, its depth one typical move; a band 95 to 99 sitting at the previous close of 95 and
        // cleared by a close of 102; tightness 0.69; volume 1,500 against a fifty-day average of 1,000,
        // exactly one and a half times. Worked by hand, a breakout, and its trigger is the break itself.
        var breakout = Passing() with
        {
            Reading = Passing().Reading! with { Depth = 1.0, Tightness = 0.69 },
            PreviousClose = 95m,
            Volume = 1500,
            Bands = [new FilterBand(95m, 99m, "support", 7, true), new FilterBand(120m, 121m, "resistance", 5, true)],
            TriggerFiredTheSessionBefore = null,
        };

        var result = Gates(breakout);

        Assert.Equal(SwingGates.Breakout, result.Family);
        Assert.True(Passed(result, SwingGates.Trigger));
        Assert.Equal(7, result.BandStrength);

        // One share under the multiple, a tightness at the ceiling, a band below the previous close, and a
        // close only at the band's top edge each leave no breakout.
        Assert.Null(Gates(breakout with { Volume = 1499 }).Family);
        Assert.Null(Gates(breakout with { Reading = breakout.Reading! with { Tightness = 0.7 } }).Family);
        Assert.Null(Gates(breakout with { PreviousClose = 95.01m }).Family);
        Assert.Null(Gates(breakout with { Close = 99m }).Family);
    }

    [Fact]
    public void TheTriggerIsTheEventTonightWithNoneOnTheSessionBeforeAndFailsWhereTheSessionBeforeCannotSay()
    {
        // The one-session window, the family's variant: arrival is the event tonight with none on the
        // session before.
        var one = FilterSettings.Proposed with { ArrivalSessions = 1 };

        // A close at the previous high is not above it; back into the band after a close below its low edge is.
        Assert.False(Gates(Passing() with { Close = 101m }, one).TriggerEvent);
        Assert.True(Gates(Passing() with { Close = 100.5m, PreviousClose = 99m }, one).TriggerEvent);
        Assert.True(Passed(Gates(Passing() with { Close = 100.5m, PreviousClose = 99m }, one), SwingGates.Trigger));

        // The same event on the session before is not an arrival, and a session before that stored no result
        // cannot say, so the gate fails rather than passing on the absence.
        var again = Gates(Passing() with { TriggerFiredTheSessionBefore = true }, one);

        Assert.False(Passed(again, SwingGates.Trigger));
        Assert.Equal("the trigger fired on 2026-09-04 too, so tonight is not its arrival", Reason(again, SwingGates.Trigger));
        Assert.False(Passed(Gates(Passing() with { TriggerFiredTheSessionBefore = null }, one), SwingGates.Trigger));
        Assert.Equal(
            "no gate result is stored for 2026-09-04, so the trigger's arrival cannot be read",
            Reason(Gates(Passing() with { TriggerFiredTheSessionBefore = null }, one), SwingGates.Trigger));
        Assert.Equal("no previous session's high to read the trigger against", Reason(Gates(Passing() with { PreviousHigh = null }, one), SwingGates.Trigger));
    }

    [Fact]
    public void TheTriggerPassesWhereItFirstFiredWithinTheLastThreeSessionsAndFailsWhereItArrivedEarlierOrCannotBeRead()
    {
        // Section 17's window of 3: tonight, 2026-09-04 and 2026-09-03, each read against the session before
        // it, 2026-09-02 the last one read. Tonight's close of 101 is at the previous high and not above it,
        // so tonight's event did not happen.
        var quiet = Passing() with { Close = 101m };

        GateInputs Before(bool? on04, bool? on03, bool? on02) => quiet with
        {
            TriggerFiredTheSessionBefore = on04,
            Earlier = [new SessionEvent(new DateOnly(2026, 9, 3), on03), new SessionEvent(new DateOnly(2026, 9, 2), on02)],
        };

        Assert.Equal(3, FilterSettings.Proposed.ArrivalSessions);

        // Worked by hand. Fired on 09-04 and not on 09-03: arrived one session back, inside the window.
        var yesterday = Gates(Before(true, false, null));

        Assert.True(Passed(yesterday, SwingGates.Trigger));
        Assert.Equal("the trigger first fired on 2026-09-04, 1 session(s) before tonight, inside the 3-session window", Reason(yesterday, SwingGates.Trigger));
        Assert.Equal("2026-09-04", yesterday.Gates[3].Values[SwingGates.ArrivedValue]);

        // Fired on 09-03 and 09-04 and not on 09-02: arrived two sessions back, the window's last session.
        Assert.True(Passed(Gates(Before(true, true, false)), SwingGates.Trigger));

        // Fired on each of 09-02, 09-03 and 09-04: it arrived before the window, and the gate fails.
        var early = Gates(Before(true, true, true));

        Assert.False(Passed(early, SwingGates.Trigger));
        Assert.Equal("the trigger did not arrive in the last 3 sessions", Reason(early, SwingGates.Trigger));
        Assert.Equal("none", early.Gates[3].Values[SwingGates.ArrivedValue]);

        // Fired on none of them: no arrival.
        Assert.False(Passed(Gates(Before(false, false, false)), SwingGates.Trigger));

        // Fired on 09-03 and 09-04 with 09-02 not stored: the arrival turns on 09-02, which cannot say, so the
        // gate fails and names it rather than passing on the absence.
        var unread = Gates(Before(true, true, null));

        Assert.False(Passed(unread, SwingGates.Trigger));
        Assert.Equal("no gate result is stored for 2026-09-02, so the trigger's arrival cannot be read", Reason(unread, SwingGates.Trigger));

        // An arrival tonight passes whatever the sessions before it held, and tonight firing on every session
        // of the window and the one before it fails.
        Assert.True(Passed(Gates(Passing() with { TriggerFiredTheSessionBefore = false, Earlier = [] }), SwingGates.Trigger));

        var always = Gates(Passing() with
        {
            TriggerFiredTheSessionBefore = true,
            Earlier = [new SessionEvent(new DateOnly(2026, 9, 3), true), new SessionEvent(new DateOnly(2026, 9, 2), true)],
        });

        Assert.False(Passed(always, SwingGates.Trigger));
        Assert.Equal("the trigger fired on every session back to 2026-09-02, so it did not arrive in the last 3 sessions", Reason(always, SwingGates.Trigger));

        // The arithmetic alone: newest arrival first, and the first unread session where none is found.
        Assert.Equal(((int?)1, (int?)null), SwingGates.Arrival(false, [true, false, null], 3));
        Assert.Equal(((int?)null, (int?)3), SwingGates.Arrival(false, [true, true, null], 3));
        Assert.Equal(((int?)null, (int?)null), SwingGates.Arrival(true, [true, true, true], 3));
        Assert.Equal(((int?)2, (int?)null), SwingGates.Arrival(null, [true, true, false], 3));
    }

    [Fact]
    public void TheTradePassesATwoToOneWithItsStopBetweenOneAndTwoAndAHalfTypicalMoves()
    {
        FirstTranche Plan(decimal stop, decimal ratio) => new(102m, stop, 120m, ratio, null);

        // A reward to risk of exactly 2 passes and one ten-thousandth under it fails.
        Assert.True(Passed(Gates(Passing() with { Ladder = Plan(96m, 2m) }), SwingGates.Trade));
        Assert.False(Passed(Gates(Passing() with { Ladder = Plan(96m, 1.9999m) }), SwingGates.Trade));

        // The stop at 98 sits (102 - 98) / 4 = 1 typical move below, and passes; at 98.04, 0.99, it fails.
        // At 92 it sits 2.5 below, and passes; at 91.96, 2.51, it fails.
        Assert.True(Passed(Gates(Passing() with { Ladder = Plan(98m, 3m) }), SwingGates.Trade));
        Assert.False(Passed(Gates(Passing() with { Ladder = Plan(98.04m, 3m) }), SwingGates.Trade));
        Assert.True(Passed(Gates(Passing() with { Ladder = Plan(92m, 3m) }), SwingGates.Trade));
        Assert.False(Passed(Gates(Passing() with { Ladder = Plan(91.96m, 3m) }), SwingGates.Trade));

        // A plan with no reward to risk fails with the arithmetic's own words.
        var none = Gates(Passing() with { Ladder = new FirstTranche(102m, 96m, null, null, "no exit is traded, so there is no reward to measure") });

        Assert.Equal("no exit is traded, so there is no reward to measure", Reason(none, SwingGates.Trade));

        // Read from the swing trade's own plan, its stop half a typical move below the entry fails where
        // the ladder's passes; with its stop at 97 it sits 1.25 below at (120 - 102) / (102 - 97) = 3.6 and passes.
        var swing = FilterSettings.Proposed with { Trade = TradeInput.Swing };

        Assert.False(Passed(Gates(Passing(), swing), SwingGates.Trade));
        Assert.True(Passed(Gates(Passing() with { Bands = [new FilterBand(97m, 104m, "support", 10, true), new FilterBand(120m, 121m, "resistance", 5, true)] }, swing), SwingGates.Trade));
        Assert.Equal("no band above the close to set a target at", Reason(Gates(Passing() with { Bands = [new FilterBand(97m, 104m, "support", 10, true)] }, swing), SwingGates.Trade));
    }

    [Fact]
    public void APrintInsideTheHoldingWindowExcludesTheNameAndOneAfterItDoesNot()
    {
        Assert.Equal(15, FilterSettings.ProposedEarningsWindowSessions);

        GateResult At(int? sessions, DateOnly? on) => Gates(Passing() with { NextEarnings = on, SessionsToEarnings = sessions });

        var print = new DateOnly(2026, 9, 29);

        Assert.Equal([SwingGates.EarningsExclusion], At(15, print).Exclusions);
        Assert.Equal([SwingGates.EarningsExclusion], At(0, print).Exclusions);
        Assert.Empty(At(16, print).Exclusions);
        Assert.False(At(15, print).Passed);
        Assert.True(At(16, print).Passed);

        // A date past the closure table is not counted and excludes nothing, saying so.
        Assert.Empty(At(null, new DateOnly(2027, 6, 1)).Exclusions);
        Assert.Equal(["the next earnings date is past the end of the closure table, so its sessions are not counted"], At(null, new DateOnly(2027, 6, 1)).Notes);

        // A suspect series and a gap each exclude the name.
        Assert.Equal([SwingGates.SuspectExclusion], Gates(Passing() with { Suspect = true }).Exclusions);
        Assert.Equal([SwingGates.GapExclusion], Gates(Passing() with { Gap = new DateOnly(2026, 8, 3) }).Exclusions);
    }

    [Fact]
    public void TheNamesPassingAreOrderedByRewardToRiskThenStrengthThenBandStrengthThenTicker()
    {
        GateResult Member(string ticker, decimal ratio, double strength, int band, decimal swingStop) =>
            Gates(Passing() with
            {
                Ticker = ticker,
                Strength = strength,
                Ladder = new FirstTranche(102m, 96m, 120m, ratio, null),
                Bands = [new FilterBand(swingStop, 104m, "support", band, true), new FilterBand(120m, 121m, "resistance", 5, true)],
            });

        // Worked by hand: ZZB and ZZD share the highest reward to risk, 4, and ZZD is stronger; ZZA and ZZC
        // share 3 and the same strength, and ZZC's band is stronger; ZZE ties ZZA on all three and follows
        // it by ticker. A member failing a gate is not ranked.
        var results = new[]
        {
            Member("ZZA", 3m, 0.8, 10, 100m),
            Member("ZZB", 4m, 0.7, 10, 100m),
            Member("ZZC", 3m, 0.8, 12, 100m),
            Member("ZZD", 4m, 0.9, 10, 100m),
            Member("ZZE", 3m, 0.8, 10, 100m),
            Gates(Passing() with { Ticker = "ZZF", Strength = 0.1 }),
        };

        Assert.Equal(["ZZD", "ZZB", "ZZC", "ZZA", "ZZE"], SwingGates.Ranked(results, FilterSettings.Proposed).Select(result => result.Ticker));

        // Read from the swing trade's own plan the order follows its reward to risk instead: a stop at 101
        // gives (120 - 102) / 1 = 18, at 100 it gives 9 and at 99 it gives 6.
        var swing = new[]
        {
            Member("ZZA", 3m, 0.8, 10, 99m),
            Member("ZZB", 3m, 0.8, 10, 101m),
            Member("ZZC", 3m, 0.8, 10, 100m),
        };

        Assert.Equal(["ZZB", "ZZC", "ZZA"], SwingGates.Ranked(swing, FilterSettings.Proposed with { Trade = TradeInput.Swing, StopLow = 0, StopHigh = 5 }).Select(result => result.Ticker));
    }

    [Fact]
    public void TheFunnelCountsEachGateInOrderAndEachGateRelaxedAlone()
    {
        // Five members worked by hand: one passing everything, one failing the market alone, one failing
        // the setup alone, one failing both the trend and the trade, one passing every gate but excluded.
        var results = new[]
        {
            Gates(Passing()),
            Gates(Passing() with { Ticker = "ZZB", Breadth = new Breadth(4, 4, 1, 0.25) }),
            Gates(Passing() with { Ticker = "ZZC", Reading = Passing().Reading! with { Depth = 1.0 } }),
            Gates(Passing() with { Ticker = "ZZD", TrendState = "range", Ladder = new FirstTranche(102m, 96m, 120m, 1m, null) }),
            Gates(Passing() with { Ticker = "ZZE", Suspect = true }),
        };

        // Members 5; through the market 4; the trend 3; the setup 2; the trigger 2; the trade 2; passing 1.
        Assert.Equal([5, 4, 3, 2, 2, 2, 1], SwingFunnel.Of(results));

        // Relaxed alone, excluded names never counted: the market lets ZZB back, 2; the trend lets nobody
        // back, ZZD failing the trade too, 1; the setup lets ZZC back, 2; the trigger and the trade let
        // nobody back, 1 each.
        Assert.Equal([2, 1, 2, 1, 1], SwingFunnel.RelaxedAlone(results));

        Assert.Equal(
            "5 member(s), 4 through market (1 removed), 3 through trend and strength (1 removed), 2 through setup (1 removed), 2 through trigger (0 removed), 2 through trade (0 removed), 1 excluded, 1 passing, no filter version open, so section 17's proposed values",
            SwingFunnel.Line(SwingFunnel.Of(results), 1, 1, "none"));
    }

    [Fact]
    public void AVersionsSettingsAreWrittenWholeAndOneMissingASettingIsRefused()
    {
        var settings = FilterSettings.Proposed with { BreadthFloor = 0.45, Trade = TradeInput.Swing };

        Assert.Equal(settings, FilterSettings.Read(settings.Write()));

        var missing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.Write())!;
        missing.Remove("dryUpCeiling");

        Assert.Contains("dryUpCeiling", Assert.Throws<InvalidOperationException>(() => FilterSettings.Read(JsonSerializer.Serialize(missing))).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => FilterSettings.Read(settings.Write().Replace("\"swing\"", "\"both\"", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheFixturesGateResultsAreTheOnesWorkedByHandOverTheTwoNightStore()
    {
        var expected = Expected("gate-results");

        Assert.Equal(["gate_result"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));
        Assert.Equal(0.5, expected.GetProperty("settings").GetProperty("breadthFloor").GetDouble());
        Assert.Equal(
            ["the 2026-09-03 bands", "the 2026-09-03 trend states", "the 2026-09-03 plans of AAPL and NFLX"],
            expected.GetProperty("readFromTheStore").EnumerateArray().Select(one => one.GetString()!));

        using var store = await WithTwoNights();

        var read = 0;

        foreach (var night in expected.GetProperty("nights").EnumerateObject())
        {
            foreach (var name in night.Value.EnumerateObject())
            {
                var want = name.Value;
                var row = Assert.Single(Query(
                    store,
                    "SELECT market || '|' || trend || '|' || setup || '|' || trigger_pass || '|' || trade || '|' || IFNULL(family, '') || '|' || " +
                    "IFNULL(trigger_event, '') || '|' || CASE WHEN ladder_reward_to_risk IS NULL THEN '' ELSE printf('%.4f', ladder_reward_to_risk) END || '|' || IFNULL(swing_stop, '') || '|' || " +
                    "IFNULL(swing_target, '') || '|' || CASE WHEN swing_reward_to_risk IS NULL THEN '' ELSE printf('%.4f', swing_reward_to_risk) END || '|' || IFNULL(band_strength, '') || '|' || exclusions || '|' || passed " +
                    $"FROM gate_result WHERE ticker = '{name.Name}' AND session_date = '{night.Name}';")).Split('|');

                var gates = want.GetProperty("gates");
                string Flag(bool value) => value ? "1" : "0";
                string Text(JsonElement value) => value.ValueKind == JsonValueKind.Null ? string.Empty : value.ToString();

                Assert.Equal(
                    [
                        Flag(gates.GetProperty("market").GetBoolean()), Flag(gates.GetProperty("trend and strength").GetBoolean()),
                        Flag(gates.GetProperty("setup").GetBoolean()), Flag(gates.GetProperty("trigger").GetBoolean()),
                        Flag(gates.GetProperty("trade").GetBoolean()),
                        Text(want.GetProperty("family")),
                        want.GetProperty("triggerEvent").GetBoolean() ? "1" : "0",
                        want.GetProperty("ladderRewardToRisk").ValueKind == JsonValueKind.Null ? string.Empty : decimal.Parse(want.GetProperty("ladderRewardToRisk").GetString()!, CultureInfo.InvariantCulture).ToString("0.0000", CultureInfo.InvariantCulture),
                        Text(want.GetProperty("swingStop")),
                        Text(want.GetProperty("swingTarget")),
                        want.GetProperty("swingRewardToRisk").ValueKind == JsonValueKind.Null ? string.Empty : decimal.Parse(want.GetProperty("swingRewardToRisk").GetString()!, CultureInfo.InvariantCulture).ToString("0.0000", CultureInfo.InvariantCulture),
                        Text(want.GetProperty("bandStrength")),
                        JsonSerializer.Serialize(want.GetProperty("exclusions").EnumerateArray().Select(one => one.GetString()!).ToArray()),
                        Flag(want.GetProperty("passed").GetBoolean()),
                    ],
                    row);

                // The figures read as statistics, within a ten-thousandth of the ones worked by hand.
                var figures = Assert.Single(Query(
                    store,
                    "SELECT printf('%.6f', ladder_stop_moves) || '|' || CASE WHEN swing_stop_moves IS NULL THEN '' ELSE printf('%.6f', swing_stop_moves) END || '|' || printf('%.6f', strength) " +
                    $"FROM gate_result WHERE ticker = '{name.Name}' AND session_date = '{night.Name}';")).Split('|');

                Assert.InRange(Math.Abs(double.Parse(figures[0], CultureInfo.InvariantCulture) - want.GetProperty("ladderStopMoves").GetDouble()), 0, 0.0001);
                Assert.InRange(Math.Abs(double.Parse(figures[2], CultureInfo.InvariantCulture) - want.GetProperty("strength").GetDouble()), 0, 0.0001);

                if (want.GetProperty("swingStopMoves").ValueKind != JsonValueKind.Null)
                {
                    Assert.InRange(Math.Abs(double.Parse(figures[1], CultureInfo.InvariantCulture) - want.GetProperty("swingStopMoves").GetDouble()), 0, 0.0001);
                }

                read++;
            }
        }

        Assert.Equal(8, read);

        // Every row names no open version and the code it was written by.
        Assert.Equal(["none|" + EquityBrief.Worker.Filter.SwingFilter.CodeVersion], Query(store, "SELECT DISTINCT version || '|' || code FROM gate_result;"));
    }
}
