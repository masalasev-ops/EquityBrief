using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 17.3: the ledger's heavyweights over a constructed store. On a rebalance night of the S&P 500's book each
// member of each sector's size cut is a setup bought at the close, the one the book bought passed and picked; on a later
// rebalance night the setup the book no longer buys is sold at that close against its sector's size cut over the same
// sessions, while the ones it still buys stay open; and the later rebalance's own size cut is that night's setups.
// see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
public partial class NightlyRun
{
    static void HeavyweightRow(TemporaryStore store, int at, int place, string ticker, bool leader) =>
        store.Execute(
            "INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader) "
            + $"VALUES ('{LedgerStamp(at)}', 'Industrials', {place}, '{ticker}', 'ticker {ticker}', '1000000000', 0.1, 0.05, 0.05, 1, {(leader ? 1 : 0)});");

    [Fact]
    public async Task TheLedgersHeavyweightsAreTheSizeCutOfARebalanceSoldAtTheFirstLaterRebalanceThatDoesNotBuyThem()
    {
        using var store = new TemporaryStore().Migrated();

        // Three S&P 500 members at 100 for seventy-one sessions; the book's rebalance on the seventy-first ranks all three
        // in Industrials' size cut and buys AAA.
        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2025-01-02', NULL, '2026-01-05T21:00:00Z');");

            for (var at = 0; at <= 70; at++)
            {
                LedgerBar(store, ticker, at, 100m, 1_000_000);
            }
        }

        HeavyweightRow(store, 70, 1, "AAA", true);
        HeavyweightRow(store, 70, 2, "BBB", false);
        HeavyweightRow(store, 70, 3, "CCC", false);
        store.Execute($"INSERT INTO member_reading (index_code, session_date, ticker, close, cost) VALUES ('GSPC', '{LedgerStamp(70)}', 'AAA', '100', 0.5);");

        var clock = FixedClock.At(new DateTimeOffset(2026, 4, 15, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new SetupLedger(clock, store.DatabaseFile).NightAsync("night-heavyweights");

        // The size cut, each bought at 100 with no stop and the heavyweights' cap, AAA alone passed and picked, its cost
        // half a per cent of the buy as a fraction of it, and each path open.
        Assert.Equal(
            ["AAA|100|0|252|1|1|open|0", "BBB|100|0|252|0|0|open|0", "CCC|100|0|252|0|0|open|0"],
            Texts(store, "SELECT ticker || '|' || entry || '|' || stop || '|' || cap || '|' || live_pass || '|' || picked || '|' || end || '|' || settled FROM setup WHERE family = 'heavyweight' ORDER BY ticker;"));
        Assert.Equal(0.005, LedgerScalar<double>(store, "SELECT cost FROM setup WHERE family = 'heavyweight' AND ticker = 'AAA';"), 9);
        Assert.Equal(["GSPC|heavyweight|3|3|1"], Texts(store, "SELECT index_code || '|' || family || '|' || members || '|' || setups || '|' || live_passes FROM setup_night WHERE family = 'heavyweight';"));

        // Five sessions on: AAA at 120, BBB at 90 and CCC at 110, and the book's next rebalance buys AAA and BBB and not CCC.
        for (var at = 71; at <= 75; at++)
        {
            LedgerBar(store, "AAA", at, at == 75 ? 120m : 100m, 1_000_000);
            LedgerBar(store, "BBB", at, at == 75 ? 90m : 100m, 1_000_000);
            LedgerBar(store, "CCC", at, at == 75 ? 110m : 100m, 1_000_000);
        }

        HeavyweightRow(store, 75, 1, "AAA", true);
        HeavyweightRow(store, 75, 2, "BBB", true);
        HeavyweightRow(store, 75, 3, "CCC", false);

        await new SetupLedger(clock, store.DatabaseFile).NightAsync("night-heavyweights-after");

        // CCC sold at the rebalance's close, 110 over 100, a gain of a tenth, against its size cut over the same five
        // sessions, AAA's fifth up, BBB's tenth down and its own tenth up, a mean of a fifteenth; settled, its window
        // closed on the rebalance's session. AAA and BBB, which the book buys, stay open.
        var sold = Texts(store, $"SELECT end || '|' || sessions || '|' || ended_on || '|' || settled FROM setup WHERE family = 'heavyweight' AND ticker = 'CCC' AND session_date = '{LedgerStamp(70)}';").Single();

        Assert.Equal($"{SetupEnds.Rebalance}|5|{LedgerStamp(75)}|1", sold);
        Assert.Equal(0.1, LedgerScalar<double>(store, $"SELECT result FROM setup WHERE family = 'heavyweight' AND ticker = 'CCC' AND session_date = '{LedgerStamp(70)}';"), 9);
        Assert.Equal((0.2 - 0.1 + 0.1) / 3, LedgerScalar<double>(store, $"SELECT benchmark FROM setup WHERE family = 'heavyweight' AND ticker = 'CCC' AND session_date = '{LedgerStamp(70)}';"), 9);
        Assert.Equal(0.1 - ((0.2 - 0.1 + 0.1) / 3), LedgerScalar<double>(store, $"SELECT edge FROM setup WHERE family = 'heavyweight' AND ticker = 'CCC' AND session_date = '{LedgerStamp(70)}';"), 9);
        Assert.Equal(
            ["AAA|open|0", "BBB|open|0"],
            Texts(store, $"SELECT ticker || '|' || end || '|' || settled FROM setup WHERE family = 'heavyweight' AND session_date = '{LedgerStamp(70)}' AND ticker <> 'CCC' ORDER BY ticker;"));

        // And the later rebalance's size cut is that night's setups, AAA and BBB passed.
        Assert.Equal(["GSPC|heavyweight|3|3|2"], Texts(store, $"SELECT index_code || '|' || family || '|' || members || '|' || setups || '|' || live_passes FROM setup_night WHERE family = 'heavyweight' AND session_date = '{LedgerStamp(75)}';"));
    }
}
