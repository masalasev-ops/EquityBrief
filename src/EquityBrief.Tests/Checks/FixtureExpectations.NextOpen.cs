using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.10: the next-open check, each figure worked by hand. Where the session after a listing opened
// against its plan's range at both edges and in it; a setup's path bought at that open with the same stop, target, trail
// and cap, its result in the risk the listing's close put up; the benchmark and the heavyweights' size cut bought each at
// its own next open; each family's figures by year over a constructed ledger; and the command over a constructed store
// and over the fixture's replayed store, writing nothing.
// see: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone
public partial class FixtureExpectations
{
    static readonly DateOnly NextOpenDay = new(2026, 1, 5);

    [Fact]
    public void TheNextOpenIsPastThePlansRangeAtOrUnderItsStopAndAtOrOverItsTargetAndInsideBetween()
    {
        var fixedPlan = new SetupAnchor(NextOpenDay, 100, 95, 110, null, 10, 2);
        var trailing = new SetupAnchor(NextOpenDay, 100, 95, null, 5, 10, 2);

        Assert.Equal(NextOpenGap.UnderStop, NextOpenCheck.Gap(fixedPlan, 94));
        Assert.Equal(NextOpenGap.UnderStop, NextOpenCheck.Gap(fixedPlan, 95));
        Assert.Equal(NextOpenGap.Inside, NextOpenCheck.Gap(fixedPlan, 95.01));
        Assert.Equal(NextOpenGap.Inside, NextOpenCheck.Gap(fixedPlan, 109.99));
        Assert.Equal(NextOpenGap.OverTarget, NextOpenCheck.Gap(fixedPlan, 110));
        Assert.Equal(NextOpenGap.OverTarget, NextOpenCheck.Gap(fixedPlan, 120));
        Assert.Equal(NextOpenGap.NoOpen, NextOpenCheck.Gap(fixedPlan, null));
        Assert.Equal(NextOpenGap.NoOpen, NextOpenCheck.Gap(fixedPlan, 0));

        // A trailing stop has no target to open past.
        Assert.Equal(NextOpenGap.Inside, NextOpenCheck.Gap(trailing, 200));
        Assert.Equal(NextOpenGap.UnderStop, NextOpenCheck.Gap(trailing, 95));
    }

    [Fact]
    public void ASetupBoughtAtTheNextOpenSellsAtTheSameCloseAndStatesItsResultInTheRiskTheListingsClosePutUp()
    {
        // Bought at 100 with its stop at 95 and its target at 110, it sells at 111 on the second session: (111 - 100) / 5.
        double[] closes = [100, 104, 111];
        var fixedPlan = new SetupAnchor(NextOpenDay, 100, 95, 110, null, 5, 2);
        var atClose = SetupReplay.Replay(closes, 0, fixedPlan);

        Assert.Equal((2.2, 2, SetupEnds.Target), (atClose.Result!.Value, atClose.Sessions, atClose.End));

        // At an open of 102 it sells at the same close, (111 - 102) over the close's risk of 5, two fifths less.
        var atOpen = NextOpenCheck.Replay(closes, 0, fixedPlan, 102)!;

        Assert.Equal(1.8, atOpen.Result!.Value, 12);
        Assert.Equal((2, SetupEnds.Target), (atOpen.Sessions, atOpen.End));

        // An open over the target or under the stop buys nothing.
        Assert.Null(NextOpenCheck.Replay(closes, 0, fixedPlan, 111));
        Assert.Null(NextOpenCheck.Replay(closes, 0, fixedPlan, 94));
        Assert.Null(NextOpenCheck.Replay(closes, 0, fixedPlan, null));

        // A trail of 3 under the highest close: 106 lifts the stop to 103, 104 holds over it, and 99 sells, the third
        // session either way; bought at the close (99 - 100) / 5, at an open of 101 (99 - 101) / 5.
        double[] trailingCloses = [100, 106, 104, 99];
        var trailing = new SetupAnchor(NextOpenDay, 100, 95, null, 3, 10, 2);

        Assert.Equal((-0.2, 3, SetupEnds.Trail), (SetupReplay.Replay(trailingCloses, 0, trailing).Result!.Value, 3, SetupReplay.Replay(trailingCloses, 0, trailing).End));
        Assert.Equal(-0.4, NextOpenCheck.Replay(trailingCloses, 0, trailing, 101)!.Result!.Value, 12);
        Assert.Equal(3, NextOpenCheck.Replay(trailingCloses, 0, trailing, 101)!.Sessions);
    }

