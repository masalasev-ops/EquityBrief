using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.2: a card's stretch mark is counted over past empty nights and not over stretches, worked by
// hand one night either side of it and at the floors; a member one session before its breakout stands in the forming
// list with the price it must close above and the volume the rule needs, and is absent the night it breaks out; the
// funnels, a variant's own picks and the earnings window are each worked by hand.
// see: A card's stretch line counts its mark over past empty nights and draws none under 30 completed stretches
// see: The forming list advises and never lists a stock
// see: A variant's picks are shown on its card when chosen and its results only under its tests
public partial class FixtureExpectations
{
    // Thirty stretches of two empty nights each ended by a pick, one stretch of a hundred ended by a pick, and enough
    // pick nights after to pass the sessions floor: 511 nights, 31 completed stretches, 160 past empty nights whose
    // reached stretches are 1 and 2 thirty-one times each and 3 to 100 once each.
    static int[] StretchBase(int shortStretches = 30, int padding = 320)
    {
        var nights = new List<int>();

        for (var stretch = 0; stretch < shortStretches; stretch++)
        {
            nights.AddRange([0, 0, 1]);
        }

        nights.AddRange(Enumerable.Repeat(0, 100));
        nights.Add(1);
        nights.AddRange(Enumerable.Repeat(1, padding));

        return [.. nights];
    }

    [Fact]
    public void TheStretchMarkIsCountedOverPastEmptyNightsAndNotOverStretches()
    {
        // Over the base and one empty night: 160 past empty nights, the 152nd of them in order, 95 per cent of 160
        // rounded up, is reached at 92, since 62 nights reached 1 or 2 and the 90th night after them reached 92. Counted
        // a stretch at a time the 95th per cent of 31 lengths would have been 2.
        var reading = RuleStretch.Read([.. StretchBase(), 0]);

        Assert.Equal(1, reading.Stretch);
        Assert.Equal(92, reading.Mark);
        Assert.NotEqual(2, reading.Mark);
        Assert.Equal(31, reading.CompletedStretches);
        Assert.Equal(512, reading.Sessions);
        Assert.False(reading.Flagged);
    }

    [Fact]
    public void TheStretchIsFlaggedOneNightPastItsMarkAndNotAtIt()
    {
        // With 88 empty nights appended, 247 past empty nights, the 235th in order: 64 reached 1 or 2, two a value from
        // 3 to 87 for 170 more, so 234 by 87 and 235 at 88. The stretch of 88 is at the mark and not past it; one night
        // on, 248 past empty nights and the 236th in order, 236 by 88, so the mark stays 88 and the stretch of 89 is
        // past it.
        var at = RuleStretch.Read([.. StretchBase(), .. Enumerable.Repeat(0, 88)]);
        var past = RuleStretch.Read([.. StretchBase(), .. Enumerable.Repeat(0, 89)]);

        Assert.Equal<(int, int?, bool)>((88, 88, false), (at.Stretch, at.Mark, at.Flagged));
        Assert.Equal<(int, int?, bool)>((89, 88, true), (past.Stretch, past.Mark, past.Flagged));
    }

    [Fact]
    public void NoMarkIsDrawnUnderThirtyCompletedStretchesOrUnderTheSessionsFloor()
    {
        // Twenty-eight short stretches and the long one complete 29: no mark. Twenty-nine and the long one complete 30
        // over 508 sessions: a mark. Thirty and the long one over 503 sessions: no mark, and a mark at 504.
        Assert.Null(RuleStretch.Read([.. StretchBase(shortStretches: 28, padding: 330), 0]).Mark);
        Assert.Equal(29, RuleStretch.Read([.. StretchBase(shortStretches: 28, padding: 330), 0]).CompletedStretches);
        Assert.NotNull(RuleStretch.Read([.. StretchBase(shortStretches: 29, padding: 320), 0]).Mark);
        Assert.Equal(30, RuleStretch.Read([.. StretchBase(shortStretches: 29, padding: 320), 0]).CompletedStretches);

        var short503 = StretchBase(padding: 312);
        var at504 = StretchBase(padding: 313);

        Assert.Equal(503, short503.Length);
        Assert.Null(RuleStretch.Read(short503).Mark);
        Assert.Equal(504, at504.Length);
        Assert.NotNull(RuleStretch.Read(at504).Mark);

        // A rule listing tonight has no stretch, and one evaluated on no night reads nothing.
        Assert.Equal(0, RuleStretch.Read([0, 0, 1]).Stretch);
        Assert.Equal(new StretchReading(0, null, false, 0, 0), RuleStretch.Read([]));

        // Each night's reading in turn is the reading over the nights to it.
        var each = RuleStretch.ReadEach([0, 0, 1, 0]);

        Assert.Equal([1, 2, 0, 1], each.Select(reading => reading.Stretch));
        Assert.Equal([0, 0, 1, 1], each.Select(reading => reading.CompletedStretches));
    }

