using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Worker.Facts;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: the four readings of a member's reported quarters and the state they give,
// worked by hand over constructed quarters on both sides of each cut point and exactly at it, and the
// fixture's four names read from their captures and off the rows the replay stored.
// see: Four readings of a member's reported quarters are worked out every night, and its state is read from sales and operating margin alone
public partial class FixtureExpectations
{
    static readonly DateOnly NewestQuarter = new(2026, 6, 30);

    // One constructed quarter, `back` quarters before the newest, every figure a reading reads given
    // unless the test says otherwise: sales ten per cent up, the margin two points wider, an actual equal
    // to its estimate, cash of 110 against profit of 100, four quarters' earnings of 4 and a close after
    // the report of 80.
    internal static ReportedQuarter Quarter(
        int back,
        decimal? growth = 0.10m,
        decimal? margin = 0.30m,
        decimal? earlier = 0.28m,
        decimal? actual = 1.00m,
        decimal? estimate = 1.00m,
        decimal? cash = 110m,
        decimal? income = 100m,
        decimal? trailing = 4m,
        decimal? closeAfter = 80m)
    {
        var end = new DateOnly(NewestQuarter.AddMonths(-3 * back).Year, NewestQuarter.AddMonths(-3 * back).Month, 1).AddMonths(1).AddDays(-1);

        return new ReportedQuarter(
            end, end.AddDays(30), end.AddDays(25), 1000m, margin * 1000m, income, cash,
            actual, estimate, trailing, growth, 0.05m, margin, earlier, closeAfter, end.AddDays(26));
    }

    internal static ReportedQuarter[] Quarters(int count) => [.. Enumerable.Range(0, count).Select(back => Quarter(back))];

    // Four quarters whose sales fell and whose margin narrowed against a year earlier in the two newest.
    internal static ReportedQuarter[] Falling() =>
        [Quarter(0, growth: -0.02m, margin: 0.20m, earlier: 0.25m), Quarter(1, growth: -0.01m, margin: 0.22m, earlier: 0.23m), Quarter(2), Quarter(3)];

    // A member's readings on a night, stored as the night's readings step stores them.
    internal static void StoreReadings(TemporaryStore store, string night, string ticker, Readings readings) =>
        store.Execute(
            "INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) VALUES " +
            $"('{ticker}', '{night}', '{readings.State}', " +
            (readings.ReadFrom is { } from ? $"'{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'" : "NULL") +
            $", NULL, NULL, '{readings.ToJson().Replace("'", "''", StringComparison.Ordinal)}');");

    [Fact]
    public void TheStateIsReadFromTheTwoNewestQuartersOfSalesAndOperatingMarginAlone()
    {
        Assert.Equal(2, QuarterReadings.TrajectoryQuarters);

        // Sales up and the margin wider than a year earlier in both of the two newest quarters.
        Assert.Equal(FundamentalState.Improving, QuarterReadings.Of(Quarters(4), 80m, null).State);

        // Sales down and the margin narrower in both.
        ReportedQuarter[] falling = [Quarter(0, growth: -0.02m, margin: 0.20m, earlier: 0.25m), Quarter(1, growth: -0.01m, margin: 0.22m, earlier: 0.23m), Quarter(2), Quarter(3)];

        Assert.Equal(FundamentalState.Deteriorating, QuarterReadings.Of(falling, 80m, null).State);

        // Any other combination is steady: one quarter narrower, sales at nought, a margin where it stood.
        Assert.Equal(FundamentalState.Steady, QuarterReadings.Of([Quarter(0, margin: 0.27m), Quarter(1), Quarter(2), Quarter(3)], 80m, null).State);
        Assert.Equal(FundamentalState.Steady, QuarterReadings.Of([Quarter(0, growth: 0m), Quarter(1), Quarter(2), Quarter(3)], 80m, null).State);
        Assert.Equal(FundamentalState.Steady, QuarterReadings.Of([Quarter(0, margin: 0.28m), Quarter(1), Quarter(2), Quarter(3)], 80m, null).State);
        Assert.Equal(FundamentalState.Steady, QuarterReadings.Of([Quarter(0, growth: -0.02m), Quarter(1, growth: -0.01m), Quarter(2), Quarter(3)], 80m, null).State);

        // One quarter, or a newest quarter lacking its year-earlier margin, cannot be read.
        Assert.Equal(FundamentalState.NotEnoughQuarters, QuarterReadings.Of([Quarter(0)], 80m, null).State);
        Assert.Equal(FundamentalState.NotEnoughQuarters, QuarterReadings.Of([Quarter(0, earlier: null), Quarter(1)], 80m, null).State);
        Assert.Equal(QuarterReadings.TooFew, QuarterReadings.Of([Quarter(0)], 80m, null).Trajectory.Absent);

        // No quarter at all.
        var none = QuarterReadings.Of([], null, null);

        Assert.Equal(FundamentalState.NoFundamentalsYet, none.State);
        Assert.Null(none.ReadFrom);

        // The state reads nothing else: improving beside earnings that ran ahead of cash, a record of
        // misses and a valuation it cannot read.
        ReportedQuarter[] ahead = [.. Enumerable.Range(0, 4).Select(back => Quarter(back, cash: 50m, actual: 0.50m))];
        var readings = QuarterReadings.Of(ahead, null, null);

        Assert.Equal(FundamentalState.Improving, readings.State);
        Assert.Equal(QuarterReadings.AheadOfCash, readings.Quality.Band);
        Assert.Equal(4, readings.Record.Missed);
        Assert.NotNull(readings.Valuation.Absent);

        // The run of quarters the margin stood on the newest's side, from the newest.
        Assert.Equal(4, readings.Trajectory.MarginRun);
        Assert.Equal(1, QuarterReadings.Of([Quarter(0), Quarter(1, margin: 0.27m), Quarter(2), Quarter(3)], 80m, null).Trajectory.MarginRun);
    }

