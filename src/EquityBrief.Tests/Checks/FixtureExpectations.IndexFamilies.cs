using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Indices;
using Xunit;

namespace EquityBrief.Tests.Checks;

// The S&P 400's and 600's provisional rules read on the night by the sweep's own code into tables of their own, every
// member's answer stored, each index's list drawn by the S&P 500's own rule with one trade a stock across every card of
// every index, and the trades each list keeps.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
public partial class FixtureExpectations
{
    const string IndexNight = "2026-10-02";

    // A year of bars ending on the night, a calendar day a session as the breakout's tests write them: closes near 99
    // for most of the year, the newest twenty sessions' ranges half the twenty before, and the night closing at the
    // given close on the given volume, every other session trading a million shares.
    static IReadOnlyList<FamilyBar> IndexYear(decimal close, long volume, long average = 1_000_000)
    {
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return
        [
            .. Enumerable.Range(0, 252).Select(index => index switch
            {
                < 211 => new FamilyBar(night.AddDays(index - 251), 100m, 98m, 99m, average),
                < 231 => new FamilyBar(night.AddDays(index - 251), 101m, 99m, 100m, average),
                < 251 => new FamilyBar(night.AddDays(index - 251), 100.5m, 99.5m, 100m, average),
                _ => new FamilyBar(night, close + 0.5m, close - 1m, close, volume),
            }),
        ];
    }

    // Four quarters filed before the night, each with the net income given.
    static void Quarters(TemporaryStore store, string ticker, params decimal[] income)
    {
        var ends = new[] { "2025-09-30", "2025-12-31", "2026-03-31", "2026-06-30" };
        var filed = new[] { "2025-10-30", "2026-02-10", "2026-04-30", "2026-07-30" };

        for (var at = 0; at < income.Length; at++)
        {
            store.Execute(
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income) " +
                FormattableString.Invariant($"VALUES ('{ticker}', '2026-08-01T00:00:00Z', '2026-08-01', '{ends[at]}', '{filed[at]}', '{income[at]}', '{income[at]}');"));
        }
    }

    // The S&P 400 on the constructed night. IA breaks out above its half year's high on twice its volume, ID on 1.7
    // times, IB on 1.6 and IC on 1.8, each clearing the $5 floor and the $10 million of dollar volume a day with four
    // profitable quarters. IB's trade on an S&P 500 card from the week before is still open, and IC's on the S&P 600's
    // list from before it moved; FL breaks out on twice its volume trading 50,000 shares a day, $5 million under the
    // floor; PR breaks out with four quarters summing to a loss; NH closes under the high. Every member closes above its
    // 200-day average, so the index's breadth is 1 and its market check open.
    static TemporaryStore IndexStore()
    {
        var store = new TemporaryStore().Migrated();
        var members = new (string Ticker, IReadOnlyList<FamilyBar> Bars, decimal[] Income)[]
        {
            ("IA", IndexYear(102m, 2_000_000), [10m, 10m, 10m, 10m]),
            ("IB", IndexYear(102m, 1_600_000), [10m, 10m, 10m, 10m]),
            ("IC", IndexYear(102m, 1_800_000), [10m, 10m, 10m, 10m]),
            ("ID", IndexYear(102m, 1_700_000), [10m, 10m, 10m, 10m]),
            ("FL", IndexYear(102m, 100_000, 50_000), [10m, 10m, 10m, 10m]),
            ("PR", IndexYear(102m, 2_000_000), [10m, -30m, 5m, 5m]),
            ("NH", IndexYear(100.2m, 2_000_000), [10m, 10m, 10m, 10m]),
        };

        foreach (var (ticker, bars, income) in members)
        {
            StoreYear(store, ticker, bars);
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            Quarters(store, ticker, income);
        }

        // IB's S&P 500 trade, listed on 2026-09-28 by the breakout's card with no outcome yet; IC's on the S&P 600's list
        // on 2026-09-25, not ended.
        store.Execute("INSERT INTO family_night (session_date, families) VALUES ('2026-09-28', '[\"pullback\",\"breakout\",\"drift\"]');");
        store.Execute("INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES ('2026-09-28', 'IB', 'breakout', 'listed', 1, '[]', NULL, NULL);");
        store.Execute("INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES ('SML', 'breakout', 'IC', '2026-09-25', 1, '98', '95', NULL, '3', 63);");

        return store;
    }

    [Fact]
    public async Task EachIndexsNightStoresEveryMembersAnswerAndDrawsItsListWithOneTradeAStockAcrossEveryIndex()
    {
        using var store = IndexStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-test");
        var mid = outcome.Nights.Single(night => night.Index == "MID");

        // Seven members read, every one above its 200-day average: breadth 1, the market check open.
        Assert.Equal((7, 1.0, true), (mid.Members, mid.Breadth, mid.MarketOpen));

        // Every member's answer under the breakout: IA, IC, ID and IB passing in the order of their volume, 2.0, 1.8, 1.7
        // and 1.6 times; FL under the floor, PR failing the profit check and NH with no setup.
        Assert.Equal(
            [
                $"FL|0|null|{IndexNightRead.UnderTheFloors}",
                "IA|1|1|null",
                "IB|1|4|null",
                "IC|1|2|null",
                "ID|1|3|null",
                $"NH|0|null|{IndexNightRead.NoSetup}",
                $"PR|0|null|{IndexNightRead.NoProfit}",
            ],
            FamilyRows(store, "SELECT ticker, passed, place, reason FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' ORDER BY ticker;"));

        // Every member answers under every family, the pullback and the drift with no setup for any of them.
        Assert.Equal(["21"], FamilyRows(store, "SELECT COUNT(*) FROM index_family_result WHERE index_code = 'MID';"));
        Assert.Equal([IndexNightRead.NoSetup], FamilyRows(store, "SELECT DISTINCT reason FROM index_family_result WHERE index_code = 'MID' AND family IN ('pullback', 'drift');"));

        // The list: IA first; IB held by its S&P 500 trade and IC by its S&P 600 trade, each naming the index holding it;
        // ID second.
        Assert.Equal(
            [
                $"IA|{FamilyList.Listed}|1|null",
                $"IB|{FamilyList.OpenTrade}|null|GSPC",
                $"IC|{FamilyList.OpenTrade}|null|SML",
                $"ID|{FamilyList.Listed}|2|null",
            ],
            FamilyRows(store, "SELECT ticker, state, place, held_index FROM index_family_pick WHERE index_code = 'MID' ORDER BY ticker;"));

        // The two listed rows kept as trades, bought at the night's close, and the night's row stating the rule.
        Assert.Equal(["IA|102|1", "ID|102|2"], FamilyRows(store, "SELECT ticker, entry, place FROM index_family_trade WHERE index_code = 'MID' AND session_date = '" + IndexNight + "' ORDER BY ticker;"));
        Assert.Equal((2, 2), (mid.Listed, mid.HeldByATrade));
        Assert.Contains("\"dollarVolume\":10000000", FamilyRows(store, "SELECT settings FROM index_family_night WHERE index_code = 'MID';").Single(), StringComparison.Ordinal);
    }
}
