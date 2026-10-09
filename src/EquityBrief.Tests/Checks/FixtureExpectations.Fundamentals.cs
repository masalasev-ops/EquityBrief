using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.8: the fundamentals-first family worked by hand. Each part of its rule on both sides of its
// threshold, a reading not held passing none; its 27 settings the grid registered before the search, the provisional
// among them; the search's floors and the ideas' test of the latest years over constructed trades, and what luck passes
// of 27; and a filing the refresh stores, filed before the session, changing the member's answer on that night through
// the night's own readings and answers, while one filed on the session is read from the next.
// see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
public partial class FixtureExpectations
{
    // The rows 17.8 adds that this check reaches: section 17's parts and grid, and section 18's member the facts do not
    // reach.
    internal static readonly string[] FundamentalsClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The fundamentals-first family"),
        CheckReach.Key(Scope.LimitsTable, "The fundamentals-first search"),
        CheckReach.Key(Scope.FailureTable, "A member no filed fact reaches"),
    ];

    // Every reading the rule reads set to pass at the provisional setting, those named set apart.
    static double?[] Improving(params (string Column, double? Value)[] set)
    {
        var readings = new double?[LedgerReadings.Count];

        foreach (var (column, value) in new (string, double?)[]
        {
            ("revenue_growth", 0.25), ("growth_change", 0.15), ("gross_margin_change", 0.04), ("operating_margin_change", 0.10),
            ("cash_over_income", 1.25), ("close_over_long", 1.10), ("fifty_over_long", 1.05),
        })
        {
            readings[LedgerReadings.IndexOf(column)] = value;
        }

        foreach (var (column, value) in set)
        {
            readings[LedgerReadings.IndexOf(column)] = value;
        }

        return readings;
    }

    static readonly DateOnly FundamentalsNight = new(2026, 8, 4);

    // Four quarters filed before the night, newest last, each with the net income given.
    static FiledIncome[] Quarters(params decimal[] income) =>
    [
        .. income.Select((net, at) => new FiledIncome(new DateOnly(2025, 9, 30).AddMonths(3 * at), new DateOnly(2025, 10, 30).AddMonths(3 * at), net, net, null)),
    ];

    [Fact]
    public void EachPartOfTheFundamentalsFirstRuleIsWorkedByHandOnBothSidesOfItsThreshold()
    {
        var setting = FundamentalsRule.Provisional;
        var profitable = Quarters(10m, 10m, 10m, 10m);
        string? Fails(double?[] readings, FiledIncome[]? quarters = null) => FundamentalsRule.FailsOn(setting, readings, quarters ?? profitable, FundamentalsNight);

        // Every part passing.
        Assert.Null(Fails(Improving()));

        // The profit in the index's form: four quarters summing above nothing with the newest above nothing; a newest loss
        // with the sum still above nothing fails, and so does a sum of nothing.
        Assert.Equal(FundamentalsRule.NoProfit, Fails(Improving(), Quarters(30m, 10m, 10m, -1m)));
        Assert.Equal(FundamentalsRule.NoProfit, Fails(Improving(), Quarters(10m, -10m, -10m, 10m)));
        Assert.True(FundamentalsRule.ProfitInTheIndexForm(Quarters(30m, 10m, 10m, 0.01m), FundamentalsNight));

        // A newer quarter's loss filed on the session is not read, so the four before it pass; filed the day before, it
        // is the newest the check reads and fails it.
        FiledIncome LossFiled(DateOnly filed) => new(new DateOnly(2026, 7, 31), filed, -5m, -5m, null);

        Assert.Null(Fails(Improving(), [.. profitable, LossFiled(FundamentalsNight)]));
        Assert.Equal(FundamentalsRule.NoProfit, Fails(Improving(), [.. profitable, LossFiled(FundamentalsNight.AddDays(-1))]));

        // Revenue above the floor and growing faster than the quarter before: growth of nothing at a floor of nothing
        // fails and a hair over passes; a change of nothing fails; a growth not held fails.
        Assert.Equal(FundamentalsRule.NoRevenue, Fails(Improving(("revenue_growth", 0.0))));
        Assert.Null(Fails(Improving(("revenue_growth", 0.0001))));
        Assert.Equal(FundamentalsRule.NoRevenue, Fails(Improving(("growth_change", 0.0))));
        Assert.Equal(FundamentalsRule.NoRevenue, Fails(Improving(("revenue_growth", null))));
        Assert.Equal(FundamentalsRule.NoRevenue, FundamentalsRule.FailsOn(setting with { GrowthFloor = 0.25 }, Improving(), profitable, FundamentalsNight));
        Assert.Null(FundamentalsRule.FailsOn(setting with { GrowthFloor = 0.2499 }, Improving(), profitable, FundamentalsNight));

        // Either margin, both, or the operating alone.
        var grossOnly = Improving(("operating_margin_change", 0.0));
        var operatingOnly = Improving(("gross_margin_change", -0.01));

        Assert.Null(Fails(grossOnly));
        Assert.Null(Fails(operatingOnly));
        Assert.Equal(FundamentalsRule.NoMargin, Fails(Improving(("gross_margin_change", 0.0), ("operating_margin_change", 0.0))));
        Assert.Equal(FundamentalsRule.NoMargin, FundamentalsRule.FailsOn(setting with { Margins = MarginRule.Both }, grossOnly, profitable, FundamentalsNight));
        Assert.Null(FundamentalsRule.FailsOn(setting with { Margins = MarginRule.Both }, Improving(), profitable, FundamentalsNight));
        Assert.Equal(FundamentalsRule.NoMargin, FundamentalsRule.FailsOn(setting with { Margins = MarginRule.Operating }, grossOnly, profitable, FundamentalsNight));
        Assert.Null(FundamentalsRule.FailsOn(setting with { Margins = MarginRule.Operating }, operatingOnly, profitable, FundamentalsNight));

        // Cash from operations at least the income: exactly the floor passes, a hair under fails, none held fails.
        Assert.Null(Fails(Improving(("cash_over_income", 1.0))));
        Assert.Equal(FundamentalsRule.NoCash, Fails(Improving(("cash_over_income", 0.9999))));
        Assert.Equal(FundamentalsRule.NoCash, Fails(Improving(("cash_over_income", null))));

        // The uptrend: the close above its 200-day average and the 50-day above it, each strictly.
        Assert.Equal(FundamentalsRule.NoTrend, Fails(Improving(("close_over_long", 1.0))));
        Assert.Null(Fails(Improving(("close_over_long", 1.0001))));
        Assert.Equal(FundamentalsRule.NoTrend, Fails(Improving(("fifty_over_long", 0.99))));
        Assert.Equal(FundamentalsRule.NoTrend, Fails(Improving(("fifty_over_long", null))));
    }

    [Fact]
    public void TheSearchReadsTheTwentySevenRegisteredSettingsUnderTheFamilyFloorsWithLuckStated()
    {
        // Three levels of each of three dials, every pairing once, the provisional setting among them.
        Assert.Equal(27, FundamentalsRule.Grid.Count);
        Assert.Equal(27, FundamentalsRule.Grid.Select(setting => setting.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(FundamentalsRule.Provisional, FundamentalsRule.Grid);
        Assert.Equal("growth>0|margins=either|cash>=0.8", FundamentalsRule.Grid[0].Key);
        Assert.Equal("growth>0.1|margins=operating|cash>=1.2", FundamentalsRule.Grid[^1].Key);

        // Three hundred trades, one a year of eight above nothing short of the eight: the first scored year a loss and the
        // other seven a gain passes, seven of eight with the last three each a gain; 299 trades does not; a loss in two of
        // the last three does not, though six of eight stand above nothing.
        static (DateOnly, double)[] Trades(int count, Func<int, double> edgeOfYear) =>
            [.. Enumerable.Range(0, count).Select(at => (new DateOnly(2019 + (at % 8), 6, 1), edgeOfYear(at % 8)))];

        var passing = FundamentalsSearch.Figures(FundamentalsRule.Provisional, Trades(300, year => year == 0 ? -1 : 1));

        Assert.True(passing.Passed);
        Assert.Equal((300, 7, 3), (passing.Trades, passing.YearsBetter, passing.RecentBetter));
        Assert.False(FundamentalsSearch.Figures(FundamentalsRule.Provisional, Trades(299, year => year == 0 ? -1 : 1)).Passed);

        var lateLosses = FundamentalsSearch.Figures(FundamentalsRule.Provisional, Trades(400, year => year is 5 or 7 ? -1 : 1));

        Assert.Equal((6, 1), (lateLosses.YearsBetter, lateLosses.RecentBetter));
        Assert.False(lateLosses.Passed);

        // Luck alone passes 34 of the 256 ways eight years can fall: the 37 with six or more years above nothing less the
        // three whose first five are all above and one of the last three is, so about 3.6 of 27.
        Assert.Equal(27 * 34 / 256.0, FundamentalsSearch.Luck(27), 12);
    }

    [Fact]
    public async Task AFilingTheRefreshStoresChangesTheFamilysAnswerForThatMemberOnThatNight()
    {
        using var store = new TemporaryStore().Migrated();

        // AAA on the S&P 400, rising from 50 to 100 over 260 weekdays to 2026-08-04 on a million shares a day, its four
        // quarters filed before the night each profitable, and its filer's CIK on its company row.
        var day = FundamentalsNight;
        var sessions = new List<DateOnly>();

        while (sessions.Count < 260)
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(day);
            }

            day = day.AddDays(-1);
        }

        sessions.Reverse();
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'AAA', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('AAA', '2026-08-01T23:40:00.000Z', '0000000101');");

        for (var at = 0; at < sessions.Count; at++)
        {
            var close = Math.Round(50m + (50m * at / (sessions.Count - 1)), 2);
            var stamp = sessions[at].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                FormattableString.Invariant($"VALUES ('AAA', '{stamp}', '{close}', '{close + 0.5m}', '{close - 0.5m}', '{close}', 1000000, 'constructed', '{stamp}T21:00:00Z', '{close}');"));
        }

        foreach (var quarter in Quarters(10m, 10m, 10m, 10m))
        {
            store.Execute(
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income) " +
                FormattableString.Invariant($"VALUES ('AAA', '2026-08-01T00:00:00Z', '2026-08-01', '{quarter.PeriodEnd:yyyy-MM-dd}', '{quarter.FilingDate:yyyy-MM-dd}', '10', '10');"));
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 8, 4, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // The member's answer under the family on the night, through the night's own readings and answers, AAA at a
        // pullback's buy point.
        async Task<string?> AnswerAsync()
        {
            await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
            await connection.OpenAsync();

            var (names, income) = await IndexFamilies.InputsAsync(connection, "MID", FundamentalsNight, default);
            var inputs = IndexNightRead.Prepare("MID", FundamentalsNight, names, income)!;
            var name = Array.FindIndex(inputs.Series, one => one.Name.Ticker == "AAA");
            var readings = await new SetupLedger(clock, store.DatabaseFile).ReadingsTonightAsync("MID");
            var read = IndexNightRead.Fundamentals(inputs, FundamentalsRule.Provisional, readings, [(name, new IndexTrade(100m, 96m, 108m, null, 20, 2.0))]);
            var answer = IndexNightRead.Answers(inputs, FundamentalsRule.Name, read).Single(one => one.Ticker == "AAA");

            return answer.Passed ? null : answer.Reason;
        }

        void Facts(string filed)
        {
            foreach (var fact in Business(filed))
            {
                store.Execute(
                    "INSERT OR REPLACE INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ("
                    + FormattableString.Invariant($"'0000000101', '{fact.Concept}', '{fact.Start:yyyy-MM-dd}', '{fact.End:yyyy-MM-dd}', '{fact.Value}', '{fact.Filed:yyyy-MM-dd}', 'x', 'x', 'refresh');"));
            }
        }

        // No facts stored: the revenue is not read and the member fails the revenue check.
        Assert.Equal(FundamentalsRule.NoRevenue, await AnswerAsync());

        // The refresh stores the quarter to 2026-06-30 filed on the night's own session: not read until the next, so the
        // quarter before is the newest read, its growth 0.10 with no change read, and the member still fails.
        Facts("2026-08-04");

        Assert.Equal(FundamentalsRule.NoRevenue, await AnswerAsync());

        // The same quarter filed the day before the session: revenue up 0.25 and growing 0.15 faster, both margins up and
        // cash 1.25 times the income, so the member passes on that night.
        Facts("2026-08-03");

        Assert.Null(await AnswerAsync());
    }
}
