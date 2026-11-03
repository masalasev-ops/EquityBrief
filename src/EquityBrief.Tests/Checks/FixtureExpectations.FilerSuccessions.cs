using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 22.24: a company that re-filed under a new SEC number carries its predecessor's facts up to the day its new filer's first
// report starts. Worked by hand over XOM's numbers.
// see: A company that re-filed under a new SEC number carries its predecessor's facts up to the day its new filer's first report starts
public partial class FixtureExpectations
{
    const string XomBefore = "0000034088";
    const string XomNow = "0002115436";

    [Fact]
    public void AFilersFirstReportStartsAtItsNewestPeriodOfFlowsAndNoCoverPageDate()
    {
        // The first report a 10-Q filed 2026-08-03 for the quarter to 2026-06-30, stating six months and three, a balance and
        // a cover page's date; a later 10-Q is not read.
        FiledPeriod[] periods =
        [
            new("B", "10-Q", "2026-11-03", "2026-07-01", "2026-09-30"),
            new("A", "10-Q", "2026-08-03", "2026-01-01", "2026-06-30"),
            new("A", "10-Q", "2026-08-03", "2026-04-01", "2026-06-30"),
            new("A", "10-Q", "2026-08-03", "2026-06-30", "2026-06-30"),
            new("A", "10-Q", "2026-08-03", "2026-07-31", "2026-07-31"),
            new("C", "8-K", "2026-07-01", "2025-01-01", "2025-12-31"),
        ];

        Assert.Equal(new DateOnly(2026, 4, 1), FilerSuccessions.FirstReportStarts(periods));

        // A first report that is annual starts at its year; a filer with no report and one with no flows read none.
        Assert.Equal(new DateOnly(2023, 10, 1), FilerSuccessions.FirstReportStarts([new("K", "10-K", "2024-02-22", "2023-01-01", "2023-12-31"), new("K", "10-K", "2024-02-22", "2023-10-01", "2023-12-31")]));
        Assert.Null(FilerSuccessions.FirstReportStarts([new("C", "8-K", "2026-07-01", "2025-01-01", "2025-12-31")]));
        Assert.Null(FilerSuccessions.FirstReportStarts([new("A", "10-Q", "2026-08-03", "2026-06-30", "2026-06-30")]));
    }