    [Fact]
    public void TheBenchmarkAtTheNextOpenEntersEachMemberAtItsOwnOpenAndLeavesOutAnOpenPastItsRange()
    {
        // A plan of 2 moves under the buy and a target at 2 risks, on members whose move is 2: each stop 4 under its close
        // and each target 8 over it.
        var plan = new SetupPlan(2, 2, null, 10);

        // The first opens at 102 inside 96 to 108 and sells at 109, (109 - 102) / 4; the second opens at 45 under its stop
        // of 46 and the third at 89 over its target of 88, neither entered; the fourth's prices stopped on its session.
        (double[] Closes, double[] Opens, int Bar, double Move, bool Stopped)[] members =
        [
            ([100, 103, 109], [100, 102, 109], 0, 2, false),
            ([50, 47, 45], [50, 45, 45], 0, 2, false),
            ([80, 90, 90], [80, 89, 90], 0, 2, false),
            ([30], [30], 0, 2, true),
        ];

        var reading = NextOpenCheck.Benchmark(members, NextOpenDay, plan);

        Assert.Equal((1.75, 1, 2, 1, true), (reading.Mean!.Value, reading.Ended, reading.Entered, reading.Stopped, reading.Settled));

        // A member entered inside its range whose path is still open keeps the benchmark unsettled.
        var open = NextOpenCheck.Benchmark([.. members, ([40, 41], [40, 40.5], 0, 2, false)], NextOpenDay, plan);

        Assert.Equal((1.75, 1, 3, false), (open.Mean!.Value, open.Ended, open.Entered, open.Settled));

        // Bought at the close, the first two end and the third gaps over its target before it starts: (109 - 100) / 4,
        // (45 - 50) / 4 and (90 - 80) / 4 over the three.
        var atClose = SetupBenchmark.Of([.. members.Select(member => (member.Closes, member.Bar, member.Move, member.Stopped))], NextOpenDay, plan);

        Assert.Equal((2.25 - 1.25 + 2.5) / 3, atClose.Mean!.Value, 12);
    }

    [Fact]
    public void AHeavyweightsSetupAndItsSizeCutAtTheNextOpenAreFractionsOfTheSumTheListingsClosePutToEach()
    {
        // Listed at 100 and ended two sessions on at 121: 0.21 bought at the close, (121 - 105) / 100 at an open of 105.
        double[] closes = [100, 110, 121];
        double[] opens = [100, 105, 120];

        Assert.Equal(0.16, NextOpenCheck.HeavyweightResult(closes, 0, 2, opens[1])!.Value, 12);
        Assert.Null(NextOpenCheck.HeavyweightResult(closes, 0, 2, null));
        Assert.Null(NextOpenCheck.HeavyweightResult(closes, 0, 3, opens[1]));

        // The cut: the stock itself, a member from 50 opening at 48 and ending at 60, (60 - 48) / 50, and a member whose
        // prices stopped short of the path's end, left out; the mean of the two ended and settled.
        (double[] Closes, double[] Opens, int From, bool Stopped)[] cut =
        [
            (closes, opens, 0, false),
            ([50, 50, 60], [50, 48, 60], 0, false),
            ([20], [20], 0, true),
        ];

        var reading = NextOpenCheck.HeavyweightCut(cut, 2);

        Assert.Equal(((0.16 + 0.24) / 2, 2, 3, 1, true), (reading.Mean!.Value, reading.Ended, reading.Entered, reading.Stopped, reading.Settled));

        // A member still open whose prices did not stop keeps it unsettled.
        Assert.False(NextOpenCheck.HeavyweightCut([.. cut, ([10, 11], [10, 11], 0, false)], 2).Settled);
    }

    [Fact]
    public void EachFamilysFiguresByYearAreMeansOverTheSetupsPricedBothWaysWithTheOpensPastTheRangeCounted()
    {
        NextOpenPricing[] setups =
        [
            new("pullback", 2024, NextOpenGap.Inside, 1.0, 0.5, 0.5, 0.0),
            new("pullback", 2024, NextOpenGap.OverTarget, 2.0, 1.0, null, null),
            new("pullback", 2025, NextOpenGap.Inside, -1.0, -1.5, -1.5, -2.0),
            new("pullback", 2025, NextOpenGap.UnderStop, -1.0, null, null, null),
            new("pullback", 2025, NextOpenGap.NoOpen, 0.4, 0.2, null, null),
            new("breakout", 2025, NextOpenGap.Inside, 3.0, null, 2.0, null),
        ];

        var groups = NextOpenCheck.Groups(setups, ["pullback", "breakout"]);

        // Worked by hand: the pullback's two pairs average 0 at the close and -0.5 at the open, their edges -0.5 and -1;
        // each year holds one pair; the breakout's one setup has no edge either way.
        Assert.Equal(
            [
                new NextOpenGroup("pullback", null, 5, 2, 0.0, -0.5, 2, -0.5, -1.0, 1, 1, 1),
                new NextOpenGroup("pullback", 2024, 2, 1, 1.0, 0.5, 1, 0.5, 0.0, 0, 1, 0),
                new NextOpenGroup("pullback", 2025, 3, 1, -1.0, -1.5, 1, -1.5, -2.0, 1, 0, 1),
                new NextOpenGroup("breakout", null, 1, 1, 3.0, 2.0, 0, null, null, 0, 0, 0),
                new NextOpenGroup("breakout", 2025, 1, 1, 3.0, 2.0, 0, null, null, 0, 0, 0),
            ],
            groups);
        Assert.Equal((-0.5, -0.5), (groups[0].ResultDifference, groups[0].EdgeDifference));
        Assert.Null(groups[3].EdgeDifference);

        // A family the order does not name follows the ones it does.
        Assert.Equal(["breakout", "pullback"], NextOpenCheck.Groups(setups, ["breakout"]).Select(group => group.Family).Distinct());
    }

