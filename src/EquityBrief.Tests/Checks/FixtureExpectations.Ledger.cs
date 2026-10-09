using EquityBrief.Core;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3: the ledger's readings worked by hand as they stood, each market series reading at its
// window's edge, the loose gates on both sides of each threshold, a setup's edge against the same plan on every member,
// the readings of a session read the same whatever the series holds after it, the readings' pin, and the fixture's
// night writing one row a family with every setup inside its family's gates.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public partial class FixtureExpectations
{
    // The rows 17.3's ledger adds that this check reaches: section 17's two rows and section 18's row on a reading.
    internal static readonly string[] LedgerClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Setup ledger's loose gates"),
        CheckReach.Key(Scope.LimitsTable, "Setup ledger's history budget"),
        CheckReach.Key(Scope.FailureTable, "A reading the ledger cannot read"),
    ];

    // Every row 17.3's ledger adds, named after phase 16's report until phase 17's own pair is checked: the component's
    // catalogue and matrix rows, its two stores, the three above, section 18's row on an index's part and the night's step.
    internal static string[] LedgerRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Setup ledger"),
        CheckReach.Key(Scope.MatrixTable, "Setup ledger"),
        CheckReach.Key(Scope.StoresTable, "Setups"),
        CheckReach.Key(Scope.StoresTable, "Setup nights"),
        .. LedgerClaims,
        CheckReach.Key(Scope.FailureTable, "An index's setups the ledger could not compute"),
        CheckReach.Key(NightlyRunSteps.Heading, "Append tonight's setups to the ledger on every index the night read: every member-session a family's loose gates pass, the pullback's, the breakout's and the drift's, with the live rule's own pass and the night's pick beside it, its plan as prices, its readings as they stood and its path still open, and one row a family an index with the members the gates were read over; and close the windows of the setups stored before whose paths ended on tonight's close or whose benchmark, the same plan on every member of their session, has every member's path ended, a failure in an index's part named on the step's row while the step goes on (see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood)."),
    ];

    static readonly DateOnly LedgerDay = new(2026, 10, 8);

    static IReadOnlyList<(DateOnly Session, double Close)> Series(params double[] closes) =>
        [.. closes.Select((close, at) => (LedgerDay.AddDays(at - closes.Length + 1), close))];

    [Fact]
    public void EachMarketSeriesReadingIsWorkedByHandAtItsWindowsEdgeOverTheSeriesOwnSessions()
    {
        // Five closes ending on the day: the day's close over the mean of three is 110 over 100, over the mean of all
        // five 110 over 94, and over the close two sessions before 110 over 100; a window one session wider than the
        // series reads none.
        var series = Series(80, 90, 100, 90, 110);

        Assert.Equal(110.0, SeriesReadings.CloseOn(series, LedgerDay));
        Assert.Equal(1.1, SeriesReadings.OverAverage(series, LedgerDay, 3)!.Value, 12);
        Assert.Equal(1.1, SeriesReadings.OverBefore(series, LedgerDay, 2)!.Value, 12);
        Assert.Equal(110.0 / 94.0, SeriesReadings.OverAverage(series, LedgerDay, 5)!.Value, 12);
        Assert.Null(SeriesReadings.OverAverage(series, LedgerDay, 6));
        Assert.Equal(1.375, SeriesReadings.OverBefore(series, LedgerDay, 4)!.Value, 12);
        Assert.Null(SeriesReadings.OverBefore(series, LedgerDay, 5));

        // A day the series has no session on reads the last session before it, and a day before the series none.
        Assert.Equal(110.0, SeriesReadings.CloseOn(series, LedgerDay.AddDays(3)));
        Assert.Null(SeriesReadings.CloseOn(series, LedgerDay.AddDays(-5)));
        Assert.Equal(90.0, SeriesReadings.CloseOn(series, LedgerDay.AddDays(-3)));

        // One series over another across two sessions: 110 over 100 against 50 over 40 is 1.1 over 1.25.
        var against = Series(30, 35, 40, 45, 50);

        Assert.Equal(1.1 / 1.25, SeriesReadings.RelativeReturn(series, against, LedgerDay, 2)!.Value, 12);
        Assert.Null(SeriesReadings.RelativeReturn(series, Series(40, 50), LedgerDay, 2));

        // Liquidity is the base-10 logarithm of the mean of close times volume over fifty sessions, none under fifty.
        var window = Enumerable.Range(0, 50).Select(_ => (Close: 100.0, Volume: 1_000_000L)).ToArray();

        Assert.Equal(8.0, LedgerReadings.Liquidity(window)!.Value, 12);
        Assert.Null(LedgerReadings.Liquidity(window[..49]));
        Assert.Null(LedgerReadings.Liquidity(Enumerable.Range(0, 50).Select(_ => (Close: 100.0, Volume: 0L)).ToArray()));

        Assert.Null(LedgerReadings.Over(1, double.NaN));
        Assert.Null(LedgerReadings.Over(double.NaN, 1));
        Assert.Null(LedgerReadings.Over(1, 0));
        Assert.Equal(0.5, LedgerReadings.Over(1, 2));
        Assert.Null(LedgerReadings.Figure(double.NaN));
        Assert.Equal((1.0, 0.0), (LedgerReadings.Flag(true)!.Value, LedgerReadings.Flag(false)!.Value));

        // The catalogue names each column once.
        Assert.Equal(LedgerReadings.All.Count, LedgerReadings.All.Select(reading => reading.Column).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(45, LedgerReadings.Count);
    }

    [Fact]
    public void EachLooseGateIsWorkedByHandOnBothSidesOfItsThresholdAndAtIt()
    {
        // The pullback: strength at the floor passes and a hundredth under does not; depth at either end passes and a
        // hundredth past either does not; a trigger 14 sessions old passes and 15 does not, one that never fired does
        // not; a plan paying half its risk passes and a hundredth under does not.
        Assert.True(SetupGates.Pullback(0.20, 0.5, 14, 0.5));
        Assert.False(SetupGates.Pullback(0.19, 0.5, 14, 0.5));
        Assert.True(SetupGates.Pullback(0.20, 12, 0, 0.5));
        Assert.False(SetupGates.Pullback(0.20, 0.49, 0, 0.5));
        Assert.False(SetupGates.Pullback(0.20, 12.01, 0, 0.5));
        Assert.False(SetupGates.Pullback(0.20, 2, 15, 0.5));
        Assert.False(SetupGates.Pullback(0.20, 2, -1, 0.5));
        Assert.False(SetupGates.Pullback(0.20, 2, 3, 0.49));
        Assert.False(SetupGates.Pullback(double.NaN, 2, 3, 1));

        // The breakout: a close above the high passes and at it does not; the average volume passes and a hundredth
        // under does not; ranges at 1.25 pass and a hundredth over do not.
        Assert.True(SetupGates.Breakout(100.01, 100, 1.0, 1.25));
        Assert.False(SetupGates.Breakout(100, 100, 1.0, 1.25));
        Assert.False(SetupGates.Breakout(100.01, 100, 0.99, 1.25));
        Assert.False(SetupGates.Breakout(100.01, 100, 1.0, 1.26));
        Assert.False(SetupGates.Breakout(100.01, double.NaN, 1.0, 1.0));

        // The drift: a surprise above nothing with a rise of a quarter of a move passes; nothing and a hair under do not.
        Assert.True(SetupGates.Drift(0.01, 0.25));
        Assert.False(SetupGates.Drift(0, 0.25));
        Assert.False(SetupGates.Drift(5, 0.24));
        Assert.False(SetupGates.Drift(5, double.NaN));

        // The floors: $5 at the price passes and a cent under does not; half the index's dollar floor passes on the
        // S&P 400 and a dollar under does not, while the S&P 500 has none and a volume not held fails an index with one.
        Assert.True(SetupGates.ClearsTheFloors("GSPC", 5m, null));
        Assert.False(SetupGates.ClearsTheFloors("GSPC", 4.99m, null));
        Assert.True(SetupGates.ClearsTheFloors("MID", 5m, 5_000_000m));
        Assert.False(SetupGates.ClearsTheFloors("MID", 5m, 4_999_999m));
        Assert.False(SetupGates.ClearsTheFloors("MID", 5m, null));
        Assert.True(SetupGates.ClearsTheFloors("SML", 5m, 2_500_000m));
        Assert.False(SetupGates.ClearsTheFloors("SML", 5m, 2_499_999m));
    }

    [Fact]
    public void ASetupsEdgeIsWorkedByHandAgainstTheSamePlanOnEveryMemberAndTheBenchmarkSettlesOnlyOnceEveryPathHasEnded()
    {
        // A plan two moves under the buy with a target two risks above, capped at five sessions. Three members with a
        // typical move of 1 each, bought at their closes on the first session: the first falls through its stop of 98
        // on the second close, 97, for -1.5 risks; the second reaches its target of 54 on the second close for 2; and
        // the third runs to the cap's close, 10.5, for +0.25 risks. The mean of the three is 0.25.
        var plan = new SetupPlan(2, 2, null, 5);
        var first = new double[] { 100, 99, 97, 96, 96, 96, 96 };
        var second = new double[] { 50, 51, 54, 60, 60, 60, 60 };
        var third = new double[] { 10, 10.2, 10.4, 10.5, 10.6, 10.5, 20 };

        var benchmark = SetupBenchmark.Of([(first, 0, 1.0), (second, 0, 1.0), (third, 0, 1.0)], LedgerDay, plan);

        Assert.Equal((3, 3, true), (benchmark.Ended, benchmark.Entered, benchmark.Settled));
        Assert.Equal(0.25, benchmark.Mean!.Value, 12);

        // The first member's own setup under the same plan: its result -1.5, so its edge against the benchmark is -1.75.
        var anchor = plan.On(LedgerDay, 100, 1.0)!;

        Assert.Equal(new SetupAnchor(LedgerDay, 100, 98, 104, null, 5, 2), anchor);
        Assert.Equal(new SetupOutcome(-1.5, 2, SetupEnds.Stop), SetupReplay.Replay(first, 0, anchor));
        Assert.Equal(-1.75, SetupReplay.Replay(first, 0, anchor).Result!.Value - benchmark.Mean!.Value, 12);

        // The series cut after three closes: two members have ended and the third is still open, so the mean is over
        // two and the benchmark is not settled.
        var cut = SetupBenchmark.Of([(first[..3], 0, 1.0), (second[..3], 0, 1.0), (third[..3], 0, 1.0)], LedgerDay, plan);

        Assert.Equal((0.25, 2, 3, false), (cut.Mean!.Value, cut.Ended, cut.Entered, cut.Settled));

        // A member with no move, one whose bar the session misses, and one whose stop would sit at or under nothing
        // is not entered; a trailing plan places its trail in the member's own move.
        Assert.Equal(new BenchmarkReading(null, 0, 0), SetupBenchmark.Of([(first, 0, 0.0), (second, -1, 1.0), (third, 0, 6.0)], LedgerDay, plan));
        Assert.Equal(new SetupAnchor(LedgerDay, 100, 97, null, 4.5, 63, 3), new SetupPlan(3, null, 4.5, 63).On(LedgerDay, 100, 1.0));
    }

    [Fact]
    public void AReadingIsReadAsItStoodWhateverTheSeriesHoldsAfterTheSession()
    {
        // Eighty sessions of a constructed series; the readings of the sixty-first are the same read over the series
        // cut there and over the whole, so nothing after the session reaches them, and the sixty-second's differ.
        var calendar = Enumerable.Range(0, 80).Select(at => new DateOnly(2026, 1, 2).AddDays(at)).ToArray();
        var bars = calendar.Select((session, at) =>
        {
            var close = 100m + (at % 7) + (at / 10m);

            return new SweepBar(session, close + 1m, close - 1m, close, 1_000_000 + (at * 1000), close, close);
        }).ToArray();

        double?[] ReadAt(int count, int bar)
        {
            var window = calendar[..count];
            var sessionAt = window.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
            var series = new[] { SweepColumns.Series(new SweepName("AAA", bars[..count], [(null, null)], [calendar[70]]), sessionAt) };
            var sessions = SweepColumns.Sessions(series, window);
            var members = SweepBenchmark.On(series, window.Length);
            var closes = series[0].Bars.Select(one => Core.Prices.Statistic.FromPrice(one.Close)).ToArray();
            var (highs, lows) = SweepIdeas.HighsAndLows(series, members, window.Length);
            var market = new LedgerMarket(new Dictionary<string, IReadOnlyList<(DateOnly, double)>>(StringComparer.Ordinal)
            {
                [LedgerMarket.Vix] = [.. window.Select((session, at) => (session, 15.0 + at))],
            });

            return LedgerSetups.Readings("GSPC", 0, series[0], closes, bar, sessions[bar], members, window, bar, market, new LedgerContext(new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), new Dictionary<string, string?>(), highs, lows));
        }

        var cut = ReadAt(61, 60);
        var whole = ReadAt(80, 60);
        var ahead = ReadAt(80, 61);

        Assert.Equal(cut, whole);
        Assert.NotEqual(cut, ahead);

        // Worked by hand off the construction: the close over its 20-session average; the weekdays to the print on
        // file ten days on, Tuesday 2026-03-03 to Friday 2026-03-13, eight of them; the VIX's close and its change
        // over ten sessions; and the readings the construction does not reach.
        var close = Core.Prices.Statistic.FromPrice(bars[60].Close);
        var average20 = bars[41..61].Average(bar => Core.Prices.Statistic.FromPrice(bar.Close));

        Assert.Equal((new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 13)), (calendar[60], calendar[70]));
        Assert.Equal(close / average20, whole[LedgerReadings.IndexOf("close_over_twenty")]!.Value, 9);
        Assert.Equal(8, whole[LedgerReadings.IndexOf("earnings_sessions")]);
        Assert.Equal(75.0, whole[LedgerReadings.IndexOf("vix")]);
        Assert.Equal(75.0 / 65.0, whole[LedgerReadings.IndexOf("vix_change")]!.Value, 12);
        Assert.Null(whole[LedgerReadings.IndexOf("index_over_long")]);
        Assert.Null(whole[LedgerReadings.IndexOf("close_over_long")]);
        Assert.Equal(0.0, whole[LedgerReadings.IndexOf("profit")]);
        Assert.Null(whole[LedgerReadings.IndexOf("freshness")]);
    }

    [Fact]
    public void TheReadingsVersionIsThePinOfTheCatalogueAndTheSourceThatFillsIt()
    {
        var sources = LedgerSources.Select(File.ReadAllText).ToArray();

        Assert.Equal(LedgerReadings.Version, SourcePin.Of(sources, LedgerReadings.VersionDeclaration));

        // A line of the filling source moved moves the pin, and a comment added does not.
        Assert.NotEqual(LedgerReadings.Version, SourcePin.Of([sources[0], sources[1] + "\nvar moved = 1;\n"], LedgerReadings.VersionDeclaration));
        Assert.Equal(LedgerReadings.Version, SourcePin.Of([sources[0], sources[1] + "\n// a comment\n"], LedgerReadings.VersionDeclaration));
    }

    internal static IReadOnlyList<string> LedgerSources { get; } =
    [
        Path.Combine(Repository.Root, "src", "EquityBrief.Core", "Ledger", "LedgerReadings.cs"),
        Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger", "LedgerSetups.cs"),
    ];

    [Fact]
    public async Task TheFixturesNightWritesOneLedgerRowAFamilyAndEverySetupSitsInsideItsFamilysGates()
    {
        // The ledger expectation names the two tables the step writes and says what it derives: one row a family of
        // the three on the index the night read, worked here from the families rather than frozen from a run.
        var expected = Expected("ledger");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(3, expected.GetProperty("setupNightsAnIndex").GetInt32());
        Assert.Equal(LedgerSetups.Families, expected.GetProperty("families").EnumerateArray().Select(family => family.GetString()!).ToArray());
        Assert.StartsWith("every setup row's family is one of the three", expected.GetProperty("setups").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("setup is the step's other table", expected.GetProperty("setupTable").GetString(), StringComparison.Ordinal);

        using var store = await Replayed();

        var outcome = await new SetupLedger(FixedClock.At(Instant, SessionZones.UnitedStates), store.DatabaseFile).NightAsync("ledger-fixture");

        Assert.Equal(new DateOnly(2026, 9, 4), outcome.Session);
        Assert.All(outcome.Indices, index => Assert.Null(index.Fault));
        // One row a family on the index the night read, each over the same members, and none for an index it did not.
        var nights = Query(store, "SELECT index_code || '|' || family || '|' || members || '|' || source FROM setup_night WHERE session_date = '2026-09-04' ORDER BY index_code, family;");
        var members = Query(store, "SELECT DISTINCT members FROM setup_night WHERE index_code = 'GSPC';").Single();

        Assert.True(int.Parse(members, System.Globalization.CultureInfo.InvariantCulture) > 0, "the fixture's night held no member");
        Assert.Equal([$"GSPC|breakout|{members}|night", $"GSPC|drift|{members}|night", $"GSPC|pullback|{members}|night"], nights);
        Assert.Equal(["ok"], Query(store, $"SELECT outcome FROM run_log WHERE run_id = 'ledger-fixture' AND stage = '{SetupLedger.Stage}';"));

        // Every setup written sits inside its family's loose gates, read back off its own readings and its session.
        Assert.All(Query(store, "SELECT family || '|' || session_date || '|' || source || '|' || pin FROM setup;"), row =>
        {
            var parts = row.Split('|');

            Assert.Contains(parts[0], LedgerSetups.Families);
            Assert.Equal(("2026-09-04", SetupLedger.NightSource, LedgerReadings.Version), (parts[1], parts[2], parts[3]));
        });
        Assert.Empty(Query(store, $"SELECT ticker FROM setup WHERE family = 'pullback' AND (strength < {SetupGates.PullbackStrength} OR depth < {SetupGates.PullbackDepthLow} OR depth > {SetupGates.PullbackDepthHigh} OR freshness > {SetupGates.PullbackFreshness} OR reward_to_risk < {SetupGates.PullbackRewardToRisk});"));
        Assert.Empty(Query(store, $"SELECT ticker FROM setup WHERE family = 'breakout' AND (volume_multiple < {SetupGates.BreakoutVolume} OR range_ratio > {SetupGates.BreakoutRangeCeiling});"));
        Assert.Empty(Query(store, $"SELECT ticker FROM setup WHERE family = 'drift' AND (surprise_percent <= 0 OR reaction_moves < {SetupGates.DriftReactionMoves});"));
        Assert.Empty(Query(store, "SELECT ticker FROM setup WHERE end <> 'open' OR settled <> 0 OR result IS NOT NULL;"));
    }
}