    [Fact]
    public void TheRecordCountsTheEightNewestQuartersCarryingBothFiguresAndAQuarterWithNoActualIsNeverMet()
    {
        Assert.Equal((8, 4), (QuarterReadings.RecordQuarters, QuarterReadings.RecordMinimum));

        // Ten quarters carrying both figures: the eight newest are counted and the two oldest, both beats,
        // are not.
        ReportedQuarter[] ten = [.. Enumerable.Range(0, 10).Select(back => back >= 8 ? Quarter(back, actual: 1.50m) : Quarter(back))];
        var record = QuarterReadings.Record(ten);

        Assert.Equal((8, 0, 8, 0), (record.Quarters.Count, record.Beat, record.Met, record.Missed));

        // The newest quarter has an estimate and no actual: it has not reported, so it is left out
        // rather than read as met, and the eight counted reach one quarter further back.
        ten[0] = Quarter(0, actual: null);
        record = QuarterReadings.Record(ten);

        Assert.DoesNotContain(ten[0].PeriodEnd, record.Quarters);
        Assert.Equal((8, 1, 7, 0), (record.Quarters.Count, record.Beat, record.Met, record.Missed));

        // Four is the fewest stated and three read too few.
        Assert.Null(QuarterReadings.Record(Quarters(4)).Absent);
        Assert.Equal(QuarterReadings.TooFew, QuarterReadings.Record(Quarters(3)).Absent);
    }

    [Fact]
    public void AnEstimateIsMetWithinACentOrOnePercentOfItWhicheverIsLarger()
    {
        Assert.Equal((0.01m, 0.01m), (QuarterReadings.MetCents, QuarterReadings.MetShare));

        // Below a dollar the cent is the larger: a gap of exactly a cent meets and a cent more does not.
        Assert.Equal(0, QuarterReadings.Against(0.51m, 0.50m));
        Assert.Equal(1, QuarterReadings.Against(0.52m, 0.50m));
        Assert.Equal(0, QuarterReadings.Against(0.49m, 0.50m));
        Assert.Equal(-1, QuarterReadings.Against(0.48m, 0.50m));

        // Above a dollar the share is: exactly 1% of 3.00 is 0.03, which meets, and 0.04 does not.
        Assert.Equal(0, QuarterReadings.Against(2.97m, 3.00m));
        Assert.Equal(-1, QuarterReadings.Against(2.96m, 3.00m));
        Assert.Equal(0, QuarterReadings.Against(3.03m, 3.00m));
        Assert.Equal(1, QuarterReadings.Against(3.04m, 3.00m));

        // A negative estimate takes the share of its size.
        Assert.Equal(0, QuarterReadings.Against(-2.02m, -2.00m));
        Assert.Equal(-1, QuarterReadings.Against(-2.03m, -2.00m));
    }