    [Fact]
    public async Task TheCheckOverAConstructedLedgerStatesTheFiguresWorkedByHandAndOverTheFixturesFindsNoSettledSetup()
    {
        var expected = Expected("next-open");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.StartsWith("19.10's check", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);
        Assert.Equal(["setup_night"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var days = new List<DateOnly>();

        for (var day = NextOpenDay; days.Count < 30; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                days.Add(day);
            }
        }

        string Stamp(int at) => days[at].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // Flat with a range of 2 through the 21st session, the listing's, then AAA opening at 102 and closing at 103 and
        // 109, and BBB opening at 45 and closing at 47 and 45.
        (decimal Open, decimal Close, decimal Low, decimal High) Bar(string ticker, int at) => (ticker, at) switch
        {
            ("AAA", 21) => (102, 103, 101, 104),
            ("AAA", >= 22) => (109, 109, 108, 110),
            ("BBB", 21) => (45, 47, 44, 48),
            ("BBB", >= 22) => (45, 45, 44, 46),
            ("AAA", _) => (100, 100, 99, 101),
            _ => (50, 50, 49, 51),
        };

        using var store = new TemporaryStore().Migrated();

        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-02-13T21:10:00Z');");

            for (var at = 0; at < days.Count; at++)
            {
                var (open, close, low, high) = Bar(ticker, at);

                store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{Stamp(at)}', '{open}', '{high}', '{low}', '{close}', 1000000, 'constructed', '{Stamp(at)}T21:00:00Z', '{close}');");
            }
        }

        foreach (var (ticker, entry, stop, target, result, edge, end) in new[] { ("AAA", 100, 96, 108, 2.25, 1.75, "target"), ("BBB", 50, 46, 58, -1.25, -1.75, "stop") })
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, target, cap, result, benchmark, edge, sessions, end, ended_on, settled, source, pin) "
                + $"VALUES ('GSPC', 'pullback', '{ticker}', '{Stamp(20)}', 'the pullback', 1, '{entry}', '{stop}', '{target}', 10, {result.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0.5, {edge.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 2, '{end}', '{Stamp(22)}', 1, 'history', '{LedgerReadings.Version}');");
        }

        var before = Query(store, "SELECT COUNT(*) FROM run_log;").Single();
        using var output = new StringWriter();
        var ledger = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 2, 16, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(0, await ledger.NextOpenAsync(IndexFamilies.LargeIndex, output));
        SweepRelease(store);

        var line = expected.GetProperty("constructedLine").GetString()!;
        var said = output.ToString();

        Assert.Contains($"{SetupLedger.NextOpenStage}: {line}", said, StringComparison.Ordinal);
        Assert.Contains($"{SetupLedger.NextOpenStage}: {line.Replace("every year", "2026", StringComparison.Ordinal)}", said, StringComparison.Ordinal);
        Assert.Contains($"2 of 2 settled setup(s) on the S&P 500 priced over the history read through {Stamp(29)}, 0 whose stock or session the history does not hold; 2 of 2 results at the close worked again as stored to a millionth and 2 to a thousandth, and 2 of the 2 stored edges to a millionth and 2 to a thousandth; nothing written", said, StringComparison.Ordinal);

        // It writes nothing: no run log row, and every setup as it was.
        Assert.Equal(before, Query(store, "SELECT COUNT(*) FROM run_log;").Single());
        Assert.Equal(["AAA|2.25|1.75", "BBB|-1.25|-1.75"], Query(store, "SELECT ticker || '|' || result || '|' || edge FROM setup ORDER BY ticker;"));

        // Over the fixture's replayed store every setup the night appended is open on its night, so none is priced.
        using var replayed = await FixtureReplay.ReplayedAsync();
        using var fixture = new StringWriter();

        Assert.NotEmpty(Query(replayed, "SELECT family FROM setup_night;"));
        Assert.Equal(expected.GetProperty("fixtureSettled").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), Query(replayed, "SELECT COUNT(*) FROM setup WHERE settled = 1;").Single());
        Assert.Equal(0, await new SetupLedger(FixedClock.At(Instant, SessionZones.UnitedStates), replayed.DatabaseFile).NextOpenAsync(IndexFamilies.LargeIndex, fixture));
        Assert.Equal($"{SetupLedger.NextOpenStage}: the ledger holds no settled setup on the S&P 500, so nothing was priced", fixture.ToString().Trim());
    }
}
