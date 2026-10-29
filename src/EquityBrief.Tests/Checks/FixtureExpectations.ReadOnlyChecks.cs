using System.Text.RegularExpressions;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Returns;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.3: the three read-only checks, each worked by hand. A setup's score under a fold's stored
// model, its rank among the model's hundredths and its group; the three pricings of a trade whose high reaches its target
// before a close triggers, of one whose stop closes first and of one that reaches neither; the open floor's parts over a
// constructed ledger against the tester's own judge, and the skip that spends a listing's place; and each check writing
// no store row, read off its declaration and its source.
// see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
public partial class FixtureExpectations
{
    [Fact]
    public void ASetupsScoreUnderAFoldsStoredModelAndItsGroupAreWorkedByHand()
    {
        // Three readings weighed at 0.6, -0.3 and 0.4 over spreads of 0.1, 10 and 0.2, and the model's hundredths on the
        // S&P 500 one hundred and one scores from -1 to +1, a fiftieth apart.
        int[] weighed = [LedgerReadings.IndexOf("close_over_twenty"), LedgerReadings.IndexOf("rsi"), LedgerReadings.IndexOf("volume_ratio")];
        var hundredths = Enumerable.Range(0, 101).Select(hundredth => (hundredth / 50.0) - 1.0).ToArray();
        var model = new RidgeModel(weighed, [0.02, 50, 0.05], [0.1, 10, 0.2], [0.6, -0.3, 0.4], [0, 0], 0, 400, -2, 2, new Dictionary<string, IReadOnlyList<double>> { ["GSPC"] = hundredths });

        double?[] Readings(double closeOverTwenty, double rsi, double volumeRatio)
        {
            var readings = new double?[LedgerReadings.Count];

            readings[weighed[0]] = closeOverTwenty;
            readings[weighed[1]] = rsi;
            readings[weighed[2]] = volumeRatio;

            return readings;
        }

        // By hand: 0.6 / 0.1 x 0.05 + (-0.3) / 10 x 60 + 0.4 / 0.2 x 1.5 = 0.3 - 1.8 + 3.0 = 1.5, above every hundredth, so
        // the last at or under it is the hundredth of a hundred and the group the top one.
        var top = ScoreAudit.Place(model, "GSPC", Readings(0.05, 60, 1.5));

        Assert.NotNull(top);
        Assert.Equal(1.5, top.Value.Score, 12);
        Assert.Equal(100, top.Value.Rank);
        Assert.Equal(4, top.Value.Group);

        // 0.6 / 0.1 x 0.01 + (-0.3) / 10 x 20 + 0.4 / 0.2 x 0.0 = 0.06 - 0.6 = -0.54: the hundredths at or under it run from -1
        // to -0.54 exactly, twenty-four of them, so the rank is 23 and the group the second.
        var low = ScoreAudit.Place(model, "GSPC", Readings(0.01, 20, 0));

        Assert.NotNull(low);
        Assert.Equal(-0.54, low.Value.Score, 12);
        Assert.Equal(23, low.Value.Rank);
        Assert.Equal(1, low.Value.Group);

        // A reading the model weighs not held places nothing, and an index the model learned on no setup of places nothing.
        Assert.Null(ScoreAudit.Place(model, "GSPC", Readings(0.01, 20, 0).Select((reading, at) => at == weighed[1] ? null : reading).ToArray()));
        Assert.Null(ScoreAudit.Place(model, "MID", Readings(0.05, 60, 1.5)));

        // The five groups of twenty, on both sides of each edge, the hundredth of a hundred in the top one.
        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 4], new[] { 0, 19, 20, 39, 40, 59, 60, 79, 80, 99, 100 }.Select(ScoreAudit.GroupOf));

        // The rows over placed setups: each group's setups, those winning, the mean edge after costs, the picks and the
        // passes, a group with none stated at nought.
        var rows = ScoreAudit.Rows(
            [
                new ScoreAudit.Placed("drift", "ridge", 2023, 1.5, 100, 4, Picked: true, LivePass: true, Result: 2.0, Edge: 0.5),
                new ScoreAudit.Placed("drift", "ridge", 2023, 1.2, 90, 4, Picked: false, LivePass: true, Result: -1.0, Edge: -1.5),
                new ScoreAudit.Placed("drift", "ridge", 2024, -0.54, 23, 1, Picked: false, LivePass: false, Result: 0.5, Edge: null),
            ],
            ScoreAudit.Folds);

        Assert.Equal(5, rows.Count);
        Assert.Equal((2, 1, -0.5, 1, 2), (rows[4].Setups, rows[4].Winning, rows[4].MeanEdge, rows[4].Picked, rows[4].Passed));
        Assert.Equal((1, 1, (double?)null, 0, 0), (rows[1].Setups, rows[1].Winning, rows[1].MeanEdge, rows[1].Picked, rows[1].Passed));
        Assert.Equal(0, rows[0].Setups);
        Assert.Equal(0.5, rows[4].WinningShare);
    }

    [Fact]
    public void TheThreePricingsOfATradeAreWorkedByHandOnBothSidesOfEachExit()
    {
        // The plan: bought at 100's close in the ledger's terms, a stop at 95 and a target at 110, so the risk is 5, five
        // sessions at most; the open after the listing is 101, which every pricing buys at.
        var anchor = new SetupAnchor(new DateOnly(2024, 3, 4), 100, 95, 110, null, 5, 1.0);

        // A: the second session's high reaches 110 while its close stays under; the third closes at 112, over the target.
        double[] closesA = [100, 103, 108, 112, 113, 114, 115];
        double[] opensA = [99, 101, 103, 109, 113, 113.5, 114.5];
        double[] highsA = [100.5, 104, 111, 112.5, 113.5, 114.5, 115.5];
        var a = ExitPricings.Price(closesA, opensA, highsA, 0, anchor, 101);

        Assert.NotNull(a);
        Assert.Equal((2.2, 3, SetupEnds.Target), (Math.Round(a.AtClose.Result!.Value, 10), a.AtClose.Sessions, a.AtClose.End));
        Assert.Equal((2.4, 4, SetupEnds.Target), (Math.Round(a.AtNextOpen.Result!.Value, 10), a.AtNextOpen.Sessions, a.AtNextOpen.End));
        Assert.Equal((1.8, 2, SetupEnds.Target), (Math.Round(a.Resting.Result!.Value, 10), a.Resting.Sessions, a.Resting.End));

        // B: the second session closes at 94, under the stop, and the third opens at 93; no high reaches the target.
        double[] closesB = [100, 99, 94, 92, 91, 90, 89];
        double[] opensB = [99, 101, 98, 93, 92, 91, 90];
        double[] highsB = [100.5, 102, 99, 94, 93, 92, 91];
        var b = ExitPricings.Price(closesB, opensB, highsB, 0, anchor, 101);

        Assert.NotNull(b);
        Assert.Equal((-1.4, 2, SetupEnds.Stop), (Math.Round(b.AtClose.Result!.Value, 10), b.AtClose.Sessions, b.AtClose.End));
        Assert.Equal((-1.6, 3, SetupEnds.Stop), (Math.Round(b.AtNextOpen.Result!.Value, 10), b.AtNextOpen.Sessions, b.AtNextOpen.End));
        Assert.Equal((-1.6, 3, SetupEnds.Stop), (Math.Round(b.Resting.Result!.Value, 10), b.Resting.Sessions, b.Resting.End));

        // C: neither level is reached in five sessions, so every pricing sells at the fifth close, 105.
        double[] closesC = [100, 101, 102, 103, 104, 105, 106];
        double[] opensC = [99, 101, 101.5, 102.5, 103.5, 104.5, 105.5];
        double[] highsC = [100.5, 102, 103, 104, 105, 106, 107];
        var c = ExitPricings.Price(closesC, opensC, highsC, 0, anchor, 101);

        Assert.NotNull(c);
        Assert.Equal((0.8, 5, SetupEnds.Cap), (Math.Round(c.AtClose.Result!.Value, 10), c.AtClose.Sessions, c.AtClose.End));
        Assert.Equal((0.8, 5, SetupEnds.Cap), (Math.Round(c.AtNextOpen.Result!.Value, 10), c.AtNextOpen.Sessions, c.AtNextOpen.End));
        Assert.Equal((0.8, 5, SetupEnds.Cap), (Math.Round(c.Resting.Result!.Value, 10), c.Resting.Sessions, c.Resting.End));

        // A session reaching the target and closing under the stop sells at the target under the resting order, and at the
        // stop's close under the ledger's own pricing.
        double[] closesD = [100, 94, 93, 92, 91, 90, 89];
        double[] opensD = [99, 101, 95, 93, 92, 91, 90];
        double[] highsD = [100.5, 111, 96, 94, 93, 92, 91];
        var d = ExitPricings.Price(closesD, opensD, highsD, 0, anchor, 101);

        Assert.NotNull(d);
        Assert.Equal(SetupEnds.Stop, d.AtClose.End);
        Assert.Equal((1.8, 1, SetupEnds.Target), (Math.Round(d.Resting.Result!.Value, 10), d.Resting.Sessions, d.Resting.End));

        // No open after the listing prices nothing, and a trailing plan rests no order: its second close under the trail
        // the first set sells at the next open under both of the other pricings.
        Assert.Null(ExitPricings.Price(closesA, opensA, highsA, 0, anchor, null));

        var trailing = new SetupAnchor(new DateOnly(2024, 3, 4), 100, 95, null, 4, 5, 1.0);
        double[] closesE = [100, 108, 103, 102, 101, 100, 99];
        double[] opensE = [99, 101, 107, 102.5, 101.5, 100.5, 99.5];
        double[] highsE = [100.5, 120, 120, 120, 120, 120, 120];
        var e = ExitPricings.Price(closesE, opensE, highsE, 0, trailing, 101);

        Assert.NotNull(e);
        Assert.Equal((0.4, 2, SetupEnds.Trail), (Math.Round(e.AtClose.Result!.Value, 10), e.AtClose.Sessions, e.AtClose.End));
        Assert.Equal((0.3, 3, SetupEnds.Trail), (Math.Round(e.AtNextOpen.Result!.Value, 10), e.AtNextOpen.Sessions, e.AtNextOpen.End));
        Assert.Equal((0.3, 3, SetupEnds.Trail), (Math.Round(e.Resting.Result!.Value, 10), e.Resting.Sessions, e.Resting.End));

        // The groups over two priced setups of one family: paired means and their differences, and the ends counted.
        var groups = SetupLedger.ExitGroups(
            [
                new SetupLedger.ExitPricedSetup("drift", 2024, 2.2, a, Benchmark: 0.2, Cost: 0.1),
                new SetupLedger.ExitPricedSetup("drift", 2024, -1.4, b, Benchmark: -0.4, Cost: null),
            ],
            [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(("drift", (int?)null, 2), (groups[0].Family, groups[0].Year, groups[0].Priced));
        Assert.Equal(0.4, groups[0].ResultAtClose!.Value, 10);
        Assert.Equal(0.4, groups[0].ResultAtNextOpen!.Value, 10);
        Assert.Equal(0.1, groups[0].ResultResting!.Value, 10);
        Assert.Equal(-0.3, groups[0].ResultRestingLessClose!.Value, 10);
        Assert.Equal(((2.2 - 0.2 - 0.1) + (-1.4 + 0.4)) / 2, groups[0].EdgeAtClose!.Value, 10);
        Assert.Equal((1, 1, 1, 1), (groups[0].TargetsAtClose, groups[0].TargetsResting, groups[0].StopsAtClose, groups[0].StopsResting));
        Assert.Equal(2024, groups[1].Year);
    }

    [Fact]
    public void TheOpenFloorsPartsOverAConstructedLedgerEqualTheTestersAndASkipSpendsTheListingsPlace()
    {
        // A history from 2019 whose test years begin in 2022, ending the cap's sessions and five more past the eighth block
        // of the test years, since a block is read only once the cap has traded after its last session, so eight blocks
        // hold units and are read.
        const int cap = 60;
        var calendar = Weekdays(new DateOnly(2019, 1, 2), 1400);
        var opens = Array.FindIndex(calendar, session => session.Year >= LoopFolds.FirstTestYear);

        calendar = calendar[..(opens + (8 * Blocks.Sessions) + cap + 5)];

        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), calendar, 0, LoopFolds.Of(calendar, calendar[^1]));

        Assert.Equal(opens, read.Opens);
        Assert.Equal(calendar.Where(session => session.Year >= LoopFolds.FirstTestYear).Select(session => session.Year).Distinct(), read.Folds.Select(fold => fold.Year));

        // The drift's live rule with one unit a block at an edge of nothing, and the open floor with the same units at +1,
        // so every block reads the floor one risk ahead: by hand the identity alone among the 256 sign vectors reaches the
        // observed, one in 256, under the bar; every counted year is better; and eight units fall short of the count.
        var sessions = Enumerable.Range(0, 8).Select(block => opens + (block * Blocks.Sessions) + 1).ToArray();
        var head = new RuleUnderTest(DriftRule.Name, "live S&P 400 drift rule", RuleTests.Live, "the drift", LoopProcedures.Risks, cap, [.. sessions.Select(session => (session, (double?)0.0))]);
        var floorWords = WalkForwardTester.OpenFloorWordsOf(head.Rule);

        Assert.Equal("live S&P 400 drift rule, skipping a fill whose reward to risk at the open is under 1", floorWords);

        var floor = new RuleUnderTest(DriftRule.Name, floorWords, RuleTests.Variant, floorWords, LoopProcedures.Risks, cap, [.. sessions.Select(session => (session, (double?)1.0))]);
        var tested = RuleTests.Judge(read, [head, floor]);
        var judged = tested.Single(one => ReferenceEquals(one.Rule, floor));

        Assert.Equal(head.Rule, judged.Against);
        Assert.NotNull(judged.Verdict);
        Assert.Equal(1.0 / 256, judged.Verdict.Adjusted);
        Assert.True(judged.Verdict.Gate);
        Assert.Equal(8, judged.Verdict.Blocks);
        Assert.Equal(8, judged.Verdict.Units);
        Assert.False(judged.Verdict.Counts);
        Assert.Equal(read.Folds.Select(fold => fold.Year), judged.Evidence!.Years.Select(year => year.Year));
        Assert.All(judged.Evidence.Years.Where(year => year.ProposedUnits > 0), year => Assert.True(year.Better));
        Assert.Equal(8, judged.Evidence.Years.Sum(year => year.ProposedUnits));
        Assert.Equal(8.0, judged.Evidence.Years.Sum(year => year.ProposedTotal));
        Assert.Equal(0.0, judged.Evidence.Years.Sum(year => year.CurrentTotal));

        // The rule's own verdict against its benchmark stands beside it, read first.
        Assert.Equal(head.Rule, tested[0].Rule.Rule);
        Assert.Equal(RuleTests.Benchmark, tested[0].Against);

        // The skip the open floor walks with: six listings one night, five a night, the top one skipped. Its place is
        // spent and its stock left free, so four are bought and the sixth is not.
        FamilyListing Listing(int name, double order) => new(name, 10, 10, order, 0, 100, 95, 110, double.NaN, 5, 1.0);

        var listings = Enumerable.Range(0, 6).Select(name => Listing(name, 6 - name)).ToArray();
        var skipped = 0;
        var kept = RuleReplay.WalkSkippingAtTheOpen(listings, ["A", "B", "C", "D", "E", "F"], _ => 0, _ => (1.0, 3), _ => 0.0, listing => listing.Name == 0, () => skipped++);

        Assert.Equal(1, skipped);
        Assert.Equal([1, 2, 3, 4], kept.Select(trade => trade.Listing.Name));

        // The floor's own predicate: a fill leaving less than one risk of reward to its risk is skipped, one leaving exactly
        // one is kept, and a fill at or under the stop is skipped.
        var plan = Listing(0, 1);

        Assert.True(RuleReplay.LeavesUnder(103, plan, RuleRecorder.OpenFloor));
        Assert.False(RuleReplay.LeavesUnder(102.5, plan, RuleRecorder.OpenFloor));
        Assert.True(RuleReplay.LeavesUnder(95, plan, RuleRecorder.OpenFloor));
        Assert.False(RuleReplay.LeavesUnder(null, plan, RuleRecorder.OpenFloor));
    }

    [Fact]
    public void EachReadOnlyCheckDeclaresNoWriteAndItsSourceHoldsNoWritingStatement()
    {
        Assert.All(ScoreAudit.Access.Stores, touch => Assert.Equal(Touch.Read, touch.Touch));
        Assert.Empty(ScoreAudit.Access.Feeds);

        string[] sources =
        [
            "src/EquityBrief.Worker/Loop/ScoreAudit.cs",
            "src/EquityBrief.Worker/Ledger/SetupLedger.Exits.cs",
            "src/EquityBrief.Worker/Loop/WalkForwardTester.OpenFloor.cs",
            "src/EquityBrief.Worker/Loop/CheckReports.cs",
            "src/EquityBrief.Core/Ledger/ExitPricings.cs",
        ];

        foreach (var source in sources)
        {
            var text = File.ReadAllText(Path.Combine(Repository.Root, source));

            Assert.DoesNotMatch(new Regex(@"\b(INSERT INTO|DELETE FROM|UPDATE\s+\w+\s+SET)\b"), text);
            Assert.DoesNotContain("ExecuteNonQuery", text, StringComparison.Ordinal);
        }

        // The page names its check's title and carries every cell, and a figure cell reads as its number.
        var page = CheckReports.Page("Exits on the S&P 600", ["one line"], [new CheckReports.Table("heading", ["Family", "Priced"], [["drift", "12"]], "a note")], new DateTimeOffset(2026, 10, 29, 20, 0, 0, TimeSpan.Zero));

        Assert.Contains("<title>Exits on the S&amp;P 600</title>", page, StringComparison.Ordinal);
        Assert.Contains("<td>drift</td><td>12</td>", page, StringComparison.Ordinal);
        Assert.Contains("a note", page, StringComparison.Ordinal);
        Assert.Equal("+0.250", CheckReports.Cell(0.25, signed: true));
        Assert.Equal("-", CheckReports.Cell(null));
        Assert.Equal("50.0%", CheckReports.Share(1, 2));
    }
}
