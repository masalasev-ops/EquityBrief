using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// A setup family's sweep: its walks and their benchmark worked by hand, five a night with one open trade a
// stock, a setting's figures and the proposal over constructed records, the breakout's readings read back
// against the rows the night stored over the fixture, and a constructed history whose report states a known
// answer.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
public partial class FixtureExpectations
{
    // The claims the family sweeps make, which this check reaches: section 17's rows for the breakout's grid and
    // for every family sweep's floors and test, and section 18's row for a family no setting of which meets them.
    internal static readonly string[] FamilySweepClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Breakout sweep grid"),
        CheckReach.Key(Scope.LimitsTable, "Family sweep floors"),
        CheckReach.Key(Scope.LimitsTable, "Family sweep test"),
        CheckReach.Key(Scope.FailureTable, "A family sweep with no setting meeting its floors"),
    ];

    [Fact]
    public void TheTrailingWalkRaisesItsStopWithTheHighestCloseNeverLowersItAndEndsAtItsCap()
    {
        // Bought at 100 with the stop 4 beneath and trailing by 4: 103 raises it to 99, 106 to 102, 104 leaves
        // it at 102, never lowered, and 101.9 under it sells after 4 sessions, at 1.9 over a risk of 4.
        double[] closes = [100, 103, 106, 104, 101.9, 120];

        Assert.Equal(0.475, FamilyWalks.Trailing(closes, 0, 100, 96, 4, 10, out var sessions)!.Value, 12);
        Assert.Equal(4, sessions);

        // A close at the trail does not sell: 103 raises it to 99, and 99 holds; the cap's close ends the trade.
        double[] holding = [100, 103, 99, 100];

        Assert.Equal(0, FamilyWalks.Trailing(holding, 0, 100, 96, 4, 3, out sessions)!.Value, 12);
        Assert.Equal(3, sessions);

        // A history that ends before the trade does says nothing of it, and it holds the stock for its cap.
        Assert.Null(FamilyWalks.Trailing([100, 101, 102], 0, 100, 96, 4, 5, out sessions));
        Assert.Equal(5, sessions);
    }

    [Fact]
    public void TheFixedWalkSellsUnderItsStopAtItsTargetAndAtItsCap()
    {
        // Bought at 100, stopped at 95 and won at 110: a risk of 5.
        Assert.Equal(-1.2, FamilyWalks.Fixed([100, 97, 94, 120], 0, 100, 95, 110, 5, out var sessions)!.Value, 12);
        Assert.Equal(2, sessions);

        // A close at the stop holds, and one at the target wins there.
        Assert.Equal(2, FamilyWalks.Fixed([100, 95, 105, 110, 90], 0, 100, 95, 110, 5, out sessions)!.Value, 12);
        Assert.Equal(3, sessions);

        // Neither reached by the cap: its close.
        Assert.Equal(0.4, FamilyWalks.Fixed([100, 101, 102, 103], 0, 100, 95, 110, 2, out sessions)!.Value, 12);
        Assert.Equal(2, sessions);

        Assert.Null(FamilyWalks.Fixed([100, 101], 0, 100, 95, 110, 5, out sessions));
    }

    [Fact]
    public void TheWalkKeepsFiveANightInTheFamilysOrderAndOneOpenTradeAStock()
    {
        string[] tickers = ["A", "B", "C", "D", "E", "F", "G"];

        static FamilyListing Listing(int name, int session, double order) => new(name, session, session, order, 0, 100, 96, double.NaN, 4, 63);

        // On session 10 the seven are listed, A highest; B and C tie, and C's ticker is the later. The first five
        // are kept, each holding its stock for 3 sessions but E, whose trade ends on the next.
        var listings = new List<FamilyListing>
        {
            Listing(6, 10, 1), Listing(5, 10, 2), Listing(4, 10, 3), Listing(3, 10, 4), Listing(2, 10, 5), Listing(1, 10, 5), Listing(0, 10, 7),

            // On session 12, A is still held, through 13; E is free again, its trade having ended on 11; F was
            // never kept and is free; so E and F are kept, in their order.
            Listing(0, 12, 9), Listing(5, 12, 1), Listing(4, 12, 2),
        };

        var kept = FamilySweep.Walk(
            listings,
            tickers,
            _ => 0,
            listing => (1.0, listing.Name == 4 && listing.Session == 10 ? 1 : 3),
            _ => 0.25);

        Assert.Equal(
            ["A 10", "B 10", "C 10", "D 10", "E 10", "E 12", "F 12"],
            kept.Select(trade => tickers[trade.Listing.Name] + " " + trade.Listing.Session.ToString(CultureInfo.InvariantCulture)));
        Assert.All(kept, trade => Assert.Equal(0.25, trade.Benchmark));
    }

    [Fact]
    public void ASettingsFiguresAreWorkedByHand()
    {
        static FamilyTrade Trade(int session, int year, double? result, double benchmark) =>
            new(new FamilyListing(0, session, session, 0, 0, 100, 96, double.NaN, 4, 63), year, result, benchmark);

        // Four kept: two in 2019, one in 2024 and one the history has not reached the end of. The edges are
        // 0.5, -0.8 and 2, so the edge is 1.7 over 3, the plain result 2 over 3, 2019's edge -0.15 and
        // 2024's 2, one year above nothing, and the last three years' edge 2. With five left out by size
        // nothing is left.
        var figures = FamilySweep.Figures(
            "constructed",
            [Trade(1, 0, 1.0, 0.5), Trade(2, 0, -1.0, -0.2), Trade(3, 5, 2.0, 0), Trade(3, 5, null, double.NaN)],
            100);

        Assert.Equal((4, 3, 3, 100), (figures.Listed, figures.Trades, figures.Nights, figures.ScoredNights));
        Assert.Equal(1.7 / 3, figures.Edge!.Value, 12);
        Assert.Equal(2.0 / 3, figures.Result!.Value, 12);
        Assert.Equal(-0.15, figures.YearEdge[0]!.Value, 12);
        Assert.Equal(2, figures.YearEdge[5]!.Value, 12);
        Assert.Equal([2, 0, 0, 0, 0, 1, 0, 0], figures.YearTrades);
        Assert.Equal(1, figures.YearsBeating);
        Assert.Equal(2, figures.RecentEdge!.Value, 12);
        Assert.Null(figures.EdgeWithoutLargest);

        // The standard error: the edges' deviations from 1.7 over 3 squared, over 2, over 3, rooted.
        var mean = 1.7 / 3;
        var variance = (Math.Pow(0.5 - mean, 2) + Math.Pow(-0.8 - mean, 2) + Math.Pow(2 - mean, 2)) / 2;

        Assert.Equal(Math.Sqrt(variance / 3), figures.StandardError!.Value, 12);
        Assert.False(figures.MeetsFloors);
    }

    [Fact]
    public void TheProposalIsTheBestEdgeAmongTheSettingsMeetingTheFloorsWithItsNeighboursAsVariants()
    {
        // Two dials of three levels, the provisional setting at the middle of each.
        var grid = new FamilyGrid([("a", [1, 2, 3]), ("b", [10, 20, 30])], [1, 1]);

        FamilyFigures Figures(int[] setting, int trades, int years, double edge) =>
            new(grid.Key(setting), trades, trades, 50, 100, edge, edge, new int[8], new double?[8], years, edge, edge, 0.01);

        var read = new List<(int[] Setting, FamilyFigures Figures)>();

        foreach (var setting in grid.Settings)
        {
            // Every setting meets the floors at an edge of 0.1, but these: a=3|b=30 has the best edge of all
            // and too few trades; a=1|b=10 and a=1|b=20 tie at 0.3, and a=1|b=20 moves one dial from the
            // provisional setting where a=1|b=10 moves two; a=2|b=30 fails its years.
            var key = grid.Key(setting);
            var figures = key switch
            {
                "a=3|b=30" => Figures(setting, FamilySweep.TradeFloor - 1, 8, 0.9),
                "a=1|b=10" or "a=1|b=20" => Figures(setting, FamilySweep.TradeFloor, FamilySweep.YearsBeating, 0.3),
                "a=2|b=30" => Figures(setting, 400, FamilySweep.YearsBeating - 1, 0.5),
                _ => Figures(setting, 400, 8, 0.1),
            };

            read.Add((setting, figures));
        }

        var proposal = FamilySweep.Propose(grid, read);

        Assert.Equal("a=1|b=20", proposal.Proposed!.Key);

        // Its neighbours, one step on one dial: a=2|b=20, a=1|b=10 and a=1|b=30, the higher edge first.
        Assert.Equal(["a=1|b=10", "a=1|b=30", "a=2|b=20"], proposal.Variants.Select(variant => variant.Key));

        // Where no setting meets the floors, nothing is proposed.
        var none = FamilySweep.Propose(grid, [.. read.Select(one => (one.Setting, one.Figures with { Trades = 10 }))]);

        Assert.True(none.NonePassed);
        Assert.Empty(none.Variants);
    }

    [Fact]
    public void ASweepNoSettingOfWhichPassesStatesItsFiveStrongestWithTheirShortfallsAndWhatCouldBeTriedNext()
    {
        // Two dials of three levels, the provisional setting at the middle of each, and no setting meeting the
        // floors. A setting's edge is a tenth of its two levels' places, each counted from one, summed, plus 0.3:
        // a=3|b=30 the highest at 0.9 with 120 trades in 8 years, a=3|b=20 and a=2|b=30 tied at 0.8 and taken by
        // the key, a=2|b=30 holding 100 trades past the floor and so short of nothing but its years. Every setting
        // holds the trades and years the switch gives it.
        var grid = new FamilyGrid([("a", [1, 2, 3]), ("b", [10, 20, 30])], [1, 1]);
        var read = new List<(int[] Setting, FamilyFigures Figures)>();

        foreach (var setting in grid.Settings)
        {
            var key = grid.Key(setting);
            var edge = ((setting[0] + 1) + (setting[1] + 1)) / 10.0 + 0.3;
            var (trades, years) = key switch
            {
                "a=3|b=30" => (120, 8),
                "a=3|b=20" => (290, 5),
                "a=2|b=30" => (FamilySweep.TradeFloor + 100, 4),
                _ => (50, 2),
            };

            read.Add((setting, new FamilyFigures(key, trades, trades, 50, 100, edge, edge, new int[8], new double?[8], years, edge, edge, 0.01)));
        }

        var proposal = FamilySweep.Propose(grid, read);
        var run = new FamilySweepRun(BreakoutRule.Name, "breakouts'", new DateOnly(2019, 1, 2), new DateOnly(2026, 10, 2), 3, 100, 100, 9, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var report = FamilySweepReport.Build(run, grid, read, proposal);

        Assert.True(proposal.NonePassed);

        // The five strongest, the highest edge first and a tie on the edge to the key: a=3|b=30 at 0.9, then
        // a=2|b=30 and a=3|b=20 at 0.8, then a=1|b=30 and a=2|b=20 and a=3|b=10 at 0.7, the first two of them.
        var strongest = System.Text.RegularExpressions.Regex.Matches(report, "<tr data-key=\"(?<key>[^\"]+)\" data-trades-short=\"(?<trades>\\d+)\" data-years-short=\"(?<years>\\d+)\">")
            .Select(match => (match.Groups["key"].Value, int.Parse(match.Groups["trades"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["years"].Value, CultureInfo.InvariantCulture)))
            .ToArray();

        Assert.Equal(
            [("a=3|b=30", 180, 0), ("a=2|b=30", 0, 2), ("a=3|b=20", 10, 1), ("a=1|b=30", 250, 4), ("a=2|b=20", 250, 4)],
            strongest);
        Assert.Contains("<p class=\"none-passed\" data-shown=\"5\">", report, StringComparison.Ordinal);

        // What could be tried next: four of the five short of the trades by 10 to 250, four short of the years by
        // 1 to 4; the strongest reads both dials at the top of the grid; and the ideas' run's tests that fit the
        // breakout, named, with the page saying it cannot tell which have run.
        Assert.Contains("4 of the 5 fall short of 300 trades, by 10 to 250", report, StringComparison.Ordinal);
        Assert.Contains("4 of the 5 fall short of an edge above nothing in 6 of the 8 years, by 1 to 4 year(s)", report, StringComparison.Ordinal);
        Assert.Contains("The strongest setting reads a at 3, the highest level the grid holds: a level above it could be read.", report, StringComparison.Ordinal);
        Assert.Contains("The strongest setting reads b at 30, the highest level the grid holds: a level above it could be read.", report, StringComparison.Ordinal);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(FamilyIdeas.For(BreakoutRule.Name)[0].Rule), report, StringComparison.Ordinal);
        Assert.Contains("this page cannot say which of them have run on it", report, StringComparison.Ordinal);

        // The family keeps its provisional settings and keeps listing, and the page never says it is set aside.
        Assert.Contains("The family keeps its provisional settings and keeps listing until the operator rules on it", report, StringComparison.Ordinal);
        Assert.DoesNotContain("set aside", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("set-aside", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTrailingBenchmarkIsTheSamePlanOnEveryMemberThatNightWorkedByHand()
    {
        // Three members over 30 weekdays, every bar a point either side of its close, so each member's typical
        // move is 2 while its closes move a point or less. After session 20: X rises 2 a session, Y falls 5 on
        // the first, and Z joined on session 25, so it holds no bar that night and is not read.
        var calendar = Weekdays(new DateOnly(2026, 1, 5), 30);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);

        decimal[] x = [.. Enumerable.Range(0, 30).Select(day => day <= 20 ? 100m : 100m + (2m * (day - 20)))];
        decimal[] y = [.. Enumerable.Range(0, 30).Select(day => day <= 20 ? 50m : 45m)];

        var series = new[]
        {
            SweepColumns.Series(Constructed("X", calendar, x, 1000), sessionAt),
            SweepColumns.Series(Constructed("Y", calendar, y, 1000), sessionAt),
            SweepColumns.Series(Constructed("Z", [.. calendar.Skip(25)], [100m, 100m, 100m, 100m, 100m], 1000), sessionAt),
        };
        var closes = series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();
        var members = SweepBenchmark.On(series, calendar.Length);

        Assert.Equal(2, series[0].Atr[20], 9);
        Assert.Equal(2, series[1].Atr[20], 9);

        // At 2 moves the stop is 4 beneath and trails by 4, held at most 5 sessions: X never closes under its
        // trail and ends at its cap 10 above, 2.5 risks; Y closes 5 under on the first session, -1.25.
        Assert.Equal((2.5 - 1.25) / 2, BreakoutSweep.BenchmarkOn(series, closes, members, 20, 2, 5), 12);
    }

    [Fact]
    public async Task TheBreakoutSweepReadsEveryMemberAsTheNightStoredItOverTheFixture()
    {
        // On the fixture's night, the session sampled, every member the breakout's rule stored a row for: the
        // year's high, the volume over its fifty-session average, the newer ranges over the older and the
        // typical move are the ones the night's own rule read, each read through the sweep's own path from the
        // bars the store holds. The two nights' store is run through the families' evaluator for that night,
        // as the night's step after the swing filter runs it.
        using var store = await WithTwoNights();

        await new Worker.Families.FamilyEvaluator(Core.Time.FixedClock.At(FixtureEvening, Core.Time.SessionZones.UnitedStates), store.DatabaseFile).RunAsync("two-nights-fixture-family-rules");

        var inputs = await new SweepHistory(store.DatabaseFile).ReadAsync(FixtureNight);
        var rows = new Dictionary<(string Ticker, DateOnly Night), string>();
        var day = FixtureNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var ticker in SweepRows(store, $"SELECT ticker FROM family_result WHERE session_date = '{day}' AND family = '{BreakoutRule.Name}' ORDER BY ticker;"))
        {
            rows[(ticker, FixtureNight)] = SweepRows(store, $"SELECT gates FROM family_result WHERE session_date = '{day}' AND family = '{BreakoutRule.Name}' AND ticker = '{ticker}';").Single();
        }

        SweepRelease(store);

        var sessionAt = inputs.Sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var compared = 0;

        Assert.True(rows.Count >= 4, $"The fixture's night stored {rows.Count} breakout row(s), expected a row for each of its four members.");

        foreach (var ((ticker, night), stored) in rows)
        {
            var one = SweepColumns.Series(inputs.Names.Single(name => name.Ticker == ticker), sessionAt);
            var bar = Array.IndexOf(one.SessionAt, sessionAt[night]);
            var gates = FamilyRule.GatesOf(stored).ToDictionary(gate => gate.Name, gate => gate.Values);
            var high = BreakoutSweep.HighsBefore(one.Bars, BreakoutRule.HighSessions)[bar];
            var ranges = BreakoutSweep.RangesBefore(one.Bars, BreakoutRule.RangeSessions)[bar];
            var volume = Statistic.FromVolume(one.Bars[bar].Volume) / one.Volume50[bar];

            static void Same(IReadOnlyDictionary<string, string> values, string key, double read, string what)
            {
                if (values.TryGetValue(key, out var held))
                {
                    var value = double.Parse(held, CultureInfo.InvariantCulture);

                    Assert.True(Math.Abs(value - read) <= 1e-9 * Math.Max(1, Math.Abs(value)), $"{what}: the night stored {held} and the sweep read {read}.");
                }
                else
                {
                    Assert.True(double.IsNaN(read), $"{what}: the night read none and the sweep read {read}.");
                }
            }

            Same(gates[BreakoutRule.NewHigh], "high", high, $"{ticker} {night} high");
            Same(gates[BreakoutRule.Volume], "multiple", volume, $"{ticker} {night} volume");
            Same(gates[BreakoutRule.Tightened], "ratio", ranges, $"{ticker} {night} ranges");

            if (gates[FamilyRule.Trade].ContainsKey("typical move"))
            {
                Same(gates[FamilyRule.Trade], "typical move", one.Atr[bar], $"{ticker} {night} typical move");
            }

            compared++;
        }

        Assert.Equal(rows.Count, compared);
    }

    [Fact]
    public void TheBreakoutSweepStatesAConstructedHistorysKnownAnswerInItsReport()
    {
        // Three members over the weekdays from 2018-01-01 past 2019, each bar a point either side of its
        // close, rising a hundredth a session on 1,000 shares, so every close sits above its 200-day average,
        // no close passes the highs before it and every typical move is 2. On 2019-02-01 A closes 5 above the
        // session before on 3,000 shares: a new high on heavy volume after ranges that narrowed a little, as a
        // share of a rising close. It then rises a point a session for three and falls 6 on the fourth.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var breakout = Array.IndexOf(calendar, new DateOnly(2019, 2, 1));
        var a = new decimal[calendar.Length];
        var volumes = new long[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            var drift = 100m + (0.01m * day);

            a[day] = day < breakout ? drift
                : day == breakout ? a[day - 1] + 5
                : day <= breakout + 3 ? a[day - 1] + 1
                : day == breakout + 4 ? a[day - 1] - 6
                : a[day - 1];
            volumes[day] = day == breakout ? 3000 : 1000;
        }

        var drifting = Enumerable.Range(0, calendar.Length).Select(day => 100m + (0.01m * day)).ToArray();
        var series = new[]
        {
            SweepColumns.Series(Constructed("A", calendar, a, volumes), sessionAt),
            SweepColumns.Series(Constructed("B", calendar, drifting, 1000), sessionAt),
            SweepColumns.Series(Constructed("C", calendar, drifting, 1000), sessionAt),
        };
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var firstScored = Array.FindIndex(calendar, day => day >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var sweep = new BreakoutSweep(series, members);
        var readings = sweep.Readings(sessions, firstScored);
        var tickers = new[] { "A", "B", "C" };
        var read = new List<(int[] Setting, FamilyFigures Figures)>();

        foreach (var setting in BreakoutSweep.Grid.Settings)
        {
            var trades = FamilySweep.Walk(BreakoutSweep.Listings(readings, setting), tickers, session => calendar[session].Year - SweepColumns.FirstScored.Year, sweep.Exit, sweep.Benchmark);

            read.Add((setting, FamilySweep.Figures(BreakoutSweep.Grid.Key(setting), trades, nights)));
        }

        // The one reading is A's breakout.
        Assert.Equal([(0, breakout)], readings.Select(reading => (reading.Name, reading.Bar)));

        // A's typical move that night takes in the 6 its own range reached from the close before: 13 sessions
        // at 2 and one at 6, over 14, so the provisional stop of 2 moves is 64 over 14 beneath. The trail it
        // sets is 3 over that from the third session's close, 2 under it, and the fourth's close 3 under the
        // buy sells it: -3 over 64 over 14, -0.65625 risks. B and C, bought the same night on the same plan at a
        // typical move of 2, rise a hundredth a session to their cap of 63: 0.63 over 4 each.
        var risk = 2 * ((13 * 2) + 6) / 14.0;
        var result = -3 / risk;
        var benchmark = (result + (2 * (0.63 / 4))) / 3;
        var provisional = read.Single(one => BreakoutSweep.Grid.Changes(one.Setting) == 0).Figures;

        Assert.Equal(-0.65625, result, 12);
        Assert.Equal((1, 1), (provisional.Listed, provisional.Trades));
        Assert.Equal(result - benchmark, provisional.Edge!.Value, 9);

        // Every setting holding one trade at most, none meets the floors and nothing is proposed; the report says
        // so and states the provisional setting's record as read here.
        var proposal = FamilySweep.Propose(BreakoutSweep.Grid, read);
        var run = new FamilySweepRun(BreakoutRule.Name, "breakouts'", calendar[firstScored], calendar[^1], 3, nights, nights, readings.Count, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var report = FamilySweepReport.Build(run, BreakoutSweep.Grid, read, proposal);

        Assert.True(proposal.NonePassed);
        Assert.Contains("class=\"none-passed\"", report, StringComparison.Ordinal);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(FamilySweepReport.Test), report, StringComparison.Ordinal);

        var row = System.Text.RegularExpressions.Regex.Match(report, $"data-key=\"{System.Text.RegularExpressions.Regex.Escape(provisional.Key)}\" data-trades=\"(?<trades>\\d+)\" data-edge=\"(?<edge>[^\"]+)\"><td>The provisional setting</td>");

        Assert.True(row.Success, "The report draws no row for the provisional setting.");
        Assert.Equal("1", row.Groups["trades"].Value);
        Assert.Equal(result - benchmark, double.Parse(row.Groups["edge"].Value, CultureInfo.InvariantCulture), 3);
    }

    static DateOnly[] Weekdays(DateOnly from, int count)
    {
        var days = new List<DateOnly>();

        for (var day = from; days.Count < count; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return [.. days];
    }

    static SweepName Constructed(string ticker, IReadOnlyList<DateOnly> days, IReadOnlyList<decimal> closes, long volume) =>
        Constructed(ticker, days, closes, [.. Enumerable.Repeat(volume, days.Count)]);

    // A member held throughout, each bar a point either side of its close.
    static SweepName Constructed(string ticker, IReadOnlyList<DateOnly> days, IReadOnlyList<decimal> closes, IReadOnlyList<long> volumes) =>
        new(
            ticker,
            [.. days.Select((day, at) => new SweepBar(day, closes[at] + 1, closes[at] - 1, closes[at], volumes[at], closes[at]))],
            [(null, null)],
            []);
}