    [Fact]
    public void EarningsQualityReadsItsBandsAtTheirCutPointsAndALossIsNotCompared()
    {
        Assert.Equal((4, 0.8m, 1.2m), (QuarterReadings.QualityQuarters, QuarterReadings.QualityLow, QuarterReadings.QualityHigh));

        QualityReading Over(decimal cash, decimal income) =>
            QuarterReadings.Quality([.. Enumerable.Range(0, 4).Select(back => Quarter(back, cash: cash / 4m, income: income / 4m))]);

        // Exactly at each cut point is in line, and a step outside is the band beyond.
        Assert.Equal((0.8m, QuarterReadings.InLine), (Over(80m, 100m).Ratio, Over(80m, 100m).Band));
        Assert.Equal(QuarterReadings.AheadOfCash, Over(79.99m, 100m).Band);
        Assert.Equal((1.2m, QuarterReadings.InLine), (Over(120m, 100m).Ratio, Over(120m, 100m).Band));
        Assert.Equal(QuarterReadings.BackedByCash, Over(120.01m, 100m).Band);

        // A year's net income at or below nought is a loss and is not compared.
        Assert.Equal(QuarterReadings.Loss, Over(50m, 0m).Absent);
        Assert.Equal(QuarterReadings.Loss, Over(50m, -40m).Absent);
        Assert.Null(Over(50m, -40m).Ratio);

        // A quarter lacking a figure, and three quarters, are not read.
        Assert.Equal(QuarterReadings.Incomplete, QuarterReadings.Quality([Quarter(0), Quarter(1, cash: null), Quarter(2), Quarter(3)]).Absent);
        Assert.Equal(QuarterReadings.TooFew, QuarterReadings.Quality(Quarters(3)).Absent);
    }

    [Fact]
    public void TheValuationPlacesTonightsMultipleInTheThirdsOfTheQuartersOwnRange()
    {
        Assert.Equal((12, 8), (QuarterFetch.Kept, QuarterReadings.ValuationMinimum));

        // Twelve quarters, each four quarters' earnings of 1, whose closes after their reports make
        // multiples of 10 to 20 and 22: the range is 10 to 22, twelve wide, so its thirds end at 14 and 18.
        decimal[] closes = [22m, 20m, 19m, 18m, 17m, 16m, 15m, 14m, 13m, 12m, 11m, 10m];
        ReportedQuarter[] twelve = [.. closes.Select((close, back) => Quarter(back, trailing: 1m, closeAfter: close))];

        ValuationReading At(decimal tonight) => QuarterReadings.Valuation(twelve, tonight);

        Assert.Equal((10m, 22m), (At(15m).Low, At(15m).High));
        Assert.Equal(QuarterReadings.CheapEnd, At(11m).Position);
        Assert.Equal(QuarterReadings.CheapEnd, At(13.99m).Position);
        Assert.Equal(QuarterReadings.Middle, At(14m).Position);
        Assert.Equal(QuarterReadings.Middle, At(18m).Position);
        Assert.Equal(QuarterReadings.ExpensiveEnd, At(18.01m).Position);
        Assert.Equal(QuarterReadings.ExpensiveEnd, At(25m).Position);
        Assert.Equal(15m, At(15m).Multiple);

        // Seven multiples read too few, whatever tonight's close.
        ReportedQuarter[] seven = [.. twelve.Select((quarter, back) => back < 7 ? quarter : quarter with { CloseAfter = null })];

        Assert.Equal(QuarterReadings.TooFew, QuarterReadings.Valuation(seven, 15m).Absent);

        // Earnings at or below nought give no multiple, and no close on the fetch's basis gives none.
        ReportedQuarter[] losing = [twelve[0] with { EpsTrailing = -0.5m }, .. twelve[1..]];

        Assert.Equal(QuarterReadings.NoEarnings, QuarterReadings.Valuation(losing, 15m).Absent);
        Assert.Equal(QuarterReadings.NoBasis, QuarterReadings.Valuation(twelve, null).Absent);
    }

    // What the fixture's four names read, from their captures alone.
    static async Task<QuarterFetch> FetchedFromTheCaptures(string ticker)
    {
        var night = new DateOnly(2026, 9, 8);
        var fetched = await RecordedFundamentalsFeed.FromFolder(Folder()).FundamentalsAsync(ticker);
        var closes = await RecordedHistoricalBarFeed.FromFolder(Folder()).BarsAsync(ticker, night.AddYears(-QuarterFetch.PriceYears), night);

        return QuarterFetch.From(fetched, closes);
    }

    static decimal Figure(JsonElement value) => decimal.Parse(value.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);

