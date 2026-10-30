using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// A constructed S&P 500 history whose three strong members dip to a support band and turn up on session 276, each a
// pullback setup there, with four weaker members rising beneath them; and the facts each strong member filed before that
// session, its revenue up 0.25 on the year-earlier quarter and growing 0.15 faster, both margins wider, and its cash from
// operations the stated multiple of its net income. The shape was found by walking the ledger's own gates over a grid of
// periods, depths and trends, and is kept here as the shape the family's setups are worked over.
internal static class BusinessFirstHistory
{
    public const int Night = 276;

    public static readonly string[] Strong = ["AAA", "BBB", "CCC"];

    // The cash each strong member's newest year holds over its net income of 40: AAA 1.25, BBB 0.6 and CCC 0.59.
    public static readonly IReadOnlyDictionary<string, decimal> Cash = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["AAA"] = 50m,
        ["BBB"] = 24m,
        ["CCC"] = 23.6m,
    };

    public static DateOnly[] Calendar(int count)
    {
        var days = new List<DateOnly>();

        for (var day = new DateOnly(2025, 1, 6); days.Count < count; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return [.. days];
    }

    // A strong member's close, a quarter of a point a session up with a wave of three points over twenty sessions; a
    // weaker one's, five hundredths up with a small wobble of its own.
    public static decimal StrongClose(int at) => Math.Round((decimal)(50 + (0.25 * at) + (3 * Math.Sin(2 * Math.PI * at / 20))), 2);

    public static decimal WeakClose(int other, int at) => Math.Round((decimal)(40 + (0.05 * at) + (0.5 * Math.Sin(at / (3.0 + other)))), 2);

    public static IEnumerable<string> Tickers => [.. Strong, "Z0", "Z1", "Z2", "Z3"];

    public static decimal Close(string ticker, int at) => ticker.StartsWith('Z') ? WeakClose(ticker[1] - '0', at) : StrongClose(at);

    // A session's bar: its high and low half a point either side of its close, opening at the close before, on a
    // million shares.
    public static SweepBar Bar(DateOnly[] calendar, string ticker, int at) =>
        new(calendar[at], Close(ticker, at) + 0.5m, Close(ticker, at) - 0.5m, Close(ticker, at), 1_000_000, at == 0 ? Close(ticker, at) : Close(ticker, at - 1), Close(ticker, at));

    public static SweepHistoryInputs Inputs(int sessions)
    {
        var calendar = Calendar(sessions);

        return new SweepHistoryInputs(
            calendar[^1],
            calendar,
            [.. Tickers.Select(ticker => new SweepName(ticker, [.. Enumerable.Range(0, sessions).Select(at => Bar(calendar, ticker, at))], [(new DateOnly(2020, 1, 2), null)], []))],
            0,
            0,
            0,
            0,
            "constructed");
    }

    // A strong member's facts: the quarters to 2024-06-30 and 2024-09-30 at 100, the quarter to 2025-06-30 at 110 and the
    // newest, to 2025-09-30, at 125 filed on 2025-11-03; its gross profit 40 and then 55 and its operating income 10 and
    // then 25 on the two third quarters; its net income for 2024 at 40 and its cash from operations as stated, both filed on
    // 2025-02-14. A newer quarter filed after the night, whose revenue fell, is added where asked.
    public static IReadOnlyList<FiledFactRow> Facts(decimal cash, bool later = false)
    {
        static FiledFactRow Row(string concept, string start, string end, decimal value, string filed) =>
            new(concept, DateOnly.Parse(start, CultureInfo.InvariantCulture), DateOnly.Parse(end, CultureInfo.InvariantCulture), value, DateOnly.Parse(filed, CultureInfo.InvariantCulture), "10-Q", "x");

        List<FiledFactRow> facts =
        [
            Row("Revenues", "2024-04-01", "2024-06-30", 100m, "2024-08-01"),
            Row("Revenues", "2024-07-01", "2024-09-30", 100m, "2024-11-01"),
            Row("Revenues", "2025-04-01", "2025-06-30", 110m, "2025-08-01"),
            Row("Revenues", "2025-07-01", "2025-09-30", 125m, "2025-11-03"),
            Row("GrossProfit", "2024-07-01", "2024-09-30", 40m, "2024-11-01"),
            Row("GrossProfit", "2025-07-01", "2025-09-30", 55m, "2025-11-03"),
            Row("OperatingIncomeLoss", "2024-07-01", "2024-09-30", 10m, "2024-11-01"),
            Row("OperatingIncomeLoss", "2025-07-01", "2025-09-30", 25m, "2025-11-03"),
            Row("NetIncomeLoss", "2023-01-01", "2023-12-31", 30m, "2024-02-15"),
            Row("NetIncomeLoss", "2024-01-01", "2024-12-31", 40m, "2025-02-14"),
            Row("NetCashProvidedByUsedInOperatingActivities", "2024-01-01", "2024-12-31", cash, "2025-02-14"),
        ];

        if (later)
        {
            facts.Add(Row("Revenues", "2024-10-01", "2024-12-31", 100m, "2025-02-14"));
            facts.Add(Row("Revenues", "2025-10-01", "2025-12-31", 60m, "2026-02-02"));
            facts.Add(Row("NetCashProvidedByUsedInOperatingActivities", "2025-01-01", "2025-12-31", 1m, "2026-02-02"));
            facts.Add(Row("NetIncomeLoss", "2025-01-01", "2025-12-31", 40m, "2026-02-02"));
        }

        return facts;
    }

    // Four quarters to 2025-09-30, each filed a month after it ends with a net income of 10, so the profit in the index's
    // form passes.
    public static IReadOnlyList<FiledIncome> Quarters() =>
    [
        .. Enumerable.Range(0, 4).Select(at => new FiledIncome(new DateOnly(2024, 12, 31).AddMonths(3 * at), new DateOnly(2025, 1, 31).AddMonths(3 * at), 10m, 10m, null)),
    ];

    public static Dictionary<string, IReadOnlyList<FiledIncome>> Income() =>
        Strong.ToDictionary(ticker => ticker, _ => Quarters(), StringComparer.Ordinal);

    public static Dictionary<string, IReadOnlyList<FiledFactRow>> AllFacts(bool later = false) =>
        Strong.ToDictionary(ticker => ticker, ticker => Facts(Cash[ticker], later), StringComparer.Ordinal);

    // The history stored through the night as the S&P 500's: every member's bars, and each strong member's filer, its
    // facts as first filed and its four quarters, every one filed before the night. Returns the night.
    public static DateOnly Seed(TemporaryStore store)
    {
        var calendar = Calendar(Night + 1);

        static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var ticker in Tickers)
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2020-01-02', NULL, '2025-01-06T21:00:00Z');");

            for (var at = 0; at < calendar.Length; at++)
            {
                var bar = Bar(calendar, ticker, at);

                store.Execute(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                    FormattableString.Invariant($"VALUES ('{ticker}', '{Stamp(bar.Session)}', '{bar.Open}', '{bar.High}', '{bar.Low}', '{bar.Close}', {bar.Volume}, 'constructed', '{Stamp(bar.Session)}T21:00:00Z', '{bar.RawClose}');"));
            }
        }

        foreach (var (ticker, at) in Strong.Select((ticker, at) => (ticker, at)))
        {
            var cik = FormattableString.Invariant($"{101 + at:0000000000}");

            store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2025-11-04T23:40:00.000Z', '{cik}');");

            foreach (var fact in Facts(Cash[ticker]))
            {
                store.Execute(
                    "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ("
                    + FormattableString.Invariant($"'{cik}', '{fact.Concept}', '{Stamp(fact.Start)}', '{Stamp(fact.End)}', '{fact.Value}', '{Stamp(fact.Filed)}', 'x', 'x', 'refresh');"));
            }

            foreach (var quarter in Quarters())
            {
                store.Execute(
                    "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income) " +
                    $"VALUES ('{ticker}', '2025-11-04T00:00:00Z', '2025-11-04', '{Stamp(quarter.PeriodEnd)}', '{Stamp(quarter.FilingDate)}', '10', '10');");
            }
        }

        return calendar[^1];
    }
}

