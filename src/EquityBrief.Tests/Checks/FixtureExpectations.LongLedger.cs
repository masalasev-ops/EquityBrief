using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Chart;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Ledger;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.6: the long reader's series worked by hand over a constructed store, a bad print read past, a start
// honoured, a history's end cutting the series at its mark and the long prices scaled onto the store's own closes after them; a
// trade, a bankruptcy and a benchmark member whose prices stop worked by hand under both readings; the standing years' result
// at the last price, the crossing of an unexplained jump, a family's year under one reading and the older jumps' comparison
// on both sides; no row of `setup` moving, no card, hook or night reading the new tables and no source a rule's version pins
// among those 23.6 writes.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public partial class FixtureExpectations
{
    internal static string[] LongLedgerClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The long tables' budget"),
        CheckReach.Key(Scope.FailureTable, "A long ledger session the build cannot read"),
    ];

    internal static string[] LongLedgerRows => [.. ComponentAccess.LongLedgerRows, .. SchemaColumns.LongLedgerStoreRows, .. LongLedgerClaims, .. Reading.ReadSurface.LongYearsRows];

    // A long price row, its open, high and low at its close.
    static string LongBarRow(string ticker, string day, decimal close) =>
        FormattableString.Invariant($"('{ticker}', '{day}', '{close}', '{close}', '{close}', '{close}', '{close}', 1000, 'r')");

    [Fact]
    public async Task TheLongReadersSeriesReadsPastABadPrintStartsAtItsMarkEndsAtItsHistorysEndAndMeetsTheStoresOwnClosesAtTheirScale()
    {
        using var store = new TemporaryStore().Migrated();
        string[] days = ["2005-01-03", "2005-01-04", "2005-01-05", "2005-01-06", "2005-01-07", "2005-01-10"];
        decimal[] first = [10m, 11m, 99m, 12m, 13m, 14m];
        decimal[] second = [20m, 20m, 20m, 20m, 20m, 21m];

        store.Execute("INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('MID', 'AAA', '2005-01-03', NULL, 'fund', 'r'), ('MID', 'BBB', '2005-01-03', '2005-01-05', 'fund', 'r'), ('MID', 'BBB', '2005-01-07', NULL, 'fund', 'r'), ('SML', 'CCC', '2005-01-03', NULL, 'fund', 'r');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days.Select((day, at) => LongBarRow("AAA", day, first[at])).Concat(days.Select((day, at) => LongBarRow("BBB", day, second[at])))) + ";");
        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('BBB', '2005-01-07', '40', '40', '40', '40', 1000, 'test', '2005-01-07T22:00:00Z', '40'), ('BBB', '2005-01-10', '42', '42', '42', '42', 1000, 'test', '2005-01-10T22:00:00Z', '42');");
        store.Execute("INSERT INTO session_mark (ticker, session_date, kind, says, ruled, run_id) VALUES "
            + $"('AAA', '2005-01-04', '{SessionMarking.StartsHere}', 's', '2026-11-08T00:00:00Z', 'r'), "
            + $"('AAA', '2005-01-05', '{SessionMarking.BadPrint}', 's', '2026-11-08T00:00:00Z', 'r'), "
            + $"('AAA', '2005-01-07', '{SessionMarking.HistoryEnds}', 's', '2026-11-08T00:00:00Z', 'r');");
        store.Execute("INSERT INTO long_print (ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, read_from, run_id) VALUES ('BBB', '2005-01-05', 'after', '1', '0.9', 5.0, 'provider', 'r'), ('BBB', '2005-01-06', 'unstated', NULL, NULL, NULL, '8-K item 2.02', 'r');");

        var history = new LongHistoryReader(store.DatabaseFile);

        Assert.Equal(new DateOnly(2005, 1, 10), await history.NewestSessionAsync());

        var read = await history.ReadAsync("MID", new DateOnly(2005, 1, 10));
        var aaa = read.Names.Single(name => name.Ticker == "AAA");
        var bbb = read.Names.Single(name => name.Ticker == "BBB");

        Assert.Equal(["AAA", "BBB"], read.Names.Select(name => name.Ticker));
        Assert.Equal([(new DateOnly(2005, 1, 4), 11m), (new DateOnly(2005, 1, 6), 12m), (new DateOnly(2005, 1, 7), 13m)], aaa.Bars.Select(bar => (bar.Session, bar.Close)));
        Assert.Equal([40m, 40m, 40m, 40m, 40m, 42m], bbb.Bars.Select(bar => bar.Close));
        Assert.Equal(2, bbb.Spans.Count);
        Assert.True(bbb.MemberOn(new DateOnly(2005, 1, 4)));
        Assert.False(bbb.MemberOn(new DateOnly(2005, 1, 6)));
        Assert.True(bbb.MemberOn(new DateOnly(2005, 1, 10)));
        Assert.Equal([new DateOnly(2005, 1, 5), new DateOnly(2005, 1, 6)], bbb.Prints);
        Assert.Equal([(new DateOnly(2005, 1, 5), true, 5.0)], bbb.Surprises.Select(surprise => (surprise.EventDate, surprise.After, surprise.Percent)));
        Assert.All(read.Names, name => Assert.False(name.Survivor));
        Assert.Equal(["CCC"], (await history.ReadAsync("SML", new DateOnly(2005, 1, 10))).Names.Select(name => name.Ticker));
    }

    // A move checked and kept reads as it stands, and a series no mark names is returned whole.
    [Fact]
    public void AMarkCheckedAndKeptMovesNothingAndTheLatestStartAndEarliestEndStand()
    {
        var bars = Enumerable.Range(0, 5).Select(at => new Core.Sweep.SweepBar(new DateOnly(2006, 3, 1).AddDays(at), 1m, 1m, 10m + at, 1)).ToArray();

        Assert.Equal(bars, LongLedger.Marked(bars, []));
        Assert.Equal(bars, LongLedger.Marked(bars, [new SessionMark(bars[2].Session, SessionMarking.CheckedAndKept, "kept")]));
        Assert.Equal(
            [bars[2].Session, bars[3].Session],
            LongLedger.Marked(bars,
            [
                new SessionMark(bars[1].Session, SessionMarking.StartsHere, "s"),
                new SessionMark(bars[2].Session, SessionMarking.StartsHere, "s"),
                new SessionMark(bars[3].Session, SessionMarking.HistoryEnds, "e"),
                new SessionMark(bars[4].Session, SessionMarking.HistoryEnds, "e"),
            ]).Select(bar => bar.Session));
    }

    // A trade whose stock's prices stop while it is open has no result as ruled and ends at its last price with its series
    // held there: bought at 10 with its stop at 9, its target at 13 and a cap of 5, its prices stopping at 10.8, it ends at the
    // cap's close of 10.8, 0.8 of its risk. A bankruptcy's last close of 0.4 under the stop is sold as ruled at an open its
    // store never holds and so is never a result; held at its last price it is sold at 0.4, the loss it was, 9.6 risks.
    [Fact]
    public void ATradeAndABankruptcyWhosePricesStopEndAtTheirLastPriceHeldThereAndHaveNoResultAsRuled()
    {
        var anchor = new SetupAnchor(new DateOnly(2008, 9, 2), 10, 9, 13, null, 5, 1);
        double[] closes = [10, 10.4, 10.8];

        Assert.Null(SetupReplay.Replay(closes, 0, anchor).Result);

        var held = LongLedger.HeldFlat(closes, 6);
        var walked = SetupReplay.Replay(held, 0, anchor);

        Assert.Equal([10, 10.4, 10.8, 10.8, 10.8, 10.8, 10.8, 10.8, 10.8], held);
        Assert.Equal(0.8, walked.Result!.Value, 9);
        Assert.Equal(SetupEnds.Cap, walked.End);

        double[] failing = [10, 9.6, 0.4];
        double[] opens = [10, 9.7, 0.5];
        var stopped = SetupReplay.Replay(failing, 0, anchor);

        Assert.Equal(-9.6, stopped.Result!.Value, 9);
        Assert.Null(ExitFills.Sold(stopped, at => failing[at], at => at + 1 < opens.Length ? opens[at + 1] : null, 0, anchor.Risk).Result);

        var heldCloses = LongLedger.HeldFlat(failing, 3);
        var heldOpens = LongLedger.HeldFlat(opens, 3, failing[^1]);

        Assert.Equal([10, 9.7, 0.5, 0.4, 0.4, 0.4], heldOpens);

        Assert.Equal(-9.6, ExitFills.Sold(SetupReplay.Replay(heldCloses, 0, anchor), at => heldCloses[at], at => at + 1 < heldOpens.Length ? heldOpens[at + 1] : null, 0, anchor.Risk).Result!.Value, 9);
        Assert.Same(failing, LongLedger.HeldFlat(failing, 0));
    }

    // A benchmark member whose prices stop with its path open is left out as ruled, the benchmark settling on the member that
    // ran to its cap, 1.0; held at its last price of 10.5 it ends at the cap there, 0.5, and the benchmark reads their mean,
    // 0.75, with no member left out.
    [Fact]
    public void ABenchmarkMemberWhosePricesStopIsLeftOutAsRuledAndCountedAtItsLastPriceHeldThere()
    {
        var plan = new SetupPlan(1, 3, null, 5);
        var session = new DateOnly(2008, 9, 2);
        double[] running = [10, 10, 10, 10, 10, 11, 11];
        double[] stopping = [10, 10.5];

        var (asRuled, _) = SetupBenchmark.WithMembers([(running, running, 0, 1.0, false), (stopping, stopping, 0, 1.0, true)], session, plan);

        Assert.Equal(1.0, asRuled.Mean!.Value, 9);
        Assert.Equal((1, 2, 1), (asRuled.Ended, asRuled.Entered, asRuled.Stopped));
        Assert.True(asRuled.Settled);

        var held = LongLedger.HeldFlat(stopping, 5);
        var (lastPrice, _) = SetupBenchmark.WithMembers([(running, running, 0, 1.0, false), (held, held, 0, 1.0, false)], session, plan);

        Assert.Equal(0.75, lastPrice.Mean!.Value, 9);
        Assert.Equal((2, 2, 0), (lastPrice.Ended, lastPrice.Entered, lastPrice.Stopped));
    }

    // A standing trade at its last price: a swing trade bought at 10.2 on an entry of 10 with its stop at 9 and a last close of
    // 9.4, -0.8 of its risk; a book bought at 50 with a last close of 40, -20%; none bought at no price or with its stop over
    // its entry.
    [Fact]
    public void AStandingTradeAtItsLastPriceReadsInItsFamilysForm()
    {
        Assert.Equal(-0.8, LongLedger.AtLastPrice(book: false, 10.2, 10, 9, 9.4)!.Value, 9);
        Assert.Equal(-0.2, LongLedger.AtLastPrice(book: true, 50, 50, 0, 40)!.Value, 9);
        Assert.Null(LongLedger.AtLastPrice(book: false, 0, 10, 9, 9.4));
        Assert.Null(LongLedger.AtLastPrice(book: false, 10, 9, 9, 9.4));
    }

    // A hold crosses a jump standing after its session and on or before its end, and an open hold none; a family's year counts
    // its setups, the settled, those at target and the means over the settled, the crossing one apart.
    [Fact]
    public void AJumpIsCrossedInsideTheHoldAloneAndAFamilysYearSetsTheCrossingTradesApart()
    {
        DateOnly[] jumps = [new(2010, 5, 6)];

        Assert.True(LongLedger.Crosses(jumps, new DateOnly(2010, 5, 3), new DateOnly(2010, 5, 6)));
        Assert.False(LongLedger.Crosses(jumps, new DateOnly(2010, 5, 6), new DateOnly(2010, 5, 20)));
        Assert.False(LongLedger.Crosses(jumps, new DateOnly(2010, 5, 3), new DateOnly(2010, 5, 5)));
        Assert.False(LongLedger.Crosses(jumps, new DateOnly(2010, 5, 3), null));

        // A member's jump is set apart only while it is a member: joined on the day and gone on the day it left.
        (DateOnly, DateOnly?)[] spans = [(new DateOnly(2010, 5, 6), new DateOnly(2010, 6, 1)), (new DateOnly(2011, 1, 3), null)];

        Assert.True(LongLedger.MemberOn(spans, new DateOnly(2010, 5, 6)));
        Assert.False(LongLedger.MemberOn(spans, new DateOnly(2010, 6, 1)));
        Assert.False(LongLedger.MemberOn(spans, new DateOnly(2010, 12, 31)));
        Assert.True(LongLedger.MemberOn(spans, new DateOnly(2026, 11, 6)));

        var year = Assert.Single(LongLedger.Years(
        [
            new LongTrade("pullback", 2010, true, true, 1.0, 0.9, false),
            new LongTrade("pullback", 2010, true, false, -1.0, -1.1, true),
            new LongTrade("pullback", 2010, true, false, 0.5, 0.4, false),
            new LongTrade("pullback", 2010, false, false, null, null, false),
        ], LongLedger.AsRuled));

        Assert.Equal(("pullback", 2010, LongLedger.AsRuled, 4, 3, 1, 1), (year.Family, year.Year, year.Reading, year.Setups, year.Settled, year.Targets, year.Crossing));
        Assert.Equal(0.5 / 3, year.EdgeMean!.Value, 9);
        Assert.Equal(0.2 / 3, year.EdgeAfterCostMean!.Value, 9);
        Assert.Equal(0.65, year.EdgeAfterCostWithout!.Value, 9);
    }

    // The older decade's unexplained jumps a stock-year against twice the newer years': 21 over 100 against 10 over 100 is
    // above it, 20 over 100 is not.
    [Fact]
    public void TheOlderJumpsAreAboveTwiceTheNewerYearsOnlyPastTwice()
    {
        Assert.True(LongLedger.OlderJumpsAbove(21, 100, 10, 100));
        Assert.False(LongLedger.OlderJumpsAbove(20, 100, 10, 100));
        Assert.False(LongLedger.OlderJumpsAbove(5, 0, 10, 100));
        Assert.Equal("2005 to 2009", LongLedger.EraOf(2008));
        Assert.Equal("2020 on", LongLedger.EraOf(2026));
        Assert.False(LongLedger.ChosenOn(2018));
        Assert.True(LongLedger.ChosenOn(2019));
    }

    static readonly string[] LongLedgerTables = ["long_setup", "long_reading", "departed_setup"];

    internal static readonly string[] LongLedgerSources =
    [
        "src/EquityBrief.Core/History/LongLedger.cs",
        "src/EquityBrief.Worker/History/LongHistoryReader.cs",
        "src/EquityBrief.Worker/History/LongLedgerReader.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.Long.cs",
        "src/EquityBrief.Api/Reading/ReadApi.LongYears.cs",
        "src/EquityBrief.Web/App/SinglePageApp.LongYears.cs",
    ];

    // The long ledger writes only its own tables, a session once written frozen, and no row of `setup` moves; no card, hook or
    // night reads them, the History page's read and the measures alone, from 23.9 the sector splits' and from 23.8 the buy and sell points' among them; and no source a rule's version or the readings'
    // version pins is one 23.6 writes.
    [Fact]
    public void NoRowOfSetupMovesNoNightOrCardReadsTheLongLedgerAndNoSourceARulesVersionPinsIsOne23Point6Writes()
    {
        var longBuild = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger", "SetupLedger.Long.cs"));
        var ledger = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger", "SetupLedger.cs"));

        // The long build's own source runs none of the standing ledger's writes but through its statements, every one of
        // which names a long table or the standing ones the night writes, and nothing anywhere updates or removes a long setup.
        Assert.DoesNotMatch(new Regex(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+setup\b", RegexOptions.IgnoreCase), longBuild);
        Assert.DoesNotMatch(new Regex(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+setup\b", RegexOptions.IgnoreCase), Regex.Match(ledger, "static readonly string InsertLongSetup.*?const string InsertLongReading.*?;\";", RegexOptions.Singleline).Value);
        Assert.DoesNotMatch(new Regex(@"\b(UPDATE|DELETE\s+FROM)\s+long_setup\b", RegexOptions.IgnoreCase), longBuild + ledger);
        Assert.All(new[] { "ClearTheNight", "ClearTheNightsRows", "InsertSetup", "Settle", "InsertNight" }, statement => Assert.DoesNotMatch($@"\b{statement}\b", longBuild));
        Assert.Equal(Core.Components.Touch.Read | Core.Components.Touch.Insert, SetupLedger.Access.Stores.Single(touch => touch.Store == Core.Components.Store.LongSetup).Touch);

        var readers = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}EquityBrief.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => LongLedgerTables.Any(table => Regex.IsMatch(File.ReadAllText(path), $@"\b{table}\b")))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["src/EquityBrief.Api/Reading/ReadApi.LongYears.cs", "src/EquityBrief.Data/Migrations/SchemaMigrations.cs", "src/EquityBrief.Worker/History/LongLedgerReader.cs", "src/EquityBrief.Worker/History/SectorSplitsReader.cs", "src/EquityBrief.Worker/Ledger/SetupLedger.Long.cs", "src/EquityBrief.Worker/Ledger/SetupLedger.cs", "src/EquityBrief.Worker/Loop/PlanPointTest.cs"],
            readers);

        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(LongLedgerSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(LongLedgerSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Worker/Sweep/SweepHistory.cs", pinned);
    }
}
