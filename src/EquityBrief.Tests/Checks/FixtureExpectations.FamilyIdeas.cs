using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The ideas' run on the frozen families: the touched stop under a trail worked by hand, each family's rule as frozen
// read off its sweep's grid, the ideas each family is read with and what luck passes of them, each new exit and its
// benchmark worked by hand over three members, each idea read over the rule's own listings at its switch, its count a
// night and its stops, and the report stating the tries, the passes and an idea that changes nothing.
// see: The frozen families are read with the pullback's ideas one at a time, and nothing they show is frozen or registered
public partial class FixtureExpectations
{
    [Fact]
    public void TheTouchedStopUnderATrailIsThePlansStopUntilTheClosesRaiseItAndIsReadAgainstTheOpenAndTheLow()
    {
        // Bought at 100 with the stop at 96, a risk of 4, and a trail of 4. The close at 104 raises the stop to 100,
        // and the next session's low at 99.5 reaches it: sold at the stop, 0, on the second session.
        Assert.Equal(0, SweepWalk.TouchedTrailing([100, 101, 102], [99, 99, 99.5], [100, 104, 103], 0, 100, 96, 4, 10, out var sessions)!.Value, 12);
        Assert.Equal(2, sessions);

        // The same session opening at 98, under the raised stop, sells at its open, -0.5.
        Assert.Equal(-0.5, SweepWalk.TouchedTrailing([100, 101, 98], [99, 99, 97], [100, 104, 103], 0, 100, 96, 4, 10, out sessions)!.Value, 12);
        Assert.Equal(2, sessions);

        // A session is read against the stop the closes before it set: the first session's low at 97 is above the
        // plan's 96, though its own close at 110 raises the stop to 106. The close at 109 leaves the stop at 106,
        // never lowered, and the third session's low at 105.9 sells at 106, 1.5.
        Assert.Equal(1.5, SweepWalk.TouchedTrailing([100, 100, 108, 106.5], [99, 97, 107, 105.9], [100, 110, 109, 107], 0, 100, 96, 4, 10, out sessions)!.Value, 12);
        Assert.Equal(3, sessions);

        // The cap's close ends a trade the stop never reached, a session holding no open is read from its low
        // alone, and a history that ends first says nothing.
        Assert.Equal(0.5, SweepWalk.TouchedTrailing([100, 101, 102], [99, 100, 101], [100, 101, 102], 0, 100, 96, 4, 2, out sessions)!.Value, 12);
        Assert.Equal(2, sessions);
        Assert.Equal(-1, SweepWalk.TouchedTrailing([100, 0], [99, 95], [100, 97], 0, 100, 96, 4, 10, out _)!.Value, 12);
        Assert.Null(SweepWalk.TouchedTrailing([100, 101], [99, 100], [100, 101], 0, 100, 96, 4, 5, out sessions));
        Assert.Equal(5, sessions);
    }

    [Fact]
    public void EachFamilysRuleAsFrozenIsItsLiveSettingOnItsSweepsGridAndASettingOffTheGridIsRefused()
    {
        // The breakout froze at a high of 126 sessions, a volume 1.5 times its average, a range ceiling of 0.85
        // and a stop 1.5 moves beneath; the drift at a window of 3 sessions, a reaction of 0.5 moves, a volume 2
        // times its average and a target 2.5 times the risk. Each is a level of its sweep's grid.
        var breakout = FamilyIdeas.Frozen(BreakoutRule.Name, BreakoutSweep.Grid);
        var drift = FamilyIdeas.Frozen(DriftRule.Name, DriftSweep.Grid);

        Assert.Equal(new[] { 0, 1, 0, 0 }, breakout);
        Assert.Equal("high=126|volume=1.5|ceiling=0.85|stop=1.5", BreakoutSweep.Grid.Key(breakout));
        Assert.Equal(new[] { 0, 0, 2, 1 }, drift);
        Assert.Equal("window=3|reaction=0.5|volume=2|target=2.5", DriftSweep.Grid.Key(drift));

        // A value no level holds, a setting short of a dial and a family the run does not read are refused.
        Assert.Throws<ArgumentException>(() => FamilyIdeas.Levels(BreakoutSweep.Grid, 127, 1.5, 0.85, 1.5));
        Assert.Throws<ArgumentException>(() => FamilyIdeas.Levels(BreakoutSweep.Grid, 126, 1.5, 0.85));
        Assert.Throws<ArgumentException>(() => FamilyIdeas.Frozen(LeaderRule.Name, LeaderSweep.Grid));
    }

