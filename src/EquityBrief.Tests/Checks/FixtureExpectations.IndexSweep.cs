using System.Globalization;
using System.Net;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 15.4: an S&P 400 or 600 sweep's walk worked by hand, the floors and the quality a listing clears on
// its own session, each trade's cost in risks at its prices as traded, and a constructed history run through the sweep's
// own command on both indices, its figures, its report and its answer read back against what the rules give.
// see: Each index runs every family as rules of its own, ranked and benchmarked on that index's members alone
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
// see: A trade's cost comes off the trade and not its benchmark
public partial class FixtureExpectations
{
    // One member's sixty sessions from 2026-03-02, each closing at 10 as the store adjusts it and at the given price as it
    // traded, on 500,000 shares a session unless the session is given another.
    static SweepSeries IndexMember(decimal traded, IReadOnlyDictionary<int, long>? volumes = null)
    {
        var days = Weekdays(new DateOnly(2026, 3, 2), 60);
        var sessionAt = days.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);

        return SweepColumns.Series(
            new SweepName("AAA", [.. days.Select((day, at) => new SweepBar(day, 11m, 9m, 10m, volumes is not null && volumes.TryGetValue(at, out var held) ? held : 500_000, 10m, traded))], [(null, null)], []),
            sessionAt);
    }

    [Fact]
    public void AnIndexListingClearsOnItsCloseAsTradedTheFiftySessionsToItAtItsIndexsFloorAndItsQuartersAsTheyStood()
    {
        // Fifty sessions to the fiftieth bar at 10 on 500,000 shares hold a mean of $5 million, read on the close the store
        // adjusts, which the volume is stated beside: the S&P 600's floor and not the S&P 400's. Forty-nine hold none.
        var member = IndexMember(20m);

        Assert.True(IndexSweepRunner.Clears("SML", member, 49, [], IndexQuality.Off));
        Assert.False(IndexSweepRunner.Clears("MID", member, 49, [], IndexQuality.Off));
        Assert.False(IndexSweepRunner.Clears("SML", member, 48, [], IndexQuality.Off));

        // The listing's own bar is one of the fifty and the bar fifty-one back is not: a share fewer on the fiftieth bar
        // takes the mean a fifth of a dollar under the floor, a first session trading nothing takes it to $4.9 million,
        // and on the session after it the fifty left hold the floor again.
        Assert.False(IndexSweepRunner.Clears("SML", IndexMember(20m, new Dictionary<int, long> { [49] = 499_999 }), 49, [], IndexQuality.Off));
        Assert.False(IndexSweepRunner.Clears("SML", IndexMember(20m, new Dictionary<int, long> { [0] = 0 }), 49, [], IndexQuality.Off));
        Assert.True(IndexSweepRunner.Clears("SML", IndexMember(20m, new Dictionary<int, long> { [0] = 0 }), 50, [], IndexQuality.Off));

        // The floor at a multiple: twice it asks $10 million of the S&P 600, half of it $5 million of the S&P 400.
        Assert.False(IndexSweepRunner.Clears("SML", member, 49, [], IndexQuality.Off, 2m));
        Assert.True(IndexSweepRunner.Clears("MID", member, 49, [], IndexQuality.Off, IndexSweepRunner.DollarVolumeHalf));

        // The price is the close as it traded and not as the store adjusts it: $5 clears and $4.99 does not, though the
        // stored close is 10; a bar carrying no traded close is read at its stored one.
        Assert.True(IndexSweepRunner.Clears("SML", IndexMember(5m), 49, [], IndexQuality.Off));
        Assert.False(IndexSweepRunner.Clears("SML", IndexMember(4.99m), 49, [], IndexQuality.Off));
        Assert.True(IndexSweepRunner.Clears("SML", IndexMember(0m), 49, [], IndexQuality.Off));

        // The quality on the listing's own session, the sixtieth, 2026-05-22: four quarters filed before it earning 40 in
        // all pass the profit gate, and with the fourth filed on the session itself three are read and do not, while the
        // quality off reads none. The cover asks operating income of twice the interest: 15 a quarter against 7.5 passes
        // and against 7.51 does not, a financial company passing whatever it files and the gate alone not reading it.
        var day = new DateOnly(2026, 5, 22);
        FiledIncome[] four =
        [
            new(PullDate(2025, 6, 30), PullDate(2025, 8, 7), 10m, 15m, 7.5m),
            new(PullDate(2025, 9, 30), PullDate(2025, 11, 6), 10m, 15m, 7.5m),
            new(PullDate(2025, 12, 31), PullDate(2026, 2, 12), 10m, 15m, 7.5m),
            new(PullDate(2026, 3, 31), PullDate(2026, 5, 7), 10m, 15m, 7.5m),
        ];
        FiledIncome[] fourthOnTheSession = [.. four[..3], four[3] with { FilingDate = day }];
        FiledIncome[] costly = [.. four.Select(quarter => quarter with { InterestExpense = 7.51m })];

        Assert.Equal(day, member.Bars[59].Session);
        Assert.True(IndexSweepRunner.Clears("SML", member, 59, four));
        Assert.False(IndexSweepRunner.Clears("SML", member, 59, fourthOnTheSession));
        Assert.True(IndexSweepRunner.Clears("SML", member, 59, fourthOnTheSession, IndexQuality.Off));
        Assert.True(IndexSweepRunner.Clears("SML", member, 59, four, IndexQuality.Cover, sector: GicsSectors.Industrials));
        Assert.False(IndexSweepRunner.Clears("SML", member, 59, costly, IndexQuality.Cover, sector: GicsSectors.Industrials));
        Assert.True(IndexSweepRunner.Clears("SML", member, 59, costly, IndexQuality.Profit, sector: GicsSectors.Industrials));
        Assert.True(IndexSweepRunner.Clears("SML", member, 59, costly, IndexQuality.Cover, sector: GicsSectors.Financials));

        // And the floors stand under every quality: a listing short of them passes none.
        Assert.False(IndexSweepRunner.Clears("MID", member, 59, four, IndexQuality.Off));
    }

    // No company's count, sector or split but the counts given, so a trade is read in the band of a company with no count.
    static HeavyweightHistory IndexCompanies(IReadOnlyDictionary<string, IReadOnlyList<FiledCount>>? counts = null) =>
        new(
            new Dictionary<string, (string? Cik, string? Sector)>(StringComparer.Ordinal),
            counts ?? new Dictionary<string, IReadOnlyList<FiledCount>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<FiledSplit>>(StringComparer.Ordinal),
            [],
            []);

    [Fact]
    public void AnIndexTradesCostIsTakenInRisksAtItsPricesAsTradedAndItsCompanysValueOnTheBuy()
    {
        // One session, 2026-05-22, its close stored at 20 and traded at the price given.
        var day = new DateOnly(2026, 5, 22);
        var at = new Dictionary<DateOnly, int> { [day] = 0 };

        SweepSeries Closing(decimal traded) =>
            SweepColumns.Series(new SweepName("AAA", [new SweepBar(day, 21m, 19m, 20m, 1_000, 20m, traded)], [(null, null)], []), at);

        // A company with no count is read in the $1 to 2 billion band. Bought at $20 with its stop at $19 and sold two risks
        // up at $22, both in the $20 to 40 price band at 0.089 per cent: half of that on 20 and on 22, 0.0089 and 0.00979
        // dollars, 0.01869 of its one-dollar risk, and twice that at double.
        Assert.Equal(0.01869, IndexSweepRunner.CostInRisk(Closing(20m), 0, 20, 19, 2, IndexCompanies(), 1), 12);
        Assert.Equal(0.03738, IndexSweepRunner.CostInRisk(Closing(20m), 0, 20, 19, 2, IndexCompanies(), TradeCost.Doubled), 12);

        // The sale is put where the result puts it: a risk down is $19, in the $10 to 20 band at 0.088, 0.0089 and 0.00836.
        Assert.Equal(0.01726, IndexSweepRunner.CostInRisk(Closing(20m), 0, 20, 19, -1, IndexCompanies(), 1), 12);

        // Stored at half the price it traded at, a later two for one split taken out of the stored prices: as traded they
        // are 40, 38 and 44, in the $40 band at 0.129 per cent, 0.0258 and 0.02838 over a two-dollar risk, 0.02709, where
        // the stored prices would read 0.01869.
        Assert.Equal(0.02709, IndexSweepRunner.CostInRisk(Closing(40m), 0, 20, 19, 2, IndexCompanies(), 1), 12);

        // A count of 30 million shares filed before the session values the company at $600 million on its $20 close, the
        // $500 million to $1 billion band, at 0.170 per cent: 0.017 and 0.0187, 0.0357. Filed on the session itself the
        // count is not read, and the trade is read in the band of no count again.
        IReadOnlyDictionary<string, IReadOnlyList<FiledCount>> Filed(DateOnly on) =>
            new Dictionary<string, IReadOnlyList<FiledCount>>(StringComparer.Ordinal) { ["AAA"] = [new FiledCount(new DateOnly(2026, 3, 31), on, 30_000_000m, day)] };

        Assert.Equal(0.0357, IndexSweepRunner.CostInRisk(Closing(20m), 0, 20, 19, 2, IndexCompanies(Filed(new DateOnly(2026, 5, 7))), 1), 12);
        Assert.Equal(0.01869, IndexSweepRunner.CostInRisk(Closing(20m), 0, 20, 19, 2, IndexCompanies(Filed(day)), 1), 12);

        // A listing's own buy and stop are the ones read.
        Assert.Equal(0.01869, IndexSweepRunner.CostInRisk(Closing(20m), new FamilyListing(0, 0, 0, 1, 0, 20, 19, 22, double.NaN, 63), 2, IndexCompanies(), 1), 12);
    }

    [Fact]
    public async Task AnIndexSweepOverAConstructedHistoryKeepsWhatClearsItsIndexsFloorAndStatesItsEdgeAfterEachTradesCost()
    {
        // The breakout sweep's constructed history, three members over the weekdays from 2018-01-01 past 2019, each bar a
        // point either side of its close and rising a hundredth a session, A breaking out 5 above the session before on
        // 2019-02-01 on three times its volume, rising a point a session for three and falling 6 on the fourth. Here they
        // trade 70,000 shares a session and A 210,000 on its breakout, about $7.5 million a session over the fifty to it:
        // over the S&P 600's $5 million floor and under the S&P 400's $10 million. Each files four quarters earning a
        // dollar each before 2019, and all three are today's members of both indices, read as survivors only.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var breakout = Array.IndexOf(calendar, new DateOnly(2019, 2, 1));
        var a = new decimal[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < breakout ? 100m + (0.01m * day)
                : day == breakout ? a[day - 1] + 5
                : day <= breakout + 3 ? a[day - 1] + 1
                : day == breakout + 4 ? a[day - 1] - 6
                : a[day - 1];
        }

        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();

        using (var connection = store.Open())
        {
            using var transaction = connection.BeginTransaction();
            using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $day, $close, $high, $low, $close, $volume, 'constructed', '2026-10-06T00:00:00Z', $close);";

            var ticker = insert.Parameters.Add("$ticker", SqliteType.Text);
            var session = insert.Parameters.Add("$day", SqliteType.Text);
            var close = insert.Parameters.Add("$close", SqliteType.Text);
            var high = insert.Parameters.Add("$high", SqliteType.Text);
            var low = insert.Parameters.Add("$low", SqliteType.Text);
            var volume = insert.Parameters.Add("$volume", SqliteType.Integer);

            foreach (var name in new[] { "A", "B", "C" })
            {
                for (var day = 0; day < calendar.Length; day++)
                {
                    var price = name == "A" ? a[day] : 100m + (0.01m * day);

                    ticker.Value = name;
                    session.Value = calendar[day].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    close.Value = price.ToString(CultureInfo.InvariantCulture);
                    high.Value = (price + 1).ToString(CultureInfo.InvariantCulture);
                    low.Value = (price - 1).ToString(CultureInfo.InvariantCulture);
                    volume.Value = name == "A" && day == breakout ? 210_000 : 70_000;
                    insert.ExecuteNonQuery();
                }
            }

            using var rest = connection.CreateCommand();

            rest.Transaction = transaction;
            rest.CommandText = string.Concat(
                from index in new[] { "MID", "SML" }
                from name in new[] { "A", "B", "C" }
                select $"INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('{index}', '{name}', 'US', NULL, NULL, NULL, 'constructed');")
                + string.Concat(
                    from name in new[] { "A", "B", "C" }
                    from quarter in new[] { ("2017-12-31", "2018-02-15"), ("2018-03-31", "2018-05-10"), ("2018-06-30", "2018-08-09"), ("2018-09-30", "2018-11-08") }
                    select $"INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) VALUES ('{name}', '{quarter.Item1}', '{quarter.Item2}', '1.00', '2.00', NULL, 'constructed');");
            rest.ExecuteNonQuery();
            transaction.Commit();
        }

        var clock = new SweepClock(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var runner = new IndexSweepRunner(clock, store.DatabaseFile, store.Root, sweeps.Path, TextWriter.Null);

        Assert.Equal(0, await runner.RunAsync("SML", BreakoutRule.Name, survivorsOnly: true));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Assert.Equal(0, await runner.RunAsync("MID", BreakoutRule.Name, survivorsOnly: true));
        SweepRelease(store);

        var provisional = BreakoutSweep.Grid.Key([.. BreakoutSweep.Grid.Provisional]);

        (JsonDocument Figures, string Report, SweepAnswer? Answer) Run(string name)
        {
            var folder = Path.Combine(sweeps.Path, name);

            return (
                JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, IndexSweepRunner.FiguresFile))),
                File.ReadAllText(Path.Combine(folder, SweepFolder.ReportFile)),
                SweepAnswer.Read(File.ReadAllText(Path.Combine(folder, SweepAnswer.File))));
        }

        JsonElement Provisional(JsonDocument figures, string read) =>
            figures.RootElement.GetProperty(read).EnumerateArray().Single(one => one.GetProperty("Key").GetString() == provisional);

        var (small, smallReport, smallAnswer) = Run("20261006T120000Z");
        var (mid, midReport, midAnswer) = Run("20261006T120100Z");

        using (small)
        using (mid)
        {
            // A's breakout is the one listing a setting can make. Its trade sold at the fourth session's close 3 under its
            // buy, -0.65625 of its risk, and B and C bought on the same plan rose to their cap, 0.1575 each, so the plan
            // on every member of the index read -0.11375 and the edge before costs is -0.5425, as the S&P 500's sweep
            // reads it. The cost comes off A's trade alone: bought at its close and sold 3 under it, both in the $40 band
            // of a company with no count at 0.129 per cent, over its risk of 2 typical moves of 32 over 14, to a millionth
            // where its stop is stored to four places.
            var entry = (double)a[breakout];
            var cost = 0.129 / 200 * (entry + entry - 3) / (2 * 32 / 14.0);
            var before = -0.65625 - ((-0.65625 + (2 * 0.1575)) / 3);

            Assert.Equal(-0.5425, before, 12);

            // On the S&P 600 every listing clears the floor and the gate, and its edge is read before costs, after each
            // trade's cost and at double the cost.
            Assert.Equal(new SweepAnswer("SML", BreakoutRule.Name, null, false), smallAnswer);
            Assert.True(small.RootElement.GetProperty("listed").GetInt64() > 0);
            Assert.Equal(small.RootElement.GetProperty("listed").GetInt64(), small.RootElement.GetProperty("kept").GetInt64());
            Assert.Equal(1, Provisional(small, "afterCosts").GetProperty("Trades").GetInt32());
            Assert.Equal(before, Provisional(small, "beforeCosts").GetProperty("Edge").GetDouble(), 9);
            Assert.Equal(before - cost, Provisional(small, "afterCosts").GetProperty("Edge").GetDouble(), 1e-6);
            Assert.Equal(before - (2 * cost), Provisional(small, "atDoubleCost").GetProperty("Edge").GetDouble(), 1e-6);

            // On the S&P 400 the same listings are made and none clears the floor, so no setting trades.
            Assert.Equal(new SweepAnswer("MID", BreakoutRule.Name, null, false), midAnswer);
            Assert.Equal(small.RootElement.GetProperty("listed").GetInt64(), mid.RootElement.GetProperty("listed").GetInt64());
            Assert.Equal(0, mid.RootElement.GetProperty("kept").GetInt64());
            Assert.Equal(0, Provisional(mid, "afterCosts").GetProperty("Trades").GetInt32());
            Assert.Equal(JsonValueKind.Null, Provisional(mid, "afterCosts").GetProperty("Edge").ValueKind);
        }

        // Each report says which index it read and that its members are survivors only, and that no setting passed.
        Assert.Contains(WebUtility.HtmlEncode("Survivors only: the S&P 600's 3 members today"), smallReport, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlEncode("Survivors only: the S&P 400's 3 members today"), midReport, StringComparison.Ordinal);
        Assert.Contains("class=\"none-passed\"", smallReport, StringComparison.Ordinal);
        Assert.Contains("class=\"none-passed\"", midReport, StringComparison.Ordinal);
    }
}
