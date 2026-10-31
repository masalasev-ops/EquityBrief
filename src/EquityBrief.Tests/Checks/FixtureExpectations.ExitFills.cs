using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Fills;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.14: every swing trade a close's signal ends sold at the open of the session after that close,
// each figure worked by hand; the fill step selling a stored trade there and waiting while that open is not stored; the
// drift surprises check over a constructed store, writing nothing; the per-rule test's flag split and the graded
// exposure's reversed VIX worked by hand; and no source a rule's version pins among those 21.14 writes.
// see: A swing trade sold on a close's signal is sold at the next session's open by the fill step, and no rule's version moves because of it
public partial class FixtureExpectations
{
    // The rows 21.14 adds to the architecture: the drift surprises check's catalogue and matrix rows.
    internal static readonly string[] ExitFillRows =
    [
        CheckReach.Key(Scope.CatalogueTable, "Drift surprises check"),
        CheckReach.Key(Scope.MatrixTable, "Drift surprises check"),
    ];

    // The sources 21.14 writes, none of which a rule's version pins.
    static readonly string[] ExitFillSources =
    [
        "src/EquityBrief.Core/Ledger/ExitFills.cs",
        "src/EquityBrief.Core/Cards/Management.cs",
        "src/EquityBrief.Worker/Fills/TradeFiller.cs",
        "src/EquityBrief.Worker/Loop/OpenFills.cs",
        "src/EquityBrief.Worker/Loop/ExitProcedures.cs",
        "src/EquityBrief.Worker/Loop/RuleWalk.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/LoopReadings.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Cards/RuleReplay.cs",
        "src/EquityBrief.Worker/Cards/RuleRecorder.cs",
        "src/EquityBrief.Worker/Cards/TakenFollower.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.Exits.cs",
        "src/EquityBrief.Worker/Families/DriftSurprises.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Own.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.OwnPullback.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Dip.cs",
    ];

    // A plan bought at 100's close with its stop at 95, five a risk, its target at 110 and five sessions; closes and
    // opens oldest first, the listing at bar 0.
    [Fact]
    public void AStopAndATargetAreSoldAtTheNextOpenAndTheCapAtItsCloseWorkedByHand()
    {
        double[] opens = [100, 99, 97, 93, 92, 91, 90];
        double? After(double[] path, int bar) => bar + 1 < path.Length && bar + 1 < opens.Length ? opens[bar + 1] : null;

        // The stop: bought at bar 1's open, 99, it closes through 95 at 94 on bar 2 and is sold at bar 3's open, 93,
        // (93 - 99) / 5 = -1.2, the close's -1 bought at the open moved by (93 - 94) / 5.
        double[] stopped = [100, 98, 94, 93, 92, 91, 90];
        var bought = TradeFills.AtOpen((94 - 100) / 5.0, 100, 5, 99);
        var sale = ExitFills.Sold(new SetupOutcome(bought, 2, SetupEnds.Stop), at => stopped[at], at => After(stopped, at), 0, 5);

        Assert.Equal((true, 3, -1.2), (sale.AtOpen, sale.Bar, Math.Round(sale.Result!.Value, 12)));
        Assert.Equal(SetupEnds.Stop, ExitFills.EndOf(at => stopped[at], 0, 100, 95, 110, null, 2, 5));

        // The target: a close at 111 on bar 2 sold at bar 3's open of 113, 2.2 at the close moved by (113 - 111) / 5.
        double[] high = [100, 105, 111, 112, 112, 112, 112];
        double[] highOpens = [100, 101, 110, 113, 112, 112, 112];
        var target = ExitFills.Sold(new SetupOutcome(2.2, 2, SetupEnds.Target), at => high[at], at => at + 1 < highOpens.Length ? highOpens[at + 1] : null, 0, 5);

        Assert.Equal((true, 3, 2.6), (target.AtOpen, target.Bar, Math.Round(target.Result!.Value, 12)));

        // The cap: five closes reaching neither level, sold at the fifth's own close, its result as it stood.
        double[] capped = [100, 101, 102, 103, 104, 106, 120];
        var cap = ExitFills.Sold(new SetupOutcome(1.2, 5, SetupEnds.Cap), at => capped[at], at => After(capped, at), 0, 5);

        Assert.Equal((false, 5, 1.2), (cap.AtOpen, cap.Bar, cap.Result));
        Assert.Equal(SetupEnds.Cap, ExitFills.EndOf(at => capped[at], 0, 100, 95, 110, null, 5, 5));

        // A stop on the newest close held waits on the open after it, with no result.
        double[] newest = [100, 98, 94];
        var waiting = ExitFills.Sold(new SetupOutcome(-1.2, 2, SetupEnds.Stop), at => newest[at], at => After(newest, at), 0, 5);

        Assert.Equal((true, 3, null as double?), (waiting.AtOpen, waiting.Bar, waiting.Result));

        // A time exit and the dip's own end sell at their close.
        Assert.False(ExitFills.AtNextOpen(SetupEnds.Time));
        Assert.False(ExitFills.AtNextOpen(SetupEnds.Exit));
        Assert.True(ExitFills.AtNextOpen(SetupEnds.Trail));
    }