    [Fact]
    public void EachFamilyIsReadWithTheIdeasThatFitItAndLuckIsCountedOverThem()
    {
        // The six switches, the touched stop, the best three, the stop at least a move beneath and the two holds on
        // both; the two trails on the drift alone, whose plan has a target where the breakout's already trails.
        string[] both = ["a1", "a2", "a3", "a4", "a5", "a6", "b", "c", "e", "h10", "h20"];

        Assert.Equal(both, FamilyIdeas.For(BreakoutRule.Name).Select(idea => idea.Key));
        Assert.Equal([.. both, "d2", "d3"], FamilyIdeas.For(DriftRule.Name).Select(idea => idea.Key));

        var drift = FamilyIdeas.For(DriftRule.Name).ToDictionary(idea => idea.Key);

        Assert.Equal(6, drift.Values.Count(idea => idea.Market && idea.Switch is not null));
        Assert.Equal(3, drift["c"].PerNight);
        Assert.Equal(5, drift["b"].PerNight);
        Assert.True(drift["e"].StopAtLeastAMove);
        Assert.Equal(
            [IdeaExit.TouchedStop, IdeaExit.HoldTen, IdeaExit.HoldTwenty, IdeaExit.TrailTwo, IdeaExit.TrailThree],
            new[] { "b", "h10", "h20", "d2", "d3" }.Select(key => drift[key].Exit));

        // An idea with no effect passes the yearly half in 34 of the 256 ways eight years can fall, so of 11 tries
        // luck alone passes 374 over 256 and of 13, 442 over 256.
        Assert.Equal(34, SweepIdeas.LuckPatterns());
        Assert.Equal(374.0 / 256, Read(BreakoutRule.Name, 11).Luck, 12);
        Assert.Equal(442.0 / 256, Read(DriftRule.Name, 13).Luck, 12);

        static FamilyIdeasRead Read(string family, int tries)
        {
            var figures = SweepIdeas.Figures("none", [], 10);

            return new FamilyIdeasRead(family, "frozen", figures, [.. FamilyIdeas.For(family).Take(tries).Select(idea => new FamilyIdeaReading(idea, figures, SweepIdeas.Test(figures, figures, idea.Market)))]);
        }
    }

