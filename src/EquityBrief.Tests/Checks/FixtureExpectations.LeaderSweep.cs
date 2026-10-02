using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The sector leaders' sweep: the sectors ranked and the members placed by the night's own rule, a name with no
// sector left out and counted, the standings read back against the rows the night stored over the fixture,
// and constructed candidates whose report states a known answer.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors
public partial class FixtureExpectations
{
    // The claims the sector leaders' sweep makes, which this check reaches: section 17's row for its grid and
    // section 18's row for a name the membership names no sector for.
    internal static readonly string[] LeaderSweepClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Leader sweep grid"),
        CheckReach.Key(Scope.FailureTable, "A leader sweep name with no sector"),
    ];

    // Twelve members over the weekdays from 2018-01-01, each rising a fixed amount a session from 100, so each
    // close stands above its 200-day average and each long return is ordered by its rise: six in Tech rising
    // 0.06 down to 0.01, five in Energy rising 0.005 down to 0.001, and N, rising fastest, with no sector.
    static (DateOnly[] Calendar, SweepSeries[] Series, string[] Tickers) LeaderHistory()
    {
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var names = new List<(string Ticker, string? Sector, decimal Rise)>
        {
            ("T1", "Tech", 0.06m), ("T2", "Tech", 0.05m), ("T3", "Tech", 0.04m), ("T4", "Tech", 0.03m), ("T5", "Tech", 0.02m), ("T6", "Tech", 0.01m),
            ("E1", "Energy", 0.005m), ("E2", "Energy", 0.004m), ("E3", "Energy", 0.003m), ("E4", "Energy", 0.002m), ("E5", "Energy", 0.001m),
            ("N", null, 0.1m),
        };

        var series = names
            .Select(name => SweepColumns.Series(
                Constructed(name.Ticker, calendar, [.. Enumerable.Range(0, calendar.Length).Select(day => 100m + (name.Rise * day))], 1000) with { Sector = name.Sector },
                sessionAt))
            .ToArray();

        return (calendar, series, [.. names.Select(name => name.Ticker)]);
    }

    [Fact]
    public void TheLeadersStandingsAreTheNightsOwnAndANameWithNoSectorIsLeftOutAndCounted()
    {
        var (calendar, series, tickers) = LeaderHistory();
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var firstScored = Array.FindIndex(calendar, day => day >= SweepColumns.FirstScored);
        var (leaders, carrying, notCarrying) = LeaderSweep.Standings(series, sessions, members, firstScored);

        // Eleven names carry a sector and one does not, and N, the strongest of all, is in no ranking.
        Assert.Equal((11, 1), (carrying, notCarrying));
        Assert.DoesNotContain(leaders, leader => tickers[leader.Name] == "N");

        // On the first scored session Tech ranks first and Energy second, and the loosest setting, the top four
        // sectors and the top half, takes three of Tech's six and three of Energy's five, each in its place.
        Assert.Equal(
            ["T1 1 6 1", "T2 1 6 2", "T3 1 6 3", "E1 2 5 1", "E2 2 5 2", "E3 2 5 3"],
            leaders
                .Where(leader => leader.Session == firstScored)
                .OrderBy(leader => leader.Rank)
                .ThenBy(leader => leader.Place)
                .Select(leader => FormattableString.Invariant($"{tickers[leader.Name]} {leader.Rank} {leader.Counted} {leader.Place}")));

        // A quarter, a third and a half of a sector, rounded up.
        Assert.Equal((2, 2, 3), (LeaderSweep.Cut(6, 4), LeaderSweep.Cut(5, 4), LeaderSweep.Cut(5, 2)));
    }

    [Fact]
    public async Task TheLeadersSweepReadsTheFixturesNightAsTheNightStoredIt()
    {
        // On the fixture's night, every member the leader's rule stored a row for: the long return, its place in
        // its sector and how many of the sector hold a return are the sweep's own standings, read through the
        // sweep's path from the bars and the membership the store holds.
        using var store = await WithTwoNights();

        await new Worker.Families.FamilyEvaluator(Core.Time.FixedClock.At(FixtureEvening, Core.Time.SessionZones.UnitedStates), store.DatabaseFile).RunAsync("two-nights-fixture-family-rules");

        var inputs = await new SweepHistory(store.DatabaseFile).ReadAsync(FixtureNight);
        var day = FixtureNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var stored = SweepRows(store, $"SELECT ticker FROM family_result WHERE session_date = '{day}' AND family = '{LeaderRule.Name}' ORDER BY ticker;")
            .ToDictionary(ticker => ticker, ticker => SweepRows(store, $"SELECT gates FROM family_result WHERE session_date = '{day}' AND family = '{LeaderRule.Name}' AND ticker = '{ticker}';").Single());

        SweepRelease(store);

        var sessionAt = inputs.Sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var members = SweepBenchmark.On(series, inputs.Sessions.Length);
        var (rows, places) = LeaderSweep.StandingsOn(series, members, sessionAt[FixtureNight]);
        var compared = 0;

        Assert.True(stored.Count >= 4, $"The fixture's night stored {stored.Count} leader row(s), expected a row for each of its four members.");

        foreach (var (ticker, gatesJson) in stored)
        {
            var leader = FamilyRule.GatesOf(gatesJson).Single(gate => gate.Name == LeaderRule.Leader).Values;

            if (!leader.TryGetValue("return", out var held))
            {
                Assert.True(!places.TryGetValue(ticker, out var none) || none.Place is null, $"{ticker}: the night read no return and the sweep placed it.");
                continue;
            }

            var row = rows.Single(one => one.Ticker == ticker);

            Assert.Equal(double.Parse(held, CultureInfo.InvariantCulture), row.Return!.Value, 9);
            Assert.Equal(leader["place"], places[ticker].Place!.Value.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(leader["of"], places[ticker].Sector!.Counted.ToString(CultureInfo.InvariantCulture));
            compared++;
        }

        Assert.True(compared >= 1, "The fixture's night stored no return the leader's rule read.");
    }

    [Fact]
    public void TheLeadersSweepStatesConstructedCandidatesKnownAnswerInItsReport()
    {
        // Three candidates on the tenth scored session, each passing the pullback's setup, trigger and trade at the
        // live settings, none of them in an uptrend, since leadership stands in the trend gate's place: T1, first
        // in Tech, won 2 risks against a benchmark of 0.5; T3, third in Tech, lost 1 against -0.2 on a stop 0.8
        // of a move below; E1, first in Energy, won 1 against nothing. The provisional setting, the top three
        // sectors and the top quarter, lists T1 and E1, Tech first: an edge of 1.5 and 1 over 2, 1.25. The top
        // half takes T3 as well: 1.5, 1 and -0.8 over 3.
        var (calendar, series, tickers) = LeaderHistory();
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var firstScored = Array.FindIndex(calendar, day => day >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var session = firstScored + 10;
        var (leaders, _, _) = LeaderSweep.Standings(series, sessions, members, firstScored);

        SweepCandidate Candidate(string ticker, byte code, float multiple, short ends, float benchmark, double stopMoves)
        {
            var name = Array.IndexOf(tickers, ticker);
            var plan = new SweepPlanOutcomes { RewardToRisk = 2.0, StopMoves = stopMoves };
            var exit = SweepDesign.Live.ExitIndex;

            plan.Code[exit] = code;
            plan.Multiple[exit] = multiple;
            plan.Ends[exit] = ends;
            plan.Benchmark[exit] = benchmark;

            var candidate = new SweepCandidate { Name = name, Session = session, Year = 0, Block = 0, Breadth = 0.8, Uptrend = 0 };
            var high = SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh);

            candidate.Depth[high] = 2.0;
            candidate.DryUp[high] = 1.0;
            candidate.Band[(int)SweepDesign.Live.Support] = 1;
            candidate.Age[SweepCandidate.AgeAt(SweepDesign.Live.Trigger, SweepDesign.Live.Support)] = 0;
            candidate.Plans[SweepCandidate.PlanAt(SweepDesign.Live.Plan, SweepDesign.Live.Support)] = plan;

            return candidate;
        }

        var readings = LeaderSweep.Readings(
            leaders,
            [
                Candidate("T1", SweepPlanOutcomes.Win, 2.0f, 5, 0.5f, 2.0),
                Candidate("T3", SweepPlanOutcomes.Loss, -1.0f, 3, -0.2f, 0.8),
                Candidate("E1", SweepPlanOutcomes.Win, 1.0f, 4, 0.0f, 2.0),
            ],
            series);
        var byListing = readings.ToDictionary(reading => (reading.Name, reading.Session));
        var read = LeaderSweep.Grid.Settings
            .Select(setting => (setting, FamilySweep.Figures(
                LeaderSweep.Grid.Key(setting),
                FamilySweep.Walk(LeaderSweep.Listings(readings, setting), tickers, day => calendar[day].Year - SweepColumns.FirstScored.Year, listing => LeaderSweep.ExitOf(byListing[(listing.Name, listing.Session)]), listing => byListing[(listing.Name, listing.Session)].Benchmark),
                nights)))
            .ToList();
        var provisional = read.Single(one => LeaderSweep.Grid.Changes(one.setting) == 0).Item2;
        var half = read.Single(one => one.Item2.Key == "sectors=3|share=2").Item2;

        Assert.Equal(3, readings.Count);
        Assert.Equal((2, 2), (provisional.Listed, provisional.Trades));
        Assert.Equal(1.25, provisional.Edge!.Value, 6);
        Assert.Equal(0, provisional.CloseStops!.Value, 12);
        Assert.Equal(3, half.Trades);
        Assert.Equal((1.5 + 1 - 0.8) / 3, half.Edge!.Value, 6);
        Assert.Equal(1.0 / 3, half.CloseStops!.Value, 12);

        var run = new FamilySweepRun(LeaderRule.Name, "sector leaders'", calendar[firstScored], calendar[^1], tickers.Length, nights, nights, readings.Count, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "constructed");
        var report = FamilySweepReport.Build(run, LeaderSweep.Grid, read, FamilySweep.Propose(LeaderSweep.Grid, read));
        var row = System.Text.RegularExpressions.Regex.Match(report, $"data-key=\"{System.Text.RegularExpressions.Regex.Escape(provisional.Key)}\" data-trades=\"(?<trades>\\d+)\" data-edge=\"(?<edge>[^\"]+)\"><td>The provisional setting</td>");

        Assert.True(row.Success, "The report draws no row for the provisional setting.");
        Assert.Equal("2", row.Groups["trades"].Value);
        Assert.Equal(1.25, double.Parse(row.Groups["edge"].Value, CultureInfo.InvariantCulture), 3);
        Assert.Contains("<p class=\"note\">constructed</p>", report, StringComparison.Ordinal);
    }
}
