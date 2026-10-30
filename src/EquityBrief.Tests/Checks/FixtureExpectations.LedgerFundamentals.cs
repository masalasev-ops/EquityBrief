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
    public async Task AnApprovedSettingOfTheFamilyIsReadByThePerRuleTestAndFrom21Point4TheNightComputesTheFamilyOnNoIndex()
    {
        // AAA on the night with its cash at 0.9 of its income and every other part passing: the provisional rule asks for
        // the income whole and refuses it at its cash; the per-rule test walks the family at the setting an approval stored,
        // with the hooks stored beside it, and at the provisional setting where none is stored.
        var inputs = BusinessFirstHistory.Inputs(BusinessFirstHistory.Night + 1);
        var prepared = IndexNightRead.Prepare(IndexFamilies.LargeIndex, inputs.Sessions[^1], inputs.Names, BusinessFirstHistory.Income())!;
        Func<string, IReadOnlyList<double?>?> readings = ticker => ticker == "AAA" ? Improving(("cash_over_income", 0.9)) : null;

        string? Reason(IndexFamilyRead read) =>
            IndexNightRead.Answers(prepared, FundamentalsRule.Name, read).Single(one => one.Ticker == "AAA") is var answer && answer.Passed ? null : answer.Reason;

        var lower = EquityBrief.Core.Loop.LoopChange.OfSetting([0, 0, 0], FundamentalsRule.At([0, 0, 0]).Short, 0);
        var hooks = EquityBrief.Core.Loop.LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [EquityBrief.Core.Loop.RuleHooks.ExitParameter] = 7 });

        // The per-rule test walks the family at the same setting with the hooks stored beside it, and at the provisional
        // setting where none is stored.
        var exitHook = EquityBrief.Core.Loop.LoopChange.OfSetting([0, 0, 0], FundamentalsRule.At([0, 0, 0]).Short, 0) with { Hooks = hooks.Hooks };

        Assert.Equal((FundamentalsRule.Provisional, FundamentalsRule.Provisional.Key), EquityBrief.Worker.Loop.RuleTests.FundamentalsStanding(null));
        Assert.Equal((FundamentalsRule.At([0, 0, 0]), "growth>0|margins=either|cash>=0.8"), EquityBrief.Worker.Loop.RuleTests.FundamentalsStanding(lower));
        Assert.Equal(FundamentalsRule.At([0, 0, 0]), EquityBrief.Worker.Loop.RuleTests.FundamentalsStanding(exitHook).Setting);
        Assert.StartsWith("growth>0|margins=either|cash>=0.8; ", EquityBrief.Worker.Loop.RuleTests.FundamentalsStanding(exitHook).Words, StringComparison.Ordinal);

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

    // Every weekday from the first scored session to early March 2024: three years a fold of 2022 learns on, and the test
    // years 2022, 2023 and the months of 2024.
    static DateOnly[] FundamentalsLoopCalendar()
    {
        var days = new List<DateOnly>();

        for (var day = EquityBrief.Worker.Sweep.SweepColumns.FirstScored; day <= new DateOnly(2024, 3, 8); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return [.. days];
    }

    // A setting's walk over the calendar: a trade entered on every session and ended five sessions on, none past the
    // history's end, each earning the edge given for its entry session over a benchmark of nothing.
    static IReadOnlyList<EquityBrief.Worker.Loop.ExitProcedures.Walked> FundamentalsWalk(DateOnly[] calendar, Func<int, double> edge) =>
    [
        .. Enumerable.Range(0, calendar.Length).Select(entry =>
        {
            var ended = entry + 5 < calendar.Length ? entry + 5 : (int?)null;
            var listing = new FamilyListing(0, entry, entry, 0, 0, 100, 95, 110, double.NaN, 63);

            return new EquityBrief.Worker.Loop.ExitProcedures.Walked(entry, ended, new FamilyTrade(listing, calendar[entry].Year - EquityBrief.Worker.Sweep.SweepColumns.FirstScored.Year, ended is null ? null : edge(entry), 0), string.Empty);
        }),
    ];

    [Fact]
    public void TheFamilysProcedureIsWorkedOverConstructedFoldsAcrossItsTwentySevenSettings()
    {
        var calendar = FundamentalsLoopCalendar();
        var folds = EquityBrief.Core.Loop.LoopFolds.Of(calendar, calendar[^1]);
        var read = new EquityBrief.Worker.Loop.LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<FiledIncome>>(), calendar, 0, folds);
        var grid = EquityBrief.Worker.Loop.FundamentalsSearch.Grid;
        var settings = grid.Settings;

        // The grid is the family's 27 settings in the search's order, the growth floor slowest, the provisional among them
        // at its places: no growth floor, either margin and cash at the income.
        Assert.Equal(27, settings.Count);
        Assert.Equal(FundamentalsRule.Grid, settings.Select(FundamentalsRule.At));
        Assert.Equal([0, 0, 1], grid.Provisional);
        Assert.Equal(FundamentalsRule.Provisional, FundamentalsRule.At(grid.Provisional.ToArray()));

        // Every setting earns a fifth of a risk a trade but two: revenue up more than five per cent with both margins
        // wider and cash at 1.2 of the income, two dials moved, and cash at 1.2 alone, one dial moved, each earning a whole
        // risk. A fold's search takes the best edge meeting the floors and, between the two, the one moving fewer dials.
        int PlaceOf(int[] places) => Enumerable.Range(0, settings.Count).Single(at => settings[at].SequenceEqual(places));

        var twoMoved = PlaceOf([1, 1, 2]);
        var oneMoved = PlaceOf([0, 0, 2]);
        IReadOnlyList<IReadOnlyList<EquityBrief.Worker.Loop.ExitProcedures.Walked>> walked =
        [
            .. Enumerable.Range(0, settings.Count).Select(at => FundamentalsWalk(calendar, _ => at == twoMoved || at == oneMoved ? 1.0 : 0.2)),
        ];
        var current = walked[PlaceOf([0, 0, 1])];
        var proposal = EquityBrief.Worker.Loop.LoopProcedures.Fundamentals(read, walked, current, FundamentalsRule.Provisional, 63);

        Assert.Equal(FundamentalsRule.Name, proposal.Family);
        Assert.Equal(EquityBrief.Worker.Loop.LoopProcedures.Grid, proposal.Proposal);
        Assert.Equal(FundamentalsRule.Provisional.Words, proposal.Current);
        Assert.Equal(FundamentalsRule.At([0, 0, 2]).Words, proposal.Words);
        Assert.Equal([0, 0, 2], proposal.Change!.Places);
        Assert.Equal("growth 0|margins either|cash 1.2", proposal.Change.Key);
        Assert.Equal("the setting growth 0, margins either, cash 1.2", proposal.Change.Words());
        Assert.Null(proposal.Change.StopFloor);
        Assert.Equal([2022, 2023, 2024], proposal.Folds.Select(one => one.Fold.Year));
        Assert.All(proposal.Folds, one => Assert.Equal(FundamentalsRule.At([0, 0, 2]).Words, one.Chosen));
        Assert.Equal(3, proposal.StableFolds);

        // In each test year the chosen setting's trades earn four fifths of a risk more than the rule today's, unit for
        // unit, every one of its trades that ended in its year.
        foreach (var year in proposal.Evidence.Years)
        {
            Assert.True(year.ProposedUnits > 0);
            Assert.Equal(year.CurrentUnits, year.ProposedUnits);
            Assert.Equal(0.8 * year.ProposedUnits, year.ProposedTotal - year.CurrentTotal, 6);
        }

        // Where a fold's years before hold too few trades for the floors in proportion, it chooses nothing and tests the
        // rule today against itself: here every walk holds a trade every twentieth session, about thirteen a year, under
        // the 113, 150 and 188 trades three, four and five learning years read, so no fold chooses a setting.
        IReadOnlyList<IReadOnlyList<EquityBrief.Worker.Loop.ExitProcedures.Walked>> sparse =
        [
            .. walked.Select(one => (IReadOnlyList<EquityBrief.Worker.Loop.ExitProcedures.Walked>)[.. one.Where(trade => trade.Entry % 20 == 0)]),
        ];
        var thin = EquityBrief.Worker.Loop.LoopProcedures.Fundamentals(read, sparse, sparse[PlaceOf([0, 0, 1])], FundamentalsRule.Provisional, 63);

        Assert.Equal([null, null, null], thin.Folds.Select(one => one.Chosen));
        Assert.Null(thin.Words);
        Assert.Null(thin.Change);
        Assert.Equal(0, thin.StableFolds);
        Assert.All(thin.Evidence.Years, year => Assert.Equal(year.CurrentTotal, year.ProposedTotal));

        // A walk a setting short is refused rather than read against the wrong settings.
        Assert.Throws<ArgumentException>(() => EquityBrief.Worker.Loop.LoopProcedures.Fundamentals(read, [.. walked.Skip(1)], current, FundamentalsRule.Provisional, 63));
    }
}