    [Fact]
    public void ATrailOnItsCapsSessionAndAHalfSoldFirstAreReadAsTheWalksSellThem()
    {
        // A trail of 4 under the highest close: 100, 108, 110, then 105 on the cap's session three, under 110 - 4 = 106,
        // is the trail's end, sold at the next open; at 107 it is the cap's.
        double[] under = [100, 108, 110, 105];
        double[] over = [100, 108, 110, 107];

        Assert.Equal(SetupEnds.Trail, ExitFills.EndOf(at => under[at], 0, 100, 96, null, 4, 3, 3));
        Assert.Equal(SetupEnds.Cap, ExitFills.EndOf(at => over[at], 0, 100, 96, null, 4, 3, 3));

        // Half sold once a close reaches one risk, 105: bar 2's close of 106 sells the first half at bar 3's open, 104,
        // half of (104 - 106) / 5 = -0.2; the rest ends at its stop on bar 4 and sells at bar 5's open, 93, half of
        // (93 - 94) / 5 = -0.1; the whole's 0.0 at the closes is -0.3.
        double[] closes = [100, 102, 106, 103, 94, 93];
        double[] opens = [100, 101, 105, 104, 95, 93];
        var anchor = new SetupAnchor(DateOnly.MinValue, 100, 95, 115, null, 10, null);
        var choice = ExitMenu.Swing.First(one => one.Kind == ExitKind.HalfBreakEven);
        var outcome = new SetupOutcome(0.0, 4, SetupEnds.Stop);
        var half = ExitFills.HalfSoldOn(at => closes[at], 0, anchor, choice, outcome);

        Assert.Equal(2, half);
        Assert.Equal(-0.3, Math.Round(ExitFills.Sold(outcome, at => closes[at], at => at + 1 < opens.Length ? opens[at + 1] : null, 0, 5, half).Result!.Value, 12));
        Assert.Null(ExitFills.HalfSoldOn(at => closes[at], 0, anchor, null, outcome));
    }

    [Fact]
    public void TheFillStepSellsAStoredTradeAtTheNextOpenAndWaitsWhileThatOpenIsNotStored()
    {
        var night = new DateOnly(2026, 10, 1);
        var trade = new TradeFiller.StoredTrade("family_trade", "GSPC", "the drift rule", "KEY", night, 100, 95, 110, 1.0, -1.2, null, null)
        {
            EndedOn = night.AddDays(2),
            Cap = 60,
        };

        // Bought at 99 the session after, the close's -1.2 is -1.0 at the open; it closed through its stop at 94 on its
        // second session and is sold at the third's open, 93: -1.2 again, sold on that session at 93.
        IReadOnlyList<(DateOnly, double, double)> path = [(night, 100, 100), (night.AddDays(1), 99, 98), (night.AddDays(2), 96, 94), (night.AddDays(3), 93, 93)];
        var sold = TradeFiller.Sold(trade, -1.0, path);

        Assert.Equal((night.AddDays(3), 93.0, -1.2), (sold.On, sold.Price, Math.Round(sold.Result!.Value, 12)));

        // With the third session not stored yet it waits, no result and no sale.
        var waiting = TradeFiller.Sold(trade, -1.0, [.. path.Take(3)]);

        Assert.Equal((null as DateOnly?, null as double?, null as double?), (waiting.On, waiting.Price, waiting.Result));

        // A scored plan unresolved at its cap sells at its own close, as stated in shares of its close.
        var plan = new TradeFiller.StoredTrade("forward_return", "GSPC", "setup", "KEY", night, 1, 0.95, 1.1, null, 0.4, null, null, Relative: true)
        {
            EndedOn = night.AddDays(2),
            Outcome = "unresolved",
        };
        var capped = TradeFiller.Sold(plan, 0.6, [(night, 100, 100), (night.AddDays(1), 99, 101), (night.AddDays(2), 102, 102), (night.AddDays(3), 105, 104)]);

        Assert.Equal((night.AddDays(2), 102.0, 0.6), (capped.On, capped.Price, capped.Result));
    }