// fixture-expectations, 19.9: the fundamentals-first family's setups in the ledger. Each loose gate worked by hand on both
// sides of its threshold and at it; the family's setups over a constructed history the pullback's at their own plan, kept
// where the revenue and the cash clear the gates, each with the provisional rule's pass beside it as the night reads it;
// and the point-in-time check holding over the family, a filing after the session reaching none of its readings.
// see: The fundamentals-first family's setups are the pullback's whose revenue and cash clear a grid step past the widest level its search tried
public partial class FixtureExpectations
{
    [Fact]
    public void EachOfTheFundamentalsFirstFamilysLooseGatesIsWorkedByHandOnBothSidesOfItsThreshold()
    {
        // The revenue: a hundredth over -0.05 passes, -0.05 itself does not, since the gate is a grid step below the
        // search's floor of nothing; the cash at 0.6 passes and a hundredth under does not; neither reading held passes
        // nothing, the cash not held being a member whose net income is not above nothing.
        Assert.Equal(-0.05, SetupGates.FundamentalsGrowth);
        Assert.Equal(0.6, SetupGates.FundamentalsCash);
        Assert.Equal(SetupGates.FundamentalsGrowth, FundamentalsRule.GrowthFloors.Min() - 0.05, 12);
        Assert.Equal(SetupGates.FundamentalsCash, FundamentalsRule.CashFloors.Min() - 0.2, 12);

        Assert.True(SetupGates.Fundamentals(-0.04, 0.6));
        Assert.False(SetupGates.Fundamentals(-0.05, 0.6));
        Assert.False(SetupGates.Fundamentals(-0.06, 5));
        Assert.True(SetupGates.Fundamentals(0.25, 0.6));
        Assert.False(SetupGates.Fundamentals(0.25, 0.59));
        Assert.False(SetupGates.Fundamentals(null, 1.25));
        Assert.False(SetupGates.Fundamentals(0.25, null));

        // The cash is read only where the year's net income is above nothing: a year losing money holds no reading, so the
        // member fails the gate and stays out however much cash it made.
        static FiledFactRow Year(string concept, decimal value) =>
            new(concept, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), value, new DateOnly(2025, 2, 14), "10-K", "x");