    static DateOnly Day(JsonElement value) => DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // One name's readings against the expectation worked outside this repository.
    static void AsWorkedByHand(JsonElement expected, QuarterFetch fetch, Readings readings)
    {
        Assert.Equal(QuarterFetch.Kept, fetch.Quarters.Count);
        Assert.Equal(Day(expected.GetProperty("newest")), fetch.Quarters.Max(quarter => quarter.PeriodEnd));
        Assert.Equal((Day(expected.GetProperty("basis")[0]), Figure(expected.GetProperty("basis")[1])), (fetch.BasisSession!.Value, fetch.BasisClose!.Value));

        var trajectory = expected.GetProperty("trajectory").EnumerateArray().ToArray();

        Assert.Equal(trajectory.Length, readings.Trajectory.Quarters.Count);

        for (var at = 0; at < trajectory.Length; at++)
        {
            var read = readings.Trajectory.Quarters[at];

            Assert.Equal(
                (Day(trajectory[at].GetProperty("quarter")), Figure(trajectory[at].GetProperty("salesGrowth")), Figure(trajectory[at].GetProperty("salesGrowthBefore")), Figure(trajectory[at].GetProperty("margin")), Figure(trajectory[at].GetProperty("marginYearEarlier"))),
                (read.Quarter, read.SalesGrowth, read.SalesGrowthBefore!.Value, read.Margin, read.MarginYearEarlier));
        }

        foreach (var row in expected.GetProperty("closesAfter").EnumerateArray())
        {
            var quarter = Assert.Single(fetch.Quarters, one => one.PeriodEnd == Day(row[0]));

            Assert.Equal((Day(row[1]), Figure(row[2])), (quarter.CloseAfterSession!.Value, quarter.CloseAfter!.Value));
        }

        foreach (var row in expected.GetProperty("trailing").EnumerateArray())
        {
            Assert.Equal(Figure(row[1]), Assert.Single(fetch.Quarters, one => one.PeriodEnd == Day(row[0])).EpsTrailing);
        }

        Assert.Equal(expected.GetProperty("state").GetString(), readings.State);
        Assert.Equal(expected.GetProperty("marginRun").GetInt32(), readings.Trajectory.MarginRun);

        var record = expected.GetProperty("record");

        Assert.Equal(
            (record.GetProperty("quarters").GetInt32(), record.GetProperty("beat").GetInt32(), record.GetProperty("met").GetInt32(), record.GetProperty("missed").GetInt32()),
            (readings.Record.Quarters.Count, readings.Record.Beat, readings.Record.Met, readings.Record.Missed));

        var quality = expected.GetProperty("quality");

        Assert.Equal((Figure(quality.GetProperty("ratio")), quality.GetProperty("band").GetString()), (readings.Quality.Ratio!.Value, readings.Quality.Band));

        var valuation = expected.GetProperty("valuation");

        Assert.Equal((valuation.GetProperty("multiples").GetInt32(), valuation.GetProperty("absent").GetString()), (readings.Valuation.Quarters.Count, readings.Valuation.Absent));
    }

    [Fact]
    public async Task TheFixturesFourNamesReadAsWorkedByHandFromTheirCaptures()
    {
        var expected = Expected("reported-quarters");
        var names = expected.GetProperty("names").EnumerateObject().ToArray();

        Assert.Equal(["AAPL", "KEYS", "MSFT", "NFLX"], names.Select(name => name.Name));

        foreach (var name in names)
        {
            var fetch = await FetchedFromTheCaptures(name.Name);

            AsWorkedByHand(name.Value, fetch, QuarterReadings.Of(fetch.Quarters, fetch.BasisClose, null));
        }

        // NFLX's record read without the tolerance: the cent-apart quarters its split left all read as
        // beats, six of the eight, which is why the tolerance is stated.
        var nflx = await FetchedFromTheCaptures("NFLX");
        var counted = nflx.Quarters.Where(quarter => quarter.EpsActual is not null && quarter.EpsEstimate is not null).Take(QuarterReadings.RecordQuarters).ToArray();

        Assert.Equal(6, counted.Count(quarter => quarter.EpsActual > quarter.EpsEstimate));
    }

