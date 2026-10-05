using EquityBrief.Core.Time;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// nightly-run, 14.3: the night's own request for reports takes a stock the sector heavyweights bought that night after
// every swing family's picks, and never one they carried from an earlier month.
// see: The six reports a night are taken in turn across the three indices, one at a time in the page's order
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
public partial class NightlyRun
{
    [Fact]
    public async Task TheNightAsksAReportForAStockTheHeavyweightsBoughtThatNightAfterTheFamiliesPicksAndNoneForOneCarried()
    {
        using var store = new TemporaryStore().Migrated();

        // The families drew the night: P1 and P2 listed. The heavyweights bought X1 and X2 tonight, and P1 too, which is
        // asked for once; C1 they bought a month before and still hold.
        store.Execute("INSERT INTO family_night (session_date, families) VALUES ('2026-10-01', '[\"pullback\",\"breakout\",\"drift\"]');");
        store.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES " +
            "('2026-10-01', 'P1', 'pullback', 'listed', 1, '[]', NULL, NULL), ('2026-10-01', 'P2', 'breakout', 'listed', 2, '[]', NULL, NULL);");
        store.Execute(
            "INSERT INTO heavyweight_holding (ticker, entered_on, sector, company, entry_close, growth, cut, through) VALUES " +
            "('X2', '2026-10-01', 'Utilities', 'ticker X2', '10', 1.0, '[]', '2026-10-01'), " +
            "('X1', '2026-10-01', 'Energy', 'ticker X1', '10', 1.0, '[]', '2026-10-01'), " +
            "('P1', '2026-10-01', 'Financials', 'ticker P1', '10', 1.0, '[]', '2026-10-01'), " +
            "('C1', '2026-09-01', 'Energy', 'ticker C1', '10', 1.1, '[]', '2026-10-01');");

        var ask = await RequestDrain.AskForTheNightAsync(store.DatabaseFile, new DateOnly(2026, 10, 1), FixedClock.At(new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        // The families' picks in the page's order, then the heavyweights' buys in the order of their sectors, P1 once.
        Assert.Equal(["P1", "P2", "X1", "X2"], ask.Asked);
        Assert.Contains("X1 is number 3 on the list, and a report on it was asked for", ask.Line, StringComparison.Ordinal);

        // One request a name asked, each marked as asked by the night, and none for the holding carried.
        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM research_request WHERE asked_from = 'night';"));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM research_request WHERE ticker = 'C1';"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM research_request WHERE ticker = 'P1';"));
    }
}
