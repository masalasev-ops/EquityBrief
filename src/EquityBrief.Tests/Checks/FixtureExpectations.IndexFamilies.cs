using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Families;
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

    // Three S&P 400 members of one sector over 261 calendar days to 2026-10-01 and one more to 2026-10-02, and IJH beside
    // them: the fund rising 1 per cent and falling 0.8 on alternate days, L1 moving 1.5 times the fund and 0.1 per cent a
    // day more, L2 1.1 times and 0.1 more, and L3 half the fund and 0.3 less, each close rounded to four places, a
    // million shares a day, a hundred million shares filed and four profitable quarters each.
    static (TemporaryStore Store, Dictionary<string, decimal[]> Closes) HeavyweightIndexStore()
    {
        var store = new TemporaryStore().Migrated();
        var night = new DateOnly(2026, 10, 1);
        var days = Enumerable.Range(0, 262).Select(at => night.AddDays(at - 260)).ToArray();
        var fund = new decimal[262];
        var closes = new Dictionary<string, decimal[]>(StringComparer.Ordinal) { ["L1"] = new decimal[262], ["L2"] = new decimal[262], ["L3"] = new decimal[262] };
        var moves = new Dictionary<string, (decimal Beta, decimal Drift)>(StringComparer.Ordinal) { ["L1"] = (1.5m, 0.001m), ["L2"] = (1.1m, 0.001m), ["L3"] = (0.5m, -0.003m) };

        fund[0] = 100m;

        foreach (var ticker in closes.Keys)
        {
            closes[ticker][0] = 100m;
        }

        for (var at = 1; at < 262; at++)
        {
            var move = at % 2 == 0 ? 0.01m : -0.008m;

            fund[at] = Math.Round(fund[at - 1] * (1 + move), 4);

            foreach (var (ticker, (beta, drift)) in moves)
            {
                closes[ticker][at] = Math.Round(closes[ticker][at - 1] * (1 + (beta * move) + drift), 4);
            }
        }

        foreach (var (ticker, series) in closes)
        {
            StoreYear(store, ticker, [.. Enumerable.Range(0, 261).Select(at => new FamilyBar(days[at], Math.Round(series[at] * 1.005m, 4), Math.Round(series[at] * 0.995m, 4), series[at], 1_000_000))]);
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2025-01-02', NULL, '2025-01-02T00:00:00Z');");
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES ('{ticker}', '2026-09-01T00:00:00Z', 'CIK{ticker}', 'Industrials', NULL, NULL, NULL);");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
            store.Execute($"UPDATE reported_quarter SET shares = '100000000', basis_session = '2026-06-30', basis_close = '100' WHERE ticker = '{ticker}';");
        }

        store.Execute(
            "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
            string.Join(", ", Enumerable.Range(0, 262).Select(at => FormattableString.Invariant($"('IJH', '{Day(days[at])}', '{fund[at]}', '{fund[at]}', '{fund[at]}', '{fund[at]}', 'test')"))) + ";");

        return (store, closes);
    }

    [Fact]
    public async Task EachIndexsSectorHeavyweightsBuyTheLeadersOnTheFirstNightOfAMonthAndCarryAndSellAsTheBookKeepsThem()
    {
        var (store, closes) = HeavyweightIndexStore();

        using (store)
        {
            var clock = FixedClock.At(new DateTimeOffset(2026, 10, 1, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

            // 2026-10-01, the book's first night: L1 returns 85.4 per cent over the 251 sessions and L2 68.9, against the
            // sector's mean of 35.9, both above their 50-day average and it above their 200-day, with betas of 1.5 and
            // 1.1; L3 returns -46.5 per cent with a beta of 0.5. The two leaders are bought at the night's closes.
            var first = await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-first");

            Assert.Equal(new IndexHeavyweightsOutcome(true, 2, 0, 2), first.Nights.Single(night => night.Index == "MID").Heavyweights);
            Assert.Equal(
                [$"L1|2026-10-01|Industrials|{closes["L1"][260]}|1|null|null", $"L2|2026-10-01|Industrials|{closes["L2"][260]}|1|null|null"],
                FamilyRows(store, "SELECT ticker, entered_on, sector, entry_close, growth, ended_on, reason FROM index_heavyweight_holding WHERE index_code = 'MID' ORDER BY ticker;"));

            // 2026-10-02, the same month: no rebalance. L1 is carried by its close over the night before's; L2 left the
            // index that day, so it is sold at its last close as a member, the session it was last carried to, with its
            // growth unchanged.
            StoreYear(store, "L1", [new FamilyBar(new DateOnly(2026, 10, 2), closes["L1"][261], closes["L1"][261], closes["L1"][261], 1_000_000)]);
            StoreYear(store, "L2", [new FamilyBar(new DateOnly(2026, 10, 2), closes["L2"][261], closes["L2"][261], closes["L2"][261], 1_000_000)]);
            StoreYear(store, "L3", [new FamilyBar(new DateOnly(2026, 10, 2), closes["L3"][261], closes["L3"][261], closes["L3"][261], 1_000_000)]);
            store.Execute("UPDATE membership SET \"left\" = '2026-10-02' WHERE ticker = 'L2';");

            var second = await new IndexFamilies(FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("night-second");
            var carried = Statistic.FromRatio(closes["L1"][261] / closes["L1"][260]);

            Assert.Equal(new IndexHeavyweightsOutcome(false, 0, 1, 1), second.Nights.Single(night => night.Index == "MID").Heavyweights);
            Assert.Equal(
                [
                    FormattableString.Invariant($"L1|2026-10-02|null|null"),
                    FormattableString.Invariant($"L2|2026-10-01|2026-10-01|{IndexHeavyweights.LeftTheIndex}"),
                ],
                FamilyRows(store, "SELECT ticker, through, ended_on, reason FROM index_heavyweight_holding WHERE index_code = 'MID' ORDER BY ticker;"));
            Assert.Equal(carried, double.Parse(FamilyRows(store, "SELECT growth FROM index_heavyweight_holding WHERE ticker = 'L1';").Single(), CultureInfo.InvariantCulture), 9);
            Assert.Equal(["2026-10-01|1", "2026-10-02|0"], FamilyRows(store, "SELECT session_date, rebalanced FROM index_family_night WHERE index_code = 'MID' ORDER BY session_date;"));
        }
    }

    [Fact]
    public void TheSixReportsANightAreTakenInTurnAcrossTheThreeIndicesOneAtATime()
    {
        // Every index with picks: two each, in turn, the S&P 500 first.
        Assert.Equal(
            [("A1", 0), ("B1", 1), ("C1", 2), ("A2", 0), ("B2", 1), ("C2", 2)],
            EquityBrief.Worker.Research.RequestDrain.TakenInTurn([["A1", "A2", "A3"], ["B1", "B2", "B3"], ["C1", "C2"]], 6));

        // The plan's own case: the S&P 600 lists nothing and the S&P 400 one, so the turns go 500, 400, 500, 500, 500,
        // 500, the 400 passing its second turn and the 600 every turn.
        Assert.Equal(
            [("A1", 0), ("B1", 1), ("A2", 0), ("A3", 0), ("A4", 0), ("A5", 0)],
            EquityBrief.Worker.Research.RequestDrain.TakenInTurn([["A1", "A2", "A3", "A4", "A5", "A6"], ["B1"], []], 6));

        // A name on two lists is taken once, under the first list to reach it; fewer names than six take every one.
        Assert.Equal(
            [("A1", 0), ("B1", 1), ("C2", 2), ("A2", 0)],
            EquityBrief.Worker.Research.RequestDrain.TakenInTurn([["A1", "A2"], ["B1"], ["A1", "C2"]], 6));
    }

    [Fact]
    public async Task TheSAndP500sListHoldsBackAStockWhoseTradeOnAnSAndP400ListIsStillOpenAndNamesThatIndex()
    {
        // The lister's own night of 2026-09-30, the pullback passing PE, PJ, PA, PB, PD, PF, PG, PH and PI in its order,
        // PA's and PD's S&P 500 trades still open. PF's breakout trade on the S&P 400's list from 2026-09-29 has not
        // ended, so PF is held back too and its row names the S&P 400; the five listed move down to PH.
        using var store = FamilyStore();

        store.Execute("INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES ('MID', 'breakout', 'PF', '2026-09-29', 1, '50', '47', NULL, '3', 63);");

        var clock = FixedClock.At(new DateTimeOffset(2026, 9, 30, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-index");

        Assert.Equal(
            [
                "PE|listed|1|null|null|null",
                "PJ|listed|2|null|null|null",
                "PB|listed|3|null|null|null",
                "PG|listed|4|null|null|null",
                "PH|listed|5|null|null|null",
                "PA|open trade|null|pullback|2026-09-28|null",
                "PD|open trade|null|pullback|2026-09-28|null",
                "PF|open trade|null|breakout|2026-09-29|MID",
                "PI|past five|null|null|null|null",
            ],
            FamilyRows(store, "SELECT ticker, state, place, held_family, held_night, held_index FROM family_pick WHERE session_date = '2026-09-30' ORDER BY state <> 'listed', state, place, ticker;"));
    }
}