        Assert.Null(FiledFacts.Read([Year("NetIncomeLoss", -10m), Year("NetCashProvidedByUsedInOperatingActivities", 50m)], new DateOnly(2025, 3, 3)).CashOverIncome);
        Assert.Equal(1.25, FiledFacts.Read([Year("NetIncomeLoss", 40m), Year("NetCashProvidedByUsedInOperatingActivities", 50m)], new DateOnly(2025, 3, 3)).CashOverIncome!.Value, 12);
    }

    // The ledger's families on one session of the constructed history, read through the given facts.
    static IReadOnlyList<FamilySetups> BusinessFirstFamilies(SweepHistoryInputs inputs, int at, IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>> facts)
    {
        var sessionAt = inputs.Sessions.Select((day, place) => (day, place)).ToDictionary(pair => pair.day, pair => pair.place);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var columns = SweepColumns.Sessions(series, inputs.Sessions);
        var members = SweepBenchmark.On(series, inputs.Sessions.Length);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, inputs.Sessions.Length);

        return LedgerSetups.On(IndexFamilies.LargeIndex, series, columns, members, inputs.Sessions, at, LedgerMarket.None, new LedgerContext(BusinessFirstHistory.Income(), NoSectors, highs, lows, facts) { Ranks = new FactorRanks(series, members) });
    }

    [Fact]
    public void TheFamilysSetupsAreThePullbacksWhoseRevenueAndCashClearTheGatesEachWithTheProvisionalRulesPass()
    {
        var inputs = BusinessFirstHistory.Inputs(BusinessFirstHistory.Night + 1);
        var families = BusinessFirstFamilies(inputs, BusinessFirstHistory.Night, BusinessFirstHistory.AllFacts());

        // The ledger reads four swing families, the business-first family last, each over the seven members.
        Assert.Equal(LedgerSetups.Families, families.Select(family => family.Family));
        Assert.All(families, family => Assert.Equal(7, family.Members));

        var pullbacks = families.Single(family => family.Family == SetupFamilies.Pullback).Rows;
        var business = families.Single(family => family.Family == FundamentalsRule.Name).Rows;

        // The three strong members are pullback setups on the night; AAA's cash of 1.25 and BBB's of 0.6 clear the gate and
        // CCC's of 0.59 does not, whatever its revenue.
        Assert.Equal(BusinessFirstHistory.Strong, pullbacks.Select(row => row.Ticker));
        Assert.Equal(["AAA", "BBB"], business.Select(row => row.Ticker));

        foreach (var row in business)
        {
            var pullback = pullbacks.Single(one => one.Ticker == row.Ticker);

            // Each at the pullback's own anchor and plan, with the pullback's readings, the business's among them as filed
            // before the session: revenue 125 over 100, a growth of 0.25, 0.15 faster than 110 over 100.
            Assert.Equal(pullback.Anchor, row.Anchor);
            Assert.Equal(pullback.Plan, row.Plan);
            Assert.Equal(pullback.Readings, row.Readings);
            Assert.Equal(0.25, row.Reading("revenue_growth")!.Value, 12);
            Assert.Equal(0.15, row.Reading("growth_change")!.Value, 12);
            Assert.Equal(double.Parse(BusinessFirstHistory.Cash[row.Ticker].ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) / 40, row.Reading("cash_over_income")!.Value, 12);
            Assert.Equal(LedgerSetups.FundamentalsWords, row.Rule);
            Assert.Equal(inputs.Sessions[BusinessFirstHistory.Night], row.Session);
        }

        // The provisional rule passes AAA, which clears the base, the market check, the floors and every part of the rule
        // at a cash of 1.0; BBB's cash of 0.6 fails its cash check, so it is a setup the rule did not pass.
        Assert.True(business.Single(row => row.Ticker == "AAA").LivePass);
        Assert.False(business.Single(row => row.Ticker == "BBB").LivePass);
        Assert.Equal(FundamentalsRule.NoCash, FundamentalsRule.FailsOn(FundamentalsRule.Provisional, business.Single(row => row.Ticker == "BBB").Readings, BusinessFirstHistory.Quarters(), inputs.Sessions[BusinessFirstHistory.Night]));

        // With no fact filed, no business reading is held and no member is a setup of the family.
        Assert.Empty(BusinessFirstFamilies(inputs, BusinessFirstHistory.Night, NoFacts).Single(family => family.Family == FundamentalsRule.Name).Rows);
    }

    [Fact]
    public void ThePointInTimeCheckHoldsOverTheFamilyAndAFilingAfterTheSessionReachesNoneOfItsReadings()
    {
        // The whole history runs forty sessions past the night, with a filing after it in which revenue fell and the cash
        // dried up; the setups read off the whole history on the night are the ones rebuilt from the history cut there.
        var inputs = BusinessFirstHistory.Inputs(BusinessFirstHistory.Night + 40);
        var facts = BusinessFirstHistory.AllFacts(later: true);
        var stored = BusinessFirstFamilies(inputs, BusinessFirstHistory.Night, facts).Single(family => family.Family == FundamentalsRule.Name).Rows;
        var night = inputs.Sessions[BusinessFirstHistory.Night];
        var cut = LedgerPointInTime.Cut(inputs, night);
        var market = LedgerPointInTime.Cut(LedgerMarket.None, night);

        Assert.Equal(["AAA", "BBB"], stored.Select(row => row.Ticker));

        foreach (var row in stored)
        {
            var (held, rebuilt) = LedgerPointInTime.Setup(IndexFamilies.LargeIndex, cut, market, BusinessFirstHistory.Income(), NoSectors, facts, FundamentalsRule.Name, row.Ticker);

            Assert.True(held);
            Assert.Empty(LedgerPointInTime.Differences(row.Readings, rebuilt!));
            Assert.Equal(0.25, rebuilt![LedgerReadings.IndexOf("revenue_growth")]!.Value, 12);
        }

        // CCC is held on the night and is no setup of the family there; and a stored revenue reading taken from the filing
        // after the session, 60 over 100, is named by its column with both figures.
        Assert.Equal((true, (double?[]?)null), LedgerPointInTime.Setup(IndexFamilies.LargeIndex, cut, market, BusinessFirstHistory.Income(), NoSectors, facts, FundamentalsRule.Name, "CCC"));

        var ahead = (double?[])stored[0].Readings.Clone();

        ahead[LedgerReadings.IndexOf("revenue_growth")] = -0.4;

        Assert.Equal([("revenue_growth", (double?)-0.4, (double?)0.25)], LedgerPointInTime.Differences(ahead, LedgerPointInTime.Setup(IndexFamilies.LargeIndex, cut, market, BusinessFirstHistory.Income(), NoSectors, facts, FundamentalsRule.Name, "AAA").Readings!).Select(one => (one.Reading, one.Stored, one.Rebuilt is { } value ? Math.Round(value, 12) : (double?)null)));
    }

    [Fact]
    public async Task From21Point4TheNightComputesTheFamilyOnNoIndexAndReadsNoSettingAnApprovalStoredForIt()
    {
        // AAA on the night with its cash at 0.9 of its income and every other part passing: the provisional rule asks for
        // the income whole and refuses it at its cash.
        var inputs = BusinessFirstHistory.Inputs(BusinessFirstHistory.Night + 1);
        var prepared = IndexNightRead.Prepare(IndexFamilies.LargeIndex, inputs.Sessions[^1], inputs.Names, BusinessFirstHistory.Income())!;
        Func<string, IReadOnlyList<double?>?> readings = ticker => ticker == "AAA" ? Improving(("cash_over_income", 0.9)) : null;

        string? Reason(IndexFamilyRead read) =>
            IndexNightRead.Answers(prepared, FundamentalsRule.Name, read).Single(one => one.Ticker == "AAA") is var answer && answer.Passed ? null : answer.Reason;

        var lower = EquityBrief.Core.Loop.LoopChange.OfSetting([0, 0, 0], FundamentalsRule.At([0, 0, 0]).Short, 0);
        var hooks = EquityBrief.Core.Loop.LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [EquityBrief.Core.Loop.RuleHooks.ExitParameter] = 7 });

        Assert.Equal(FundamentalsRule.NoCash, Reason(IndexNightRead.Provisional(prepared, FundamentalsRule.Name, readings)));

        // Over a store of the same history with the setting stored for the family on the S&P 500, the index families' step
        // from 21.4 reads the S&P 500 for no family and, no page trade of the family's standing open on it, not at all: no
        // night row of its own, no answer under the family on any index, and the stored setting unread.
        // see: Business first leaves the night at the one move of the pins, its rows and record kept and its one open trade run out under its own plan
        using var store = new TemporaryStore().Migrated();
        var night = BusinessFirstHistory.Seed(store);
        var clock = EquityBrief.Core.Time.FixedClock.At(new DateTimeOffset(2026, 1, 28, 3, 0, 0, TimeSpan.Zero), EquityBrief.Core.Time.SessionZones.UnitedStates);
        var ledger = new SetupLedger(clock, store.DatabaseFile);

        store.Execute($"INSERT INTO provisional_setting (index_code, family, change, words, set_at, run_id, proposal) VALUES ('GSPC', 'fundamentals', '{lower.Json}', '{lower.Words()}', '2026-01-27T15:00:00Z', 'run', 'the grid');");
        store.Execute($"INSERT INTO provisional_setting (index_code, family, change, words, set_at, run_id, proposal) VALUES ('GSPC', 'pullback', '{hooks.Json}', '{hooks.Words()}', '2026-01-27T15:00:00Z', 'run', 'the exit');");
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-business", readingsOf: ledger.ReadingsTonightAsync);

        Assert.Empty(FamilyRows(store, $"SELECT settings FROM index_family_night WHERE index_code = 'GSPC' AND session_date = '{night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}';"));
        Assert.Empty(FamilyRows(store, "SELECT ticker FROM index_family_result WHERE family = 'fundamentals';"));
        Assert.Equal(["MID", "SML"], FamilyRows(store, $"SELECT index_code FROM index_family_night WHERE session_date = '{night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' ORDER BY index_code;"));
        Assert.All(FamilyRows(store, "SELECT settings FROM index_family_night;"), settings => Assert.DoesNotContain("\"fundamentals\"", settings, StringComparison.Ordinal));
    }
}
