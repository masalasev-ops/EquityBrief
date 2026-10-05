using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The earnings drift's sweep: its readings the night's own rule's over the same member, its target the nearer
// of a band and the setting's multiple of the risk, the night's print on the fixture's night, a print the
// pulled surprises hold and the calendar does not read from the pull, and a constructed history whose report
// states a known answer.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
public partial class FixtureExpectations
{
    // The claims the earnings drift's sweep makes, which this check reaches: section 17's row for its grid.
    internal static readonly string[] DriftSweepClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Drift sweep grid"),
    ];

    // Three members over the weekdays from 2018-01-01, each bar a point either side of its close, rising a
    // hundredth a session on 1,000 shares, every typical move 2. A reports before the open on 2019-02-01 and
    // closes 4 above the session before on 3,000 shares, a surprise of 10%; it closes 3 higher on the next
    // session and then 2 under the reaction's close, under the reaction session's low, from then on.
    static (DateOnly[] Calendar, SweepSeries[] Series, int Reaction) DriftHistory()
    {
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var reaction = Array.IndexOf(calendar, new DateOnly(2019, 2, 1));
        var a = new decimal[calendar.Length];
        var volumes = new long[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < reaction ? 100m + (0.01m * day)
                : day == reaction ? a[day - 1] + 4
                : day == reaction + 1 ? a[day - 1] + 3
                : a[reaction] - 2;
            volumes[day] = day == reaction ? 3000 : 1000;
        }

        var drifting = Enumerable.Range(0, calendar.Length).Select(day => 100m + (0.01m * day)).ToArray();
        var reporting = Constructed("A", calendar, a, volumes) with { SurprisesFiled = [new SweepSurprise(calendar[reaction], false, 10)] };

        return (
            calendar,
            [
                SweepColumns.Series(reporting, sessionAt),
                SweepColumns.Series(Constructed("B", calendar, drifting, 1000), sessionAt),
                SweepColumns.Series(Constructed("C", calendar, drifting, 1000), sessionAt),
            ],
            reaction);
    }

    [Fact]
    public void TheDriftSweepsReadingsAreTheNightsOwnRulesOverTheSameMember()
    {
        // On the reaction's session and two after it, the sweep's reading and the night's own rule fed the same
        // bars, prints, typical moves, average volume and bands: the rise in typical moves, the volume multiple,
        // the reaction's low, the stop and the provisional setting's target are the same figures.
        var (calendar, series, reaction) = DriftHistory();
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var firstScored = Array.FindIndex(calendar, day => day >= SweepColumns.FirstScored);
        var readings = new DriftSweep(series, members).Readings(sessions, firstScored);
        var provisional = DriftSweep.Grid.Settings.Single(setting => DriftSweep.Grid.Changes(setting) == 0);
        var one = series[0];

        Assert.Equal([reaction, reaction + 1], readings.Select(reading => reading.Bar));

        foreach (var bar in new[] { reaction, reaction + 1 })
        {
            var reading = readings.Single(held => held.Bar == bar);
            var listing = DriftSweep.Listings(readings, provisional).Single(held => held.Bar == bar);
            var result = DriftRule.Evaluate(new DriftInputs(
                "A",
                OpenMarket,
                [.. one.Bars.Take(bar + 1).Select(held => new FamilyBar(held.Session, held.High, held.Low, held.Close, held.Volume))],
                new DriftPrint(calendar[reaction], calendar[reaction], true, 10),
                one.Atr[reaction - 1],
                one.Volume50[reaction],
                one.Atr[bar],
                [.. SweepCandidates.LevelsOn(one, bar).Select(level => level.LowEdge)],
                []));
            var gates = result.Gates.ToDictionary(gate => gate.Name, gate => gate.Values);

            Assert.True(result.Passed, $"The night's rule does not pass A on {Day(calendar[bar])}.");
            Assert.Equal(double.Parse(gates[DriftRule.Reaction]["moves"], CultureInfo.InvariantCulture), reading.Reaction, 9);
            Assert.Equal(double.Parse(gates[DriftRule.Volume]["multiple"], CultureInfo.InvariantCulture), reading.Volume, 9);
            Assert.Equal(double.Parse(gates[DriftRule.Held]["low"], CultureInfo.InvariantCulture), reading.Stop, 9);
            Assert.Equal(double.Parse(gates[FamilyRule.Trade]["stop"], CultureInfo.InvariantCulture), listing.Stop, 9);
            Assert.Equal(double.Parse(gates[FamilyRule.Trade]["target"], CultureInfo.InvariantCulture), listing.Target, 6);
        }
    }

    [Fact]
    public void TheDriftsTargetIsTheNearerOfABandAndTheSettingsMultipleOfTheRisk()
    {
        // Bought at 100 with the stop at the reaction's low of 98, a risk of 2, the provisional setting's multiple
        // of 2.5 aims 5 above, at 105. A band at 104 is nearer and is the target; one at 107 is farther, so the
        // multiple's 105 is; and with no band far enough above the close, 105.
        var provisional = DriftSweep.Grid.Settings.Single(setting => DriftSweep.Grid.Changes(setting) == 0);

        static DriftReading Reading(int name, double band) => new(name, 0, 0, 0, 10, 2, 3, 100, 98, band, 2);

        Assert.Equal(
            [104.0, 105.0, 105.0],
            DriftSweep.Listings([Reading(0, 104), Reading(1, 107), Reading(2, double.NaN)], provisional).Select(listing => listing.Target));
    }

    [Fact]
    public async Task TheDriftSweepReadsTheFixturesNightsPrintAsTheNightStoredIt()
    {
        // On the fixture's night, every member the drift's rule stored a print for: the reaction session the
        // night read and how many sessions before the night it sits are the sweep's own newest print's.
        using var store = await WithTwoNights();

        await new Worker.Families.FamilyEvaluator(Core.Time.FixedClock.At(FixtureEvening, Core.Time.SessionZones.UnitedStates), store.DatabaseFile).RunAsync("two-nights-fixture-family-rules");

        var inputs = await new SweepHistory(store.DatabaseFile).ReadAsync(FixtureNight);
        var day = FixtureNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var stored = SweepRows(store, $"SELECT ticker FROM family_result WHERE session_date = '{day}' AND family = '{DriftRule.Name}' ORDER BY ticker;")
            .ToDictionary(ticker => ticker, ticker => SweepRows(store, $"SELECT gates FROM family_result WHERE session_date = '{day}' AND family = '{DriftRule.Name}' AND ticker = '{ticker}';").Single());

        SweepRelease(store);

        var sessionAt = inputs.Sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var compared = 0;

        foreach (var (ticker, gatesJson) in stored)
        {
            var print = FamilyRule.GatesOf(gatesJson).Single(gate => gate.Name == DriftRule.Print).Values;

            if (!print.TryGetValue("back", out var back))
            {
                continue;
            }

            var one = SweepColumns.Series(inputs.Names.Single(name => name.Ticker == ticker), sessionAt);
            var bar = Array.IndexOf(one.SessionAt, sessionAt[FixtureNight]);
            var newest = one.NewestSurprise[bar];

            Assert.True(newest >= 0, $"{ticker}: the night read a print the sweep holds no surprise for.");
            Assert.Equal(print["session"], one.Bars[one.SurpriseBar[newest]].Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Assert.Equal(int.Parse(back, CultureInfo.InvariantCulture), bar - one.SurpriseBar[newest]);
            compared++;
        }

        Assert.True(compared >= 1, "The fixture's night stored no print the drift's rule read.");
    }

    [Fact]
    public async Task APrintThePulledSurprisesHoldAndTheCalendarDoesNotIsReadFromThePull()
    {
        // A member with ten sessions of bars, a pulled surprise on the fourth that the calendar does not hold,
        // and a calendar print on the eighth with a surprise of its own: the pull is read, and the calendar is
        // not, so the newest surprise on every session from the fourth is the pulled one.
        using var store = new TemporaryStore().Migrated();
        var days = Weekdays(new DateOnly(2026, 3, 2), 10);

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'AAA', NULL, NULL, '2026-03-13T21:10:00Z');");

        foreach (var day in days)
        {
            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{Day(day)}', '100', '101', '99', '100', 1000, 'bulk', '2026-03-13T21:10:00Z', '100');");
        }

        store.Execute($"INSERT INTO pulled_surprise (ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, pull) VALUES ('AAA', '{Day(days[3])}', 'before', '1.10', '1.00', 10, 'history-pull-surprises');");
        store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('AAA', '{Day(days[7])}', 'earnings', 'before', '{{\"surprise\":\"5\",\"estimate\":\"1\",\"actual\":\"1.05\"}}', '2026-03-13T21:10:00Z');");

        var inputs = await new SweepHistory(store.DatabaseFile).ReadAsync(days[^1]);

        SweepRelease(store);

        var name = inputs.Names.Single();
        var sessionAt = inputs.Sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var one = SweepColumns.Series(name, sessionAt);

        Assert.Equal(SweepHistory.PulledSurprises, inputs.SurpriseSource);
        Assert.Equal([(days[3], 10.0)], name.Surprises.Select(surprise => (surprise.EventDate, surprise.Percent)));
        Assert.Equal([-1, -1, -1, 0, 0, 0, 0, 0, 0, 0], one.NewestSurprise);
        Assert.Equal(3, one.SurpriseBar[0]);
    }

    [Fact]
    public void TheDriftSweepStatesAConstructedHistorysKnownAnswerInItsReport()
    {
        // A is bought on the reaction's close, stopped at its low a point beneath and aimed 2.5 points above, and
        // the next close, 3 above, sells it at 3 risks; the next session is under the low, so it is never listed
        // again. Its typical move that night takes in the 5 its range reached from the close before, 31 over 14,
        // so the plan's stop is 14 over 31 moves: B and C, bought the same night on that plan at a typical move of
        // 2, a risk of 28 over 31, rise a hundredth a session to their cap of 60, 0.6 over 28 over 31 each.
        var (calendar, series, reaction) = DriftHistory();
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var firstScored = Array.FindIndex(calendar, day => day >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var adapter = FamilySweepRunner.For(DriftRule.Name, series, sessions, members, firstScored);
        var tickers = new[] { "A", "B", "C" };
        var read = adapter.Grid.Settings
            .Select(setting => (setting, FamilySweep.Figures(adapter.Grid.Key(setting), FamilySweep.Walk(adapter.Listings(setting), tickers, session => calendar[session].Year - SweepColumns.FirstScored.Year, adapter.Exit, adapter.Benchmark), nights)))
            .ToList();
        var provisional = read.Single(one => adapter.Grid.Changes(one.setting) == 0).Item2;
        var benchmark = (3 + (2 * (0.6 / (28.0 / 31)))) / 3;

        Assert.Equal((1, 1), (provisional.Listed, provisional.Trades));
        Assert.Equal(3 - benchmark, provisional.Edge!.Value, 9);
        Assert.Equal(1, provisional.CloseStops!.Value, 12);

        var proposal = FamilySweep.Propose(adapter.Grid, read);
        var run = new FamilySweepRun(DriftRule.Name, "earnings drift's", calendar[firstScored], calendar[^1], 3, nights, nights, adapter.Readings, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var report = FamilySweepReport.Build(run, adapter.Grid, read, proposal);
        var row = System.Text.RegularExpressions.Regex.Match(report, $"data-key=\"{System.Text.RegularExpressions.Regex.Escape(provisional.Key)}\" data-trades=\"(?<trades>\\d+)\" data-edge=\"(?<edge>[^\"]+)\"><td>The provisional setting</td>");

        Assert.True(proposal.NonePassed);
        Assert.True(row.Success, "The report draws no row for the provisional setting.");
        Assert.Equal("1", row.Groups["trades"].Value);
        Assert.Equal(3 - benchmark, double.Parse(row.Groups["edge"].Value, CultureInfo.InvariantCulture), 3);

        // The provisional setting lists A on the reaction's session and the next; the walk keeps the first, the
        // second falling on the session its trade still held the stock through.
        Assert.Equal([reaction, reaction + 1], adapter.Listings(DriftSweep.Grid.Settings.Single(setting => DriftSweep.Grid.Changes(setting) == 0)).Select(listing => listing.Bar));
    }
}
