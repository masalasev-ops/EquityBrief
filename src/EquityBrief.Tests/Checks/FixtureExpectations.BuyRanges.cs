using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: the buy range worked by hand. A pick opening inside its range buys at the open, one opening above
// it waits for the price to come back to the ceiling for 2 sessions and is not bought where it does not, and one opening
// under the floor is not bought; the widths table's tally and its choice are worked over constructed figures, the choice read
// off 2005 to 2018 alone; a card's plan reads in the operator's words; and the long history's walker buys in a range only
// where it is given one, a pick not bought naming its side and keeping the result the open made.
// see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
public partial class FixtureExpectations
{
    // Section 17's row of the buy range and section 18's row of a pick that opens outside it.
    internal static readonly string[] BuyRangeClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The buy range"),
        CheckReach.Key(Scope.FailureTable, "A pick that opens outside its buy range"),
    ];

    internal static string[] BuyRangeRows =>
        [.. ComponentAccess.RangeWidthRows, .. SchemaColumns.BuyRangeStoreRows, .. BuyRangeClaims, .. Reading.ReadSurface.BuyRangeRows];

    static readonly BuyRange Range102Over96 = new(102, 96);

    static RangeFill Filled(double[] opens, double[] lows, BuyRange range) => BuyRanges.FillOf(opens, lows, 0, range);

    [Fact]
    public void ARangeIsSetFromTheLastCloseAndAPickFillsInsideAboveOrNotAtAllAsWorkedByHand()
    {
        // The night's close 100 and its typical daily move 2: a ceiling of 1 move is 102 and a floor of 2 is 96.
        Assert.Equal(new BuyRange(102, 96), BuyRanges.Of(100, 2, new RangeWidths(1, 2)));
        Assert.Equal(new BuyRange(101, null), BuyRanges.Of(100, 2, new RangeWidths(0.5, null)));
        Assert.Equal(new BuyRange(16, null), BuyRanges.Of(10, 6, new RangeWidths(1, 2)));
        Assert.Null(BuyRanges.Of(100, 0, new RangeWidths(1, 2)));
        Assert.Equal(12, BuyRanges.Widths.Count);
        Assert.Equal(["0.5 / 1", "0.5 / 2", "0.5 / none", "1 / 1", "1 / 2", "1 / none", "1.5 / 1", "1.5 / 2", "1.5 / none", "2 / 1", "2 / 2", "2 / none"], BuyRanges.Widths.Select(one => one.Key));

        // Inside the range, its ceiling and its floor among it: bought at that open on the session after the night.
        Assert.Equal(new WaitFill(1, 101), Filled([100, 101, 100], [99, 100, 99], Range102Over96).Fill);
        Assert.Equal(new WaitFill(1, 102), Filled([100, 102, 100], [99, 101, 99], Range102Over96).Fill);
        Assert.Equal(new WaitFill(1, 96), Filled([100, 96, 100], [99, 95, 99], Range102Over96).Fill);

        // Above it: the order at the ceiling fills at the ceiling where the first session comes back to it, at the second's
        // open where that opens at or under it, at the ceiling where the second comes back to it, and buys nothing otherwise.
        Assert.Equal(new WaitFill(1, 102), Filled([100, 103, 100], [99, 101.5, 99], Range102Over96).Fill);
        Assert.Equal(new WaitFill(2, 101.5), Filled([100, 104, 101.5], [99, 103, 101], Range102Over96).Fill);
        Assert.Equal(new WaitFill(2, 102), Filled([100, 104, 103], [99, 103, 101.9], Range102Over96).Fill);

        var missed = Filled([100, 104, 104, 101], [99, 103, 103, 100], Range102Over96);

        Assert.Equal((null, BuyRanges.Above), (missed.Fill, missed.NotBought));

        // Under the floor: not bought, however the price moves after.
        var under = Filled([100, 95.9, 100], [99, 95, 99], Range102Over96);

        Assert.Equal((null, BuyRanges.Below), (under.Fill, under.NotBought));

        // No floor: any open at or under the ceiling buys.
        Assert.Equal(new WaitFill(1, 50), Filled([100, 50, 100], [99, 49, 99], new BuyRange(102, null)).Fill);

        // Waiting: no open after the night yet, or the order's second session not yet held.
        Assert.True(Filled([100], [99], Range102Over96).Waiting);
        Assert.True(Filled([100, 104], [99, 103], Range102Over96).Waiting);
    }

    [Fact]
    public void ACardsPlanReadsInTheOperatorsWordsAndNoLineSaysItBuysAtWhateverPriceItOpens()
    {
        Assert.Equal(
            ["Buys between $96.00 and $102.00", "Opens above $102.00: waits up to 2 days for the price to come back to $102.00, then not bought.", "Opens below $96.00: not bought."],
            BuyRanges.PlanLines(Range102Over96));
        Assert.Equal(
            ["Buys at $1,250.50 or less", "Opens above $1,250.50: waits up to 2 days for the price to come back to $1,250.50, then not bought."],
            BuyRanges.PlanLines(new BuyRange(1250.5, null)));
        Assert.Equal("not bought: opened above its range", BuyRanges.NotBoughtWords(BuyRanges.Above));
        Assert.Equal("not bought: opened below its range", BuyRanges.NotBoughtWords(BuyRanges.Below));
        Assert.DoesNotContain(BuyRanges.PlanLines(Range102Over96), line => line.Contains("whatever price it opens", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWidthsTallyAndItsChoiceAreWorkedByHandAndTheChoiceReadsTheEarlyYearsAlone()
    {
        // Four picks the open made +1.0, -0.5, +0.2 and -1.0 on: the range buys the first at +0.9 and the third at +0.25, and
        // does not buy the second, which opened above, or the fourth, which opened below, so it kept out of 0.5 and 1.0 of
        // losses and missed no winner.
        var tally = RangeTally.Empty.Add(1.0, 0.9, null).Add(-0.5, null, BuyRanges.Above).Add(0.2, 0.25, null).Add(-1.0, null, BuyRanges.Below);

        Assert.Equal((4, 2, 1, 1), (tally.Offered, tally.Bought, tally.Above, tally.Below));
        Assert.Equal(1.15, tally.Edge, 9);
        Assert.Equal((0.0, 1.5), (tally.Missed, tally.Saved));
        Assert.Equal(0.2875, tally.EdgePerPick!.Value, 9);
        Assert.Equal(0.375, tally.SavedPerPick!.Value, 9);

        // A winner it did not buy is missed.
        Assert.Equal(0.7, RangeTally.Empty.Add(0.7, null, BuyRanges.Above).Missed, 9);

        RangeTally Edged(int bought, double perPick) => new(bought, bought, 0, 0, perPick * bought, 0, 0);

        var (narrow, wide, wider, thin) = (new RangeWidths(0.5, 1), new RangeWidths(1, 2), new RangeWidths(2, null), new RangeWidths(1.5, 1));

        // The highest edge a pick resting on 100 fills or more, a tie to the wider ceiling; one resting on 99 is not read.
        Assert.Equal(wider, BuyRanges.Chosen([(narrow, Edged(100, 0.0625)), (wide, Edged(160, 0.125)), (wider, Edged(120, 0.125)), (thin, Edged(99, 0.25))]));
        Assert.Null(BuyRanges.Chosen([(thin, Edged(99, 0.25))]));

        // Over two setups of an index, every setup together: the early years choose 1 / 2, and the later years, where 0.5 / 1
        // leads by far, move nothing. The choice is marked on both of its rows together and on no other.
        var rows = BuyRanges.Rows("GSPC",
        [
            ("pullback", null, Edged(200, 0.05), Edged(200, 0.05)),
            ("pullback", narrow, Edged(100, 0.06), Edged(100, 0.90)),
            ("pullback", wide, Edged(100, 0.08), Edged(100, 0.01)),
            ("breakout", narrow, Edged(100, 0.06), Edged(100, 0.90)),
            ("breakout", wide, Edged(100, 0.07), Edged(100, 0.01)),
        ]);
        var chosen = rows.Where(row => row.Chosen).ToArray();

        Assert.Equal(16, rows.Count);
        Assert.Equal([(BuyRanges.AllSetups, "1 / 2", BuyRanges.Early), (BuyRanges.AllSetups, "1 / 2", BuyRanges.Late)], chosen.Select(row => (row.Family, row.Width, row.Years)));
        Assert.Equal(200, rows.Single(row => row.Family == BuyRanges.AllSetups && row.Width == "1 / 2" && row.Years == BuyRanges.Early).Tally.Offered);
        Assert.Equal(BuyRanges.TheOpen, rows[0].Width);
    }

    [Fact]
    public void TheLongHistorysWalkerBuysInARangeOnlyWhereGivenOneAndAPickNotBoughtNamesItsSideAndKeepsItsResult()
    {
        // Four members flat at a close of 100 with a high of 101 and a low of 99, so the typical daily move on the night, the
        // fortieth session, is 2 and a range of 1 / 2 runs from 96 to 102. On the session after it AAA opens at 101, BBB at
        // 103 coming back to 101.5, CCC at 104 and 104 again never under 103, and DDD at 95.
        var calendar = Enumerable.Range(0, 60).Select(at => new DateOnly(2010, 1, 4).AddDays(at)).ToArray();
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        const int Night = 40;

        SweepName Member(string ticker, params (int At, decimal Open, decimal Low)[] after) =>
            new(ticker, [.. calendar.Select((session, at) => after.FirstOrDefault(one => one.At == at) is { At: > 0 } moved
                ? new SweepBar(session, Math.Max(moved.Open, 101m), moved.Low, 100m, 1_000_000, moved.Open, 100m)
                : new SweepBar(session, 101m, 99m, 100m, 1_000_000, 100m, 100m))], [(null, null)], []);

        var series = new[]
        {
            SweepColumns.Series(Member("AAA", (Night + 1, 101m, 99m)), sessionAt),
            SweepColumns.Series(Member("BBB", (Night + 1, 103m, 101.5m)), sessionAt),
            SweepColumns.Series(Member("CCC", (Night + 1, 104m, 103m), (Night + 2, 104m, 103m)), sessionAt),
            SweepColumns.Series(Member("DDD", (Night + 1, 95m, 94m)), sessionAt),
        };
        var names = series.Select((one, at) => (one.Name.Ticker, at)).ToDictionary(pair => pair.Ticker, pair => new List<int> { pair.at }, StringComparer.Ordinal);
        var walker = new PlanPointTest.Walker(series, calendar, names, new Dictionary<DateOnly, double>(), new Dictionary<DateOnly, string>());
        var plan = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale);
        var widths = new RangeWidths(1, 2);

        PlanPointTest.FlaggedSetup Setup(string ticker) => new("pullback", ticker, calendar[Night], 100, 90, 110, null, 5, null);

        Assert.Equal(2, series[0].Atr[Night], 9);

        // Bought at the open with no range given, the walk as it stood; in a range AAA buys at its open, BBB at the ceiling.
        Assert.Equal(103, walker.WorkedUnder(Setup("BBB"), [plan]).Single().Fill);
        Assert.Equal((101.0, 90.0), (walker.WorkedInRange(Setup("AAA"), [plan], widths).Single().Fill!.Value, walker.WorkedInRange(Setup("AAA"), [plan], widths).Single().Stop!.Value));
        Assert.Equal(102, walker.WorkedInRange(Setup("BBB"), [plan], widths).Single().Fill);

        var above = walker.WorkedInRange(Setup("CCC"), [plan], widths).Single();
        var below = walker.WorkedInRange(Setup("DDD"), [plan], widths).Single();

        Assert.Equal((BuyRanges.Above, "not bought: opened above its range", false), (above.NotBought, above.Words, above.Pick!.Filled));
        Assert.Equal((BuyRanges.Below, "not bought: opened below its range", false), (below.NotBought, below.Words, below.Pick!.Filled));

        // The tally at 1 / 2: four offered, AAA and BBB bought, CCC above and DDD below, each not bought keeping the edge the
        // open made on it as missed or kept out of.
        var setups = new[] { Setup("AAA"), Setup("BBB"), Setup("CCC"), Setup("DDD") };
        var opens = setups.Select(one => walker.PicksUnder(one, [plan]).Single()!.Edge!.Value).ToArray();
        var tally = RangeWidthTest.Tally(walker, "pullback", plan, setups).Single(one => one.Widths == widths).Early;
        var open = RangeWidthTest.Tally(walker, "pullback", plan, setups).Single(one => one.Widths is null).Early;

        Assert.Equal((4, 2, 1, 1), (tally.Offered, tally.Bought, tally.Above, tally.Below));
        Assert.Equal(opens[2..].Sum(Math.Abs), tally.Missed + tally.Saved, 9);
        Assert.Equal((4, 4), (open.Offered, open.Bought));
        Assert.Equal(opens.Sum(), open.Edge, 9);
        Assert.Equal(13, RangeWidthTest.Tally(walker, "pullback", plan, setups).Count);
    }
}
