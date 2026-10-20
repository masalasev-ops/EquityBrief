using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// Each family read on its own index's history at the next open: the market check's seven levels, the research's dials
// each at its edge, and a holding bought at the open after its rebalance, each worked by hand.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
// see: Analyst coverage is stored from each fetch and waits for dated counts before any rule tests it
// see: Each family on the S&P 400 and 600 is swept again on its own history at the next open, the market check a dial of each swing grid
// see: A family's search on the S&P 400 or 600 is judged by the tester year by year with its second stage run inside each fold
public partial class FixtureExpectations
{
    static SweepColumns.Session Breadth(double? share) => new(share, [], new Dictionary<string, int>());

    [Fact]
    public void TheMarketChecksSevenLevelsAreWorkedByHandAtEachEdge()
    {
        var calendar = Weekdays(new DateOnly(2019, 1, 1), 205);
        double?[] own = [.. Enumerable.Repeat<double?>(0.6, calendar.Length)];
        double?[] large = [.. Enumerable.Repeat<double?>(0.6, calendar.Length)];

        // On sessions 200 to 203 the index's own breadth stands exactly at 0.35, a hair under it, not held, and exactly at
        // 0.45; on sessions 200 to 202 the S&P 500's stands exactly at its floor, a hair under it and not held.
        own[200] = 0.35;
        own[201] = 0.3499;
        own[202] = null;
        own[203] = 0.45;
        large[200] = 0.45;
        large[201] = 0.4499;
        large[202] = null;

        // The fund closes 100 on sessions 0 to 199, so session 199's close stands exactly at the average of its 200 and
        // closes the lists, and session 198 holds too few; then 100.5 on session 200, above the average of the 200 to it,
        // 100.0025; 99 on session 201, under its average of 99.9975; none on session 202; and 101 after.
        var closes = new List<(DateOnly Session, double Close)>();

        for (var session = 0; session < calendar.Length; session++)
        {
            if (session != 202)
            {
                closes.Add((calendar[session], session < 200 ? 100.0 : session == 200 ? 100.5 : session == 201 ? 99.0 : 101.0));
            }
        }

        var fund = new SweepMarketSeries("IJH", closes, "constructed");
        var open = IndexSweepRunner.MarketOpen([.. own.Select(Breadth)], large, calendar, fund);

        Assert.Equal(["off", "own 0.35", "own 0.40", "own 0.45", "own 0.50", "S&P 500 0.45", "fund trend"], IndexSweepRunner.MarketLevels("MID").Select(level => level.Key));
        Assert.Equal(3, IndexSweepRunner.ProvisionalMarketLevel);
        Assert.All(open[0], Assert.True);
        Assert.Equal([true, false, false, true], new[] { 200, 201, 202, 203 }.Select(session => open[1][session]));
        Assert.Equal([false, false, false, true], new[] { 200, 201, 202, 203 }.Select(session => open[3][session]));
        Assert.Equal([false, false, false, false], new[] { 200, 201, 202, 203 }.Select(session => open[4][session]));
        Assert.Equal([true, false, false], new[] { 200, 201, 202 }.Select(session => open[5][session]));
        Assert.Equal([false, false, true, false, false, true], new[] { 198, 199, 200, 201, 202, 203 }.Select(session => open[6][session]));

        // The index's own breadth at 0.35 closes two sessions over the whole history, both inside its last sixty, and from
        // session 202 on it closes one.
        Assert.Equal((2, 2), IndexSweepRunner.ClosedSessions(open[1], 0));
        Assert.Equal((1, 1), IndexSweepRunner.ClosedSessions(open[1], 202));
        Assert.Equal((0, 0), IndexSweepRunner.ClosedSessions(open[0], 0));
    }

    [Fact]
    public void EachResearchDialIsWorkedByHandAtItsEdge()
    {
        var calendar = Weekdays(new DateOnly(2019, 1, 1), 60);
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);

        SweepSeries Closing(string ticker, Func<int, decimal> close, IReadOnlyList<SweepSurprise>? surprises = null) =>
            SweepColumns.Series(Constructed(ticker, calendar, [.. Enumerable.Range(0, calendar.Length).Select(close)], 1000) with { SurprisesFiled = surprises }, sessionAt);