    [Fact]
    public void EachNewExitIsWalkedOverTheStocksOwnBarsAndItsBenchmarkIsTheSamePlanOnEveryMemberThatNight()
    {
        // Bought at 100 with the stop at 96 and the target at 140, a risk of 4. Each session closes 2 higher and its
        // low a point under its close: the touched stop and the holds never reach the stop, the hold of 10 sells at
        // the tenth close, 120, 5, and the hold of 20 reaches the target on the twentieth, 140, 10.
        double[] closes = [.. Enumerable.Range(0, 70).Select(at => 100.0 + (2 * at))];
        double[] lows = [.. closes.Select(close => close - 1)];
        var plan = new FamilyListing(0, 0, 0, 1, 0, 100, 96, 140, double.NaN, DriftRule.CapSessions, 2);

        Assert.Equal(10, FamilyIdeas.ExitOf(IdeaExit.TouchedStop, plan, closes, lows, closes, 2, out var sessions)!.Value, 12);
        Assert.Equal(20, sessions);
        Assert.Equal(5, FamilyIdeas.ExitOf(IdeaExit.HoldTen, plan, closes, lows, closes, 2, out sessions)!.Value, 12);
        Assert.Equal(10, sessions);
        Assert.Equal(10, FamilyIdeas.ExitOf(IdeaExit.HoldTwenty, plan, closes, lows, closes, 2, out sessions)!.Value, 12);
        Assert.Equal(20, sessions);

        // The trails take the target's place: never closing under the highest close less 2 or 3 moves, the trade
        // runs to the drift's cap of 60 sessions, 220, 30.
        Assert.Equal(30, FamilyIdeas.ExitOf(IdeaExit.TrailTwo, plan, closes, lows, closes, 2, out sessions)!.Value, 12);
        Assert.Equal(DriftRule.CapSessions, sessions);

        // A breakout's plan trails at its stop's distance: the touched stop under the trail runs it to its cap of
        // 63 sessions, 226, 31.5, and the hold of 10 cuts it at 120, 5.
        var trailing = plan with { Target = double.NaN, Trail = 4, Cap = BreakoutRule.CapSessions };

        Assert.Equal(31.5, FamilyIdeas.ExitOf(IdeaExit.TouchedStop, trailing, closes, lows, closes, 2, out sessions)!.Value, 12);
        Assert.Equal(BreakoutRule.CapSessions, sessions);
        Assert.Equal(5, FamilyIdeas.ExitOf(IdeaExit.HoldTen, trailing, closes, lows, closes, 2, out _)!.Value, 12);
        Assert.Throws<ArgumentException>(() => FamilyIdeas.ExitOf(IdeaExit.Base, plan, closes, lows, closes, 2, out _));

        // Three members over 100 weekdays, every bar a point either side of its close and opening at it, so each
        // typical move is 2. After session 20, X rises 2 a session and Y opens and closes 5 under; Z joined on
        // session 25, so it holds no bar that night and is not read.
        var calendar = Weekdays(new DateOnly(2026, 1, 5), 100);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        decimal[] x = [.. Enumerable.Range(0, 100).Select(day => day <= 20 ? 100m : 100m + (2m * (day - 20)))];
        decimal[] y = [.. Enumerable.Range(0, 100).Select(day => day <= 20 ? 50m : 45m)];
        var series = new[]
        {
            SweepColumns.Series(Constructed("X", calendar, x, 1000), sessionAt),
            SweepColumns.Series(Constructed("Y", calendar, y, 1000), sessionAt),
            SweepColumns.Series(Constructed("Z", [.. calendar.Skip(25)], [.. Enumerable.Repeat(100m, 75)], 1000), sessionAt),
        };
        var members = SweepBenchmark.On(series, calendar.Length);
        var adapter = new FamilySweepRunner.Adapter(DriftSweep.Grid, 0, _ => [], _ => (null, 0), _ => double.NaN);
        var replay = new FamilyIdeaReplay(adapter, [0, 0, 0, 0], series, members, [], _ => 7, 75);
        var listing = plan with { Bar = 20, Session = 20 };

        Assert.Equal(2, series[0].Atr[20], 9);

        // The listing's stop 2 moves beneath, a risk of 4, and its target 10 times the risk. Touched, X reaches its
        // target of 140 on its twentieth session, 10, and Y opens at 45 under its stop of 46, -1.25.
        Assert.Equal((10 - 1.25) / 2, replay.Benchmark(listing, IdeaExit.TouchedStop), 12);

        // Held at most 10, X ends at 120, 5; held at most 20, at its target, 10; Y as before.
        Assert.Equal((5 - 1.25) / 2, replay.Benchmark(listing, IdeaExit.HoldTen), 12);
        Assert.Equal((10 - 1.25) / 2, replay.Benchmark(listing, IdeaExit.HoldTwenty), 12);

        // Trailing at 2 moves, X runs to the cap of 60 sessions, 220, 30; Y closes under its stop at once.
        Assert.Equal((30 - 1.25) / 2, replay.Benchmark(listing, IdeaExit.TrailTwo), 12);

        // A breakout's listing trails its stop's distance on every member: X runs to the cap of 63, 226, 31.5.
        Assert.Equal((31.5 - 1.25) / 2, replay.Benchmark(trailing with { Bar = 20, Session = 20 }, IdeaExit.TouchedStop), 12);
    }

