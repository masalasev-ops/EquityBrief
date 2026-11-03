using EquityBrief.Core.Ledger;
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
        Assert.Equal(17, FilerSuccessions.Stated.Count);
        Assert.All(FilerSuccessions.Stated, one => Assert.NotEqual(one.Predecessor, one.Successor));
        Assert.Equal(17, FilerSuccessions.Stated.Select(one => one.Ticker).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(FilerSuccessions.Stated, one => one.Ticker == "XOM");
        Assert.Contains(FilerSuccessions.Stated, one => one.Ticker == "BLK");
        Assert.DoesNotContain(FilerSuccessions.Stated, one => one.Ticker is "DOW" or "AESI" or "IR" or "RBC");
    }

    static TemporaryStore SuccessionStore()
    {
        var store = new TemporaryStore().Migrated();

        void Fact(string cik, string concept, string start, string end, string dollars, string filed, string form, string accession) =>
            store.Execute($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{cik}', '{concept}', '{start}', '{end}', '{dollars}', '{filed}', '{form}', '{accession}', 'pull');");

        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('XOM', '2026-10-30T00:00:00Z', '{XomNow}');");

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

        var xom = (await refresher.JoinPredecessorsAsync("join")).Single(one => one.Succession.Ticker == "XOM");

        // Three of the five old facts end before 2026-04-01; the one the new filer states of 2025 stands at its own figure, so
        // two are stored, and the earliest of the old filer's, 2015-12-31, is 9.2 years before the new filer's own, 2025-03-31.
        Assert.Null(xom.Refused);
        Assert.Equal(new DateOnly(2026, 4, 1), xom.Starts);
        Assert.Equal(2, xom.Facts);
        Assert.Equal("2015-12-31", xom.Earliest);
        Assert.Equal(9.2, Math.Round(xom.YearsGained!.Value, 1));

        Assert.Equal(
            ["2015-12-31|100|2016-02-24|OLD-1", "2025-03-31|301|2026-08-03|NEW-1", "2026-03-31|200|2026-05-04|OLD-2", "2026-06-30|500|2026-08-03|NEW-1"],
            FamilyRows(store, $"SELECT period_end || '|' || dollars || '|' || filed || '|' || accession FROM filed_fact WHERE cik = '{XomNow}' ORDER BY period_end;"));

        // A second run stores nothing, and the old filer's rows stand under its own number.
        var again = (await refresher.JoinPredecessorsAsync("again")).Single(one => one.Succession.Ticker == "XOM");

        Assert.Equal(0, again.Facts);
        Assert.Equal(["5"], FamilyRows(store, $"SELECT COUNT(*) FROM filed_fact WHERE cik = '{XomBefore}';"));
        Assert.Equal(["1"], FamilyRows(store, $"SELECT COUNT(*) FROM run_log WHERE stage = '{FilingsRefresher.JoinStage}' AND run_id = 'join';"));
    }

    [Fact]
    public async Task AStockWhoseNewestCompanyRowNamesAnotherFilerIsRefusedAndNotJoined()
    {
        using var store = SuccessionStore();

        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('XOM', '2026-11-01T00:00:00Z', '{XomBefore}');");

        var xom = (await new FilingsRefresher(null!, FixedClock.At(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).JoinPredecessorsAsync("join"))
            .Single(one => one.Succession.Ticker == "XOM");

        Assert.Equal(0, xom.Facts);
        Assert.Contains(XomBefore, xom.Refused, StringComparison.Ordinal);
        Assert.Equal(["2"], FamilyRows(store, $"SELECT COUNT(*) FROM filed_fact WHERE cik = '{XomNow}';"));
    }
}