        // Every bar spans two points over a flat close, so every typical move is 2: session 59 six points up is three
        // typical moves, at the cap, and 6.02 up is 3.01, past it; session 0 has no session before it and reads past any cap.
        var atTheCap = Closing("A", day => day == 59 ? 106m : 100m);
        var pastIt = Closing("B", day => day == 59 ? 106.02m : 100m);

        Assert.Equal(2.0, atTheCap.Atr[58]);
        Assert.Equal(3.0, IndexSweepRunner.DayMoves(atTheCap, 59));
        Assert.Equal(3.01, IndexSweepRunner.DayMoves(pastIt, 59), 12);
        Assert.Equal(double.PositiveInfinity, IndexSweepRunner.DayMoves(atTheCap, 0));

        // A rise of 3.01 typical moves on session 55 is inside the 21 sessions to session 59 and outside the four to it.
        var jumped = Closing("C", day => day >= 55 ? 106.02m : 100m);

        Assert.False(IndexSweepRunner.NoRiseOver(jumped, 59, 21, 3));
        Assert.True(IndexSweepRunner.NoRiseOver(jumped, 59, 4, 3));
        Assert.True(IndexSweepRunner.NoRiseOver(atTheCap, 59, 21, 3));
        Assert.False(IndexSweepRunner.NoRiseOver(pastIt, 59, 21, 3));

        // A report before the open on session 30 reacts on it, and one after the close on session 40 on session 41: each
        // stands as the newest from its reaction's session and not a session before.
        var reported = Closing("D", _ => 100m, [new SweepSurprise(calendar[30], false, 4.5), new SweepSurprise(calendar[40], true, -2)]);

        Assert.Null(IndexSweepRunner.NewestReport(reported, 29));
        Assert.Equal(4.5, IndexSweepRunner.NewestReport(reported, 30)!.Percent);
        Assert.Equal(4.5, IndexSweepRunner.NewestReport(reported, 40)!.Percent);
        Assert.Equal(-2, IndexSweepRunner.NewestReport(reported, 41)!.Percent);

        // Sold at the close three sessions before the next report, dated as it happened: a listing on session 10 before the
        // report dated on session 30 holds 30 - 3 - 10 = 17 sessions under its cap of 60; one on session 26 holds one; one on
        // 27 holds none and is not bought; one on 41, after the last report, holds as it was.
        FamilyListing On(int bar) => new(0, bar, bar, 0, 0, 100, 98, double.NaN, 2, 60, 2);

        Assert.Equal(17, IndexSweepRunner.BeforeTheNextReport(reported, calendar[10], On(10), 3)!.Value.Cap);
        Assert.Equal(1, IndexSweepRunner.BeforeTheNextReport(reported, calendar[26], On(26), 3)!.Value.Cap);
        Assert.Null(IndexSweepRunner.BeforeTheNextReport(reported, calendar[27], On(27), 3));
        Assert.Equal(60, IndexSweepRunner.BeforeTheNextReport(reported, calendar[41], On(41), 3)!.Value.Cap);

        // A count fetched on a day stands from the next day, the night fetching after its session's close, and none is read
        // before the first fetch that stored one.
        IReadOnlyList<(DateOnly Fetched, int Analysts)> counts = [(new DateOnly(2026, 10, 7), 5), (new DateOnly(2026, 10, 8), 7)];

        Assert.Null(IndexSweepRunner.AnalystsOn(counts, new DateOnly(2026, 10, 6)));
        Assert.Null(IndexSweepRunner.AnalystsOn(counts, new DateOnly(2026, 10, 7)));
        Assert.Equal(5, IndexSweepRunner.AnalystsOn(counts, new DateOnly(2026, 10, 8)));
        Assert.Equal(7, IndexSweepRunner.AnalystsOn(counts, new DateOnly(2026, 10, 9)));
        Assert.Null(IndexSweepRunner.AnalystsOn(null, new DateOnly(2026, 10, 9)));