    [Fact]
    public void EveryStatedSuccessionNamesTwoDifferentFilersAndTheStocksTheRulingNames()
    {
        Assert.Equal(15, FilerSuccessions.Stated.Count);
        Assert.All(FilerSuccessions.Stated, one => Assert.NotEqual(one.Predecessor, one.Successor));
        Assert.Equal(15, FilerSuccessions.Stated.Select(one => one.Ticker).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(FilerSuccessions.Stated, one => one.Ticker == "XOM");
        Assert.Contains(FilerSuccessions.Stated, one => one.Ticker == "BLK");
        Assert.DoesNotContain(FilerSuccessions.Stated, one => one.Ticker is "DOW" or "AESI" or "IR" or "RBC");
    }

    [Fact]
    public void EveryStockTheRulingLeavesOffIsListedWithItsReasonAndNoneIsJoined()
    {
        Assert.Equal(["AESI", "DOW", "OZK", "QDEL", "VNOM"], FilerSuccessions.LeftOff.Select(one => one.Ticker));
        Assert.All(FilerSuccessions.LeftOff, one => Assert.False(string.IsNullOrWhiteSpace(one.Why)));
        Assert.DoesNotContain(FilerSuccessions.Stated, one => FilerSuccessions.LeftOff.Any(left => left.Ticker == one.Ticker));

        // The two joined once name the filers their joined facts are taken out of.
        Assert.Equal(["QDEL", "VNOM"], FilerSuccessions.LeftOff.Where(one => one.Reversed is not null).Select(one => one.Reversed!.Ticker));
    }

    [Fact]
    public void TwoFilersAgreeOnlyWithFiveFiguresInCommonEachWithinFivePerCentOfTheLarger()
    {
        (decimal, decimal)[] same = [(10m, 10m), (20m, 20m), (30m, 30m), (40m, 40m)];

        Assert.Equal(5, FilerSuccessions.FewestInCommon);
        Assert.Equal(0.05m, FilerSuccessions.WidestGap);

        // Four in common cannot show one company, QDEL's none neither, and five can.
        Assert.Contains("only 4 figure(s)", FilerSuccessions.Disagreement(same), StringComparison.Ordinal);
        Assert.Contains("only 0 figure(s)", FilerSuccessions.Disagreement([]), StringComparison.Ordinal);
        Assert.Null(FilerSuccessions.Disagreement([.. same, (50m, 50m)]));

        // 100 against 95 is 5 of the larger 100, at the edge; 100 against 94.9 is 5.1 and past it; two nothings differ by none.
        Assert.Equal(0.05m, FilerSuccessions.Gap(100m, 95m));
        Assert.Null(FilerSuccessions.Disagreement([.. same, (100m, 95m)]));
        Assert.Contains("differs by 5.1%", FilerSuccessions.Disagreement([.. same, (100m, 94.9m)]), StringComparison.Ordinal);
        Assert.Equal(0m, FilerSuccessions.Gap(0m, 0m));

        // A loss read against a profit is measured across the two, not by their sizes alone.
        Assert.Equal(2m, FilerSuccessions.Gap(-10m, 10m));

        // TPL's widest, its second quarter's revenue of 2020, 2,705,000 of 57,273,000, is 4.72% and agrees; VNOM's net income
        // of 2021, 57.9 million against 257 million, is 77.5% and does not.
        Assert.Null(FilerSuccessions.Disagreement([.. same, (54_568_000m, 57_273_000m)]));
        Assert.Contains("differs by 77.5%", FilerSuccessions.Disagreement([.. same, (57_900_000m, 257_000_000m)]), StringComparison.Ordinal);
    }

    static TemporaryStore SuccessionStore()
    {
        var store = new TemporaryStore().Migrated();

        void Fact(string cik, string concept, string start, string end, string dollars, string filed, string form, string accession) =>
            store.Execute($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{cik}', '{concept}', '{start}', '{end}', '{dollars}', '{filed}', '{form}', '{accession}', 'pull');");

        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('XOM', '2026-10-30T00:00:00Z', '{XomNow}');");

        // Four more figures both filers state alike of a quarter before the changeover, which with the revenue below make the
        // five a join needs.
        foreach (var concept in new[] { "NetIncomeLoss", "ProfitLoss", "OperatingIncomeLoss", "NetCashProvidedByUsedInOperatingActivities" })
        {
            Fact(XomBefore, concept, "2025-01-01", "2025-03-31", "50", "2025-05-04", "10-Q", "OLD-4");
            Fact(XomNow, concept, "2025-01-01", "2025-03-31", "50", "2026-08-03", "10-Q", "NEW-1");
        }

        // The old filer: a year's revenue long before, the quarter the new filer also states at another figure, and a quarter
        // after the changeover that the old filer, still filing as a subsidiary, states of itself.
        Fact(XomBefore, "Revenues", "2015-01-01", "2015-12-31", "100", "2016-02-24", "10-K", "OLD-1");
        Fact(XomBefore, "Revenues", "2026-01-01", "2026-03-31", "200", "2026-05-04", "10-Q", "OLD-2");
        Fact(XomBefore, "Revenues", "2026-04-01", "2026-06-30", "999", "2026-08-03", "10-Q", "OLD-3");
        Fact(XomBefore, "Revenues", "2025-01-01", "2025-03-31", "300", "2025-05-04", "10-Q", "OLD-4");
        Fact(XomBefore, "Revenues", "2026-07-01", "2026-09-30", "888", "2026-11-01", "10-Q", "OLD-5");

        // The new filer's first report, for the quarter from 2026-04-01, with a comparative it states of 2025-01-01.
        Fact(XomNow, "Revenues", "2026-04-01", "2026-06-30", "500", "2026-08-03", "10-Q", "NEW-1");
        Fact(XomNow, "Revenues", "2025-01-01", "2025-03-31", "301", "2026-08-03", "10-Q", "NEW-1");

        return store;
    }

    [Fact]
    public async Task APredecessorsFactsAreCopiedOnlyBeforeTheFirstReportAndTheNewFilersOwnStand()
    {
        using var store = SuccessionStore();
        var refresher = new FilingsRefresher(null!, FixedClock.At(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        var xom = (await refresher.JoinPredecessorsAsync("join")).Joined.Single(one => one.Succession.Ticker == "XOM");

        // Three of the five old revenues end before 2026-04-01; the one the new filer states of 2025 stands at its own figure, so
        // two are stored, and the earliest of the old filer's, 2015-12-31, is 9.2 years before the new filer's own, 2025-03-31.
        // Five figures are in common, the widest 300 against 301, one of 301.
        Assert.Null(xom.Refused);
        Assert.Equal(new DateOnly(2026, 4, 1), xom.Starts);
        Assert.Equal(2, xom.Facts);
        Assert.Equal("2015-12-31", xom.Earliest);
        Assert.Equal(9.2, Math.Round(xom.YearsGained!.Value, 1));
        Assert.Equal(5, xom.InCommon);
        Assert.Equal(0.33m, Math.Round(xom.WorstGap * 100, 2));

        Assert.Equal(
            ["2015-12-31|100|2016-02-24|OLD-1", "2025-03-31|301|2026-08-03|NEW-1", "2026-03-31|200|2026-05-04|OLD-2", "2026-06-30|500|2026-08-03|NEW-1"],
            FamilyRows(store, $"SELECT period_end || '|' || dollars || '|' || filed || '|' || accession FROM filed_fact WHERE cik = '{XomNow}' AND concept = 'Revenues' ORDER BY period_end;"));

        // A second run stores nothing and reads the same figures in common, the rows it copied not among them, and the old
        // filer's rows stand under its own number.
        var again = (await refresher.JoinPredecessorsAsync("again")).Joined.Single(one => one.Succession.Ticker == "XOM");

        Assert.Equal(0, again.Facts);
        Assert.Null(again.Refused);
        Assert.Equal(new DateOnly(2026, 4, 1), again.Starts);
        Assert.Equal(5, again.InCommon);
        Assert.Equal(9.2, Math.Round(again.YearsGained!.Value, 1));
        Assert.Equal(["9"], FamilyRows(store, $"SELECT COUNT(*) FROM filed_fact WHERE cik = '{XomBefore}';"));
        Assert.Equal(["1"], FamilyRows(store, $"SELECT COUNT(*) FROM run_log WHERE stage = '{FilingsRefresher.JoinStage}' AND run_id = 'join';"));
    }

    [Fact]
    public async Task AJoinStoresNothingWhereAFigureInCommonIsPastTheGapOrTooFewAreInCommon()
    {
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // One of the five at 52 against the old filer's 50 is 2 of 52, within the gap, and at 54 it is 4 of 54, 7.4%.
        using (var near = SuccessionStore())
        {
            near.Execute($"UPDATE filed_fact SET dollars = '52' WHERE cik = '{XomNow}' AND concept = 'ProfitLoss';");

            var xom = (await new FilingsRefresher(null!, clock, near.DatabaseFile).JoinPredecessorsAsync("join")).Joined.Single(one => one.Succession.Ticker == "XOM");

            Assert.Null(xom.Refused);
            Assert.Equal(2, xom.Facts);
        }

        using (var far = SuccessionStore())
        {
            far.Execute($"UPDATE filed_fact SET dollars = '54' WHERE cik = '{XomNow}' AND concept = 'ProfitLoss';");

            var xom = (await new FilingsRefresher(null!, clock, far.DatabaseFile).JoinPredecessorsAsync("join")).Joined.Single(one => one.Succession.Ticker == "XOM");

            Assert.Equal(0, xom.Facts);
            Assert.Contains("differs by 7.4%", xom.Refused, StringComparison.Ordinal);
            Assert.Contains("XOM: not joined", xom.Said, StringComparison.Ordinal);
            Assert.Equal(["6"], FamilyRows(far, $"SELECT COUNT(*) FROM filed_fact WHERE cik = '{XomNow}';"));
        }

        using var few = SuccessionStore();

        few.Execute($"DELETE FROM filed_fact WHERE cik = '{XomNow}' AND concept = 'ProfitLoss';");

        var short4 = (await new FilingsRefresher(null!, clock, few.DatabaseFile).JoinPredecessorsAsync("join")).Joined.Single(one => one.Succession.Ticker == "XOM");

        Assert.Equal(0, short4.Facts);
        Assert.Equal(4, short4.InCommon);
        Assert.Contains("only 4 figure(s)", short4.Refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStockLeftOffHasOnlyWhatTheJoinCopiedTakenOutAndASecondRunTakesNothing()
    {
        using var store = new TemporaryStore().Migrated();

        void Fact(string cik, string concept, string end, string dollars, string accession) =>
            store.Execute($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{cik}', '{concept}', '2022-01-01', '{end}', '{dollars}', '2023-02-01', '10-K', '{accession}', 'pull');");

        const string Old = "0001602065";
        const string New = "0002074176";

        // The old filer's two facts, one copied under the new filer with the old accession, and two the new filer states itself,
        // one of them of the same period as a fact the old filer states, under the new filer's accession.
        Fact(Old, "Revenues", "2022-12-31", "100", "OLD");
        Fact(Old, "NetIncomeLoss", "2022-12-31", "57", "OLD");
        Fact(New, "Revenues", "2022-12-31", "100", "OLD");
        Fact(New, "NetIncomeLoss", "2022-12-31", "257", "NEW");
        Fact(New, "Assets", "2025-12-31", "900", "NEW");

        var refresher = new FilingsRefresher(null!, FixedClock.At(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        var joins = await refresher.JoinPredecessorsAsync("reverse");
        var vnom = joins.LeftOff.Single(one => one.LeftOff.Ticker == "VNOM");

        Assert.Equal(1, vnom.Removed);
        Assert.Contains("VNOM: left off, 11 of its 48 figures", vnom.Said, StringComparison.Ordinal);
        Assert.Contains("1 joined fact(s) taken out", vnom.Said, StringComparison.Ordinal);
        Assert.DoesNotContain(joins.Joined, one => one.Succession.Ticker == "VNOM");
        Assert.Equal(["Assets|NEW", "NetIncomeLoss|NEW"], FamilyRows(store, $"SELECT concept || '|' || accession FROM filed_fact WHERE cik = '{New}' ORDER BY concept;"));
        Assert.Equal(["2"], FamilyRows(store, $"SELECT COUNT(*) FROM filed_fact WHERE cik = '{Old}';"));

        // Every stock left off is named on the run log, and a second run takes nothing.
        Assert.Contains("DOW: left off", Assert.Single(FamilyRows(store, $"SELECT detail FROM run_log WHERE stage = '{FilingsRefresher.JoinStage}' AND run_id = 'reverse';")), StringComparison.Ordinal);

        var again = await refresher.JoinPredecessorsAsync("reverse-again");

        Assert.Equal(0, again.LeftOff.Sum(one => one.Removed));
    }

    // A feed answering one filer's facts as the archive would.
    sealed class AnsweringFacts(string cik, IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> facts) : IFilingsRefreshFeed
    {
        public int Documents => 0;

        public Task<string?> DailyIndexAsync(DateOnly day, CancellationToken cancellation = default) => Task.FromResult<string?>(null);

        public Task<string?> FilingPageAsync(string filer, string accession, CancellationToken cancellation = default) => Task.FromResult<string?>(null);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>?> FactsAsync(string filer, IReadOnlyList<string> concepts, CancellationToken cancellation = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>?>(filer == cik ? facts : new Dictionary<string, IReadOnlyList<ConceptFact>>());

        public Task<string?> OwnershipAsync(string filer, string accession, CancellationToken cancellation = default) => Task.FromResult<string?>(null);

        public Task<string?> FilingListingAsync(string filer, string accession, CancellationToken cancellation = default) => Task.FromResult<string?>(null);

        public Task<string?> FilingDocumentAsync(string filer, string accession, string file, CancellationToken cancellation = default) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task ANewFilersLaterFigureReplacesOnlyAFigureTheJoinCopiedAndOnlyWhereTheyDiffer()
    {
        using var store = SuccessionStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new FilingsRefresher(null!, clock, store.DatabaseFile).JoinPredecessorsAsync("join");

        // A filer that is no successor keeps its first filing as ever, a figure stated again differently or not.
        const string Plain = "0000000101";

        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('AAA', '2026-10-30T00:00:00Z', '{Plain}');");
        store.Execute($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{Plain}', 'Revenues', '2026-01-01', '2026-03-31', '10', '2026-05-01', '10-Q', 'PLAIN-1', 'pull');");

        // The new filer's next report: the quarter the join copied at 200 restated at 205, the year it copied at 100 stated the
        // same, and the quarter it filed itself at 500 stated at 777.
        var report = new DateOnly(2026, 11, 3);
        var feed = new AnsweringFacts(XomNow, new Dictionary<string, IReadOnlyList<ConceptFact>>
        {
            ["Revenues"] =
            [
                new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 205m, report, "10-Q", "NEW-2"),
                new(new DateOnly(2015, 1, 1), new DateOnly(2015, 12, 31), 100m, report, "10-Q", "NEW-2"),
                new(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), 777m, report, "10-Q", "NEW-2"),
            ],
        });

        await new FilingsRefresher(feed, clock, store.DatabaseFile, pause: (_, _) => Task.CompletedTask).WholeAsync("refresh");

        Assert.Equal(
            ["2015-12-31|100|2016-02-24|OLD-1", "2025-03-31|301|2026-08-03|NEW-1", "2026-03-31|205|2026-11-03|NEW-2", "2026-06-30|500|2026-08-03|NEW-1"],
            FamilyRows(store, $"SELECT period_end || '|' || dollars || '|' || filed || '|' || accession FROM filed_fact WHERE cik = '{XomNow}' AND concept = 'Revenues' ORDER BY period_end;"));

        // The old filer's own statement stands under its own number, and the restated row names the run that stored it.
        Assert.Equal(["200|OLD-2"], FamilyRows(store, $"SELECT dollars || '|' || accession FROM filed_fact WHERE cik = '{XomBefore}' AND concept = 'Revenues' AND period_end = '2026-03-31';"));
        Assert.Equal(["refresh"], FamilyRows(store, $"SELECT run_id FROM filed_fact WHERE cik = '{XomNow}' AND accession = 'NEW-2';"));

        var plain = new AnsweringFacts(Plain, new Dictionary<string, IReadOnlyList<ConceptFact>>
        {
            ["Revenues"] = [new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 11m, report, "10-Q", "PLAIN-2")],
        });

        await new FilingsRefresher(plain, clock, store.DatabaseFile, pause: (_, _) => Task.CompletedTask).WholeAsync("plain");

        Assert.Equal(["10|PLAIN-1"], FamilyRows(store, $"SELECT dollars || '|' || accession FROM filed_fact WHERE cik = '{Plain}';"));
    }

    [Fact]
    public async Task AStockWhoseNewestCompanyRowNamesAnotherFilerIsRefusedAndNotJoined()
    {
        using var store = SuccessionStore();

        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('XOM', '2026-11-01T00:00:00Z', '{XomBefore}');");

        var xom = (await new FilingsRefresher(null!, FixedClock.At(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).JoinPredecessorsAsync("join"))
            .Joined.Single(one => one.Succession.Ticker == "XOM");

        Assert.Equal(0, xom.Facts);
        Assert.Contains(XomBefore, xom.Refused, StringComparison.Ordinal);
        Assert.Equal(["6"], FamilyRows(store, $"SELECT COUNT(*) FROM filed_fact WHERE cik = '{XomNow}';"));
    }
}
