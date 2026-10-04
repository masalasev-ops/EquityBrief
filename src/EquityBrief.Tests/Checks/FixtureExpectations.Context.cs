using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 14.4: the context checks over the history. A drift print's revenue growth worked by hand over
// constructed quarters, read as first filed; the drift's filter and order over a constructed night; the replay reading
// each listing's print and counting the trades whose quarter was first filed after their buy; the pullback's RSI
// fall worked by hand and a night's first three kept by it; the drift's test held to no floor of nights; the sweep
// history reading each name's revenue through its filer; and the report's tries against what luck passes.
// see: A drift print's revenue growth is its quarter's revenue as first filed against the same quarter a year before
// see: The drift's context ideas are judged by the year tests, the test without the five largest and a thousand trades with no floor of nights
// see: The pullback's RSI-fall order keeps the night's first three by how far its RSI fell from its high session
// see: The context checks are read on the frozen families one at a time, and nothing they show is frozen or registered
public partial class FixtureExpectations
{
    // The rows the context checks add that this check reaches: section 17's three and section 18's two.
    internal static readonly string[] ContextClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "A print's revenue quarter"),
        CheckReach.Key(Scope.LimitsTable, "Context ideas' test"),
        CheckReach.Key(Scope.LimitsTable, "RSI fall order"),
        CheckReach.Key(Scope.FailureTable, "A drift print whose filer states no quarter the reading finds"),
        CheckReach.Key(Scope.FailureTable, "A pullback whose high session or night holds no RSI"),
    ];

    static DateOnly On(int year, int month, int day) => new(year, month, day);

    static FiledRevenue Filed(DateOnly start, DateOnly end, decimal value, DateOnly filed, string accession, string concept = "Revenues") =>
        new(concept, start, end, value, filed, "10-Q", accession);

    // One filer's facts over two years: its calendar quarters as each 10-Q files them, its years as each 10-K files
    // them beside the nine months before, so each fiscal fourth quarter is a year less its nine months, and its
    // quarter to 2026-06-30 filed at 125 on 2026-07-30 and stated again at 999 by a later filing.
    static readonly FiledRevenue[] TwoYears =
    [
        Filed(On(2024, 1, 1), On(2024, 9, 30), 300m, On(2024, 10, 30), "n24"),
        Filed(On(2024, 1, 1), On(2024, 12, 31), 400m, On(2025, 2, 20), "k24"),
        Filed(On(2025, 4, 1), On(2025, 6, 30), 100m, On(2025, 7, 30), "q25b"),
        Filed(On(2025, 7, 1), On(2025, 9, 30), 110m, On(2025, 10, 30), "q25c"),
        Filed(On(2025, 1, 1), On(2025, 9, 30), 320m, On(2025, 10, 30), "n25"),
        Filed(On(2025, 1, 1), On(2025, 12, 31), 450m, On(2026, 2, 20), "k25"),
        Filed(On(2026, 1, 1), On(2026, 3, 31), 140m, On(2026, 4, 30), "q26a"),
        Filed(On(2026, 4, 1), On(2026, 6, 30), 125m, On(2026, 7, 30), "q26b"),
        Filed(On(2026, 4, 1), On(2026, 6, 30), 999m, On(2026, 10, 29), "later"),
    ];

    [Fact]
    public void ADriftPrintsRevenueGrowthIsItsQuarterAsFirstFiledAgainstTheSameQuarterAYearBefore()
    {
        var quarters = FirstFiledRevenue.Quarters(TwoYears);

        // A print the day before its quarter's 10-Q is filed reads that quarter, as first filed at 125 and not as the
        // later filing's 999, against the quarter to 2025-06-30 at 100: a growth of 0.25.
        var july = RevenueGrowth.Of(On(2026, 7, 29), quarters)!;

        Assert.Equal((On(2026, 6, 30), 125m, On(2026, 7, 30)), (july.Quarter.End, july.Quarter.Value, july.Quarter.Filed));
        Assert.Equal((On(2025, 6, 30), 100m), (july.YearBefore.End, july.YearBefore.Value));
        Assert.Equal(0.25, july.Growth, 12);

        // The span's edge: 100 days after the quarter's end it is still the print's quarter, and 101 days after none is.
        Assert.Equal(0.25, RevenueGrowth.Of(On(2026, 10, 8), quarters)!.Growth, 12);
        Assert.Null(RevenueGrowth.Of(On(2026, 10, 9), quarters));

        // A fiscal fourth quarter is the year less its nine months on both sides: 450 less 320 against 400 less 300,
        // a growth of 0.30.
        var february = RevenueGrowth.Of(On(2026, 2, 5), quarters)!;

        Assert.Equal((On(2025, 12, 31), 130m, true), (february.Quarter.End, february.Quarter.Value, february.Quarter.YearLessNineMonths));
        Assert.Equal(100m, february.YearBefore.Value);
        Assert.Equal(0.30, february.Growth, 12);

        // The quarter to 2025-09-30 has no year before among the facts, so a print on it reads none.
        Assert.Null(RevenueGrowth.Of(On(2025, 10, 28), quarters));

        // A 52 or 53-week year: a year before ending a day off a year earlier is read, one ending eight days off is not,
        // and a year before of nothing reads no growth.
        FiledRevenue[] weeks =
        [
            Filed(On(2025, 3, 30), On(2025, 6, 29), 80m, On(2025, 7, 25), "w25"),
            Filed(On(2026, 3, 29), On(2026, 6, 28), 100m, On(2026, 7, 24), "w26"),
        ];

        Assert.Equal(0.25, RevenueGrowth.Of(On(2026, 7, 28), FirstFiledRevenue.Quarters(weeks))!.Growth, 12);
        Assert.Null(RevenueGrowth.Of(On(2026, 7, 28), FirstFiledRevenue.Quarters([weeks[0] with { End = On(2025, 6, 20), Start = On(2025, 3, 21) }, weeks[1]])));
        Assert.Null(RevenueGrowth.Of(On(2026, 7, 28), FirstFiledRevenue.Quarters([weeks[0] with { Value = 0m }, weeks[1]])));
    }

    static FamilyListing Listing(int name, double order) =>
        new(name, 10, 5, order, 0, 100, 95, 110, double.NaN, 60, 2);

    static PrintRevenue Growing(double growth) =>
        new(new QuarterRevenue(On(2026, 4, 1), On(2026, 6, 30), 100m, On(2026, 7, 30), "Revenues", false), new QuarterRevenue(On(2025, 4, 1), On(2025, 6, 30), 100m, On(2025, 7, 30), "Revenues", false), growth);

    [Fact]
    public void TheDriftsFilterKeepsAGrowingPrintAndItsOrderKeepsANightsFirstThreeByTheGrowth()
    {
        // One night's five listings in the drift's own order, E1 the largest surprise: growths of 0.2, -0.1, none,
        // 0.05 and 0.3.
        FamilyListing[] night = [Listing(0, 5), Listing(1, 4), Listing(2, 3), Listing(3, 2), Listing(4, 1)];
        var growth = new Dictionary<int, double> { [0] = 0.2, [1] = -0.1, [3] = 0.05, [4] = 0.3 };
        PrintRevenue? RevenueOf(FamilyListing listing) => growth.TryGetValue(listing.Name, out var read) ? Growing(read) : null;
        string[] tickers = ["E1", "E2", "E3", "E4", "E5"];

        IReadOnlyList<string> Kept(IEnumerable<FamilyListing> listings, int perNight) =>
            [.. FamilySweep.Walk(listings, tickers, _ => 0, _ => (0.0, 1), _ => 0.0, perNight).Select(trade => tickers[trade.Listing.Name])];

        // The filter keeps the prints whose revenue grew, in the drift's own order, leaving off the fall and the none.
        Assert.Equal(["E1", "E4", "E5"], Kept(FamilyIdeaReplay.ByRevenue(night, RevenueUse.Grew, RevenueOf), FamilySweep.PerNight));

        // The order keeps the night's first three by the growth, and with room for all, the fall before the none.
        Assert.Equal(["E5", "E1", "E4"], Kept(FamilyIdeaReplay.ByRevenue(night, RevenueUse.Order, RevenueOf), 3));
        Assert.Equal(["E5", "E1", "E4", "E2", "E3"], Kept(FamilyIdeaReplay.ByRevenue(night, RevenueUse.Order, RevenueOf), FamilySweep.PerNight));

        // Read by no idea, the night is the drift's own order.
        Assert.Equal(["E1", "E2", "E3", "E4", "E5"], Kept(FamilyIdeaReplay.ByRevenue(night, RevenueUse.None, RevenueOf), FamilySweep.PerNight));
    }

    [Fact]
    public void TheReplayReadsEachListingsPrintAndCountsTheTradesWhoseQuarterWasFirstFiledAfterTheirBuy()
    {
        // Three members held at 100 over 40 weekdays from Monday 2026-07-20. A reports before the open on 2026-07-28,
        // session 6, its quarter to 2026-06-30 filed at 125 two days later against 100 a year before; B reports on
        // 2026-08-05, session 12, its quarter filed at 90 that same day against 100; C has no filer the pull found.
        var calendar = Weekdays(On(2026, 7, 20), 40);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        SweepName Reporting(string ticker, DateOnly? report) =>
            Constructed(ticker, calendar, [.. Enumerable.Repeat(100m, 40)], 1000) with { SurprisesFiled = report is { } on ? [new SweepSurprise(on, false, 10)] : [] };
        var series = new[]
        {
            SweepColumns.Series(Reporting("A", On(2026, 7, 28)), sessionAt),
            SweepColumns.Series(Reporting("B", On(2026, 8, 5)), sessionAt),
            SweepColumns.Series(Reporting("C", On(2026, 8, 5)), sessionAt),
        };
        var revenue = new Dictionary<string, IReadOnlyList<FiledRevenue>>(StringComparer.Ordinal)
        {
            ["A"] = [Filed(On(2025, 4, 1), On(2025, 6, 30), 100m, On(2025, 7, 30), "a25"), Filed(On(2026, 4, 1), On(2026, 6, 30), 125m, On(2026, 7, 30), "a26")],
            ["B"] = [Filed(On(2025, 4, 1), On(2025, 6, 30), 100m, On(2025, 8, 5), "b25"), Filed(On(2026, 4, 1), On(2026, 6, 30), 90m, On(2026, 8, 5), "b26")],
        };

        Assert.Equal((6, 12), (sessionAt[On(2026, 7, 28)], sessionAt[On(2026, 8, 5)]));

        // The rule lists A before its print and on it, and B and C on theirs; each trade is sold the session after.
        static FamilyListing Listed(int name, int session, double order) =>
            new(name, session, session, order, 0, 100, 96, 108, double.NaN, DriftRule.CapSessions, 2);

        FamilyListing[] listings = [Listed(0, 3, 1), Listed(0, 6, 1), Listed(1, 12, 2), Listed(2, 12, 1)];
        var adapter = new FamilySweepRunner.Adapter(DriftSweep.Grid, listings.Length, _ => listings, _ => (1.0, 1), _ => 0.25);
        var replay = new FamilyIdeaReplay(adapter, [0, 0, 0, 0], series, SweepBenchmark.On(series, calendar.Length), [], _ => 7, 40, calendar, revenue);

        // A listing before its name's print reads none, A's on its print 0.25 and B's -0.1, and C, with no filer, none.
        Assert.Null(replay.RevenueOf(listings[0]));
        Assert.Equal(0.25, replay.RevenueOf(listings[1])!.Growth, 12);
        Assert.Equal(-0.1, replay.RevenueOf(listings[2])!.Growth, 12);
        Assert.Null(replay.RevenueOf(listings[3]));
        Assert.Equal((4, 2), (replay.Listings, replay.ListingsWithRevenue));

        // The filter keeps A's trade on its print, whose quarter was first filed two sessions after the buy; the order
        // keeps all four, B's quarter filed on the session it was bought, which is not after it.
        Assert.Equal(1, replay.Evaluate("r1", ContextIdeas.Drift[0]).Trades);
        Assert.Equal(4, replay.Evaluate("r2", ContextIdeas.Drift[1]).Trades);
        Assert.Equal((1, 1), replay.RevenueRead["r1"]);
        Assert.Equal((2, 1), replay.RevenueRead["r2"]);

        // The rule as frozen reads no revenue.
        Assert.Equal(4, replay.Evaluate("as frozen", null).Trades);
        Assert.False(replay.RevenueRead.ContainsKey("as frozen"));
    }

    static IdeaListing Pullback(int name, double rewardToRisk, double rsiFall) =>
        new(name, name, 10, 5, 0, rewardToRisk, 0.5, 2, 1.5, null!, rsiFall);

    [Fact]
    public void ThePullbacksRsiFallIsWorkedByHandAndKeepsANightsFirstThree()
    {
        // The RSI on the high's session less the night's: from 70 to 45 is a fall of 25, from 60 to 45 one of 15, and
        // a session holding no RSI, or no high found, reads none.
        double[] rsi = [70, 65, 60, 50, 45];

        Assert.Equal(25, SweepIdeas.RsiFall(rsi, 4, 4), 12);
        Assert.Equal(15, SweepIdeas.RsiFall(rsi, 4, 2), 12);
        Assert.True(double.IsNaN(SweepIdeas.RsiFall([double.NaN, 65, 60], 2, 2)));
        Assert.True(double.IsNaN(SweepIdeas.RsiFall(rsi, 4, -1)));

        // One night's five names, the list's order by reward to risk P1 to P5, their falls 10, none, 25, 15 and 5: the
        // order keeps P3, P4 and P1, the name with no fall after every name with one, and the list's own order the first
        // three by reward to risk.
        IdeaListing[] night = [Pullback(0, 5, 10), Pullback(1, 4, double.NaN), Pullback(2, 3, 25), Pullback(3, 2, 15), Pullback(4, 1, 5)];
        string[] tickers = ["P1", "P2", "P3", "P4", "P5"];

        IReadOnlyList<string> Kept(int perNight, IdeaOrder order) =>
            [.. SweepIdeas.Walk(night, tickers, perNight, _ => (0.0, 1, 0.0), order).Select(trade => tickers[trade.Listing.Name])];

        Assert.Equal(["P3", "P4", "P1"], Kept(SweepIdeas.BestOf, IdeaOrder.RsiFall));
        Assert.Equal(["P3", "P4", "P1", "P5", "P2"], Kept(int.MaxValue, IdeaOrder.RsiFall));
        Assert.Equal(["P1", "P2", "P3"], Kept(SweepIdeas.BestOf, IdeaOrder.List));

        // The rule the run reads is the pullback's base, three a night, by the fall.
        Assert.Equal((SweepIdeas.BestOf, IdeaOrder.RsiFall), (ContextIdeas.PullbackOrder.PerNight, ContextIdeas.PullbackOrder.Order));
        Assert.Equal(SweepIdeas.BaseRule.Setting, ContextIdeas.PullbackOrder.Setting);
        Assert.NotEqual(ContextIdeas.PullbackOrder.Key, ContextIdeas.PullbackFirstThree.Key);
    }

    static IdeaFigures Yearly(double edge, int nights) =>
        new("idea", 1_200, 1_200, nights, 100, edge, 0.1, 0.01,
            [.. Enumerable.Repeat(150, 8)], [.. Enumerable.Repeat<double?>(edge, 8)], [.. Enumerable.Repeat(1.0, 8)],
            edge, 3.0, edge - 0.05, 2.0, 0.1);

    [Fact]
    public void ADriftIdeaShortOfTheFloorOfNightsPassesTheDriftsTestAndFailsThePullbacks()
    {
        // Better than the rule in every year, on 1,200 trades, and listing on a fifth of the nights.
        var idea = Yearly(0.2, 20);
        var rule = Yearly(0.1, 20);

        Assert.True(ContextIdeas.DriftTest(idea, rule).Passes);
        Assert.False(ContextIdeas.PullbackTest(idea, rule).Passes);
        Assert.False(ContextIdeas.PullbackTest(idea, rule).EnoughNights);

        // Listing on most nights, the pullback's test passes it too.
        Assert.True(ContextIdeas.PullbackTest(Yearly(0.2, 90), Yearly(0.1, 90)).Passes);

        // Every drift idea reads the revenue.
        Assert.All(ContextIdeas.Drift, one => Assert.NotEqual(RevenueUse.None, one.Revenue));
    }

    [Fact]
    public async Task TheSweepHistoryReadsEachNamesRevenueThroughItsFilerAndNothingFiledAfterItsEnd()
    {
        using var store = new TemporaryStore().Migrated();

        // Two classes of one filer and a name filing no CIK; one quarter filed by the history's end and one after it.
        store.Execute(
            "INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull) VALUES " +
            "('AAA', '0000000001', 'Energy', NULL, NULL, NULL, NULL, 'p'), ('AAB', '0000000001', 'Energy', NULL, NULL, NULL, NULL, 'p'), ('NOC', NULL, 'Energy', NULL, NULL, NULL, NULL, 'p');");
        store.Execute(
            "INSERT INTO pulled_revenue (cik, concept, period_start, period_end, accession, dollars, filed, form, pull) VALUES " +
            "('0000000001', 'Revenues', '2026-04-01', '2026-06-30', 'one', '125', '2026-07-30', '10-Q', 'p'), " +
            "('0000000001', 'Revenues', '2026-07-01', '2026-09-30', 'two', '130', '2026-10-29', '10-Q', 'p');");

        var read = await new SweepHistory(store.DatabaseFile).RevenueAsync(On(2026, 10, 2));

        Assert.Equal(["AAA", "AAB"], read.Keys.Order(StringComparer.Ordinal));
        Assert.All(read.Values, facts => Assert.Equal([(On(2026, 6, 30), 125m)], facts.Select(fact => (fact.End, fact.Value))));
    }

    [Fact]
    public void TheContextReportStatesEachFamilysTriesAgainstWhatLuckAlonePasses()
    {
        var passing = new IdeaTest(false, 8, 3, true, true, true, true, true);
        var failing = new IdeaTest(false, 2, 0, false, false, true, true, true);
        var figures = Yearly(0.2, 20);
        var read = new ContextIdeasRead(
            "window 3, reaction 0.5, volume 2, target 2.5",
            figures,
            1_000,
            900,
            [new ContextReading("r1", ContextIdeas.Drift[0].Rule, figures, passing, 400, 120), new ContextReading("r2", ContextIdeas.Drift[1].Rule, figures, failing, 300, 90)],
            figures,
            [new ContextReading(ContextIdeas.PullbackKey, ContextIdeas.PullbackRule, figures, failing)],
            figures);

        // Luck: two tries at 34 of the 256 patterns a try is about 0.27, and one about 0.13.
        Assert.Equal(34, SweepIdeas.LuckPatterns());
        Assert.Equal(
            "2 ideas were tried on the earnings drift as frozen and 1 passed, where luck alone passes about 0.27; 1 on the pullback's base and 0 passed, where luck alone passes about 0.13. Passed: r1.",
            ContextIdeasReport.InWords(read));

        var page = ContextIdeasReport.Build(new ContextIdeasRun(On(2019, 1, 2), On(2026, 10, 2), 692, 1_949, 670, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), read);

        Assert.Contains("data-listings=\"1000\" data-with-revenue=\"900\"", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"filed-after\" data-read=\"400\" data-after=\"120\">", page, StringComparison.Ordinal);
        Assert.Contains("<section class=\"idea\" data-idea=\"r1\" data-passes=\"yes\">", page, StringComparison.Ordinal);
        Assert.Contains($"<section class=\"idea\" data-idea=\"{ContextIdeas.PullbackKey}\" data-passes=\"no\">", page, StringComparison.Ordinal);
        Assert.Equal(1.0 * 2 * 34 / 256, ContextIdeasRead.Luck(2), 12);
        Assert.Equal("0.13", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", ContextIdeasRead.Luck(1)));
    }
}