        // A dip five sessions since its reference high: a report reacting six sessions back came before it, one five back on
        // the high's own session and one on the dip's last session are inside it, and none at all passes.
        SweepCandidate Dip(short since)
        {
            var candidate = new SweepCandidate { SurpriseSessions = since };

            candidate.PullbackSessions[1] = 5;

            return candidate;
        }

        Assert.Equal([true, false, false, true], new short[] { 6, 5, 0, -1 }.Select(since => IndexSweepRunner.NoReportInTheDip(Dip(since), 1)));

        // Over the five sessions to session 59, X falls from 100 to 90, Y to 96 and Z stays: the sector's mean is -0.14 over
        // three, X fell more than it and Y did not, and a sector holding one return reads none.
        var x = Closing("X", day => day < 55 ? 100m : 90m);
        var y = Closing("Y", day => day < 55 ? 100m : 96m);
        var z = Closing("Z", _ => 100m);

        Assert.Equal(-0.10, IndexSweepRunner.ReturnOver(x, 59, 5)!.Value, 12);
        Assert.Equal(0.0, IndexSweepRunner.ReturnOver(x, 59, 4)!.Value, 12);
        Assert.Null(IndexSweepRunner.ReturnOver(x, 59, 60));
        Assert.Equal(-0.14 / 3, IndexSweepRunner.SectorMean([x, y, z], 59, 5)!.Value, 12);
        Assert.True(IndexSweepRunner.ReturnOver(x, 59, 5) < IndexSweepRunner.SectorMean([x, y, z], 59, 5));
        Assert.False(IndexSweepRunner.ReturnOver(y, 59, 5) < IndexSweepRunner.SectorMean([x, y, z], 59, 5));
        Assert.Null(IndexSweepRunner.SectorMean([x], 59, 5));
    }

    static SweepMeasures Measured(int scored, double?[] yearEdge, double? average = null, double?[]? yearAverage = null) =>
        new(scored, scored, scored, 0, null, null, null, average, null, [.. Enumerable.Repeat(scored / 8, 8)], new double?[8], new double?[8], new double?[8], yearAverage ?? new double?[8], yearEdge, 0, 0, 0, 0);

    static double?[] HigherIn(params int[] years) => [.. Enumerable.Range(0, 8).Select(year => years.Contains(year) ? 0.2 : (double?)0.1)];

    [Fact]
    public void ALevelIsKeptByTheSecondStagesOwnRuleOverEightYearsAndInProportionOverAFoldsFewer()
    {
        var without = Measured(400, HigherIn());

        // Over eight years the rule is the second stage's own: six years higher with two of the last three kept, six with
        // one of them not, and six with two of them leaving 299 trades not.
        foreach (var (with, kept) in new[] { (Measured(300, HigherIn(0, 1, 2, 3, 6, 7)), true), (Measured(300, HigherIn(0, 1, 2, 3, 4, 5)), false), (Measured(299, HigherIn(0, 1, 2, 3, 6, 7)), false), (Measured(300, HigherIn(0, 1, 2, 3, 4)), false) })
        {
            Assert.Equal(kept, OwnProcedures.KeptOver(without, with, false, 8));
            Assert.Equal(SweepDials.Kept(without, with, false), OwnProcedures.KeptOver(without, with, false, 8));
        }

        // A fold learning on four years keeps a level higher in three of them, six of every eight rounded up, with two of
        // its own last three, at 150 trades and not 149; two years higher, or the higher years past the fold's own, are
        // not kept.
        Assert.True(OwnProcedures.KeptOver(without, Measured(150, HigherIn(1, 2, 3)), false, 4));
        Assert.True(OwnProcedures.KeptOver(without, Measured(150, HigherIn(0, 1, 2)), false, 4));
        Assert.False(OwnProcedures.KeptOver(without, Measured(149, HigherIn(1, 2, 3)), false, 4));
        Assert.False(OwnProcedures.KeptOver(without, Measured(150, HigherIn(0, 1)), false, 4));
        Assert.False(OwnProcedures.KeptOver(without, Measured(150, HigherIn(4, 5, 6, 7)), false, 4));

        // A switch is read on each year's total and kept only where its result a trade rises as well.
        double?[] lower = [.. Enumerable.Repeat<double?>(0.1, 8)];
        double?[] higher = [.. Enumerable.Repeat<double?>(0.3, 8)];

        Assert.True(OwnProcedures.KeptOver(Measured(400, lower, 0.1, lower), Measured(400, lower, 0.3, higher), true, 4));
        Assert.False(OwnProcedures.KeptOver(Measured(400, lower, 0.4, lower), Measured(400, lower, 0.3, higher), true, 4));
    }

    [Fact]
    public void AFoldsPullbackWalkTalliesOnlyTheTradesThatEndedBeforeItsYearAndHoldsItsFloorsInProportion()
    {
        var design = SweepDesign.Live with { Hold = 10 };
        var setting = SweepLoosest();

        // Three listings of one stock on sessions 10, 20 and 40, each a win of one risk against a benchmark of 0.2 that
        // ends 15 sessions after its listing, so the one on 20 is kept off by the trade still open from 10.
        SweepCandidate Listed(int session)
        {
            var candidate = SweepPassing(1, session);
            var outcomes = new SweepPlanOutcomes { RewardToRisk = 2, StopMoves = 1 };

            Array.Fill(outcomes.Code, SweepPlanOutcomes.Win);
            Array.Fill(outcomes.Null, 0.3f);
            Array.Fill(outcomes.BreakEven, 0.4f);
            Array.Fill(outcomes.Multiple, 1f);
            Array.Fill(outcomes.Benchmark, 0.2f);
            Array.Fill(outcomes.Ends, (short)15);
            candidate.Plans[SweepCandidate.PlanAt(design.Plan, design.Support)] = outcomes;

            return candidate;
        }

        var picks = SweepStages.Picks([Listed(10), Listed(20), Listed(40)], design);
        var tally = SweepStages.Tally.OneExit(design.ExitIndex, SweepStages.SessionsOf(picks));

        // A fold opening on session 30: the trade from 10 ended on 25 and is tallied, under its year moved three places;
        // the listing on 20 is kept off and counted so; the trade from 40 ends on 55, after the fold opens, and is not
        // tallied though it still holds its stock.
        OwnProcedures.WalkUntil(picks, setting, ConditionSetting.Off, tally, 30, 3);

        var fold = SweepMeasures.Of(tally.SumsOf(0), tally.NightsOf(0), SweepNights);

        Assert.Equal((1, 1, 1, 1), (fold.Listed, fold.Entered, fold.Scored, fold.NightsListing));
        Assert.Equal(1, fold.YearScored[3]);
        Assert.Equal(0.8, fold.YearEdge[3]!.Value, 6);
        Assert.Equal(1, fold.Blocked);
        Assert.Equal(1, fold.YearScored.Sum());

        // With no fold and no shift the walk is the stages' own walk, sum for sum.
        var whole = SweepStages.Tally.OneExit(design.ExitIndex, SweepStages.SessionsOf(picks));

        OwnProcedures.WalkUntil(picks, setting, ConditionSetting.Off, tally, int.MaxValue, 0);
        SweepStages.Walk(picks, setting, ConditionSetting.Off, whole);

        Assert.Equal(whole.Sums, tally.Sums);
        Assert.Equal(whole.Nights, tally.Nights);
        Assert.Equal(2, SweepMeasures.Of(tally.SumsOf(0), tally.NightsOf(0), SweepNights).Scored);

        // The trades over the whole history are the two the walk keeps, each with the session it ended on.
        Assert.Equal([new OwnProcedures.PulledBack(10, 25, 0.8), new OwnProcedures.PulledBack(40, 55, 0.8)], OwnProcedures.Trades(picks, design, setting, ConditionSetting.Off).Select(one => one with { Edge = Math.Round(one.Edge!.Value, 6) }));

        // A fold learning on four years reads its trades, years and blocks as eight years' worth: 150 trades, three years
        // beating the break-even and 11 blocks meet the floors of 300, six and 22, and 149 trades do not.
        SweepSummary Summary(int scored, byte years, byte blocks) => new(scored, 0.6f, 0.4f, 0.3f, 1f, 0.8f, years, years, true, blocks, 0.7f, 0);

        Assert.True(OwnProcedures.InProportion(Summary(150, 3, 11), 4).MeetsTheFloors);
        Assert.False(OwnProcedures.InProportion(Summary(149, 3, 11), 4).MeetsTheFloors);
        Assert.False(OwnProcedures.InProportion(Summary(150, 2, 11), 4).MeetsTheFloors);
        Assert.False(OwnProcedures.InProportion(Summary(150, 3, 10), 4).MeetsTheFloors);
        Assert.Equal(Summary(299, 5, 21), OwnProcedures.InProportion(Summary(299, 5, 21), 8));
    }

    [Fact]
    public void AHoldingAndItsSizeCutAreBoughtAtTheOpenAfterTheRebalance()
    {
        var calendar = Weekdays(new DateOnly(2019, 1, 1), 30);
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);

        // Bought at the rebalance on session 10 and sold at session 20's close: the holding from 100 to 120, opening at 101
        // on session 11; its cut's other two from 50 to 55 opening at 51, and from 20 to 19 opening at 20.
        SweepSeries Held(string ticker, decimal from, decimal open, decimal to) =>
            SweepColumns.Series(
                new SweepName(
                    ticker,
                    [.. calendar.Select((day, at) => new SweepBar(day, (at <= 10 ? from : to) + 1, (at <= 10 ? from : to) - 1, at <= 10 ? from : to, 1000, at == 11 ? open : at <= 10 ? from : to))],
                    [(null, null)],
                    []),
                sessionAt);

        var series = new[] { Held("H", 100m, 101m, 120m), Held("P", 50m, 51m, 55m), Held("Q", 20m, 20m, 19m) };
        var tape = new HeavyweightTape
        {
            Tickers = ["H", "P", "Q"],
            Calendar = calendar,
            Close = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())],
            Average200 = [.. series.Select(_ => new double[calendar.Length])],
            Member = [.. series.Select(_ => Enumerable.Repeat(true, calendar.Length).ToArray())],
            LastMemberClose = [.. series.Select(_ => Enumerable.Range(0, calendar.Length).ToArray())],
            Index = new double[calendar.Length],
            First = 0,
        };
        var rebalances = new Dictionary<int, HeavyweightRebalance> { [10] = new HeavyweightRebalance([("Tech", new[] { 0 }, new[] { 0, 1, 2 })]) };
        var walked = new HeavyweightTrade(0, "Tech", 10, 20, "sold", 0.20, 0.0833, null, null, 0);
        var atOpen = IndexSweepRunner.AtTheOpen(walked, tape, series, rebalances);

        // Its result of 0.20 less its gap of 0.01 over its close is 0.19; the cut's 0.19, 0.10 - 0.02 = 0.08 and -0.05 - 0
        // average 0.07333, an edge of 0.11667, each through the core's one pricing function.
        Assert.Equal(TradeFills.AtOpen(0.20, 1, 1, 1.01)!.Value, atOpen.Result!.Value, 12);
        Assert.Equal(0.19, atOpen.Result!.Value, 12);
        Assert.Equal((0.19 + 0.08 - 0.05) / 3, atOpen.Cut!.Value, 12);
        Assert.Equal(0.19 - ((0.19 + 0.08 - 0.05) / 3), atOpen.Edge!.Value, 12);

        // A holding still held stands as walked.
        var held = walked with { End = null };

        Assert.Equal(held, IndexSweepRunner.AtTheOpen(held, tape, series, rebalances));

        // A swing trade's result at the open is the tester's: the one pricing function the tester's walks call, 1.5 risks
        // bought at the close of 100 with a risk of 2 is 1.0 bought at the open of 101.
        var fills = new OpenFills(series, SweepBenchmark.On(series, calendar.Length));
        var listing = new FamilyListing(0, 10, 10, 0, 0, 100, 98, 110, double.NaN, 20, 2);

        Assert.Equal(TradeFills.AtOpen(1.5, 100, 2, 101)!.Value, fills.Exit((1.5, 10), listing).Result!.Value, 12);
        Assert.Equal(1.0, fills.Exit((1.5, 10), listing).Result!.Value, 12);
    }
}