    // A member's sessions: 126 before tonight with highs at 100, each a range of 2 on a close of 90, and tonight's bar.
    static BreakoutInputs FormingInputs(decimal close, long volume, bool marketOpen = true, double? typicalMove = 1.0, double? volumeAverage = 1_000_000)
    {
        var bars = Enumerable.Range(0, 126)
            .Select(at => new FamilyBar(new DateOnly(2026, 1, 2).AddDays(at), 100m, 98m, 90m, 900_000))
            .Append(new FamilyBar(new DateOnly(2026, 6, 1), Math.Max(close, 99m), 97m, close, volume))
            .ToArray();

        return new BreakoutInputs("FRM", new Gate(FamilyRule.Market, marketOpen, "constructed", FamilyRule.Values()), bars, volumeAverage, typicalMove, []);
    }

    [Fact]
    public void AMemberOneSessionBeforeItsBreakoutIsFormingWithItsPriceAndVolumeAndAbsentTheNightItBreaksOut()
    {
        var rule = BreakoutRule.Live;
        var settings = FormingSettings.Defaults;

        // Half a typical move under the 126-session high of 100, on 800,000 shares against the 1,500,000 the rule
        // would need at 1.5 times the 1,000,000 average, its ranges as wide as before, so the ratio of 1 sits at the
        // list's ceiling and over the rule's 0.85: forming, still failing the new high, the volume and the tightening.
        var forming = FormingList.Read(FormingInputs(99.5m, 800_000), rule, settings);

        Assert.NotNull(forming);
        Assert.Equal("FRM", forming!.Ticker);
        Assert.Equal(99.5m, forming.Close);
        Assert.Equal(100m, forming.High);
        Assert.Equal(0.5, forming.MovesUnder, 9);
        Assert.Equal(1_500_000, forming.VolumeNeeded, 6);
        Assert.Equal(800_000, forming.Volume, 6);
        Assert.Equal(1.0, forming.RangeRatio, 9);
        Assert.Equal([BreakoutRule.NewHigh, BreakoutRule.Volume, BreakoutRule.Tightened], forming.Missing);

        // The night it closes above the high it is a breakout and not forming one, whatever its volume.
        Assert.Null(FormingList.Read(FormingInputs(100.5m, 800_000), rule, settings));
        Assert.Null(FormingList.Read(FormingInputs(100.5m, 2_000_000), rule, settings));

        // At the high it is not above it and still forming; two moves under it is too far; the market check closed, no
        // typical move and no average volume each leave it off.
        Assert.NotNull(FormingList.Read(FormingInputs(100m, 800_000), rule, settings));
        Assert.Null(FormingList.Read(FormingInputs(98m, 800_000), rule, settings));
        Assert.Null(FormingList.Read(FormingInputs(99.5m, 800_000, marketOpen: false), rule, settings));
        Assert.Null(FormingList.Read(FormingInputs(99.5m, 800_000, typicalMove: null), rule, settings));
        Assert.Null(FormingList.Read(FormingInputs(99.5m, 800_000, volumeAverage: null), rule, settings));

        // On the volume the rule needs it still fails the new high and the tightening alone.
        Assert.Equal([BreakoutRule.NewHigh, BreakoutRule.Tightened], FormingList.Read(FormingInputs(99.5m, 1_500_000), rule, settings)!.Missing);

        // The nearest misses first: the fewest gates failing, then the nearest to the high.
        var ordered = FormingList.Order(
        [
            FormingList.Read(FormingInputs(99.0m, 800_000), rule, settings)!,
            FormingList.Read(FormingInputs(99.5m, 800_000), rule, settings)!,
            FormingList.Read(FormingInputs(99.0m, 1_500_000), rule, settings)!,
        ]);

        Assert.Equal([(1.0, 2), (0.5, 3), (1.0, 3)], ordered.Select(member => (Math.Round(member.MovesUnder, 6), member.Missing.Count)));
    }

    [Fact]
    public void TheFunnelCountsEachGateAndEveryGateBefore()
    {
        string[] order = [SwingGates.Market, SwingGates.Trend, SwingGates.Setup];
        IReadOnlyDictionary<string, string>[] answers =
        [
            new Dictionary<string, string> { [SwingGates.Market] = "passed", [SwingGates.Trend] = "passed", [SwingGates.Setup] = "passed" },
            new Dictionary<string, string> { [SwingGates.Market] = "passed", [SwingGates.Trend] = "failed", [SwingGates.Setup] = "passed" },
            new Dictionary<string, string> { [SwingGates.Market] = "passed", [SwingGates.Trend] = "passed", [SwingGates.Setup] = "failed" },
            new Dictionary<string, string> { [SwingGates.Market] = "failed", [SwingGates.Trend] = "passed", [SwingGates.Setup] = "passed" },
        ];

        // A member passing a later gate while failing an earlier one is not counted at the later gate.
        Assert.Equal([(SwingGates.Market, 3), (SwingGates.Trend, 2), (SwingGates.Setup, 1)], RuleCards.Funnel(order, answers));

        // An index rule's parts off the first each member failed: five members, one passing, one failing at the setup,
        // one at the floors, one at the profit check and one at the cover.
        var parts = RuleCards.IndexFunnel(
        [
            ("breakout", true, null),
            ("breakout", false, IndexNightRead.NoSetup),
            ("breakout", false, IndexNightRead.UnderTheFloors),
            ("breakout", false, IndexNightRead.NoProfit),
            ("breakout", false, IndexNightRead.NoCover),
        ]);

        Assert.Equal([(IndexNightRead.MarketClosed, 5), (IndexNightRead.NoSetup, 4), (IndexNightRead.UnderTheFloors, 3), (IndexNightRead.NoProfit, 2), (IndexNightRead.NoCover, 1)], parts);
        Assert.Equal(RuleRows.ReadGates(RuleRows.GatesJson(parts)), parts);
    }

