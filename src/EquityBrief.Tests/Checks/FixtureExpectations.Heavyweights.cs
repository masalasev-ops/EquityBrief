using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 14.3: the sector heavyweights. A rebalance's reading worked by hand over constructed
// members, the trend gate and the average's exit at their edges, the rebalance calendar over a month opening after
// a holiday and after a night not run, the night's value on its count's basis, and the book over four constructed
// nights: what it buys at the rank as it stood, what it carries, what it sells and why, a night run again and an
// earlier night.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
// see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it
// see: A sector's return is the mean of its members' own returns over the look-back
// see: A heavyweight leaving the index is sold at its last session's close as a member
// see: A heavyweight's result is the product of its daily close ratios since its buy, carried each night
// see: The night values a member from its newest fetch, tonight's close brought to the count's basis by the fetch's own close
public partial class FixtureExpectations
{
    // The rows the sector heavyweights add that this check reaches: section 17's four and section 18's four.
    internal static readonly string[] HeavyweightClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Heavyweights' size cut"),
        CheckReach.Key(Scope.LimitsTable, "Heavyweights' look-back"),
        CheckReach.Key(Scope.LimitsTable, "Heavyweights' leaders"),
        CheckReach.Key(Scope.LimitsTable, "Heavyweights' rebalance and exits"),
        CheckReach.Key(Scope.FailureTable, "A member whose newest fetch files no sector"),
        CheckReach.Key(Scope.FailureTable, "A member whose newest fetch files no share count"),
        CheckReach.Key(Scope.FailureTable, "A sector holding fewer companies than the size cut"),
        CheckReach.Key(Scope.FailureTable, "A stock holding too few sessions for its averages or its look-back"),
        CheckReach.Key(Scope.FixtureTable, "member companies"),
    ];

    const string TechSector = GicsSectors.InformationTechnology;
    const string CareSector = GicsSectors.HealthCare;

    static HeavyweightMember Heavy(string ticker, string company, string? sector, decimal? value, double? lookBack, bool trend = true, decimal dollars = 1_000m) =>
        new(ticker, company, sector, value, dollars, lookBack, 100.0, trend ? 99.0 : 101.0, trend ? 98.0 : 97.0);

    [Fact]
    public void ARebalanceReadsEachSectorsLargestCompaniesOnceAndBuysTheLeaderAboveNothingInItsTrend()
    {
        HeavyweightMember[] members =
        [
            // Information Technology: seven listings of six companies, T5 and T6 two classes of one company, and NV with
            // no value. By value T1 600, T2 550, T3 520, T4 420, then the company of T5 and T6 at 345, held by T5, which
            // traded the more dollars, and T7 at 100, sixth and out of the five.
            Heavy("T1", "CIK 1", TechSector, 600m, 0.20),
            Heavy("T2", "CIK 2", TechSector, 550m, 0.10),
            Heavy("T3", "CIK 3", TechSector, 520m, 0.30, trend: false),
            Heavy("T4", "CIK 4", TechSector, 420m, 0.05),
            Heavy("T5", "CIK 5", TechSector, 345m, 0.15, dollars: 2_000m),
            Heavy("T6", "CIK 5", TechSector, 345m, 0.15),
            Heavy("T7", "CIK 7", TechSector, 100m, 0.40),
            Heavy("NV", "CIK 8", TechSector, null, 0.00),

            // Health Care: two companies, fewer than the five.
            Heavy("H1", "CIK 11", CareSector, 1_010m, 0.01),
            Heavy("H2", "CIK 12", CareSector, 198m, -0.01),

            // Energy: two leads of nothing. Utilities: two leads tied, the third a member with no return.
            Heavy("E1", "CIK 21", GicsSectors.Energy, 300m, 0.05),
            Heavy("E2", "CIK 22", GicsSectors.Energy, 200m, 0.05),
            Heavy("U1", "CIK 31", GicsSectors.Utilities, 300m, 0.10),
            Heavy("U2", "CIK 32", GicsSectors.Utilities, 200m, 0.10),
            Heavy("U3", "CIK 33", GicsSectors.Utilities, 100m, -0.20),
            Heavy("U4", "CIK 34", GicsSectors.Utilities, 50m, null),

            // A member filing no sector stands in none, however it leads.
            Heavy("NS", "CIK 41", null, 9_999m, 0.90),
        ];

        var sectors = HeavyweightRule.Read(members, HeavyweightRule.Provisional);

        Assert.Equal([GicsSectors.Energy, CareSector, TechSector, GicsSectors.Utilities], sectors.Select(sector => sector.Sector));

        // Information Technology's mean is every member's own return, the one with no value among them:
        // 0.20 + 0.10 + 0.30 + 0.05 + 0.15 + 0.15 + 0.40 + 0.00 = 1.35 over 8, 0.16875.
        var tech = sectors.Single(sector => sector.Sector == TechSector);

        Assert.Equal(8, tech.Counted);
        Assert.Equal(0.16875, tech.Return!.Value, 12);
        Assert.Equal(
            [(1, "T1", 600m), (2, "T2", 550m), (3, "T3", 520m), (4, "T4", 420m), (5, "T5", 345m)],
            tech.Largest.Select(ranked => (ranked.Place, ranked.Ticker, ranked.Value)));

        // Each lead is its return less 0.16875. T3 leads by the most, 0.13125, and fails the trend gate, so the leader
        // is T1, the largest lead above nothing in its trend, 0.03125.
        Assert.Equal([0.03125, -0.06875, 0.13125, -0.11875, -0.01875], tech.Largest.Select(ranked => Math.Round(ranked.Lead!.Value, 12)));
        Assert.Equal([true, true, false, true, true], tech.Largest.Select(ranked => ranked.Trend));
        Assert.Equal(["T1"], tech.Leaders);
        Assert.Equal([true, false, false, false, false], tech.Largest.Select(ranked => ranked.Leader));

        // Health Care holds two, both ranked, H1 leading a mean of nothing by a hundredth.
        var care = sectors.Single(sector => sector.Sector == CareSector);

        Assert.Equal(["H1", "H2"], care.Largest.Select(ranked => ranked.Ticker));
        Assert.Equal(["H1"], care.Leaders);

        // Energy's two each lead by nothing, and nothing is bought; Utilities' two tied leads settle by the ticker, the
        // member with no return standing in no mean: 0.10 + 0.10 - 0.20 over 3, nothing.
        Assert.Empty(sectors.Single(sector => sector.Sector == GicsSectors.Energy).Leaders);

        var utilities = sectors.Single(sector => sector.Sector == GicsSectors.Utilities);

        Assert.Equal(3, utilities.Counted);
        Assert.Equal(0.0, utilities.Return!.Value, 12);
        Assert.Equal(["U1"], utilities.Leaders);

        // Every sector's leaders are what the rebalance buys, the member with no sector among none.
        Assert.Equal(["H1", "T1", "U1"], HeavyweightRule.Buys(sectors).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheReplayStoresEachNamesCompanyAndItsSharesAsTheCapturedAnswersFileThem()
    {
        var expected = Expected("companies");

        using var store = await FixtureReplay.ReplayedAsync();

        // One company row a name, from the one storing fetch each made on the night, as the captured answer files it,
        // its filer kept to ten digits.
        Assert.Equal(
            [
                .. expected.GetProperty("names").EnumerateObject().Select(name =>
                    $"{name.Name}|{name.Value.GetProperty("cik").GetString()}|{name.Value.GetProperty("sector").GetString()}|{name.Value.GetProperty("group").GetString()}|{name.Value.GetProperty("industry").GetString()}|{name.Value.GetProperty("subIndustry").GetString()}"),
            ],
            Query(store, "SELECT ticker, cik, sector, industry_group, industry, sub_industry FROM company ORDER BY ticker;"));
        Assert.Equal(
            ["1"],
            Query(store, "SELECT COUNT(DISTINCT c.fetched_at) FROM company c JOIN reported_quarter r ON r.ticker = c.ticker AND r.fetched_at = c.fetched_at GROUP BY c.ticker;").Distinct());

        // Each name's newest quarter carries the count its balance sheet files for the period, with the day it was filed.
        foreach (var name in expected.GetProperty("names").EnumerateObject())
        {
            var newest = name.Value.GetProperty("newestCount");
            var row = Query(store, $"SELECT period_end, filing_date, shares FROM reported_quarter WHERE ticker = '{name.Name}' ORDER BY period_end DESC LIMIT 1;").Single().Split('|');

            Assert.Equal((newest[0].GetString(), newest[1].GetString()), (row[0], row[1]));
            Assert.Equal(decimal.Parse(newest[2].GetString()!, CultureInfo.InvariantCulture), decimal.Parse(row[2], CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void TheTrendGateAndTheAverageExitAreReadAtTheirEdges()
    {
        // The close above its 50-day average and that above its 200-day; a close at its 50-day, a 50-day at its 200-day,
        // or an average the night does not hold passes no gate.
        Assert.True(HeavyweightRule.Trending(101.0, 100.0, 99.0));
        Assert.False(HeavyweightRule.Trending(100.0, 100.0, 99.0));
        Assert.False(HeavyweightRule.Trending(101.0, 100.0, 100.0));
        Assert.False(HeavyweightRule.Trending(101.0, null, 99.0));
        Assert.False(HeavyweightRule.Trending(101.0, 100.0, null));

        // A close under the 200-day average sells; one at it does not, and a stock with no average is sold by none.
        Assert.True(HeavyweightRule.Broken(99.99, 100.0));
        Assert.False(HeavyweightRule.Broken(100.0, 100.0));
        Assert.False(HeavyweightRule.Broken(50.0, null));

        // The edge is the result less the size cut's, and none while either is open.
        Assert.Equal(-0.05, HeavyweightRule.Edge(0.10, 0.15)!.Value, 12);
        Assert.Null(HeavyweightRule.Edge(null, 0.15));
        Assert.Null(HeavyweightRule.Edge(0.10, null));
    }

    [Fact]
    public void TheBookRebalancesOnTheFirstNightOfAMonthItReadsAndNamesTheNext()
    {
        // The month's first session rebalances and the second does not; the book's first night rebalances; and a month
        // whose first session the night did not run on rebalances on the night after.
        Assert.True(HeavyweightRule.Rebalances(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1)));
        Assert.False(HeavyweightRule.Rebalances(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)));
        Assert.False(HeavyweightRule.Rebalances(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1)));
        Assert.True(HeavyweightRule.Rebalances(new DateOnly(2026, 9, 1), null));
        Assert.True(HeavyweightRule.Rebalances(new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 1)));

        // January 2026 opens after the exchange's holiday on New Year's Day: its first session is the 2nd, which
        // rebalances, and the night of the year's last session names it next.
        Assert.True(HeavyweightRule.Rebalances(new DateOnly(2026, 1, 2), new DateOnly(2025, 12, 1)));
        Assert.Equal(new DateOnly(2026, 1, 2), HeavyweightRule.NextRebalance(new DateOnly(2025, 12, 31)));

        // November 2026 opens on Monday the 2nd, the 1st a Sunday; January 2027 on Monday the 4th, New Year's Day a
        // Friday the exchange keeps; and a month past the calendar's table, or a night before it, names none.
        Assert.Equal(new DateOnly(2026, 11, 2), HeavyweightRule.NextRebalance(new DateOnly(2026, 10, 5)));
        Assert.Equal(new DateOnly(2027, 1, 4), HeavyweightRule.NextRebalance(new DateOnly(2026, 12, 31)));
        Assert.Null(HeavyweightRule.NextRebalance(new DateOnly(2027, 12, 15)));
        Assert.Null(HeavyweightRule.NextRebalance(new DateOnly(2024, 6, 3)));
    }

    [Fact]
    public void TheNightValuesAMemberFromItsNewestFetchAtTonightsCloseOnTheCountsBasis()
    {
        // One sheet filed 2026-08-01 at 5 shares on the basis of a fetch whose newest session, 2026-08-31, closed at 200.
        FiledCount[] counts = [new(new DateOnly(2026, 6, 30), new DateOnly(2026, 8, 1), 5m, new DateOnly(2026, 8, 31))];

        // The store still holding the fetch's close for that session: 5 times tonight's 120.
        Assert.Equal(600m, CompanyValue.Tonight(new DateOnly(2026, 9, 1), 120m, counts, 200m, 200m));

        // A two for one split since the fetch: the store's close for that session restated to 100 and tonight's close
        // of 120 on the new basis, so the count of 5 was 10 of today's shares: 5 times 120 times 200 over 100, 1,200.
        Assert.Equal(1_200m, CompanyValue.Tonight(new DateOnly(2026, 9, 1), 120m, counts, 200m, 100m));

        // A sheet filed on the night itself is not read on it, and a fetch whose session the store no longer holds
        // values nothing.
        Assert.Null(CompanyValue.Tonight(new DateOnly(2026, 8, 1), 120m, counts, 200m, 200m));
        Assert.Null(CompanyValue.Tonight(new DateOnly(2026, 9, 1), 120m, counts, 200m, null));
    }

    // The four nights the book is read over: September's first session, the day after, October's first and the day
    // after, on which T2 leaves the index.
    static readonly DateOnly SeptemberFirst = new(2026, 9, 1);
    static readonly DateOnly SeptemberSecond = new(2026, 9, 2);
    static readonly DateOnly OctoberFirst = new(2026, 10, 1);
    static readonly DateOnly OctoberSecond = new(2026, 10, 2);

    // The session a night's look-back starts from, 126 sessions before it on the exchange's calendar, counted here from
    // the calendar and not from the book.
    static DateOnly LookBackStart(DateOnly night)
    {
        var sessions = ExchangeClosures.SessionsBetween(new DateOnly(2025, 12, 31), night.AddDays(1));

        return sessions[sessions.Count - 1 - 126];
    }

    // The closes the four nights read, every other session closing at 100, the closes each look-back starts from among
    // them. T1 closes at 110 the session before September's look-back starts and at 90 the session after it, so a
    // look-back a session long or short reads another return.
    static readonly Dictionary<(string Ticker, DateOnly Session), decimal> BookCloses = new()
    {
        [("T1", ExchangeClosures.SessionsBetween(new DateOnly(2025, 12, 31), new DateOnly(2026, 9, 2))[^128])] = 110m,
        [("T1", ExchangeClosures.SessionsBetween(new DateOnly(2025, 12, 31), new DateOnly(2026, 9, 2))[^126])] = 90m,
        [("T1", SeptemberFirst)] = 120m, [("T2", SeptemberFirst)] = 115m, [("T3", SeptemberFirst)] = 90m, [("H1", SeptemberFirst)] = 101m, [("H2", SeptemberFirst)] = 99m,
        [("T1", SeptemberSecond)] = 126m, [("T2", SeptemberSecond)] = 115m, [("T3", SeptemberSecond)] = 99m, [("H1", SeptemberSecond)] = 95m, [("H2", SeptemberSecond)] = 99m,
        [("T1", OctoberFirst)] = 120m, [("T2", OctoberFirst)] = 140m, [("T3", OctoberFirst)] = 95m, [("H1", OctoberFirst)] = 101m, [("H2", OctoberFirst)] = 99m,
        [("T1", OctoberSecond)] = 120m, [("T3", OctoberSecond)] = 95m, [("H1", OctoberSecond)] = 101m, [("H2", OctoberSecond)] = 99m,
    };

    // The 50-day and 200-day averages each night reads: T3 under its 50-day on both rebalances and H2 at its 50-day, so
    // neither passes the trend gate, and H1's close of 95 under its 200-day of 100 on September's second.
    static readonly Dictionary<string, (double Fifty, double TwoHundred)> BookAverages = new()
    {
        ["T1"] = (110.0, 100.0),
        ["T2"] = (110.0, 100.0),
        ["T3"] = (96.0, 100.0),
        ["H1"] = (100.5, 100.0),
        ["H2"] = (99.0, 98.0),
    };

    static readonly string[] BookTickers = ["H1", "H2", "T1", "T2", "T3"];

    // A store holding the members' companies and counts and their bars to a night. T1's fetch was asked on the basis of a
    // close of 200 on 2026-08-31, which the store holds at 100 after a two for one split, so its count of 5 values it as
    // 10 of today's shares. T2's fetch holds a count of 5 filed 2026-08-01 and one of 1,000 filed on September's first
    // itself, which that night does not read and October's does. T2 leaves the index on October's second.
    static TemporaryStore BookStore()
    {
        var store = new TemporaryStore().Migrated();

        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();

        void Run(string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        foreach (var ticker in BookTickers)
        {
            var left = ticker == "T2" ? "'2026-10-02'" : "NULL";

            Run($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, {left}, '2026-01-01T00:00:00Z');");
        }

        (string Ticker, string Cik, string Sector, (string End, string Filed, string Shares)[] Sheets, string BasisClose)[] fetches =
        [
            ("T1", "0000000001", TechSector, [("2026-06-30", "2026-08-01", "5")], "200"),
            ("T2", "0000000002", TechSector, [("2026-06-30", "2026-08-01", "5"), ("2026-07-31", "2026-09-01", "1000")], "100"),
            ("T3", "0000000003", TechSector, [("2026-06-30", "2026-08-01", "8")], "100"),
            ("H1", "0000000011", CareSector, [("2026-06-30", "2026-08-01", "10")], "100"),
            ("H2", "0000000012", CareSector, [("2026-06-30", "2026-08-01", "2")], "100"),
        ];

        foreach (var (ticker, cik, sector, sheets, basisClose) in fetches)
        {
            Run($"INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES ('{ticker}', '2026-08-31T23:50:00Z', '{cik}', '{sector}', NULL, NULL, NULL);");

            foreach (var (end, filed, shares) in sheets)
            {
                Run(
                    "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, basis_session, basis_close, shares) VALUES " +
                    $"('{ticker}', '2026-08-31T23:50:00Z', '2026-08-31', '{end}', '{filed}', '2026-08-31', '{basisClose}', '{shares}');");
            }
        }

        transaction.Commit();

        return store;
    }

    // Each session's bars from the first of 2026 to a night, and each member's averages on the nights the book reads,
    // a member that has left the index storing no bar after it.
    static void BarsThrough(TemporaryStore store, DateOnly from, DateOnly night)
    {
        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var session in ExchangeClosures.SessionsBetween(from.AddDays(-1), night.AddDays(1)))
        {
            foreach (var ticker in BookTickers.Where(ticker => !(ticker == "T2" && session >= OctoberSecond)))
            {
                var close = BookCloses.TryGetValue((ticker, session), out var set) ? set : 100m;

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, raw_close, volume, source, observed_at) VALUES " +
                    "($ticker, $session, $close, $close, $close, $close, $close, 1000, 'constructed', '2026-01-01T00:00:00Z');";
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$close", close.ToString(CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();

                if (session == SeptemberFirst || session == SeptemberSecond || session == OctoberFirst || session == OctoberSecond)
                {
                    var (fifty, twoHundred) = BookAverages[ticker];

                    foreach (var (name, value) in new[] { (Core.Indicators.IndicatorSeries.Sma50, fifty), (Core.Indicators.IndicatorSeries.Sma200, twoHundred) })
                    {
                        using var average = connection.CreateCommand();
                        average.Transaction = transaction;
                        average.CommandText = "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ($ticker, $session, $name, $value, 200);";
                        average.Parameters.AddWithValue("$ticker", ticker);
                        average.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        average.Parameters.AddWithValue("$name", name);
                        average.Parameters.AddWithValue("$value", value);
                        average.ExecuteNonQuery();
                    }
                }
            }
        }

        transaction.Commit();
    }

    static async Task<HeavyweightBookOutcome> BookOn(TemporaryStore store, DateOnly night, string runId) =>
        await new HeavyweightBook(FixedClock.At(new DateTimeOffset(night.ToDateTime(new TimeOnly(23, 40)), TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", runId);

    static IReadOnlyList<string> Holdings(TemporaryStore store) =>
        TextRows(store,
            "SELECT ticker || '|' || entered_on || '|' || sector || '|' || company || '|' || entry_close || '|' || through || '|' || IFNULL(ended_on, 'open') || '|' || IFNULL(exit_close, '-') || '|' || IFNULL(reason, '-') " +
            "FROM heavyweight_holding ORDER BY entered_on, ticker;");

    static double Figure(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task TheBookBuysAtTheRankAsItStoodCarriesEachHoldingAndSellsItWhereTheRuleSays()
    {
        using var store = BookStore();

        // September's first, the book's first night, rebalances. Values: T1 5 times 120 times 200 over 100, 1,200; T2 5
        // times 115, 575, its count of 1,000 filed on the night itself not read; T3 8 times 90, 720; H1 10 times 101,
        // 1,010; H2 2 times 99, 198. Information Technology ranks T1, T3, T2 as they stood. Each look-back starts at 100,
        // so the returns are 0.20, 0.15 and -0.10 and the mean 0.0833..., T1 leading by 0.1166... in its trend and
        // bought; Health Care's mean of 0.01 and -0.01 is nothing and H1 leads it by a hundredth.
        BarsThrough(store, new DateOnly(2026, 1, 1), SeptemberFirst);

        // September's look-back starts on its 126th session back, which closed at 100; the sessions either side of it
        // closed at 110 and 90.
        var start = LookBackStart(SeptemberFirst).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Equal(["100"], TextRows(store, $"SELECT close FROM bar WHERE ticker = 'T1' AND session_date = '{start}';"));
        Assert.Equal(2, Scalar(store, $"SELECT COUNT(*) FROM bar WHERE ticker = 'T1' AND close IN ('110', '90') AND session_date < '2026-09-01';"));

        var first = await BookOn(store, SeptemberFirst, "night-0901");

        Assert.Equal((true, 5, 2), (first.Rebalanced, first.Valued, first.Held));
        Assert.Equal(["H1", "T1"], first.Entered);
        Assert.Equal(
            [
                "Health Care|1|H1|CIK 0000000011|1010|1|1",
                "Health Care|2|H2|CIK 0000000012|198|0|0",
                "Information Technology|1|T1|CIK 0000000001|1200|1|1",
                "Information Technology|2|T3|CIK 0000000003|720|0|0",
                "Information Technology|3|T2|CIK 0000000002|575|1|0",
            ],
            TextRows(store, "SELECT sector || '|' || place || '|' || ticker || '|' || company || '|' || company_value || '|' || trend || '|' || leader FROM heavyweight_night WHERE session_date = '2026-09-01' ORDER BY sector, place;"));
        Assert.Equal(0.20 - (0.20 + 0.15 - 0.10) / 3, Figure(store, "SELECT lead FROM heavyweight_night WHERE session_date = '2026-09-01' AND ticker = 'T1';"), 9);
        Assert.Equal((0.20 + 0.15 - 0.10) / 3, Figure(store, "SELECT sector_return FROM heavyweight_night WHERE session_date = '2026-09-01' AND ticker = 'T1';"), 9);
        Assert.Equal(0.01, Figure(store, "SELECT lead FROM heavyweight_night WHERE session_date = '2026-09-01' AND ticker = 'H1';"), 9);
        Assert.Equal(
            ["H1|2026-09-01|Health Care|CIK 0000000011|101|2026-09-01|open|-|-", "T1|2026-09-01|Information Technology|CIK 0000000001|120|2026-09-01|open|-|-"],
            Holdings(store));
        Assert.Equal("a rebalance, 5 member(s) valued, 2 bought, 0 ended, 2 held; bought: H1, T1", Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-0901' AND stage = 'heavyweights';"));

        // September's second does not rebalance. T1 is carried by 126 over 120, 1.05, and its size cut T1, T3 and T2 by
        // 1.05, 99 over 90 and 115 over 115. H1 closes at 95 under its 200-day average of 100 and is sold at that close:
        // its result 95 over 101 less one, its size cut's the mean of 95 over 101 and H2's 99 over 99, less one.
        BarsThrough(store, SeptemberSecond, SeptemberSecond);

        var second = await BookOn(store, SeptemberSecond, "night-0902");

        Assert.Equal((false, 1), (second.Rebalanced, second.Held));
        Assert.Equal(["H1: " + HeavyweightBook.UnderTheAverage], second.Ended);
        Assert.Equal(1.05, Figure(store, "SELECT growth FROM heavyweight_holding WHERE ticker = 'T1';"), 12);
        Assert.Equal("2026-09-02", Text(store, "SELECT through FROM heavyweight_holding WHERE ticker = 'T1';"));
        var carried = HeavyweightBook.CutOf(Text(store, "SELECT cut FROM heavyweight_holding WHERE ticker = 'T1';"));

        Assert.Equal(["T1", "T3", "T2"], carried.Select(member => member.Ticker));
        Assert.Equal(1.05, carried[0].Growth, 12);
        Assert.Equal(99.0 / 90.0, carried[1].Growth, 12);
        Assert.Equal(1.0, carried[2].Growth, 12);
        Assert.All(carried, member => Assert.Equal(SeptemberSecond, member.On));
        Assert.Equal("H1|2026-09-01|Health Care|CIK 0000000011|101|2026-09-02|2026-09-02|95|" + HeavyweightBook.UnderTheAverage, Holdings(store)[0]);
        Assert.Equal(95.0 / 101.0 - 1.0, Figure(store, "SELECT result FROM heavyweight_holding WHERE ticker = 'H1';"), 12);
        Assert.Equal((95.0 / 101.0 + 1.0) / 2.0 - 1.0, Figure(store, "SELECT cut_return FROM heavyweight_holding WHERE ticker = 'H1';"), 12);
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM heavyweight_night WHERE session_date = '2026-09-02';"));

        // October's first rebalances. T2's count of 1,000 is read now, 140,000, first; T1 1,200; T3 760. The look-backs
        // start at 100 again: 0.20, 0.40 and -0.05, a mean of 0.18333..., T2 leading by 0.21666... in its trend and T1 by
        // 0.01666..., so T1 is no longer the leader and is sold at 120: carried by 120 over 126, its growth 1.0 and its
        // result nothing; its size cut T1, T3 and T2 at 1.0, 1.1 times 95 over 99 and 140 over 115. T2 is bought, and
        // H1, sold under its average in September and leading Health Care again, is bought again.
        BarsThrough(store, new DateOnly(2026, 9, 3), OctoberFirst);

        var october = await BookOn(store, OctoberFirst, "night-1001");

        Assert.Equal((true, 5, 2), (october.Rebalanced, october.Valued, october.Held));
        Assert.Equal(["H1", "T2"], october.Entered);
        Assert.Equal(["T1: " + HeavyweightBook.NoLongerTheLeader], october.Ended);
        Assert.Equal(
            ["1|T2|140000|1", "2|T1|1200|0", "3|T3|760|0"],
            TextRows(store, "SELECT place || '|' || ticker || '|' || company_value || '|' || leader FROM heavyweight_night WHERE session_date = '2026-10-01' AND sector = 'Information Technology' ORDER BY place;"));
        Assert.Equal(0.40 - (0.20 + 0.40 - 0.05) / 3, Figure(store, "SELECT lead FROM heavyweight_night WHERE session_date = '2026-10-01' AND ticker = 'T2';"), 9);

        var t1 = "T1|2026-09-01|Information Technology|CIK 0000000001|120|2026-10-01|2026-10-01|120|" + HeavyweightBook.NoLongerTheLeader;

        Assert.Contains(t1, Holdings(store));
        Assert.Equal(0.0, Figure(store, "SELECT result FROM heavyweight_holding WHERE ticker = 'T1';"), 12);

        var cut = (1.0 + 99.0 / 90.0 * (95.0 / 99.0) + 140.0 / 115.0) / 3.0 - 1.0;

        Assert.Equal(cut, Figure(store, "SELECT cut_return FROM heavyweight_holding WHERE ticker = 'T1';"), 9);
        Assert.Equal(-cut, HeavyweightRule.Edge(0.0, cut)!.Value, 9);
        Assert.Equal(["H1", "T2"], TextRows(store, "SELECT ticker FROM heavyweight_holding WHERE entered_on = '2026-10-01' ORDER BY ticker;"));

        // October's first run again replaces what it wrote and nothing else: the same rows, the same holdings.
        var holdings = Holdings(store);
        var reads = TextRows(store, "SELECT session_date || '|' || sector || '|' || place || '|' || ticker || '|' || company_value || '|' || leader FROM heavyweight_night ORDER BY session_date, sector, place;");

        await BookOn(store, OctoberFirst, "night-1001-again");

        Assert.Equal(holdings, Holdings(store));
        Assert.Equal(reads, TextRows(store, "SELECT session_date || '|' || sector || '|' || place || '|' || ticker || '|' || company_value || '|' || leader FROM heavyweight_night ORDER BY session_date, sector, place;"));

        // October's second: T2 has left the index and stores no bar, so it is sold at the close of the last session it
        // was carried to as a member, October's first, at 140, its result and its size cut's nothing.
        BarsThrough(store, OctoberSecond, OctoberSecond);

        var left = await BookOn(store, OctoberSecond, "night-1002");

        Assert.Equal((false, 1), (left.Rebalanced, left.Held));
        Assert.Equal(["T2: " + HeavyweightBook.LeftTheIndex], left.Ended);
        Assert.Contains("T2|2026-10-01|Information Technology|CIK 0000000002|140|2026-10-01|2026-10-01|140|" + HeavyweightBook.LeftTheIndex, Holdings(store));
        Assert.Equal(0.0, Figure(store, "SELECT result FROM heavyweight_holding WHERE ticker = 'T2';"), 12);
        Assert.Equal(0.0, Figure(store, "SELECT cut_return FROM heavyweight_holding WHERE ticker = 'T2';"), 12);
        Assert.Equal("no rebalance, 1 ended, 1 held; ended: T2: " + HeavyweightBook.LeftTheIndex, Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-1002' AND stage = 'heavyweights';"));

        // A night for a session earlier than one the book has read is read for nothing, and changes nothing.
        store.Execute("INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader) VALUES ('2026-10-05', 'Energy', 1, 'X', 'ticker X', '1', NULL, NULL, NULL, 0, 0);");

        var after = Holdings(store);
        var earlier = await BookOn(store, OctoberSecond, "night-1002-earlier");

        Assert.Equal("the book has read 2026-10-05, a later session than 2026-10-02, and reads no earlier one", earlier.ReadNothing);
        Assert.Equal(after, Holdings(store));
    }
}
