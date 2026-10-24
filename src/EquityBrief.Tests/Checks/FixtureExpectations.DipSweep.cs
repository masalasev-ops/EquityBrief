using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.2: the short-term dip's sweep on an index's own history, one trade's result and benchmark at the
// next open worked by hand over a constructed history and read the same by the tester's walk; the sweep's two other
// exits through the rule's own walk; and the VIX's market level at its median's edge.
// see: The dip buys at the next session's open and sells at the next open after its exit's close or its stop's, and at its last session's close
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public partial class FixtureExpectations
{
    const string DipSweepListing = "2019-01-22";

    // Three of today's S&P 400 members over the 300 weekdays from 2018-01-01, read as survivors since no fund's holdings
    // are stored, each trading 300,000 shares a session, over the index's $10 million floor. A rises a tenth a session
    // to the session before the listing's two, falls 0.3 and then 3 to close at c on the listing, rises 1 and then 3,
    // opening 0.5 over c on the session after and 2.5 over it on the third, and rises a tenth a session after; B rises a
    // twentieth a session from 50 and opens 0.02 under each close; C falls a twentieth a session from 80 and opens at its
    // close. Each bar's range is a point either side of its close for A and half a point for B and C.
    static TemporaryStore DipSweepStore()
    {
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 300);
        var listing = Array.IndexOf(calendar, DateOnly.ParseExact(DipSweepListing, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        var a = new decimal[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < listing - 1 ? 100m + (0.1m * day)
                : day == listing - 1 ? a[day - 1] - 0.3m
                : day == listing ? a[day - 1] - 3m
                : day == listing + 1 ? a[listing] + 1m
                : day == listing + 2 ? a[listing] + 3m
                : a[day - 1] + 0.1m;
        }

        var store = new TemporaryStore().Migrated();

        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();

        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $day, $open, $high, $low, $close, 300000, 'constructed', '2026-10-24T00:00:00Z', $close);";

        var ticker = insert.Parameters.Add("$ticker", SqliteType.Text);
        var session = insert.Parameters.Add("$day", SqliteType.Text);
        var open = insert.Parameters.Add("$open", SqliteType.Text);
        var close = insert.Parameters.Add("$close", SqliteType.Text);
        var high = insert.Parameters.Add("$high", SqliteType.Text);
        var low = insert.Parameters.Add("$low", SqliteType.Text);

        foreach (var name in new[] { "A", "B", "C" })
        {
            for (var day = 0; day < calendar.Length; day++)
            {
                var (price, opened, spread) = name switch
                {
                    "A" => (a[day], day == listing + 1 ? a[listing] + 0.5m : day == listing + 3 ? a[listing] + 2.5m : a[day], 1m),
                    "B" => (50m + (0.05m * day), 50m + (0.05m * day) - 0.02m, 0.5m),
                    _ => (80m - (0.05m * day), 80m - (0.05m * day), 0.5m),
                };

                ticker.Value = name;
                session.Value = calendar[day].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                open.Value = opened.ToString(CultureInfo.InvariantCulture);
                close.Value = price.ToString(CultureInfo.InvariantCulture);
                high.Value = (Math.Max(price, opened) + spread).ToString(CultureInfo.InvariantCulture);
                low.Value = (Math.Min(price, opened) - spread).ToString(CultureInfo.InvariantCulture);
                insert.ExecuteNonQuery();
            }
        }

        using var members = connection.CreateCommand();

        members.Transaction = transaction;
        members.CommandText = string.Concat(
            from name in new[] { "A", "B", "C" }
            select $"INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('MID', '{name}', 'US', NULL, NULL, NULL, 'constructed');");
        members.ExecuteNonQuery();
        transaction.Commit();

        return store;
    }

    static async Task<IndexSweepRunner.DipRead> DipSweepRead(TemporaryStore store)
    {
        var history = new SweepHistory(store.DatabaseFile);
        var through = await history.NewestSessionAsync();
        var inputs = await history.ReadAsync(through, _ => { }, default, index: "MID", asItStood: true);
        var laid = IndexSweepRunner.OwnSwingRead.Laid("MID", inputs, await history.HeavyweightAsync(through), await history.IncomeAsync(through), through);

        return IndexSweepRunner.DipRead.Of(laid, await history.ReadAsync(through, null, default), []);
    }

    [Fact]
    public async Task ADipSweepTradesResultAndBenchmarkAtTheNextOpenAreWorkedByHandAndTheTestersWalkReadsTheSameTrade()
    {
        using var store = DipSweepStore();
        var dip = await DipSweepRead(store);
        int[] provisional = [.. dip.Grid.Provisional];
        var names = dip.Tickers.ToList();
        var (a, b, c) = (names.IndexOf("A"), names.IndexOf("B"), names.IndexOf("C"));
        var session = Array.IndexOf(dip.Calendar, DateOnly.ParseExact(DipSweepListing, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        int BarOf(int name) => Array.BinarySearch(dip.Series[name].SessionAt, session);

        double Move(int name) => dip.Series[name].Atr[BarOf(name)];

        // A alone lists, on the listing's session: its 2-session RSI falls under 10 only on the 3 point fall, B never
        // drops and C closes under its 200-session average.
        var chosen = dip.FirstStage(provisional);
        var listed = Assert.Single(chosen);

        Assert.Equal((a, session), (listed.Name, listed.Session));

        // Its walk: bought at the open 0.5 over its listing close c, its stop 4 typical moves under c; the session after
        // closes at c + 1 under its 5-session average of c + 2.1, the second at c + 3 over its average of c + 2.06, so it
        // sells at the third session's open, c + 2.5. Bought at the close that is 2.5 over the risk; at the open, 2 over it.
        var trade = Assert.Single(dip.AtOpen(chosen, provisional));

        Assert.Equal(2.0 / (4 * Move(a)), trade.Result!.Value, 12);

        // The benchmark, the same plan on every member bought at the close: A's 2.5; B, whose close the session after rises
        // over its average, selling at the second session's open 0.08 over its close; and C, which never closes over its
        // average or under its stop and sells at its tenth session's close 0.5 under its own; each over 4 of its own typical
        // moves. Bought at the open each moves by its own gap over the session after, 0.5 for A, 0.03 for B and -0.05 for C,
        // in its typical moves over the plan's 4.
        var atClose = ((2.5 / Move(a)) + (0.08 / Move(b)) - (0.5 / Move(c))) / (4 * 3);
        var gaps = ((0.5 / Move(a)) + (0.03 / Move(b)) - (0.05 / Move(c))) / 3;

        Assert.Equal(atClose - (gaps / 4), trade.Benchmark, 12);

        // The tester's walk of the same setting is the sweep's after the trade's round trip at the published table.
        var tested = Assert.Single(dip.TradesUnder(provisional, []));
        var costed = Assert.Single(dip.Costed([trade], 1));

        Assert.Equal((costed.Listing, costed.Result, costed.Benchmark), (tested.Listing, tested.Result, tested.Benchmark));
        Assert.True(tested.Result < trade.Result);
    }

    [Fact]
    public void TheDipsWalkSellsOnAnExitASweepReadsInPlaceOfTheAverageAtTheNextOpen()
    {
        var day = new DateOnly(2026, 10, 5);

        // A close over its average with the trigger read false holds; the trigger read true on the second session sells at
        // the third session's open.
        var path = DipRule.Walk(
            100,
            90,
            10,
            [
                new DipSession(day, 101, 102, 101, false),
                new DipSession(day.AddDays(1), 102, 103, 104, true),
                new DipSession(day.AddDays(2), 104, 105, 104, false),
            ]);

        Assert.Equal((DipEnd.Exit, 2, 104.0, day.AddDays(2)), (path.End, path.Sessions, path.Sale!.Value, path.EndedOn!.Value));

        // With no trigger read, the average decides as the night reads it: the first close over its average sells.
        var night = DipRule.Walk(100, 90, 10, [new DipSession(day, 101, 102, 101), new DipSession(day.AddDays(1), 103, 104, 104)]);

        Assert.Equal((DipEnd.Exit, 1, 103.0), (night.End, night.Sessions, night.Sale!.Value));
    }

    [Fact]
    public void TheVixsMarketLevelIsOpenWhereItsCloseStandsAboveTheMedianOfTheSessionsBefore()
    {
        var calendar = Weekdays(new DateOnly(2026, 1, 5), 6);

        // Over three sessions before: on the fourth session the median of 10, 12 and 14 is 12, so 12.01 is open and on the
        // fifth the median of 12, 14 and 12.01 is 12.01, so 12.01 is closed; the sixth session's close is missing, so it is
        // closed; a Saturday's quote is passed over.
        var vix = new SweepMarketSeries(
            "VIX",
            [
                (calendar[0], 10), (calendar[1], 12), (calendar[2], 14), (calendar[3], 12.01), (calendar[3].AddDays(2), 99), (calendar[4], 12.01),
            ],
            "constructed");

        Assert.Equal([false, false, false, true, false, false], IndexSweepRunner.VixAboveItsMedian(calendar, vix, 3));

        // A missing close among the sessions before closes the level.
        var holed = new SweepMarketSeries("VIX", [(calendar[0], 10), (calendar[2], 14), (calendar[3], 30)], "constructed");

        Assert.False(IndexSweepRunner.VixAboveItsMedian(calendar, holed, 3)[3]);
    }
}
