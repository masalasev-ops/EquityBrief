using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.4: the walk-forward tester's windows and purge worked by hand on a constructed ledger, a read
// past a fold's end refused, the gate's step-down over constructed blocks and the gate on both sides of its bar, each of
// the three screens on both sides of its edge, a book's months worked by hand, the floors read in proportion inside a
// fold, a fold short of trades adding nothing, and a run written whole and read back.
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
public partial class FixtureExpectations
{
    // The rows 17.4's tester adds that this check reaches: section 17's five rows and section 18's two.
    internal static readonly string[] LoopClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The tester's folds"),
        CheckReach.Key(Scope.LimitsTable, "The tester's bar"),
        CheckReach.Key(Scope.LimitsTable, "The tester's fixed level"),
        CheckReach.Key(Scope.LimitsTable, "The tester's stability screen"),
        CheckReach.Key(Scope.LimitsTable, "The tester's floors"),
        CheckReach.Key(Scope.FailureTable, "A fold short of trades"),
        CheckReach.Key(Scope.FailureTable, "A read past a fold's end"),
    ];

    // Every row 17.4's tester adds, named after phase 16's report until phase 17's own pair is checked: the component's
    // catalogue and matrix rows, its three stores and the seven above.
    internal static string[] LoopRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Walk-forward tester"),
        CheckReach.Key(Scope.MatrixTable, "Walk-forward tester"),
        CheckReach.Key(Scope.StoresTable, "Loop runs"),
        CheckReach.Key(Scope.StoresTable, "Loop proposals"),
        CheckReach.Key(Scope.StoresTable, "Loop tests"),
        .. LoopClaims,
    ];

    // Every weekday from the last week of 2021 to early March 2024, the calendar the windows are worked over by hand.
    static DateOnly[] LoopCalendar()
    {
        var days = new List<DateOnly>();

        for (var day = new DateOnly(2021, 12, 27); day <= new DateOnly(2024, 3, 8); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return [.. days];
    }

    static LedgerSetupRow LoopSetup(string ticker, DateOnly session, DateOnly? ended) =>
        new("pullback", ticker, session, true, ended is null ? "open" : "stop", ended, ended is null ? null : 5, ended is null ? null : -1.0, ended is null ? null : -1.1, 100m, 95m, 110m, null);

    [Fact]
    public void TheTestersWindowsAndPurgeAreWorkedByHandAndAReadPastAFoldsEndIsRefused()
    {
        var calendar = LoopCalendar();
        var folds = LoopFolds.Of(calendar, calendar[^1]);

        // 2022 and 2023 whole, each from its first weekday to its last, and 2024 to date, the newest and partial.
        Assert.Equal(
            [
                new LoopFold(2022, new(2022, 1, 3), new(2022, 12, 30), true),
                new LoopFold(2023, new(2023, 1, 2), new(2023, 12, 29), true),
                new LoopFold(2024, new(2024, 1, 1), new(2024, 3, 8), false),
            ],
            folds);

        // A setup ended the day before 2022's first session is learned on by the 2022 fold; one ended on that session,
        // one still open and one entered in 2022 are not, the last tested in 2022 and learned on by 2023.
        var before = LoopSetup("AAA", new(2021, 12, 28), new(2021, 12, 31));
        var onTheCut = LoopSetup("BBB", new(2021, 12, 29), new(2022, 1, 3));
        var open = LoopSetup("CCC", new(2021, 12, 30), null);
        var tested = LoopSetup("DDD", new(2022, 6, 1), new(2022, 6, 15));
        LedgerSetupRow[] ledger = [before, onTheCut, open, tested];

        var first = new FoldView<LedgerSetupRow>(folds[0], ledger, setup => setup.Session, setup => setup.EndedOn);
        var second = new FoldView<LedgerSetupRow>(folds[1], ledger, setup => setup.Session, setup => setup.EndedOn);

        Assert.Equal([before], first.Learning);
        Assert.Equal([before, onTheCut, tested], second.Learning);
        Assert.True(LoopFolds.Tests(folds[0], tested.Session));
        Assert.False(LoopFolds.Tests(folds[1], tested.Session));
        Assert.Equal(folds[0], LoopFolds.Holding(folds, tested.Session));
        Assert.Null(LoopFolds.Holding(folds, before.Session));

        // The session before the cut is handed over; the cut and any session after it are refused, naming both.
        Assert.Equal([before], first.Between(new(2021, 12, 27), new(2021, 12, 31)));
        Assert.Empty(first.On(new(2021, 12, 31)));

        var refused = Assert.Throws<FoldReadRefused>(() => first.On(new(2022, 1, 3)));

        Assert.Equal(new DateOnly(2022, 1, 3), refused.Session);
        Assert.Equal(2022, refused.Fold.Year);
        Assert.Contains("A read of 2022-01-03 was refused: the fold testing 2022 is cut at 2022-01-03", refused.Message, StringComparison.Ordinal);
        Assert.Throws<FoldReadRefused>(() => first.Between(new(2021, 12, 27), new(2022, 2, 1)));
        Assert.Throws<FoldReadRefused>(() => second.On(new(2024, 1, 2)));
    }

    static PairedUnit[] InBlocks(params double[] sums) => [.. sums.Select((sum, at) => new PairedUnit(at * Blocks.Sessions, sum, null))];

    static LoopEvidence Proposed(PairedUnit[] units) => new(false, units, [], [], [.. units.Select(unit => unit.Proposed!.Value)]);

    [Fact]
    public void TheGatesStepDownIsWorkedByHandOverConstructedBlocksAndTheGateOnBothSidesOfTheBar()
    {
        // Two proposals over three blocks, every sign vector worked by hand: A at 3, 1 and 2 studentises to 3.46 and B
        // at 1, -2 and 2 to 0.28. Two of the eight vectors reach 3.46 in either, the identity and the one turning B's
        // middle block, which reads B at 5.0, so A's adjusted p-value is a quarter where alone it is an eighth; four
        // reach 0.28 for B alone, so B's is a half, never under A's.
        double[] a = [3, 1, 2];
        double[] b = [1, -2, 2];

        Assert.Equal([0.25, 0.5], LoopGate.Adjusted([a, b]));
        Assert.Equal([0.5, 0.25], LoopGate.Adjusted([b, a]));
        Assert.Equal([0.125], LoopGate.Adjusted([a]));
        Assert.Equal(SignFlip.PValue(a), LoopGate.Adjusted([a])[0]);

        // A single proposal's adjusted p-value is its own sign-flip p-value over a wider record.
        double[] wide = [1.5, -0.25, 2, 0.75, -1, 3, 0.5, 1.25, -0.5, 2.5];

        Assert.Equal(SignFlip.PValue(wide), LoopGate.Adjusted([wide])[0]);

        // Eight blocks all one way: the identity alone reaches the observed, one in 256, under the bar of 0.05 over 12;
        // the eighth block turned against it, nine in 256, over it; seven blocks, under the floor, are not read.
        var newest = (9 * Blocks.Sessions) + 100;
        var held = LoopJudge.Judge([Proposed(InBlocks(1, 1, 1, 1, 1, 1, 1, 1))], 0, newest, 0)[0];
        var turned = LoopJudge.Judge([Proposed(InBlocks(1, 1, 1, 1, 1, 1, 1, -1))], 0, newest, 0)[0];
        var seven = LoopJudge.Judge([Proposed(InBlocks(1, 1, 1, 1, 1, 1, 1))], 0, newest, 0)[0];

        Assert.Equal(1.0 / 256, held.Adjusted);
        Assert.True(held.Adjusted <= LoopGate.Bar);
        Assert.True(held.Gate);
        Assert.Equal(9.0 / 256, turned.Adjusted);
        Assert.False(turned.Gate);
        Assert.Equal(7, seven.Blocks);
        Assert.Null(seven.Adjusted);
        Assert.False(seven.Gate);

        // A block whose outcome window has not closed by the newest session is not read: the cap past the eighth block's
        // last session leaves seven.
        var unclosed = LoopJudge.Judge([Proposed(InBlocks(1, 1, 1, 1, 1, 1, 1, 1))], 0, (8 * Blocks.Sessions) - 1 + 10, 11)[0];

        Assert.Equal(7, unclosed.Blocks);
    }

    [Fact]
    public void TheThreeScreensAreEachWorkedOnBothSidesOfTheirEdge()
    {
        static LoopYear Year(int year, double current, double proposed, int currentUnits = 60, int proposedUnits = 60, bool complete = true) =>
            new(year, complete, currentUnits, proposedUnits, current, proposed);

        // Three of five complete years better, the latest among them: three in five holds.
        Assert.Equal((true, 5, 3), LoopScreens.Stability([Year(2021, 1, 2), Year(2022, 2, 1), Year(2023, 1, 2), Year(2024, 2, 1), Year(2025, 1, 2)], LoopScreens.YearTrades));

        // Two of four, the latest among them: under three in five.
        Assert.Equal((false, 4, 2), LoopScreens.Stability([Year(2022, 1, 2), Year(2023, 2, 1), Year(2024, 2, 1), Year(2025, 1, 2)], LoopScreens.YearTrades));

        // Three of five with the latest not better: refused whatever the share.
        Assert.Equal((false, 5, 3), LoopScreens.Stability([Year(2021, 1, 2), Year(2022, 1, 2), Year(2023, 1, 2), Year(2024, 2, 1), Year(2025, 2, 1)], LoopScreens.YearTrades));

        // The latest complete year one trade short of its side is not counted, so the screen cannot hold; at 50 it is.
        Assert.False(LoopScreens.Stability([Year(2023, 1, 2), Year(2024, 1, 2), Year(2025, 1, 2, proposedUnits: 49)], LoopScreens.YearTrades).Passes);
        Assert.Equal((true, 3, 3), LoopScreens.Stability([Year(2023, 1, 2), Year(2024, 1, 2), Year(2025, 1, 2, proposedUnits: 50)], LoopScreens.YearTrades));

        // A partial year is drawn and never counted: its loss leaves the screen held, and its count out of it.
        Assert.Equal((true, 3, 3), LoopScreens.Stability([Year(2023, 1, 2), Year(2024, 1, 2), Year(2025, 1, 2), Year(2026, 5, 1, complete: false)], LoopScreens.YearTrades));

        // A book's year counts at twelve months a side and not at eleven.
        Assert.True(LoopScreens.Stability([Year(2025, 1, 2, 12, 12)], LoopScreens.Fewest(book: true)).Passes);
        Assert.False(LoopScreens.Stability([Year(2025, 1, 2, 12, 11)], LoopScreens.Fewest(book: true)).Passes);

        // The trimmed total: each side's five largest by size left out, the rule's 5, -4 and three of its ones, the
        // proposal's 10, 3 and three of its twos; 2 and 2 less 1 and 1 is 2, above nothing.
        Assert.Equal(2.0, LoopScreens.Trimmed([5, -4, 1, 1, 1, 1, 1], [10, 2, 2, 2, 2, 2, 3]));

        // The proposal's kept 1 and 0.5 against the rule's 1 and 1, -0.5, not above nothing; level, nothing; and two
        // sides holding no result, none.
        Assert.Equal(-0.5, LoopScreens.Trimmed([5, -4, 1, 1, 1, 1, 1], [10, 9, 8, 7, 6, 1, 0.5]));
        Assert.Equal(0.0, LoopScreens.Trimmed([1, 1, 1, 1, 1, 1], [1, 1, 1, 1, 1, 1]));
        Assert.Null(LoopScreens.Trimmed([], []));

        // The count: 300 trades and not 299, 36 months and not 35.
        Assert.True(LoopScreens.Counts(book: false, 300));
        Assert.False(LoopScreens.Counts(book: false, 299));
        Assert.True(LoopScreens.Counts(book: true, 36));
        Assert.False(LoopScreens.Counts(book: true, 35));

        // And the four together: a verdict passes only where every part holds.
        Assert.True(new LoopVerdict(0.001, 19, true, true, 4, 3, 1.5, 400, true, 0.1).Passes);
        Assert.False(new LoopVerdict(0.001, 19, true, true, 4, 3, 0.0, 400, true, 0.1).Passes);
        Assert.False(new LoopVerdict(0.001, 19, true, false, 4, 2, 1.5, 400, true, 0.1).Passes);
        Assert.False(new LoopVerdict(0.001, 19, true, true, 4, 3, 1.5, 299, false, 0.1).Passes);
        Assert.False(new LoopVerdict(0.01, 19, false, true, 4, 3, 1.5, 400, true, 0.1).Passes);
    }

    [Fact]
    public void ABooksMonthsAreWorkedByHand()
    {
        // Four months open on sessions 0, 20, 41 and 63, and the month opening on 84 is the newest, which no next month
        // closes. Each holding's return and its size cut's run evenly with the sessions held.
        static Func<int, int, double?> Even(double perSession) => (from, to) => (to - from) * perSession;

        var held = new BookHolding(0, 30, 0.002, Even(0.001), Even(0.0005));
        var later = new BookHolding(20, 63, 0.004, Even(0.002), Even(0));
        var soldAtTheOpen = new BookHolding(0, 20, 0, Even(0.003), Even(0));

        var months = BookMonths.Edges([0, 20, 41, 63, 84], [held, later, soldAtTheOpen]);

        // The first month: the first holding's 0.020 less its cut's 0.010 and its round trip of 0.002, 0.008, and the
        // third's 0.060, a mean of 0.034. The second: the first's ten sessions, 0.010 less 0.005, and the second's 0.042
        // less its round trip of 0.004, 0.038, a mean of 0.0215; the third, sold at the month's opening close, held in
        // none of it. The third month the second holding's 0.044 alone, and the fourth none, nothing held.
        Assert.Equal(4, months.Count);
        Assert.Equal([0, 20, 41, 63], months.Select(month => month.Opens));
        Assert.Equal(0.034, months[0].Edge!.Value, 12);
        Assert.Equal(0.0215, months[1].Edge!.Value, 12);
        Assert.Equal(0.044, months[2].Edge!.Value, 12);
        Assert.Null(months[3].Edge);

        // Paired with a book holding 0.010 in the first month, nothing in the second and -0.010 in the third: the
        // second counts its nothing against 0.0215, and the fourth, held by neither, is not a unit.
        var paired = BookMonths.Paired(months, [(0, 0.010), (20, null), (41, -0.010), (63, null)]);

        Assert.Equal([0, 20, 41], paired.Select(unit => unit.Session));
        Assert.Equal(0.024, paired[0].Difference, 12);
        Assert.Equal(0.0215, paired[1].Difference, 12);
        Assert.Equal(0.054, paired[2].Difference, 12);
    }

    static FamilyFigures LoopFigures(string key, int trades, double edge, int yearsBeating) =>
        new(key, trades, trades, trades, 500, edge, edge, new int[8], new double?[8], yearsBeating, null, null, null);

    [Fact]
    public void ASearchInsideAFoldReadsTheFloorsInProportionAndAFoldShortOfTradesAddsNothing()
    {
        // Three learning years read 300 times three over eight trades, 113 once rounded up, and six times three over
        // eight years, 3; four read 150 and 3; the history's eight the floors as stated.
        Assert.Equal((113, 3), LoopProcedures.Floors(3));
        Assert.Equal((150, 3), LoopProcedures.Floors(4));
        Assert.Equal((300, 6), LoopProcedures.Floors(8));

        // The last two of four learning years, -0.2 over 20 trades and 0.3 over 30, 0.1 together; of three, the second
        // year holds no trade and the third alone reads -0.2.
        int[] trades = [10, 0, 20, 30, 0, 0, 0, 0];
        double?[] edges = [0.1, null, -0.2, 0.3, null, null, null, null];

        Assert.Equal(0.1, LoopProcedures.LastTwoYears(trades, edges, 4)!.Value, 12);
        Assert.Equal(-0.2, LoopProcedures.LastTwoYears(trades, edges, 3)!.Value, 12);

        // Over three learning years a setting one trade short of 113 is not chosen, and one at 113 is, ahead of a
        // stronger one holding two of the three years above nothing.
        var grid = BreakoutSweep.Grid;
        var settings = grid.Settings;
        var shy = (settings[0], LoopFigures(grid.Key(settings[0]), 112, 0.3, 3));
        var held = (settings[1], LoopFigures(grid.Key(settings[1]), 113, 0.1, 3));
        var patchy = (settings[2], LoopFigures(grid.Key(settings[2]), 400, 0.5, 2));

        Assert.Null(LoopProcedures.Choose(grid, [shy, patchy], 3, lastTwo: false));
        Assert.Equal(settings[1], LoopProcedures.Choose(grid, [shy, held, patchy], 3, lastTwo: false));

        // The S&P 400 drift's last two learning years together stand above nothing as well: 0.1 over 30 and -0.3 over
        // 33 do not.
        var lastLosing = (settings[1], LoopFigures(grid.Key(settings[1]), 113, 0.1, 3) with { YearTrades = [50, 30, 33, 0, 0, 0, 0, 0], YearEdge = [0.2, 0.1, -0.3, null, null, null, null, null] });

        Assert.Null(LoopProcedures.Choose(grid, [lastLosing], 3, lastTwo: true));
        Assert.Equal(settings[1], LoopProcedures.Choose(grid, [lastLosing], 3, lastTwo: false));

        // A fold whose years before met no setting's floors reads the rule's own trades on its side: its year's two
        // totals are the rule's, every block sums to nothing, and the gate reads no difference at all. A fold that chose
        // a setting earning a risk more a trade is better in its own year alone.
        var calendar = LoopCalendar();
        var folds = LoopFolds.Of(calendar, calendar[^1]);
        (int Entry, double? Edge)[] rule = [.. Enumerable.Range(0, calendar.Length / 7).Select(at => (at * 7, (double?)((at % 3) - 1)))];
        (int Entry, double? Edge)[] richer = [.. rule.Select(one => (one.Entry, one.Edge + 1))];
        var opens = Array.IndexOf(calendar, folds[0].TestFrom);

        var none = LoopProcedures.Evidence(folds, calendar, [null, null, null], rule);
        var verdict = LoopJudge.Judge([none], opens, calendar.Length - 1, 0)[0];

        Assert.All(none.Years, year => Assert.Equal(year.CurrentTotal, year.ProposedTotal));
        Assert.All(none.Years, year => Assert.Equal(year.CurrentUnits, year.ProposedUnits));
        Assert.Equal(1.0, verdict.Adjusted);
        Assert.False(verdict.Gate);
        Assert.Equal(0, verdict.Better);
        Assert.Equal(0.0, verdict.Trimmed);

        var chose = LoopProcedures.Evidence(folds, calendar, [null, richer, null], rule);

        Assert.Equal([false, true, false], chose.Years.Select(year => year.Better));
        Assert.Equal(chose.Years[1].CurrentUnits, chose.Years[1].ProposedTotal - chose.Years[1].CurrentTotal, 9);
    }

    [Fact]
    public async Task TheTestersRunIsWrittenWholeAndReadBackRowForRowAndAnIndexWithNoProcedureIsRefused()
    {
        using var store = new TemporaryStore().Migrated();
        var output = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var tester = new WalkForwardTester(clock, store.DatabaseFile, store.Root, output, (_, _) => Task.CompletedTask);

        // The S&P 500 holds no Part 0 procedure and a month is read as yyyy-MM; both refused before the store is read.
        Assert.Equal(2, await tester.RunAsync("GSPC", null));
        Assert.Contains("name an index with '--index', MID or SML", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, await tester.RunAsync("MID", "2026-13"));
        Assert.Contains("'--month' takes a month as yyyy-MM", output.ToString(), StringComparison.Ordinal);

        var calendar = LoopCalendar();
        var folds = LoopFolds.Of(calendar, calendar[^1]);
        (int Entry, double? Edge)[] rule = [.. Enumerable.Range(0, calendar.Length / 7).Select(at => (at * 7, (double?)((at % 3) - 1)))];
        (int Entry, double? Edge)[] richer = [.. rule.Select(one => (one.Entry, one.Edge + 1))];
        var evidence = LoopProcedures.Evidence(folds, calendar, [null, richer, null], rule);
        var proposal = new LoopProposalRead(
            "breakout",
            LoopProcedures.Grid,
            "the breakout rule as proposed",
            "the breakout rule today",
            LoopProcedures.Risks,
            63,
            [(folds[0], null), (folds[1], "the setting the fold chose"), (folds[2], null)],
            1,
            evidence);
        var verdict = LoopJudge.Judge([evidence], Array.IndexOf(calendar, folds[0].TestFrom), calendar.Length - 1, 63)[0];

        await tester.WriteAsync("loop-test-MID-20261009T120000Z", "2026-10", "MID", calendar[^1], clock.UtcNow, folds.Count, [new WalkForwardTester.Tested(proposal, verdict)]);

        using var connection = store.Open();

        static List<string> Rows(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();

            command.CommandText = sql;

            using var reader = command.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
            {
                rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(column => reader.IsDBNull(column) ? "null" : Convert.ToString(reader.GetValue(column), System.Globalization.CultureInfo.InvariantCulture))));
            }

            return rows;
        }

        Assert.Equal(
            ["loop-test-MID-20261009T120000Z|2026-10|MID|2024-03-08|2026-10-09T12:00:00Z|2026-10-09T12:00:00Z|3"],
            Rows(connection, "SELECT run_id, month, index_code, through, started_at, ended_at, folds FROM loop_run;"));

        var stored = Rows(connection, "SELECT family, proposal, words, current_words, unit, units, blocks, gate, stable, counted, better, counts, stable_folds, passed FROM loop_proposal;");

        Assert.Equal(
            [FormattableString.Invariant($"breakout|the grid|the breakout rule as proposed|the breakout rule today|risks|{verdict.Units}|{verdict.Blocks}|{(verdict.Gate ? 1 : 0)}|{(verdict.Stable ? 1 : 0)}|{verdict.Counted}|{verdict.Better}|{(verdict.Counts ? 1 : 0)}|1|0")],
            stored);
        Assert.Equal(
            [
                FormattableString.Invariant($"2022|1|null|{evidence.Years[0].CurrentUnits}|{evidence.Years[0].ProposedUnits}"),
                FormattableString.Invariant($"2023|1|the setting the fold chose|{evidence.Years[1].CurrentUnits}|{evidence.Years[1].ProposedUnits}"),
                FormattableString.Invariant($"2024|0|null|{evidence.Years[2].CurrentUnits}|{evidence.Years[2].ProposedUnits}"),
            ],
            Rows(connection, "SELECT year, complete, chosen, current_units, proposed_units FROM loop_test ORDER BY year;"));
        Assert.Equal(
            [.. evidence.Years.Select(year => FormattableString.Invariant($"{year.CurrentTotal:R}|{year.ProposedTotal:R}"))],
            Rows(connection, "SELECT current_total, proposed_total FROM loop_test ORDER BY year;"));
    }
}