    static RuleCards.FilterRow FilterRowOf(string ticker, double strength, int band, decimal entry, decimal clearStop, decimal? clearTarget, double? clearReward, decimal swingStop = 0m, decimal? swingTarget = null, double? swingReward = null) =>
        new(ticker, [], strength, band, entry, swingStop, swingTarget, swingReward, clearStop, clearTarget, clearReward, "{}");

    static ShadowOutcome Fired(string plan = "clear") => new("variant", true, new Dictionary<string, string> { ["plan"] = plan });

    [Fact]
    public void AVariantKeepsFiveInTheListsOwnOrderNoneItHoldsAndNoneWithoutAStop()
    {
        var kept = RuleCards.KeepPullbackPicks(
        [
            (FilterRowOf("LOW", 0.9, 9, 100m, 95m, 110m, 2.0), Fired()),
            (FilterRowOf("HIGH", 0.5, 1, 100m, 95m, 115m, 3.0), Fired()),
            (FilterRowOf("TIE2", 0.7, 5, 100m, 95m, 115m, 3.0), Fired()),
            (FilterRowOf("HELD", 0.9, 9, 100m, 95m, 120m, 4.0), Fired()),
            (FilterRowOf("NOST", 0.9, 9, 100m, 100m, 120m, 4.0), Fired()),
            (FilterRowOf("SIXA", 0.1, 1, 100m, 95m, 105m, 1.0), Fired()),
            (FilterRowOf("SIXB", 0.1, 1, 100m, 95m, 105m, 1.0), Fired()),
            (FilterRowOf("SIXC", 0.1, 1, 100m, 95m, 105m, 1.0), Fired()),
            (FilterRowOf("SWNG", 0.6, 2, 100m, 95m, 110m, 2.0, swingStop: 90m, swingTarget: 150m, swingReward: 5.0), Fired("swing")),
        ], new HashSet<string>(StringComparer.Ordinal) { "HELD" });

        // Reward to risk first, the swing plan's where the variant reads it, then strength, then band strength and
        // then the ticker; the stock held off, the one whose stop sits at its buy left out, and five at most.
        Assert.Equal(["SWNG", "TIE2", "HIGH", "LOW", "SIXA"], kept.Select(pick => pick.Ticker));
        Assert.Equal([1, 2, 3, 4, 5], kept.Select(pick => pick.Place));
        Assert.Equal<(decimal, decimal?, double?)>((90m, 150m, 5.0), (kept[0].Stop, kept[0].Target, kept[0].RewardToRisk));
        Assert.Equal(SetupFamilies.Pullbacks.CapSessions, kept[0].Cap);
    }

    [Fact]
    public void TheEarningsWindowIsCountedOverTheSessionsTheStoreHoldsAndOverCalendarDaysBeyondThem()
    {
        var night = new DateOnly(2026, 10, 7);
        DateOnly[] after = [.. Enumerable.Range(1, 10).Select(day => night.AddDays(day))];

        // Within the stored sessions: the tenth after the night is within twenty and within ten, the eleventh day
        // beyond them counts at five sessions a week.
        Assert.True(RuleCards.WithinSessions(night, night.AddDays(10), after, 20));
        Assert.True(RuleCards.WithinSessions(night, night.AddDays(10), after, 10));
        Assert.False(RuleCards.WithinSessions(night, night.AddDays(10), after, 9));
        Assert.False(RuleCards.WithinSessions(night, night, after, 20));

        // Beyond the stored sessions: 10 sessions held, then 14 calendar days at five a week are 10 more, 20 in all.
        Assert.True(RuleCards.WithinSessions(night, night.AddDays(24), after, 20));
        Assert.False(RuleCards.WithinSessions(night, night.AddDays(25), after, 20));
        Assert.True(RuleCards.WithinSessions(night, night.AddDays(28), [], 20));
        Assert.False(RuleCards.WithinSessions(night, night.AddDays(29), [], 20));
    }
}