    [Fact]
    public async Task TheDriftSurprisesCheckCountsTheRowsTheCalendarRevisedAndWritesNothing()
    {
        var night = new DateOnly(2026, 10, 1);
        SurpriseRead[] rows =
        [
            new(night, "AAA", 21.65, 12.37, true),
            new(night, "BBB", -9.09, 118.18, false),
            new(night, "CCC", 5.0, 5.001, false),
            new(night.AddDays(1), "AAA", 21.65, null, false),
        ];

        // Two rows differ to two decimals, AAA's by 9.28 and BBB's by 127.27, so the median is their mean, 68.275; BBB's
        // turns from a miss to a beat; of the two only AAA's passed.
        var check = DriftSurprises.Of(rows, (3, new DateOnly(2018, 1, 2), new DateOnly(2026, 10, 1), 2));

        Assert.Equal((2, 4, 3, 2, 2, 68.275, "BBB", 1), (check.Nights, check.Rows, check.Stocks, check.Differ, check.DifferStocks, Math.Round(check.Median!.Value, 6), check.Largest!.Ticker, check.Flipped));
        Assert.Equal(["BBB"], check.FlippedStocks);
        Assert.Equal(["AAA"], check.PassedStocks);
        Assert.EndsWith("printed and nothing written", DriftSurprises.Lines(check)[^1], StringComparison.Ordinal);

        // Over a constructed store: the drift row it reads and the pulled surprises it counts, every table's rows the same
        // after the run as before it.
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO family_result (session_date, ticker, family, passed, missed, order_by, exclusions, gates) VALUES ('2026-10-01', 'AAA', 'drift', 1, 0, 21.65, '[]', '[]');");
        store.Execute("INSERT INTO pulled_surprise (ticker, event_date, timing, surprise_percent, pull) VALUES ('AAA', '2018-01-02', 'before', 3.5, 'pull-1'), ('BBB', '2026-09-30', 'after', -1.0, 'pull-2'), ('CCC', '2026-09-30', 'after', NULL, 'pull-2');");

        var before = Counted(store);
        var said = new StringWriter();

        Assert.Equal(0, await new DriftSurprises(store.DatabaseFile, said).RunAsync());
        Assert.Equal(before, Counted(store));
        Assert.Contains("1 drift row(s) over 1 stock(s) on 1 night(s) stored a surprise", said.ToString(), StringComparison.Ordinal);
        Assert.Contains("2 print(s) with a figure from 2018-01-02 to 2026-09-30, written by 2 pull(s)", said.ToString(), StringComparison.Ordinal);
    }

    // Every table's row count in a store.
    static IReadOnlyList<string> Counted(TemporaryStore store)
    {
        var tables = Query(store, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");

        return [.. tables.Select(table => table + "=" + Query(store, $"SELECT COUNT(*) FROM \"{table}\";").Single())];
    }

    [Fact]
    public void TheGradedExposuresVixReversedHoldsItsSwitchOverTheMedianWorkedByHand()
    {
        var hooks = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExposureParameter] = 0.45 });

        double?[] Night(double breadth, double vixOverMedian)
        {
            var readings = new double?[LedgerReadings.Count];

            readings[RuleHooks.BreadthColumn] = breadth;
            readings[RuleHooks.VixOverMedianColumn] = vixOverMedian;

            return readings;
        }

        // The VIX a fifth over its median: the graded walk counts it against the night, the reversed for it; a tenth under,
        // the other way round; the breadth at the floor holding either way, so five picks against two.
        Assert.Equal((2, 5), (hooks.PicksUnder(Night(0.5, 1.2)), hooks.PicksUnder(LoopReadings.Reflected(Night(0.5, 1.2)))));
        Assert.Equal((5, 2), (hooks.PicksUnder(Night(0.5, 0.9)), hooks.PicksUnder(LoopReadings.Reflected(Night(0.5, 0.9)))));

        // Under the breadth's floor the VIX alone grades the night, two or none.
        Assert.Equal((0, 2), (hooks.PicksUnder(Night(0.4, 1.2)), hooks.PicksUnder(LoopReadings.Reflected(Night(0.4, 1.2)))));

        // The reflection is about the median, 2 - 1.2 = 0.8, leaves the night's readings whole and every other reading.
        var night = Night(0.5, 1.2);
        var reflected = LoopReadings.Reflected(night);

        Assert.Equal((0.8, 1.2, 0.5), (Math.Round(reflected[RuleHooks.VixOverMedianColumn]!.Value, 12), night[RuleHooks.VixOverMedianColumn]!.Value, reflected[RuleHooks.BreadthColumn]!.Value));
    }

    [Fact]
    public void AFlagFirstRulesMonthsAreSplitAtTheModelsCutoffBesideTheSameSettingsWithoutTheFlag()
    {
        DateOnly[] calendar = [new(2024, 3, 1), new(2024, 9, 3), new(2025, 2, 3), new(2025, 6, 2)];
        var rule = new RuleUnderTest("fundamentals", "the Fundamentals rule", "live", "words", LoopProcedures.Points, 252, Months: [(0, 0.02), (1, 0.04), (2, -0.01), (3, null)], Unflagged: [(0, 0.01), (2, 0.03), (3, 0.05)]);

        // Before 2025 two months at +3.00 points, from it one at -1.00, its fourth still open; without the flag one at +1.00
        // and two at +4.00.
        Assert.Equal(
            "fundamentals, the Fundamentals rule: its flag first, 2 month(s) at +3.00 points before the local model's cutoff's year, 2025, and 1 month(s) at -1.00 points from it; the same settings without the flag, walked unregistered and written nowhere, 1 month(s) at +1.00 points and 2 month(s) at +4.00 points",
            RuleTests.FlagLine(rule, calendar, new DateOnly(2025, 1, 31)));
    }

    [Fact]
    public void NoSourceARulesVersionPinsIsOne21Point14Writes()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(ExitFillSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(ExitFillSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Core/Ledger/TradeFills.cs", "src/EquityBrief.Core/Ledger/SetupAnchor.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }
}