    [Fact]
    public void EachIdeaIsReadOverTheRulesOwnListingsAtItsSwitchItsCountANightItsStopsAndItsExit()
    {
        // Six members held at 100 over 40 weekdays. The rule as frozen lists five on session 20 in its order, the
        // second with its stop a point beneath, under its typical move of 2, and the sixth on session 21. Its own
        // exit sells each the session after at 1 risk against a benchmark of 0.25.
        var calendar = Weekdays(new DateOnly(2026, 1, 5), 40);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var series = Enumerable.Range(0, 6)
            .Select(name => SweepColumns.Series(Constructed(((char)('A' + name)).ToString(), calendar, [.. Enumerable.Repeat(100m, 40)], 1000), sessionAt))
            .ToArray();
        var members = SweepBenchmark.On(series, calendar.Length);

        static FamilyListing Listing(int name, int session, double order, double stop) =>
            new(name, session, session, order, 0, 100, stop, 108, double.NaN, DriftRule.CapSessions, 2);

        FamilyListing[] listings =
        [
            Listing(0, 20, 5, 96), Listing(1, 20, 4, 99), Listing(2, 20, 3, 96), Listing(3, 20, 2, 96), Listing(4, 20, 1, 96),
            Listing(5, 21, 1, 96),
        ];
        var adapter = new FamilySweepRunner.Adapter(DriftSweep.Grid, listings.Length, _ => listings, _ => (1.0, 1), _ => 0.25);
        var switches = Enumerable.Range(0, 6).Select(_ => Enumerable.Repeat(true, calendar.Length).ToArray()).ToArray();

        // The VIX's switch failing on session 21 keeps the sixth off.
        switches[(int)MarketSwitch.VixUnderTwenty][21] = false;

        var replay = new FamilyIdeaReplay(adapter, [0, 0, 0, 0], series, members, switches, _ => 7, 40);
        var ideas = FamilyIdeas.For(DriftRule.Name).ToDictionary(idea => idea.Key);
        var asFrozen = replay.Evaluate("as frozen", null);

        Assert.Equal((6, 6, 2), (asFrozen.Listed, asFrozen.Trades, asFrozen.Nights));
        Assert.Equal((1.0, 0.75), (asFrozen.Result!.Value, asFrozen.Edge!.Value));
        Assert.Equal(1.0 / 6, asFrozen.CloseStops!.Value, 12);

        // The best three keep the first three of the five, the near stop among them.
        Assert.Equal(4, replay.Evaluate("c", ideas["c"]).Listed);

        // A stop at least a move beneath leaves the second off, and the switch the sixth.
        var e = replay.Evaluate("e", ideas["e"]);

        Assert.Equal((5, 0.0), (e.Listed, e.CloseStops!.Value));
        Assert.Equal(5, replay.Evaluate("a5", ideas["a5"]).Listed);
        Assert.Equal(6, replay.Evaluate("a1", ideas["a1"]).Listed);

        // A new exit replaces the rule's own over the stock's own bars: held at most 10 sessions at 100, each trade
        // comes to 0, and so does the same plan on every member that night.
        var held = replay.Evaluate("h10", ideas["h10"]);

        Assert.Equal((6, 0.0, 0.0), (held.Trades, held.Result!.Value, held.Edge!.Value));
    }

    [Fact]
    public void TheReportStatesTheTriesThePassesWhatLuckPassesAndAnIdeaThatChangesNothing()
    {
        static IdeaTrade Trade(double result) => new(new IdeaListing(0, 0, 0, 1, 7, 0, 0, 0, 2, null!), result, 0);

        var asFrozen = SweepIdeas.Figures("as frozen", [Trade(1), Trade(-1)], 10);
        var better = SweepIdeas.Figures("b", [Trade(2), Trade(-1)], 10);
        var ideas = FamilyIdeas.For(BreakoutRule.Name).ToDictionary(idea => idea.Key);
        var read = new FamilyIdeasRead(
            BreakoutRule.Name,
            "high=126|volume=1.5|ceiling=0.85|stop=1.5",
            asFrozen,
            [
                new FamilyIdeaReading(ideas["b"], better, new IdeaTest(false, 6, 2, true, true, true, true, true)),
                new FamilyIdeaReading(ideas["e"], SweepIdeas.Figures("e", [Trade(1), Trade(-1)], 10), new IdeaTest(false, 0, 0, true, false, false, true, true)),
            ]);
        var run = new FamilyIdeasRun(BreakoutRule.Name, "breakouts'", new DateOnly(2019, 1, 2), new DateOnly(2026, 10, 1), 3, 10, 2, [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var report = FamilyIdeasReport.Build(run, read);

        // Two tried and one passed, where luck alone passes 68 over 256 of two.
        var tries = Regex.Match(report, "data-tries=\"(?<tries>\\d+)\" data-passes=\"(?<passes>\\d+)\" data-luck=\"(?<luck>[^\"]+)\">(?<words>[^<]+)<");

        Assert.True(tries.Success, "The report states no tries.");
        Assert.Equal(("2", "1", "0.27"), (tries.Groups["tries"].Value, tries.Groups["passes"].Value, tries.Groups["luck"].Value));
        Assert.StartsWith(
            "2 ideas were tried on the breakout as frozen and 1 passed, where luck alone passes about 0.3 of 2. Passed: b.",
            tries.Groups["words"].Value,
            StringComparison.Ordinal);

        // Each idea's own answer, and the idea whose every figure is the rule's own says it changes nothing.
        Assert.Contains("data-idea=\"b\" data-passes=\"yes\"", report, StringComparison.Ordinal);
        Assert.Contains("data-idea=\"e\" data-passes=\"no\"", report, StringComparison.Ordinal);
        Assert.False(read.Unchanged(read.Ideas[0]));
        Assert.True(read.Unchanged(read.Ideas[1]));
        Assert.Single(Regex.Matches(report, "class=\"unchanged\""));
        Assert.Matches("data-idea=\"e\"[\\s\\S]*class=\"unchanged\"", report);
    }
}