    [Fact]
    public async Task TheReplayStoresEachNamesQuartersFromOneAskAndReadsNoMemberOnTheNightBeforeIt()
    {
        var expected = Expected("reported-quarters");
        var night = expected.GetProperty("night").GetString()!;
        var ask = expected.GetProperty("ask");

        using var store = await FixtureReplay.ReplayedAsync();

        // One ask a name, the fill's, each storing the twelve quarters the fetch keeps.
        Assert.Equal(
            ["AAPL", "KEYS", "MSFT", "NFLX"],
            Query(store, "SELECT ticker FROM quarter_ask ORDER BY ticker;"));
        Assert.All(
            Query(store, "SELECT session_date, reason, outcome, quarters, weighted, nights FROM quarter_ask;"),
            row => Assert.Equal($"{night}|{ask.GetProperty("reason").GetString()}|{ask.GetProperty("outcome").GetString()}|{ask.GetProperty("quarters").GetInt32()}|{ask.GetProperty("weighted").GetInt32()}|1", row));
        Assert.Equal(["48"], Query(store, "SELECT COUNT(*) FROM reported_quarter;"));

        // The night's readings ran before the ask, so every member reads no fundamentals yet on it.
        Assert.Equal(
            [.. new[] { "AAPL", "KEYS", "MSFT", "NFLX" }.Select(ticker => $"{ticker}|{expected.GetProperty("readingOnTheNight").GetString()}|null")],
            Query(store, $"SELECT ticker, state, read_from FROM fundamental_reading WHERE session_date = '{night}' ORDER BY ticker;"));

        // And the quarters stored, read back as a later night reads them, give each name's readings as
        // worked by hand.
        foreach (var name in expected.GetProperty("names").EnumerateObject())
        {
            var rows = Query(store, $@"
                SELECT period_end, filing_date, report_date, revenue, operating_income, net_income, operating_cash_flow,
                       eps_actual, eps_estimate, eps_trailing, sales_growth, sales_growth_before, operating_margin,
                       margin_year_earlier, close_after, close_after_session, basis_session, basis_close
                FROM reported_quarter WHERE ticker = '{name.Name}' ORDER BY period_end DESC;");

            decimal? Money(string cell) => cell == "null" ? null : decimal.Parse(cell, NumberStyles.Number, CultureInfo.InvariantCulture);
            DateOnly? Date(string cell) => cell == "null" ? null : DateOnly.ParseExact(cell, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            var cells = rows.Select(row => row.Split('|')).ToArray();
            ReportedQuarter[] quarters =
            [
                .. cells.Select(cell => new ReportedQuarter(
                    Date(cell[0])!.Value, Date(cell[1]), Date(cell[2]), Money(cell[3]), Money(cell[4]), Money(cell[5]), Money(cell[6]),
                    Money(cell[7]), Money(cell[8]), Money(cell[9]), Money(cell[10]), Money(cell[11]), Money(cell[12]), Money(cell[13]),
                    Money(cell[14]), Date(cell[15]))),
            ];

            var fetch = new QuarterFetch(quarters, Date(cells[0][16]), Money(cells[0][17]));

            AsWorkedByHand(name.Value, fetch, QuarterReadings.Of(quarters, fetch.BasisClose, null));
        }
    }

    [Fact]
    public void TheFactsFileCarriesTheReadingsOfAMemberHoldingThemAndNothingForOneWithout()
    {
        // A member holding quarters: its state, the quarter it was read from, the trajectory's figures and
        // the other readings, each as the reading holds it.
        var readings = QuarterReadings.Of(Quarters(4), null, null);
        var facts = FactsAssembler.ReadingFacts(readings).ToDictionary(fact => fact.Name, fact => fact.Value, StringComparer.Ordinal);

        Assert.Equal(FundamentalState.Improving, facts["business state"]);
        Assert.Equal("2026-06-30", facts["business state read from the quarter to"]);
        Assert.Equal("0.10", facts["sales growth on a year earlier"]);
        Assert.Equal("0.30", facts["operating margin"]);
        Assert.Equal("0.28", facts["operating margin a year earlier"]);
        Assert.Equal(("4", "0", "4", "0"), (facts["estimate quarters counted"], facts["estimates beaten"], facts["estimates met"], facts["estimates missed"]));
        Assert.Equal("1.1", facts["operating cash flow over net income"]);
        Assert.All(FactsAssembler.ReadingFacts(readings), fact => Assert.Equal(FactsAssembler.FromReadings, fact.Source));

        // A valuation it could not read adds no multiple rather than an empty one.
        Assert.DoesNotContain("earnings multiple tonight", facts.Keys);

        // A member holding none adds nothing at all.
        Assert.Empty(FactsAssembler.ReadingFacts(QuarterReadings.Of([], null, null)));
    }
}
