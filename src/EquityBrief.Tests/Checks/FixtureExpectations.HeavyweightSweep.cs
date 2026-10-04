using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 14.5: the sector heavyweights' sweep. Its settings and each one's neighbours, what luck alone
// passes, the rebalances of each period, a beta worked by hand and its floor, a fund's return in the members' mean's
// place, the walk worked by hand over constructed rebalances under each exit, the replay held to a rebalance the night's
// book stored over a constructed store, and the report's figures read back against a constructed history's known answer.
// see: The heavyweights' sweep replays the book over the pulled history across its settings and proposes the best edge among those meeting the family sweeps' floors
// see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
// see: A heavyweight's beta is read over 251 daily returns against the index
public partial class FixtureExpectations
{
    // The rows the heavyweights' sweep adds that this check reaches: section 17's row and section 18's two.
    internal static readonly string[] HeavyweightSweepClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Heavyweights' sweep grid"),
        CheckReach.Key(Scope.FailureTable, "A rebalance the night's book stored that the heavyweights' replay reads differently"),
        CheckReach.Key(Scope.FailureTable, "A heavyweight still held at the history's end"),
    ];

    [Fact]
    public void TheHeavyweightsSweepReadsEveryCombinationOfItsDialsAndNamesEachSettingsNeighbours()
    {
        // Three size cuts, three look-backs, one leader or two, the members' mean or the fund, beta off or at least one,
        // monthly or weekly, and three exits: 3 x 3 x 2 x 2 x 2 x 2 x 3, each read once.
        Assert.Equal(432, HeavyweightSweep.Settings.Count);
        Assert.Equal(432, HeavyweightSweep.Settings.Select(setting => setting.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(HeavyweightSweep.Provisional, HeavyweightSweep.Settings);
        Assert.Equal("size=5|look-back=126|leaders=1|sector=members|beta=off|rebalance=month|exit=both", HeavyweightSweep.Provisional.Key);
        Assert.Equal(HeavyweightRule.Provisional, HeavyweightSweep.Provisional.Reading);

        // The provisional setting's neighbours: the size cut to 10, the look-back to 63 and 251, two leaders, the fund,
        // the beta, the week, and the exit's two others, each one dial moved.
        var neighbours = HeavyweightSweep.Neighbours(HeavyweightSweep.Provisional).ToArray();

        Assert.Equal(
            [
                "size=10|look-back=126|leaders=1|sector=members|beta=off|rebalance=month|exit=both",
                "size=5|look-back=63|leaders=1|sector=members|beta=off|rebalance=month|exit=both",
                "size=5|look-back=251|leaders=1|sector=members|beta=off|rebalance=month|exit=both",
                "size=5|look-back=126|leaders=2|sector=members|beta=off|rebalance=month|exit=both",
                "size=5|look-back=126|leaders=1|sector=fund|beta=off|rebalance=month|exit=both",
                "size=5|look-back=126|leaders=1|sector=members|beta=at least 1|rebalance=month|exit=both",
                "size=5|look-back=126|leaders=1|sector=members|beta=off|rebalance=week|exit=both",
                "size=5|look-back=126|leaders=1|sector=members|beta=off|rebalance=month|exit=drop",
                "size=5|look-back=126|leaders=1|sector=members|beta=off|rebalance=month|exit=break",
            ],
            neighbours.Select(setting => setting.Key));
        Assert.All(neighbours, neighbour => Assert.Equal(1, neighbour.Changes(HeavyweightSweep.Provisional)));

        // At the dials' far ends, every company's cut steps back to 10 alone and the drop's exit to the other two.
        var far = HeavyweightSweep.Provisional with { Largest = HeavyweightSweep.EveryCompany, LookBack = 251, Exit = HeavyweightExit.Drop };

        Assert.Equal([10], HeavyweightSweep.Neighbours(far).Where(setting => setting.Largest != far.Largest).Select(setting => setting.Largest));
        Assert.Equal([126], HeavyweightSweep.Neighbours(far).Where(setting => setting.LookBack != far.LookBack).Select(setting => setting.LookBack));
        Assert.Equal([HeavyweightExit.Both, HeavyweightExit.Break], HeavyweightSweep.Neighbours(far).Where(setting => setting.Exit != far.Exit).Select(setting => setting.Exit));

        // Of the 256 ways eight years fall, 28 + 8 + 1 hold at least six above nothing, so luck alone would put about 62 of
        // 432 independent settings there.
        Assert.Equal(37, HeavyweightSweep.LuckPatterns());
        Assert.Equal(432.0 * 37 / 256, HeavyweightSweep.Luck(432), 12);
    }

    [Fact]
    public void EachPeriodRebalancesOnItsFirstSessionFromTheFirstScored()
    {
        // Weekdays from Wednesday 2019-01-02 to Friday 2019-02-08, Monday 2019-01-21 closed.
        DateOnly[] calendar = [.. Weekdays(new DateOnly(2019, 1, 2), 28).Where(day => day != new DateOnly(2019, 1, 21))];
        DateOnly[] On(IReadOnlyList<int> sessions) => [.. sessions.Select(session => calendar[session])];

        // The month's first session and the week's, the first scored session opening both, and the week whose Monday
        // is closed opening on its Tuesday.
        Assert.Equal([new DateOnly(2019, 1, 2), new DateOnly(2019, 2, 1)], On(HeavyweightSweep.Rebalances(calendar, 0, HeavyweightPeriod.Month)));
        Assert.Equal(
            [new DateOnly(2019, 1, 2), new DateOnly(2019, 1, 7), new DateOnly(2019, 1, 14), new DateOnly(2019, 1, 22), new DateOnly(2019, 1, 28), new DateOnly(2019, 2, 4)],
            On(HeavyweightSweep.Rebalances(calendar, 0, HeavyweightPeriod.Week)));

        // From a first scored session mid-month, that session rebalances and the next month's first after it.
        Assert.Equal([new DateOnly(2019, 1, 10), new DateOnly(2019, 2, 1)], On(HeavyweightSweep.Rebalances(calendar, 6, HeavyweightPeriod.Month)));
    }

    [Fact]
    public void ABetaIsTheSlopeOfTheStocksDailyReturnsOnTheIndexsAndAHighBetaSettingBuysOnlyAtItsFloor()
    {
        // Over 252 sessions the index moves by a hundredth up and down in turn, a fiftieth on every fifth session, and
        // the stock by twice as much each session: a slope of 2.
        var index = new double[HeavyweightRule.BetaReturns + 1];
        var stock = new double[index.Length];

        index[0] = 1_000;
        stock[0] = 50;

        for (var at = 1; at < index.Length; at++)
        {
            var move = (at % 5 == 0 ? 0.02 : 0.01) * (at % 2 == 0 ? 1 : -1);

            index[at] = index[at - 1] * (1 + move);
            stock[at] = stock[at - 1] * (1 + (2 * move));
        }

        Assert.Equal(2.0, HeavyweightRule.Beta([.. stock.Zip(index)])!.Value, 9);

        // The newest 251 returns are read where more sessions are handed in; one session fewer reads none, and an index
        // that never moves reads none.
        Assert.Equal(2.0, HeavyweightRule.Beta([(40.0, 900.0), .. stock.Zip(index)])!.Value, 9);
        Assert.Null(HeavyweightRule.Beta([.. stock.Zip(index).Skip(1)]));
        Assert.Null(HeavyweightRule.Beta([.. stock.Select(close => (close, 1_000.0))]));

        // Two leaders of one sector in their trend: T1 leading by more with a beta of 0.9, T2 with a beta of exactly 1.
        // Without the beta T1 is bought; with it T2, at the floor; and a stock with no beta is bought by no setting
        // asking for one.
        HeavyweightMember[] members =
        [
            new("T1", "CIK 1", TechSector, 600m, 1_000m, 0.30, 100.0, 99.0, 98.0, 0.9),
            new("T2", "CIK 2", TechSector, 500m, 1_000m, 0.20, 100.0, 99.0, 98.0, 1.0),
            new("T3", "CIK 3", TechSector, 400m, 1_000m, -0.20, 100.0, 99.0, 98.0, 1.5),
        ];

        Assert.Equal(["T1"], HeavyweightRule.Read(members, HeavyweightRule.Provisional).Single().Leaders);
        Assert.Equal(["T2"], HeavyweightRule.Read(members, HeavyweightRule.Provisional with { HighBeta = true }).Single().Leaders);
        Assert.Empty(HeavyweightRule.Read([members[0], members[1] with { Beta = null }, members[2]], HeavyweightRule.Provisional with { HighBeta = true }).Single().Leaders);
    }

    [Fact]
    public void AFundsReturnTakesTheMembersMeansPlaceWhereTheSettingReadsItAndASectorWithNoneBuysNothing()
    {
        // The members' mean of 0.30, 0.20 and -0.20 is 0.1, which T1 leads by 0.2; against its fund's 0.25 it leads by
        // 0.05 and T2 by nothing, and against a fund reading none nothing leads.
        HeavyweightMember[] members =
        [
            new("T1", "CIK 1", TechSector, 600m, 1_000m, 0.30, 100.0, 99.0, 98.0),
            new("T2", "CIK 2", TechSector, 500m, 1_000m, 0.20, 100.0, 99.0, 98.0),
            new("T3", "CIK 3", TechSector, 400m, 1_000m, -0.20, 100.0, 99.0, 98.0),
        ];

        var byMembers = HeavyweightRule.Read(members, HeavyweightRule.Provisional).Single();
        var fund = HeavyweightRule.Read(members, HeavyweightRule.Provisional, new Dictionary<string, double?> { [TechSector] = 0.25 }).Single();
        var none = HeavyweightRule.Read(members, HeavyweightRule.Provisional, new Dictionary<string, double?> { [TechSector] = null }).Single();

        Assert.Equal(0.1, byMembers.Return!.Value, 12);
        Assert.Equal(0.2, byMembers.Largest[0].Lead!.Value, 12);
        Assert.Equal(0.25, fund.Return!.Value, 12);
        Assert.Equal([0.05, -0.05, -0.45], fund.Largest.Select(ranked => Math.Round(ranked.Lead!.Value, 12)));
        Assert.Equal(["T1"], fund.Leaders);
        Assert.Equal(3, fund.Counted);
        Assert.Null(none.Return);
        Assert.Empty(none.Leaders);
    }

    // Four names over ten sessions from Wednesday 2019-01-02: A rising a point a session and closing under its 200-day
    // average on the tenth, where the average is 120, B leaving the index after its sixth session and trading on after
    // it, C closing under its average of 19 on the sixth, and D with no close on the fifth.
    static HeavyweightTape WalkTape()
    {
        static double[] Row(params double[] values) => values;

        var close = new[]
        {
            Row(100, 101, 102, 103, 104, 105, 106, 107, 108, 109),
            Row(50, 50, 55, 55, 60, 60, 70, 70, 70, 70),
            Row(20, 20, 20, 21, 22, 18, 18, 18, 18, 18),
            Row(10, 10, 10, 10, double.NaN, 11, 12, 13, 14, 15),
        };
        var member = new bool[][]
        {
            Enumerable.Repeat(true, 10).ToArray(),
            [.. Enumerable.Range(0, 10).Select(session => session < 6)],
            Enumerable.Repeat(true, 10).ToArray(),
            Enumerable.Repeat(true, 10).ToArray(),
        };

        return new HeavyweightTape
        {
            Tickers = ["A", "B", "C", "D"],
            Calendar = Weekdays(new DateOnly(2019, 1, 2), 10),
            Close = close,
            Average200 = [[.. Enumerable.Repeat(90.0, 9), 120.0], [.. Enumerable.Repeat(40.0, 10)], [.. Enumerable.Repeat(19.0, 10)], [.. Enumerable.Repeat(9.0, 10)]],
            Member = member,
            LastMemberClose = [.. close.Zip(member, (closes, held) => HeavyweightTape.LastMemberCloseOf(held, closes))],
            Index = [.. Enumerable.Range(0, 10).Select(session => 1_000.0 + (10 * session))],
            First = 0,
        };
    }

    // Three rebalances: on the first session A leads its sector, cut of A and B, and C the other, cut of C and D; on the
    // fifth B leads in A's place and C and D lead together; on the ninth A leads, B gone from its cut, and D alone.
    static readonly Dictionary<int, HeavyweightRebalance> WalkRebalances = new()
    {
        [0] = new([("S1", [0], [0, 1]), ("S2", [2], [2, 3])]),
        [4] = new([("S1", [1], [0, 1]), ("S2", [2, 3], [2, 3])]),
        [8] = new([("S1", [0], [0]), ("S2", [3], [2, 3])]),
    };

    static IReadOnlyList<string> Walked(HeavyweightTape tape, IEnumerable<HeavyweightTrade> trades) =>
        [.. trades.Select(trade => FormattableString.Invariant($"{tape.Tickers[trade.Name]} {trade.Entry}-{(trade.End is { } end ? end.ToString(CultureInfo.InvariantCulture) : "held")} {trade.Reason ?? "-"}"))];

    [Fact]
    public void TheWalkIsWorkedByHandOverConstructedRebalancesUnderEachExit()
    {
        var tape = WalkTape();
        double? Every(int from, int to) => HeavyweightSweep.EveryMember(tape, from, to);

        // Whichever comes first: A no longer the leader on the fifth session and sold there, B bought in its place and D,
        // with no close there, not bought; C under its average on the sixth; B sold at its last close as a member, the
        // sixth's, once it has left; on the ninth A and D bought, A sold under its average on the tenth and D held at
        // the end.
        var both = HeavyweightSweep.Walk(tape, WalkRebalances, HeavyweightExit.Both, Every);

        Assert.Equal(
            [
                "A 0-4 " + HeavyweightBook.NoLongerTheLeader,
                "C 0-5 " + HeavyweightBook.UnderTheAverage,
                "B 4-5 " + HeavyweightBook.LeftTheIndex,
                "A 8-9 " + HeavyweightBook.UnderTheAverage,
                "D 8-held -",
            ],
            Walked(tape, both));

        // A: 104 over 100, 0.04; its cut A and B, 0.04 and 60 over 50, 0.2, a mean of 0.12; an edge of -0.08. Every member
        // on its buy, A, B, C and D to their last close as members by the fifth session, D's on the fourth: 0.04, 0.2,
        // 0.1 and nothing, 0.085; the index 1,040 over 1,000, 0.04.
        var a = both[0];

        Assert.Equal((0.04, 0.12, -0.08, 0.085, 0.04), (Math.Round(a.Result!.Value, 12), Math.Round(a.Cut!.Value, 12), Math.Round(a.Edge!.Value, 12), Math.Round(a.EveryMember!.Value, 12), Math.Round(a.Index!.Value, 12)));

        // C: 18 over 20, -0.1; its cut C and D, -0.1 and 11 over 10, 0.1, nothing; an edge of -0.1. B: 60 over 60,
        // nothing, its cut A's 105 over 104 and B's nothing, never its 70 after it left.
        Assert.Equal((-0.1, 0.0, -0.1), (Math.Round(both[1].Result!.Value, 12), Math.Round(both[1].Cut!.Value, 12), Math.Round(both[1].Edge!.Value, 12)));
        Assert.Equal((0.0, (105.0 / 104.0 - 1.0) / 2), (both[2].Result!.Value, both[2].Cut!.Value));

        // A bought again on the ninth with a cut of itself alone: 109 over 108 against the same, an edge of nothing.
        Assert.Equal(0.0, both[3].Edge!.Value, 12);

        // The figures over the four ended: the edge their mean, each in 2019, none above nothing; held 4, 5, 1 and 1
        // sessions, a median of 2.5; and one held at the end.
        var figures = HeavyweightSweep.Figures("both", both);
        var edge = (-0.08 - 0.1 - ((105.0 / 104.0 - 1.0) / 2) + 0.0) / 4;

        Assert.Equal((4, 1, 0), (figures.Trades, figures.Open, figures.YearsBeating));
        Assert.Equal(edge, figures.Edge!.Value, 12);
        Assert.Equal([4, 0, 0, 0, 0, 0, 0, 0], figures.YearTrades);
        Assert.Equal(edge, figures.YearEdge[0]!.Value, 12);
        Assert.Equal((2.5, 11.0 / 4), (figures.HeldMedian!.Value, figures.HeldMean!.Value));
        Assert.False(figures.MeetsFloors);

        // On no longer leading alone: C kept under its average and sold on the ninth, no longer leading, at 18 against its
        // cut's -0.1 and D's 14 over 10, 0.4, a mean of 0.15.
        var drop = HeavyweightSweep.Walk(tape, WalkRebalances, HeavyweightExit.Drop, Every);

        Assert.Equal(
            [
                "A 0-4 " + HeavyweightBook.NoLongerTheLeader,
                "B 4-5 " + HeavyweightBook.LeftTheIndex,
                "C 0-8 " + HeavyweightBook.NoLongerTheLeader,
                "A 8-held -",
                "D 8-held -",
            ],
            Walked(tape, drop));
        Assert.Equal((-0.1, 0.15), (Math.Round(drop[2].Result!.Value, 12), Math.Round(drop[2].Cut!.Value, 12)));

        // On the average alone: A held through the fifth and the ninth, never bought twice, B bought beside it, and A sold
        // under its average on the tenth: 109 over 100, 0.09, against its cut of A and of B read at B's last close as a
        // member, 60 over 50, and never its 70 after it left: a mean of 0.145.
        var broken = HeavyweightSweep.Walk(tape, WalkRebalances, HeavyweightExit.Break, Every);

        Assert.Equal(
            [
                "C 0-5 " + HeavyweightBook.UnderTheAverage,
                "B 4-5 " + HeavyweightBook.LeftTheIndex,
                "A 0-9 " + HeavyweightBook.UnderTheAverage,
                "D 8-held -",
            ],
            Walked(tape, broken));
        Assert.Equal((0.09, 0.145), (Math.Round(broken[2].Result!.Value, 12), Math.Round(broken[2].Cut!.Value, 12)));
    }

    // The sessions of the constructed store: the exchange's from June 2025 to September's first in 2026, the night.
    static readonly DateOnly SweepNight = new(2026, 9, 1);

    static IReadOnlyList<DateOnly> SweepSessions() => ExchangeClosures.SessionsBetween(new DateOnly(2025, 5, 31), SweepNight.AddDays(1));

    // Each constructed member's close on a session by its place: T1 and T2 rising, T3 falling, H1 rising and H2 flat.
    static readonly (string Ticker, string Cik, string Sector, decimal Shares, Func<int, decimal> Close)[] SweepMembers =
    [
        ("T1", "0000000001", TechSector, 10m, at => 100m + (0.10m * at)),
        ("T2", "0000000002", TechSector, 30m, at => 120m + (0.02m * at)),
        ("T3", "0000000003", TechSector, 8m, at => 150m - (0.05m * at)),
        ("H1", "0000000011", CareSector, 20m, at => 50m + (0.05m * at)),
        ("H2", "0000000012", CareSector, 5m, _ => 80m),
    ];

    [Fact]
    public async Task TheReplayAtTheProvisionalSettingReadsTheRebalanceTheNightsBookStored()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = SweepSessions();

        Assert.Equal(SweepNight, sessions[^1]);

        // The night's inputs: each member, its bars, its company and counts as its fetch answered on the night, and its
        // averages on the night; and the sweep's: the same companies and counts as the pulls stored them, a count filed
        // after the night, a split after the day the counts are stated on, and a fund's closes.
        using (var connection = store.Open())
        using (var transaction = connection.BeginTransaction())
        {
            void Run(string sql, params (string Name, object Value)[] parameters)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;

                foreach (var (name, value) in parameters)
                {
                    command.Parameters.AddWithValue(name, value);
                }

                command.ExecuteNonQuery();
            }

            static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            foreach (var (ticker, cik, sector, shares, close) in SweepMembers)
            {
                var tonight = close(sessions.Count - 1).ToString(CultureInfo.InvariantCulture);

                Run($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2025-01-01T00:00:00Z');");

                for (var at = 0; at < sessions.Count; at++)
                {
                    Run(
                        "INSERT INTO bar (ticker, session_date, open, high, low, close, raw_close, volume, source, observed_at) VALUES ($ticker, $session, $close, $close, $close, $close, $close, 1000, 'constructed', '2025-01-01T00:00:00Z');",
                        ("$ticker", ticker), ("$session", Stamp(sessions[at])), ("$close", close(at).ToString(CultureInfo.InvariantCulture)));
                }

                Run($"INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES ('{ticker}', '2026-08-31T23:50:00Z', '{cik}', '{sector}', NULL, NULL, NULL);");
                Run(
                    "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, basis_session, basis_close, shares) VALUES " +
                    $"('{ticker}', '2026-08-31T23:50:00Z', '2026-08-31', '2026-06-30', '2026-08-01', '{Stamp(SweepNight)}', '{tonight}', '{shares.ToString(CultureInfo.InvariantCulture)}');");
                Run($"INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull) VALUES ('{ticker}', '{cik}', '{sector}', NULL, NULL, NULL, NULL, 'p');");
                Run($"INSERT INTO pulled_shares (ticker, period_end, filing_date, shares, basis_session, pull) VALUES ('{ticker}', '2026-06-30', '2026-08-01', '{shares.ToString(CultureInfo.InvariantCulture)}', '2026-10-04', 'p');");

                var closes = Enumerable.Range(0, sessions.Count).Select(at => Statistic.FromPrice(close(at))).ToArray();

                foreach (var (name, length) in new[] { (Core.Indicators.IndicatorSeries.Sma50, 50), (Core.Indicators.IndicatorSeries.Sma200, 200) })
                {
                    Run(
                        "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ($ticker, $session, $name, $value, $count);",
                        ("$ticker", ticker), ("$session", Stamp(SweepNight)), ("$name", name), ("$value", closes.TakeLast(length).Average()), ("$count", length));
                }
            }

            Run("INSERT INTO pulled_shares (ticker, period_end, filing_date, shares, basis_session, pull) VALUES ('T3', '2026-09-30', '2026-09-15', '9', '2026-10-04', 'p');");
            Run("INSERT INTO pulled_split (ticker, ex_date, new_shares, old_shares, pull) VALUES ('T1', '2026-10-10', '2', '1', 'p');");
            Run("INSERT INTO pulled_market_bar (series, session_date, open, high, low, close, pull) VALUES ('XLK', '2026-08-31', '1', '1', '1', '200', 'p'), ('XLK', '2026-09-01', '1', '1', '1', '202', 'p');");

            transaction.Commit();
        }

        // The night's book, held at the provisional setting, reads its first night as a rebalance and stores each sector's
        // largest; the comparison reads whatever setting it is handed, the runner handing it the frozen one.
        var book = await new HeavyweightBook(FixedClock.At(new DateTimeOffset(SweepNight.ToDateTime(new TimeOnly(23, 40)), TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile, Core.Families.HeavyweightRule.Provisional).RunAsync("GSPC", "night-sweep");

        Assert.True(book.Rebalanced);
        Assert.Equal(["H1", "T1"], book.Entered);

        // What the sweep reads beside the history: the companies, the counts filed by the night, every split, the fund
        // and the rows the book stored.
        var history = new SweepHistory(store.DatabaseFile);
        var inputs = await history.ReadAsync(SweepNight);
        var pulled = await history.HeavyweightAsync(SweepNight);

        Assert.Equal(5, pulled.Companies.Count);
        Assert.Single(pulled.Counts["T3"]);
        Assert.Single(pulled.Splits["T1"]);
        Assert.Equal([("XLK", 2)], pulled.Funds.Select(fund => (fund.Series, fund.Closes.Count)));
        Assert.Equal(5, pulled.Stored.Count);

        // The replay at the provisional setting reads the night's two sectors as the book stored them: T2, T1 and T3 by
        // value with T1 leading, and H1 and H2 with H1 leading.
        var night = Array.IndexOf(inputs.Sessions, SweepNight);
        var (_, laid) = HeavyweightSweep.Lay(inputs, pulled, null, new HashSet<int> { night }, night);
        IReadOnlyList<HeavyweightSector>? At(DateOnly day, HeavyweightSetting setting) => day == SweepNight ? HeavyweightSweep.Sectors(laid[night], setting) : null;

        var replayed = At(SweepNight, HeavyweightSweep.Provisional)!;

        Assert.Equal([["H1", "H2"], ["T2", "T1", "T3"]], replayed.Select(sector => sector.Largest.Select(ranked => ranked.Ticker).ToArray()));
        Assert.Equal([["H1"], ["T1"]], replayed.Select(sector => sector.Leaders.ToArray()));

        // Read against the funds, Information Technology's holds two sessions, too few for a return over the look-back,
        // and Health Care's none, so neither sector reads a return and nothing leads.
        Assert.All(At(SweepNight, HeavyweightSweep.Provisional with { Sector = HeavyweightSectorReturn.Fund })!, sector => Assert.Empty(sector.Leaders));

        var compared = HeavyweightSweep.Compare(pulled.Stored, day => At(day, HeavyweightSweep.Provisional));

        Assert.Equal((1, 2, 2), (compared.Sessions, compared.Sectors, compared.Matched));
        Assert.Empty(compared.Differences);

        // Read at another look-back the leads move and each sector is named as read differently; a session the history
        // does not hold is named too.
        var other = HeavyweightSweep.Compare(pulled.Stored, day => At(day, HeavyweightSweep.Provisional with { LookBack = 63 }));

        Assert.Equal((1, 2, 0), (other.Sessions, other.Sectors, other.Matched));
        Assert.Equal(2, other.Differences.Count);
        Assert.Equal(
            ["2026-09-01: the history holds no reading of the session"],
            HeavyweightSweep.Compare(pulled.Stored, _ => null).Differences);
    }

    [Fact]
    public void TheReportStatesAConstructedHistorysKnownAnswer()
    {
        // Two Energy companies over 300 weekdays from 2018-02-05: A rising a fifth of a point a session from 50, then
        // closing at 40 from 2019-02-15, under its 200-day average; B at 100 throughout. A's count of 10 and B's of 5 were
        // filed before the history began.
        var calendar = Weekdays(new DateOnly(2018, 2, 5), 300);
        var first = Array.IndexOf(calendar, new DateOnly(2019, 1, 2));
        var dropAt = Array.IndexOf(calendar, new DateOnly(2019, 2, 15));

        SweepName Name(string ticker, Func<int, decimal> close) => new(
            ticker,
            [.. calendar.Select((day, at) => new SweepBar(day, close(at), close(at), close(at), 1_000, close(at), close(at)))],
            [(null, null)],
            []);

        var inputs = new SweepHistoryInputs(calendar[^1], calendar, [Name("A", at => at < dropAt ? 50m + (0.2m * at) : 40m), Name("B", _ => 100m)], 0, 0, 0, 0, "constructed");
        var history = new HeavyweightHistory(
            new Dictionary<string, (string? Cik, string? Sector)> { ["A"] = ("0000000001", GicsSectors.Energy), ["B"] = ("0000000002", GicsSectors.Energy) },
            new Dictionary<string, IReadOnlyList<FiledCount>>
            {
                ["A"] = [new(new DateOnly(2017, 12, 31), new DateOnly(2018, 1, 15), 10m, new DateOnly(2019, 6, 28))],
                ["B"] = [new(new DateOnly(2017, 12, 31), new DateOnly(2018, 1, 15), 5m, new DateOnly(2019, 6, 28))],
            },
            new Dictionary<string, IReadOnlyList<FiledSplit>>(),
            [],
            []);
        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);
        var weeks = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Week);
        var (tape, sessions) = HeavyweightSweep.Lay(inputs, history, null, months.Concat(weeks).ToHashSet(), first);
        var read = HeavyweightSweep.ReadAll(tape, sessions, months, weeks);

        // At the provisional setting A leads the sector's mean of its own return and B's nothing and is bought on the first
        // session scored at 97.4, held through February's rebalance, and sold at 40 under its average: a result of 40 over
        // 97.4 less one, its cut A's and B's nothing, an edge of half the result; nothing leads in March.
        var result = (40.0 / 97.4) - 1.0;
        var provisional = read.Single(one => one.Setting == HeavyweightSweep.Provisional).Figures;

        Assert.Equal(237, first);
        Assert.Equal((1, 0), (provisional.Trades, provisional.Open));
        Assert.Equal(result, provisional.Result!.Value, 9);
        Assert.Equal(result / 2, provisional.Edge!.Value, 9);
        Assert.Equal((double)(dropAt - first), provisional.HeldMedian!.Value);
        Assert.Null(provisional.Index);

        // One trade meets no floor, so the family is set aside; the page states the provisional row, what luck alone
        // would pass and that the book has stored nothing to compare.
        var proposal = HeavyweightSweep.Propose(read);
        var run = new HeavyweightSweepRun(calendar[first], calendar[^1], 2, calendar.Length - first, months.Count, weeks.Count, 2, 2, read.Count, read.Count(one => one.Figures.MeetsFloors), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var page = HeavyweightSweepReport.Build(run, read, proposal, HeavyweightSweep.Compare([], _ => null));

        Assert.True(proposal.SetAside);
        Assert.Contains("<p class=\"set-aside\">", page, StringComparison.Ordinal);
        Assert.Contains($"<tr data-key=\"{System.Net.WebUtility.HtmlEncode(HeavyweightSweep.Provisional.Key)}\" data-trades=\"1\" data-edge=\"{HeavyweightSweepReport.Percent(result / 2)}\"><td>The provisional setting</td>", page, StringComparison.Ordinal);
        Assert.Equal("-29.47%", HeavyweightSweepReport.Percent(result / 2));
        Assert.Contains("<p class=\"luck\" data-meeting=\"0\" data-luck=\"62\">", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"compared\" data-sessions=\"0\">", page, StringComparison.Ordinal);
    }
}
