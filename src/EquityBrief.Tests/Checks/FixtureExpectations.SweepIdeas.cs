using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The ideas' run: its new exits and their benchmark worked by hand, the best three after the open-trade rule, each
// market switch read as it stood on its session and failing where a series misses one, the test at its yearly
// boundary and on the year's total, the combination's order and its two fallbacks, the base's picks read at each
// idea's setting and exit, and the report stating its tries against luck and the market series as the store
// holds them.
// see: A new idea is added to the base one at a time and kept only where it is better in six of eight years
public partial class FixtureExpectations
{
    // The claims the ideas' run makes, which this check reaches: section 17's rows for the ideas' settings and for
    // the test and the combining, and section 18's rows for a run no idea passes and a market series missing on a
    // session.
    internal static readonly string[] IdeasClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Ideas on the base"),
        CheckReach.Key(Scope.LimitsTable, "Ideas test"),
        CheckReach.Key(Scope.FailureTable, "An ideas' run in which no idea passes"),
        CheckReach.Key(Scope.FailureTable, "A market series missing on a session an idea reads"),
    ];

    [Fact]
    public void TheTouchedStopSellsAtTheStopAtTheOpenBelowItAndTakesTheStopOnADayHoldingBoth()
    {
        // Bought at 100 with the stop at 96 and the target at 110, a risk of 4.
        // A session opening at 99 whose low reaches 95.5 sells at the stop, -1.
        Assert.Equal(-1, SweepWalk.TouchedStop([100, 99], [99, 95.5], [100, 98], 0, 100, 96, 110, 10, out var sessions)!.Value, 12);
        Assert.Equal(1, sessions);

        // A session opening at 94, under the stop, sells at its open, -1.5; the session before it neither touched
        // the stop nor closed at the target.
        Assert.Equal(-1.5, SweepWalk.TouchedStop([100, 101, 94], [99, 99, 93], [100, 102, 95], 0, 100, 96, 110, 10, out sessions)!.Value, 12);
        Assert.Equal(2, sessions);

        // A session whose range holds both, its low at 95 and its close at 111: the stop is read first, -1.
        Assert.Equal(-1, SweepWalk.TouchedStop([100, 100], [99, 95], [100, 111], 0, 100, 96, 110, 10, out sessions)!.Value, 12);

        // The target stays on closes: a close at 109 sells nothing whatever the session's high, and one at 110
        // sells there, 2.5.
        Assert.Equal(2.5, SweepWalk.TouchedStop([100, 100, 105], [99, 99, 104], [100, 109, 110], 0, 100, 96, 110, 10, out sessions)!.Value, 12);
        Assert.Equal(2, sessions);

        // A low at the stop touches it; a low a cent above does not, and the cap's close ends the trade.
        Assert.Equal(-1, SweepWalk.TouchedStop([100, 100], [99, 96], [100, 99], 0, 100, 96, 110, 10, out _)!.Value, 12);
        Assert.Equal(0.25, SweepWalk.TouchedStop([100, 100], [99, 96.01], [100, 101], 0, 100, 96, 110, 1, out sessions)!.Value, 12);
        Assert.Equal(1, sessions);

        // A session holding no open is read from its low alone, and a history that ends first says nothing.
        Assert.Equal(-1, SweepWalk.TouchedStop([100, 0], [99, 95], [100, 97], 0, 100, 96, 110, 10, out _)!.Value, 12);
        Assert.Null(SweepWalk.TouchedStop([100, 101], [99, 100], [100, 101], 0, 100, 96, 110, 5, out sessions));
        Assert.Equal(5, sessions);
    }

    [Fact]
    public void TheTrailingStopIsThePlansStopUntilTheHighestCloseLessItsTrailPassesItAndIsNeverLowered()
    {
        // Bought at 100 with the plan's stop at 97 and a trail of 2 moves of 4: 104 less 8 is under 97, so the stop
        // holds; 110 raises it to 102; 106 leaves it there, never lowered; and 101 under it sells, 1 over a risk of 3.
        Assert.Equal(1.0 / 3, FamilyWalks.Trailing([100, 104, 110, 106, 101], 0, 100, 97, 8, SweepIdeas.Cap, out var sessions)!.Value, 12);
        Assert.Equal(4, sessions);

        // At 3 moves the trail is 12: 110 raises it only to 98, so 101 holds and the series ends with the trade open.
        Assert.Null(FamilyWalks.Trailing([100, 104, 110, 106, 101], 0, 100, 97, 12, SweepIdeas.Cap, out sessions));
        Assert.Equal(SweepIdeas.Cap, sessions);
    }

    [Fact]
    public void TheBestThreeAreTakenAfterTheOpenTradeRuleInTheListsOrder()
    {
        string[] tickers = ["A", "B", "C", "D", "E"];

        static IdeaListing Listing(int name, int session, double rewardToRisk, double strength = 0.5, int band = 0) =>
            new(name, name, 0, session, 0, rewardToRisk, strength, band, 1.5, new SweepPlanOutcomes());

        // On session 10, A holds the best reward to risk and is held through 12 by its open trade from session 8,
        // so it is passed over and not counted in the three. B and C tie on reward to risk and strength, and C's
        // band is the stronger; D and E tie on all three and D's ticker is the earlier. C, B and D are kept, in
        // that order, and E is not.
        var listings = new List<IdeaListing>
        {
            Listing(0, 8, 9),
            Listing(0, 10, 9), Listing(1, 10, 3, 0.7, 1), Listing(2, 10, 3, 0.7, 2), Listing(3, 10, 2), Listing(4, 10, 2),
        };

        var kept = SweepIdeas.Walk(listings, tickers, SweepIdeas.BestOf, listing => (1.0, 4, 0.25));

        Assert.Equal(
            ["A 8", "C 10", "B 10", "D 10"],
            kept.Select(trade => tickers[trade.Listing.Name] + " " + trade.Listing.Session.ToString(CultureInfo.InvariantCulture)));

        // With no cap a night every free listing is kept.
        Assert.Equal(5, SweepIdeas.Walk(listings, tickers, int.MaxValue, listing => (1.0, 4, 0.25)).Count);
    }

    [Fact]
    public void EachSwitchIsReadAsItStoodOnItsSessionAndAMissingReadingFailsIt()
    {
        // 210 sessions. Breadth climbs a hundredth a session to 1.3 on session 100 and falls a hundredth a session
        // after it; highs outnumber lows on even sessions alone; the index closes at 100 and on session 205 jumps
        // to 200; the VIX closes at 25, and from session 150 at 15.
        const int Count = 210;
        var breadth = Enumerable.Range(0, Count).Select(session => session <= 100 ? 0.3 + (0.01 * session) : 2.3 - (0.01 * session)).ToArray();
        var highs = Enumerable.Range(0, Count).Select(session => session % 2 == 0 ? 5 : 3).ToArray();
        var lows = Enumerable.Repeat(4, Count).ToArray();
        var index = Enumerable.Range(0, Count).Select(session => session >= 205 ? 200.0 : 100.0).ToArray();
        var vix = Enumerable.Range(0, Count).Select(session => session >= 150 ? 15.0 : 25.0).ToArray();
        var passes = SweepIdeas.Switches(breadth, highs, lows, index, vix);

        bool On(MarketSwitch one, int session) => passes[(int)one][session];

        // Breadth against ten sessions before: rising on 100, 1.3 against 1.2, falling on 110, 1.2 against 1.3,
        // and unread before session 10.
        Assert.True(On(MarketSwitch.BreadthRising, 100));
        Assert.False(On(MarketSwitch.BreadthRising, 110));
        Assert.False(On(MarketSwitch.BreadthRising, 9));

        Assert.True(On(MarketSwitch.HighsOverLows, 4));
        Assert.False(On(MarketSwitch.HighsOverLows, 5));

        // The index at its average is not above it; on session 205 the jump puts it above both, read on that
        // session's own close and not the one before it; the slower average is unread before session 199.
        Assert.False(On(MarketSwitch.IndexOverFifty, 204));
        Assert.True(On(MarketSwitch.IndexOverFifty, 205));
        Assert.True(On(MarketSwitch.IndexOverTwoHundred, 205));
        Assert.False(On(MarketSwitch.IndexOverTwoHundred, 198));

        Assert.False(On(MarketSwitch.VixUnderTwenty, 149));
        Assert.True(On(MarketSwitch.VixUnderTwenty, 150));
        Assert.True(On(MarketSwitch.VixFalling, 150));
        Assert.True(On(MarketSwitch.VixFalling, 159));
        Assert.False(On(MarketSwitch.VixFalling, 160));

        // A session the VIX series misses fails both of its switches that night and the falling switch ten
        // sessions on, as a gate fails on an absent value; the index's averages are unread for as long as the gap
        // sits in their window.
        vix[170] = double.NaN;
        index[205] = double.NaN;
        passes = SweepIdeas.Switches(breadth, highs, lows, index, vix);

        Assert.False(On(MarketSwitch.VixUnderTwenty, 170));
        Assert.False(On(MarketSwitch.VixFalling, 170));
        Assert.False(On(MarketSwitch.VixFalling, 180));
        Assert.True(On(MarketSwitch.VixUnderTwenty, 171));
        Assert.False(On(MarketSwitch.IndexOverFifty, 206));
        Assert.True(double.IsNaN(SweepIdeas.Average(index, SweepIdeas.FastAverage)[209]));
        Assert.Equal(100, SweepIdeas.Average(index, SweepIdeas.FastAverage)[204], 12);
    }

    [Fact]
    public void BreadthNewHighsAndLowsAndAMarketSeriesAreReadOverTheMembersAndTheCalendar()
    {
        // Three members over 230 weekdays: A rising a tenth a session closes above its 200-day average from the
        // session it holds one, B falling closes below it, and C flat closes at it, which is not above. So a third
        // stand above from session 199 and the share is unread before it.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 230);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var series = new[]
        {
            SweepColumns.Series(Constructed("A", calendar, [.. Enumerable.Range(0, 230).Select(day => 100m + (0.1m * day))], 1000), sessionAt),
            SweepColumns.Series(Constructed("B", calendar, [.. Enumerable.Range(0, 230).Select(day => 100m - (0.1m * day))], 1000), sessionAt),
            SweepColumns.Series(Constructed("C", calendar, [.. Enumerable.Repeat(100m, 230)], 1000), sessionAt),
        };
        var members = SweepBenchmark.On(series, calendar.Length);
        var breadth = SweepIdeas.BreadthAboveTheSlowAverage(series, members, calendar.Length);

        Assert.True(double.IsNaN(breadth[198]));
        Assert.Equal(1.0 / 3, breadth[199], 12);
        Assert.Equal(1.0 / 3, breadth[229], 12);

        // A value is a new high where it is the highest of the 252 ending on it, read only from the 252nd: a rising
        // series makes one every session from there, and a dip makes a new low that session and no high.
        var values = Enumerable.Range(0, 260).Select(at => (decimal)at).ToArray();

        values[255] = -1;

        var newHighs = SweepIdeas.Extremes(values, highest: true);
        var newLows = SweepIdeas.Extremes(values, highest: false);

        Assert.False(newHighs[250]);
        Assert.True(newHighs[251]);
        Assert.False(newHighs[255]);
        Assert.True(newHighs[256]);
        Assert.True(newLows[255]);
        Assert.False(newLows[254]);

        // A market series on the calendar: a day the calendar does not hold is passed over and a session the
        // series misses is none.
        var market = new SweepMarketSeries("VIX", [(calendar[0], 20.5), (new DateOnly(2018, 1, 6), 99), (calendar[2], 19)], "history-pull-market");
        var closes = SweepIdeas.OnCalendar(market, calendar);

        Assert.Equal(20.5, closes[0]);
        Assert.True(double.IsNaN(closes[1]));
        Assert.Equal(19, closes[2]);
        Assert.DoesNotContain(99.0, closes);
        Assert.All(SweepIdeas.OnCalendar(null, calendar), close => Assert.True(double.IsNaN(close)));
    }

    // Figures over constructed edges: each year's edge and total given, every other figure enough to pass.
    static IdeaFigures FiguresOf(string key, double?[] yearEdge, double[]? yearTotal = null, int trades = 2000, double nights = 0.5, double result = 0.2) =>
        new(key, trades, trades, (int)(nights * 1000), 1000, 0.1, result, 0.01, new int[8], yearEdge, yearTotal ?? new double[8], yearEdge[5..].Average(), (yearTotal ?? new double[8])[5..].Sum(), 0.1, (yearTotal ?? new double[8]).Sum(), 0.4);

    [Fact]
    public void TheYearlyTestPassesAtSixOfEightWithTwoOfTheLastThreeAndNotOneShortOfEither()
    {
        double?[] flat = [0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1];
        var against = FiguresOf("base", flat);

        // Better in 2019 to 2022 and in 2024 and 2025: six, two of them recent, and the last three together are
        // higher.
        var six = FiguresOf("six", [0.2, 0.2, 0.2, 0.2, 0.0, 0.2, 0.2, 0.1]);

        Assert.Equal((6, 2), (SweepIdeas.Test(six, against, false).YearsBetter, SweepIdeas.Test(six, against, false).RecentYearsBetter));

        // The trimmed edges tie, so it is not higher without the largest: move it a hair above.
        Assert.True(SweepIdeas.Test(six with { EdgeWithoutLargest = 0.11 }, against, false).Passes);

        // Six better with one of the last three, and five better with all three: each fails the years alone.
        var oneRecent = SweepIdeas.Test(FiguresOf("one recent", [0.2, 0.2, 0.2, 0.2, 0.2, 0.2, 0.0, 0.0]) with { EdgeWithoutLargest = 0.11, RecentEdge = 0.2 }, against, false);
        var five = SweepIdeas.Test(FiguresOf("five", [0.2, 0.2, 0.0, 0.0, 0.0, 0.2, 0.2, 0.2]) with { EdgeWithoutLargest = 0.11 }, against, false);

        Assert.Equal((6, 1, false), (oneRecent.YearsBetter, oneRecent.RecentYearsBetter, oneRecent.Passes));
        Assert.Equal((5, 3, false), (five.YearsBetter, five.RecentYearsBetter, five.Passes));

        // Each floor at its edge: 1,000 trades and 40% of the nights pass, one short of either fails.
        Assert.True(SweepIdeas.Test(six with { EdgeWithoutLargest = 0.11, Trades = 1000, Nights = 400 }, against, false).Passes);
        Assert.False(SweepIdeas.Test(six with { EdgeWithoutLargest = 0.11, Trades = 999 }, against, false).Passes);
        Assert.False(SweepIdeas.Test(six with { EdgeWithoutLargest = 0.11, Nights = 399 }, against, false).Passes);

        // Luck: of the 256 ways the years can fall, 34 pass.
        Assert.Equal(34, SweepIdeas.LuckPatterns());
    }

    [Fact]
    public void AMarketSwitchIsJudgedOnTheYearsTotalANightOffCountingNothing()
    {
        static IdeaTrade Trade(int year, double result, double benchmark) =>
            new(new IdeaListing(0, 0, 0, year * 10, year, 2, 0.5, 0, 1.5, new SweepPlanOutcomes()), result, benchmark);

        // The base trades in 2019 on a good night and a bad one, 1 and -1, and in 2026; a switch keeps the good
        // night alone. Its 2019 total is 1 against nought, better, though its 2019 edge, 1 less the market's 0.8,
        // is under the base's, the average of 0.2 and 0.5, since on the bad night the market fell further than
        // the stock did.
        var baseFigures = SweepIdeas.Figures("base", [Trade(0, 1, 0.8), Trade(0, -1, -1.5), Trade(7, 0.5, 0)], 100);
        var switchFigures = SweepIdeas.Figures("switch", [Trade(0, 1, 0.8), Trade(7, 0.5, 0)], 100);

        // A year with no trade totals nought, which a night off adds to nothing.
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0.5], baseFigures.YearTotal);
        Assert.Equal(1, switchFigures.YearTotal[0], 12);
        Assert.Equal(0.35, baseFigures.YearEdge[0]!.Value, 12);
        Assert.Equal(0.2, switchFigures.YearEdge[0]!.Value, 12);

        var onTotals = SweepIdeas.Test(switchFigures, baseFigures, onTotals: true);
        var onEdges = SweepIdeas.Test(switchFigures, baseFigures, onTotals: false);

        Assert.Equal(1, onTotals.YearsBetter);
        Assert.Equal(0, onEdges.YearsBetter);

        // On totals no floor of nights is read, and the plain result a trade must be higher: 0.75 against 0.1667.
        Assert.True(onTotals.EnoughNights);
        Assert.True(onTotals.ResultHigher);
        Assert.False(SweepIdeas.Test(baseFigures, switchFigures, onTotals: true).ResultHigher);
    }

    // A proposal over figures worked out from each rule: each year's edge the base's plus what each idea it holds
    // adds, P adding 0.05 and Q 0.03, and the two together leaving too few trades. Today's rule is the base less
    // the given amount.
    static IdeasProposal ProposeOver(double pAdds, double qAdds, double todayLess)
    {
        var p = new Idea("P", "keeps the best three", "none", IdeaKind.Selection, rule => rule with { PerNight = 3 });
        var q = new Idea("Q", "the touched stop", "none", IdeaKind.Exit, rule => rule with { Exit = IdeaExit.TouchedStop });

        IdeaFigures Evaluate(string key, IdeaRule rule)
        {
            var hasP = rule.PerNight == 3;
            var hasQ = rule.Exit == IdeaExit.TouchedStop;
            var today = rule.Setting.RewardToRisk != SweepIdeas.BaseRule.Setting.RewardToRisk;
            var edge = 0.1 + (hasP ? pAdds : 0) + (hasQ ? qAdds : 0) - (today ? todayLess : 0);
            var years = Enumerable.Repeat<double?>(edge, 8).ToArray();

            return FiguresOf(key, years, trades: hasP && hasQ ? 500 : 2000) with { Edge = edge, EdgeWithoutLargest = edge, RecentEdge = edge };
        }

        return SweepIdeas.Propose([p, q], Evaluate);
    }

    [Fact]
    public void TheIdeasThatPassAreCombinedAndWhereTheWholeFailsAddedInOrderOfTheirGain()
    {
        // P and Q each pass alone; together they leave 500 trades and fail. P gains more, so it is added first and
        // kept, and Q is not, since the whole with it fails.
        var proposal = ProposeOver(0.05, 0.03, 0.05);

        Assert.True(proposal.Ideas.All(reading => reading.Test.Passes));
        Assert.Equal(["P"], proposal.Kept);
        Assert.Equal(3, proposal.Start.PerNight);
        Assert.Equal(IdeaExit.Base, proposal.Start.Exit);
        Assert.False(proposal.BaseIsVariant);

        // Q, outside the starting point and higher than the base, is a variant one change from it, beside the
        // market floor at 50% and the depth from 1.5.
        Assert.Equal(["Q", "market 50%", "depth 1.5"], proposal.Variants.Select(variant => variant.Key));

        // Where Q gains more, it is the one kept.
        Assert.Equal(["Q"], ProposeOver(0.03, 0.05, 0.05).Kept);
    }

    [Fact]
    public void AMarketSwitchIsProposedOnItsTotalWhereItsEdgeFalls()
    {
        // A switch that keeps the list off the bad nights: each year's total rises from 1 to 1.5 and the plain
        // result from 0.2 to 0.3, while its edge falls from 0.1 to 0.05, since the market it is read against
        // fell on the nights it kept off. Judged on the total it passes and is the starting point.
        var m = new Idea("M", "keeps off the bad nights", "none", IdeaKind.Market, rule => rule with { Switches = [MarketSwitch.BreadthRising] });

        // Today's rule trails the base on every figure, so the base stands.
        IdeaFigures Evaluate(string key, IdeaRule rule)
        {
            var today = rule.Setting.RewardToRisk != SweepIdeas.BaseRule.Setting.RewardToRisk;
            var edge = today ? 0 : rule.HasSwitch ? 0.05 : 0.1;
            var total = today ? 0.5 : rule.HasSwitch ? 1.5 : 1;

            return FiguresOf(key, [.. Enumerable.Repeat<double?>(edge, 8)], [.. Enumerable.Repeat(total, 8)], result: today ? 0.1 : rule.HasSwitch ? 0.3 : 0.2)
                with { Edge = edge, EdgeWithoutLargest = edge, RecentEdge = edge };
        }

        var proposal = SweepIdeas.Propose([m], Evaluate);
        var reading = Assert.Single(proposal.Ideas);

        Assert.True(reading.Test.OnTotals);
        Assert.True(reading.Test.Passes);
        Assert.False(SweepIdeas.Test(reading.Figures, proposal.Base, onTotals: false).Passes);
        Assert.Equal(["M"], proposal.Kept);
        Assert.True(proposal.Start.HasSwitch);
        Assert.True(proposal.StartAgainstBase.OnTotals);
    }

    [Fact]
    public void WhereNoIdeaPassesTheStartingPointIsTheBaseAndWhereTheBaseFailsItIsTodaysRule()
    {
        // Neither idea moves the base: no idea passes, so the starting point is the base, holding no idea.
        var none = ProposeOver(0, 0, 0.05);

        Assert.DoesNotContain(none.Ideas, reading => reading.Test.Passes);
        Assert.Empty(none.Kept);
        Assert.Equal(SweepIdeas.BaseRule, none.Start);
        Assert.Equal(none.Base.Edge, none.StartFigures.Edge);
        Assert.Contains("no idea passed", SweepIdeasReport.StartInWords(none), StringComparison.Ordinal);

        // Today's rule above the base: the base fails against it, so the starting point is today's rule and the
        // base stands first among the variants.
        var behind = ProposeOver(0.05, 0.03, -0.05);

        Assert.True(behind.BaseIsVariant);
        Assert.Empty(behind.Kept);
        Assert.Equal(SweepIdeas.TodaysRule, behind.Start);
        Assert.Equal("the base", behind.Variants[0].Key);
    }

    [Fact]
    public void TheBasesPicksAreReadAtEachRulesSettingExitAndSwitch()
    {
        // One member over 30 weekdays and one candidate on session 20, entered on the live design's own plan with
        // its reward to risk at 1.75 and its stop 1.5 moves beneath: it passes today's rule, whose floor is 1.5,
        // and not the base's, whose floor is 2. At a reward to risk of 2.5 it passes both, and the base's own
        // exit, the hold of 10 and the hold of 20 each read their own outcome off the candidate.
        var calendar = Weekdays(new DateOnly(2026, 1, 5), 30);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var series = new[] { SweepColumns.Series(Constructed("A", calendar, [.. Enumerable.Repeat(100m, 30)], 1000), sessionAt) };
        var members = SweepBenchmark.On(series, calendar.Length);
        var allOn = Enumerable.Range(0, 6).Select(_ => Enumerable.Repeat(true, calendar.Length).ToArray()).ToArray();

        SweepCandidate Candidate(double rewardToRisk, double stopMoves)
        {
            var candidate = new SweepCandidate { Name = 0, Session = 20, Year = 7, Breadth = 0.6, Uptrend = 1, Earnings = -1 };
            var plan = new SweepPlanOutcomes { RewardToRisk = rewardToRisk, StopMoves = stopMoves };

            candidate.Strength[(int)SweepDesign.Live.Strength] = 0.9;
            candidate.Depth[SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh)] = 2;
            candidate.DryUp[SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh)] = 0.5;
            candidate.Band[(int)SweepDesign.Live.Support] = 3;
            candidate.Age[SweepCandidate.AgeAt(SweepDesign.Live.Trigger, SweepDesign.Live.Support)] = 0;

            foreach (var (hold, multiple) in new[] { (10, 0.5f), (20, 0.7f), (63, 0.9f) })
            {
                var exit = SweepAxes.ExitIndex(hold, breakEven: false);

                plan.Code[exit] = SweepPlanOutcomes.Win;
                plan.Multiple[exit] = multiple;
                plan.Benchmark[exit] = 0.1f;
                plan.Ends[exit] = 3;
            }

            candidate.Plans[SweepCandidate.PlanAt(SweepDesign.Live.Plan, SweepDesign.Live.Support)] = plan;

            return candidate;
        }

        double? ResultOf(IdeaReplay replay, IdeaRule rule) => replay.Trades(rule).SingleOrDefault().Result;

        var between = new IdeaReplay(series, [Candidate(1.75, 1.5)], members, allOn, 10);

        Assert.Equal(0.9, ResultOf(between, SweepIdeas.TodaysRule)!.Value, 6);
        Assert.Empty(between.Trades(SweepIdeas.BaseRule));

        var both = new IdeaReplay(series, [Candidate(2.5, 1.5)], members, allOn, 10);

        Assert.Equal(0.9, ResultOf(both, SweepIdeas.BaseRule)!.Value, 6);
        Assert.Equal(0.5, ResultOf(both, SweepIdeas.BaseRule with { Exit = IdeaExit.HoldTen })!.Value, 6);
        Assert.Equal(0.7, ResultOf(both, SweepIdeas.BaseRule with { Exit = IdeaExit.HoldTwenty })!.Value, 6);

        // A stop 0.75 moves beneath passes the base's stop setting of 0.5 to 4 and not idea e's of 1 to 4.
        var near = new IdeaReplay(series, [Candidate(2.5, 0.75)], members, allOn, 10);
        var e = SweepIdeas.All.Single(idea => idea.Key == "e");

        Assert.Single(near.Trades(SweepIdeas.BaseRule));
        Assert.Empty(near.Trades(e.Apply(SweepIdeas.BaseRule)));

        // A switch failing on the candidate's session keeps it off the list.
        allOn[(int)MarketSwitch.VixUnderTwenty][20] = false;

        Assert.Empty(new IdeaReplay(series, [Candidate(2.5, 1.5)], members, allOn, 10).Trades(SweepIdeas.All.Single(idea => idea.Key == "a5").Apply(SweepIdeas.BaseRule)));
        Assert.Single(new IdeaReplay(series, [Candidate(2.5, 1.5)], members, allOn, 10).Trades(SweepIdeas.BaseRule));
    }

    [Fact]
    public void TheNewExitsBenchmarkIsTheSamePlanOnEveryMemberThatNightWorkedByHand()
    {
        // Three members over 100 weekdays, every bar a point either side of its close and opening at it, so each
        // typical move is 2. After session 20, X rises 2 a session to its cap and Y opens and closes 5 under; Z
        // joined on session 25, so it holds no bar that night and is not read.
        var calendar = Weekdays(new DateOnly(2026, 1, 5), 100);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        decimal[] x = [.. Enumerable.Range(0, 100).Select(day => day <= 20 ? 100m : 100m + (2m * Math.Min(day - 20, SweepIdeas.Cap)))];
        decimal[] y = [.. Enumerable.Range(0, 100).Select(day => day <= 20 ? 50m : 45m)];
        var series = new[]
        {
            SweepColumns.Series(Constructed("X", calendar, x, 1000), sessionAt),
            SweepColumns.Series(Constructed("Y", calendar, y, 1000), sessionAt),
            SweepColumns.Series(Constructed("Z", [.. calendar.Skip(25)], [.. Enumerable.Repeat(100m, 75)], 1000), sessionAt),
        };
        var members = SweepBenchmark.On(series, calendar.Length);
        var replay = new IdeaReplay(series, [], members, [], 75);

        Assert.Equal(2, series[0].Atr[20], 9);

        // The plan's stop 2 moves beneath, a risk of 4, and its target at 2 times the risk. Touched: X closes at
        // the target, 108, on its fourth session, 2 risks; Y opens at 45 under its stop of 46, -1.25.
        Assert.Equal((2 - 1.25) / 2, replay.Benchmark(20, 2, 2, IdeaExit.TouchedStop, 0), 12);

        // Trailing at 2 moves, 4: X never closes under its trail and ends at its cap 126 above, 31.5 risks; Y
        // closes under its stop on the first session, -1.25.
        Assert.Equal((31.5 - 1.25) / 2, replay.Benchmark(20, 2, 2, IdeaExit.TrailTwo, SweepIdeas.TrailTwoMoves), 12);
    }

    [Fact]
    public async Task TheReportStatesItsTriesAgainstLuckAndTheMarketSeriesAsTheStoreHoldsThem()
    {
        using var store = new TemporaryStore().Migrated();
        var days = Weekdays(new DateOnly(2026, 3, 2), 4);

        foreach (var (series, day) in new[] { ("GSPC", days[0]), ("GSPC", days[1]), ("GSPC", days[2]), ("VIX", days[1]), ("VIX", days[3]) })
        {
            store.Execute($"INSERT INTO pulled_market_bar (series, session_date, open, high, low, close, pull) VALUES ('{series}', '{Day(day)}', '20', '21', '19', '20', 'history-pull-market-1');");
        }

        var market = await new SweepHistory(store.DatabaseFile).MarketAsync(days[2]);

        SweepRelease(store);

        // Read through the history's end, the VIX's newer session left out.
        Assert.Equal([("GSPC", 3), ("VIX", 1)], market.Select(one => (one.Series, one.Closes.Count)));
        Assert.Equal((days[0], days[2], "history-pull-market-1"), (market[0].First!.Value, market[0].Last!.Value, market[0].Pull));

        var proposal = ProposeOver(0.05, 0.03, 0.05);
        var run = new SweepIdeasRun(new DateOnly(2019, 1, 2), days[2], 3, 100, 10, 5, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var report = SweepIdeasReport.Build(run, proposal, market);
        var luck = Regex.Match(report, "data-tries=\"(?<tries>\\d+)\" data-passes=\"(?<passes>\\d+)\">(?<words>[^<]+)<");

        Assert.True(luck.Success, "The report states no tries.");
        Assert.Equal(("2", "2"), (luck.Groups["tries"].Value, luck.Groups["passes"].Value));
        Assert.Contains("in 34 of the 256 ways the years can fall, 13%, so luck alone passes about 0.3 of 2", luck.Groups["words"].Value, StringComparison.Ordinal);

        Assert.Contains("data-series=\"GSPC\" data-sessions=\"3\">GSPC, 3 sessions from 2026-03-02 to 2026-03-04, by the pull history-pull-market-1", report, StringComparison.Ordinal);
        Assert.Contains("2 weighted calls in all", report, StringComparison.Ordinal);

        // A store with no market series says so, and a series the store holds none of leaves the switches reading
        // it out of the run, named in the report.
        Assert.Contains("The store holds no market series", SweepIdeasReport.Build(run, proposal, []), StringComparison.Ordinal);
        Assert.Equal(["a5", "a6"], SweepIdeas.LeftOut([market[0]]));
        Assert.Equal(["a3", "a4", "a5", "a6"], SweepIdeas.LeftOut([]));
        Assert.Empty(SweepIdeas.LeftOut(market));
        Assert.Contains("Left out, for a series the store does not hold: a5, a6.", SweepIdeasReport.Build(run with { LeftOut = ["a5", "a6"] }, proposal, [market[0]]), StringComparison.Ordinal);

        // Each rule's row states the share of its trades whose stop sat under one typical move beside its edge.
        Assert.Matches("data-edge=\"[^\"]+\" data-close-stops=\"0.400\"><td>The starting point</td>", report);
    }

    [Fact]
    public void TheShareOfStopsUnderATypicalMoveIsWorkedByHand()
    {
        static IdeaTrade Trade(double stopMoves, double? result) =>
            new(new IdeaListing(0, 0, 0, 1, 0, 2, 0.5, 0, stopMoves, new SweepPlanOutcomes()), result, 0);

        // Four trades with a result, one with its stop at 0.5 moves and one at 0.99, and one at exactly 1, which is
        // not under; a trade with no result is in no share.
        var figures = SweepIdeas.Figures("shares", [Trade(0.5, 1), Trade(0.99, 1), Trade(1, 1), Trade(2, 1), Trade(0.5, null)], 100);

        Assert.Equal(0.5, figures.CloseStops!.Value, 12);
    }
}
