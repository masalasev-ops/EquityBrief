using System.Globalization;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Moves;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Api.Reading;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// gap-refusal. A name whose series arrives with an interior session missing is
// not stored, its stored series is left as it was, and the gap's date is named.
//
// Detection is against the calendar and never against the rows. A run of stored
// dates is self-consistent whatever is missing from it, so a series cannot be
// asked whether it is complete: four days of a five-day week look exactly like
// four days of a four-day week.
// see: A gap is a session the exchange traded and the store does not hold
// see: Bars are never interpolated
//
// The gap fixture is constructed rather than captured, and it has to be: a
// clean provider series will not produce the failure. It is the same capture
// as bars-KEYS.json with one interior session removed, so every row it keeps is
// still the provider's.
public class GapRefusal
{
    internal static CheckReach Reach => new(
        "gap-refusal",
        ["fixtures/membership-2026-09-05"],
        [CheckReach.Key(Scope.FailureTable, "A gap in one name's series, chart"),
            CheckReach.Key(Scope.FailureTable, "A gap in one name's series, level and plan sections"),
            CheckReach.Key(Scope.FixtureTable, "gap stop")]);

    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    // The session removed from the gap fixture, and its neighbours. Read from
    // the two files rather than written here, so a fixture rebuilt around a
    // different session moves this with it.
    static (DateOnly Removed, int Clean, int WithGap) TheHole()
    {
        var folder = FixtureFolder();

        var clean = Sessions(Path.Combine(folder, "bars-KEYS.json"));
        var holed = Sessions(Path.Combine(folder, "gap-KEYS.json"));

        return (clean.Except(holed).Single(), clean.Count, holed.Count);
    }

    static IReadOnlyList<DateOnly> Sessions(string file) =>
    [
        .. RecordedHistoricalBarFeed
            .Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file))
            .Select(bar => bar.SessionDate),
    ];

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    // The fixture's feed with one name's series swapped for the holed one, so
    // the refusal is exercised through the same path a night takes.
    static RecordedHistoricalBarFeed FeedWithTheGap()
    {
        var folder = FixtureFolder();

        return new RecordedHistoricalBarFeed(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = File.ReadAllText(Path.Combine(folder, "bars-AAPL.json")),
            ["MSFT"] = File.ReadAllText(Path.Combine(folder, "bars-MSFT.json")),
            ["KEYS"] = File.ReadAllText(Path.Combine(folder, "gap-KEYS.json")),
            ["NFLX"] = File.ReadAllText(Path.Combine(folder, "bars-NFLX.json")),
        });
    }

    static async Task<TemporaryStore> WithMembership()
    {
        var store = new TemporaryStore().Migrated();

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json")),
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).LoadAsync(Index, "run-0");

        return store;
    }

    // The run id is a parameter because two backfills are two runs, and the
    // run log's grain is one row per run per stage. Keyed on one id the second
    // collides on the primary key, which is the same defect 1.4 found in the
    // night's own id.
    static async Task<BackfillOutcome> BackfillAsync(
        TemporaryStore store,
        IHistoricalBarFeed feed,
        string runId = "run-1") =>
        await new Backfill(feed, FixedClock.At(Instant, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, runId);

    [Fact]
    public async Task TheLevelAndPlanSectionsOfARefusedNameSayTheyAreNotComputed()
    {
        // Section 18's gap row, its other element. The chart half is reached
        // here already: a refused series is one the store does not hold, so the
        // chart shows the absence. This is the half that waited for the level
        // and plan sections to exist, the level table since 3.5 and the plan
        // column and its tables since 4.6.
        //
        // A name whose series was refused has no bars, so it has no bands and no
        // plan, and both sections have to say so rather than drawing nothing.
        // An empty picture and a picture saying what it does not have are
        // different things, and only the second is readable.
        using var store = await WithMembership();

        await BackfillAsync(store, FeedWithTheGap());

        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new IndicatorEngine(clock, store.DatabaseFile).RunAsync("gap-indicators");
        await new SwingFinder(clock, store.DatabaseFile).RunAsync("gap-swings");
        await new VolumeProfileBuilder(clock, store.DatabaseFile).RunAsync("gap-profile");
        await new LevelBuilder(clock, store.DatabaseFile).RunAsync("gap-levels");
        await new LadderBuilder(clock, store.DatabaseFile).RunAsync(Index, "gap-ladders");

        var api = new ReadApi(store.DatabaseFile, clock);
        var marks = new MarkRenderer();

        // The refused name holds nothing, which is what makes the two sections
        // absences rather than short answers.
        Assert.Equal(0, Rows(store, "KEYS"));
        Assert.Empty(await api.LevelsAsync("KEYS"));

        // The level summary says what it does not have.
        var summary = marks.LevelSummary("KEYS", []);

        Assert.Contains("degraded", summary, StringComparison.Ordinal);

        // And the plan section says so too, in its own words, off the ladder row
        // the night wrote for it: a member with no stored bars still gets a row,
        // and its plan states the reason.
        var ladder = await api.LadderAsync("KEYS");

        Assert.NotNull(ladder);
        Assert.Contains("no stored bars", ladder!.Plan, StringComparison.Ordinal);

        var column = marks.PlanColumn("KEYS", 0m, NameScreen.PlanRows(ladder));

        Assert.Contains("has no plan to draw", column, StringComparison.Ordinal);

        // The counter-reading, over the same run: a name whose series was not
        // refused has both sections. Without it this would pass over a pipeline
        // that computed nothing for anybody.
        Assert.NotEmpty(await api.LevelsAsync("AAPL"));

        var drawn = marks.PlanColumn("AAPL", 319.97m, NameScreen.PlanRows(await api.LadderAsync("AAPL")));

        Assert.Contains("class=\"plan-column\"", drawn, StringComparison.Ordinal);
    }

    static int Rows(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM bar WHERE ticker = $t;";
        command.Parameters.AddWithValue("$t", ticker);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Fact]
    public void TheGapFixtureIsTheCleanOneWithAnInteriorSessionRemoved()
    {
        // Stated first, because every assertion below rests on it. A fixture
        // whose hole sat at an edge would be a shorter history rather than a
        // gap, and the refusal would correctly not fire.
        var (removed, clean, holed) = TheHole();

        Assert.Equal(clean - 1, holed);
        Assert.True(clean >= 250, $"The clean series holds {clean} sessions, expected at least 250.");

        var sessions = Sessions(Path.Combine(FixtureFolder(), "gap-KEYS.json"));

        Assert.True(removed > sessions.Min(), "The removed session is at or before the first stored one.");
        Assert.True(removed < sessions.Max(), "The removed session is at or after the last stored one.");

        // And the other two names hold it, which is what makes it a session the
        // exchange traded rather than one nobody traded.
        Assert.Contains(removed, Sessions(Path.Combine(FixtureFolder(), "bars-AAPL.json")));
        Assert.Contains(removed, Sessions(Path.Combine(FixtureFolder(), "bars-MSFT.json")));
    }

    [Fact]
    public async Task TheHoledSeriesIsRefusedAndItsDateIsNamed()
    {
        using var store = await WithMembership();

        var outcome = await BackfillAsync(store, FeedWithTheGap());
        var (removed, clean, _) = TheHole();

        var refused = Assert.Single(outcome.Refused);

        Assert.Equal("KEYS", refused.Ticker);
        Assert.Equal(removed, refused.SessionDate);

        // Not stored, and the two clean names are.
        Assert.Equal(0, Rows(store, "KEYS"));
        Assert.True(Rows(store, "AAPL") > 250, "AAPL was not stored, so the refusal took more than the one name.");
        Assert.True(Rows(store, "MSFT") > 250, "MSFT was not stored, so the refusal took more than the one name.");
    }

    [Fact]
    public async Task TheCleanFixtureIsUnaffected()
    {
        // The counter-test. Without it every assertion above would pass just as
        // well over a backfill that refused everything.
        using var store = await WithMembership();

        var outcome = await BackfillAsync(store, RecordedHistoricalBarFeed.FromFolder(FixtureFolder()));

        Assert.Empty(outcome.Refused);
        Assert.Equal(FixtureExpectation.CurrentMembers.Length, outcome.Owed);
        Assert.True(Rows(store, "KEYS") > 250, "KEYS was refused over a clean series.");
    }

    [Fact]
    public async Task ARefusedNameLeavesTheStoredSeriesAsItWas()
    {
        // The second half of the definition. A name that already holds a year
        // and is then offered a holed one keeps what it has: the refusal never
        // deletes, because a gap is a reason to stop rather than to discard.
        using var store = await WithMembership();

        await BackfillAsync(store, RecordedHistoricalBarFeed.FromFolder(FixtureFolder()));

        var before = Rows(store, "KEYS");

        // A second backfill offers the holed series and is owed nothing,
        // because the name already holds its year.
        var again = await BackfillAsync(store, FeedWithTheGap(), "run-2");

        Assert.Equal(0, again.Owed);
        Assert.Equal(before, Rows(store, "KEYS"));
    }

    [Fact]
    public async Task TheGapIsNamedOnTheSurfaceAPersonReads()
    {
        // A claim that something is reported is a claim about a surface. The
        // run log's detail column is where a night's failures are read, so the
        // date is asserted there rather than only on the returned record.
        using var store = await WithMembership();

        await BackfillAsync(store, FeedWithTheGap());

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT detail FROM run_log WHERE stage = $s;";
        command.Parameters.AddWithValue("$s", Backfill.Stage);

        var detail = command.ExecuteScalar() as string;
        var (removed, _, _) = TheHole();

        Assert.NotNull(detail);
        Assert.Contains("KEYS", detail!, StringComparison.Ordinal);
        Assert.Contains(removed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AHoleAtAnEdgeIsAShorterHistoryRatherThanAGap()
    {
        // The distinction the whole rule turns on. A name that joined the index
        // in March has no February bars and that is not a gap; a name whose
        // last session is Friday has no Monday until Monday's night runs.
        IReadOnlyList<DateOnly> calendar =
        [
            new(2026, 3, 2), new(2026, 3, 3), new(2026, 3, 4), new(2026, 3, 5), new(2026, 3, 6),
        ];

        IReadOnlyCollection<DateOnly> late = [new(2026, 3, 4), new(2026, 3, 5), new(2026, 3, 6)];
        IReadOnlyCollection<DateOnly> early = [new(2026, 3, 2), new(2026, 3, 3), new(2026, 3, 4)];
        IReadOnlyCollection<DateOnly> holed = [new(2026, 3, 2), new(2026, 3, 3), new(2026, 3, 6)];

        Assert.Empty(TradingCalendar.MissingFrom(late, calendar));
        Assert.Empty(TradingCalendar.MissingFrom(early, calendar));
        Assert.Equal([new(2026, 3, 4), new(2026, 3, 5)], TradingCalendar.MissingFrom(holed, calendar));
    }

    [Fact]
    public void OneSeriesCannotBeCheckedAgainstItself()
    {
        // The calendar is the union of the series in hand, so over one series it
        // is that series and nothing can be found missing. Reporting a clean
        // answer there would be a check passing by having no population, so the
        // caller is told it cannot detect rather than told there is no gap.
        var one = new Dictionary<string, IReadOnlyCollection<DateOnly>>(StringComparer.Ordinal)
        {
            ["AAPL"] = [new(2026, 3, 2), new(2026, 3, 6)],
        };

        Assert.False(TradingCalendar.CanDetect(one));
        Assert.Empty(TradingCalendar.GapsIn(one));

        var two = new Dictionary<string, IReadOnlyCollection<DateOnly>>(one, StringComparer.Ordinal)
        {
            ["MSFT"] = [new(2026, 3, 2), new(2026, 3, 4), new(2026, 3, 6)],
        };

        Assert.True(TradingCalendar.CanDetect(two));
        Assert.Equal([new Gap("AAPL", new DateOnly(2026, 3, 4))], TradingCalendar.GapsIn(two));
    }

    [Fact]
    public void TheChartShowsTheGapRatherThanDrawingThrough()
    {
        // The half of the failure row's surface that exists. A chart that
        // closed the hole would draw a series that never traded, which is the
        // reason the row gives for the whole rule. The level and plan sections
        // are the other half and are phases 2 and 3.
        var bars = RecordedHistoricalBarFeed
            .Parse(File.ReadAllText(Path.Combine(FixtureFolder(), "gap-KEYS.json")), "KEYS")
            .Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))
            .ToArray();

        var svg = new MarkRenderer().LevelChart("KEYS", bars);
        var (removed, _, holed) = TheHole();

        // One candle per stored session and none for the missing one, so the
        // hole is visible as an absence rather than closed over.
        Assert.Equal(holed, System.Text.RegularExpressions.Regex.Matches(svg, "class=\"candle\"").Count);
        Assert.DoesNotContain(FormattableString.Invariant($"data-session=\"{removed:yyyy-MM-dd}\""), svg, StringComparison.Ordinal);
    }

    // The stop every computed stage makes over a name whose stored series has an
    // interior hole.
    //
    // A decision has said since 1.5 that every computation over such a name stops
    // and reports the gap's date, and no stage did it until 6.0. The committed
    // fixture holds no gapped name, so the case is constructed: a session is removed
    // from one name's stored year and the stages are run over the result. That is
    // the only way to reach it, because a provider series does not arrive with a
    // hole in the middle and the fetcher refuses one that does.
    // see: A gap is a session the exchange traded and the store does not hold

        
    // The name the hole is cut in, and the name that has to be unaffected by
    // it, both read off the expectation rather than written here twice.
    static string GappedName => Expected("gap-stop").GetProperty("gappedName").GetString()!;
    static string ControlName => Expected("gap-stop").GetProperty("controlName").GetString()!;

    
    
    static IClock GapClock() => FixedClock.At(Instant, SessionZones.UnitedStates);

    // Sessions inside the closure table's range, WeekMonday to WeekFriday of one week.
    static readonly DateOnly WeekMonday = new(2026, 3, 2);
    static readonly DateOnly WeekTuesday = new(2026, 3, 3);
    static readonly DateOnly WeekWednesday = new(2026, 3, 4);
    static readonly DateOnly WeekThursday = new(2026, 3, 5);
    static readonly DateOnly WeekFriday = new(2026, 3, 6);

    [Fact]
    public void AnInteriorHoleIsAGapAndTheEarliestIsTheOneNamed()
    {
        var gaps = TradingCalendar.Check([WeekMonday, WeekTuesday, WeekThursday, WeekFriday]);

        Assert.True(gaps.Checked);
        Assert.Equal([WeekWednesday], gaps.Missing);
        Assert.True(gaps.HasGap);
        Assert.Equal(WeekWednesday, gaps.Earliest);
    }

    [Fact]
    public void TwoHolesNameTheEarlier()
    {
        // WeekMonday to WeekFriday of two weeks with the WeekWednesday of each removed, so
        // the answer is the first rather than the last. A stage that named the
        // latest would move its own message as a series lost more sessions.
        var gaps = TradingCalendar.Check(
        [
            WeekMonday, WeekTuesday, WeekThursday, WeekFriday,
            new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 10),
            new DateOnly(2026, 3, 12), new DateOnly(2026, 3, 13),
        ]);

        Assert.Equal(2, gaps.Missing.Count);
        Assert.Equal(WeekWednesday, gaps.Earliest);
    }

    [Fact]
    public void AHoleAtEitherEdgeIsAShorterHistoryAndNotAGap()
    {
        // The same week with the first and the last session absent. A name that
        // joined in March has no February and a name whose last session is
        // WeekFriday has no WeekMonday until WeekMonday's night runs.
        var gaps = TradingCalendar.Check([WeekTuesday, WeekWednesday, WeekThursday]);

        Assert.True(gaps.Checked);
        Assert.Empty(gaps.Missing);
        Assert.False(gaps.HasGap);
    }

    [Fact]
    public void AWeekendIsNotAGap()
    {
        var gaps = TradingCalendar.Check([WeekFriday, new DateOnly(2026, 3, 9)]);

        Assert.True(gaps.Checked);
        Assert.Empty(gaps.Missing);
    }

    [Fact]
    public void AClosureIsNotAGap()
    {
        // Christmas Day 2026 falls on a WeekFriday and the table holds it, so the
        // WeekThursday before and the WeekMonday after are adjacent sessions.
        Assert.Contains(new DateOnly(2026, 12, 25), ExchangeClosures.All);

        var gaps = TradingCalendar.Check([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 28)]);

        Assert.True(gaps.Checked);
        Assert.Empty(gaps.Missing);
    }

    [Fact]
    public void OneSessionIsNotCheckedRatherThanClean()
    {
        // No interior at all, so nothing can be missing from it and nothing was
        // compared. The two ways of holding no gap are different facts and a
        // caller handed an empty list for this one would read it as the other.
        var gaps = TradingCalendar.Check([WeekWednesday]);

        Assert.False(gaps.Checked);
        Assert.False(gaps.HasGap);
    }

    [Fact]
    public void ASeriesOutsideTheClosureTableIsNotCheckedRatherThanClean()
    {
        // The table covers 2025 to 2027 and refuses a weekday outside it rather
        // than guessing, so a series reaching further back is answered as
        // uncheckable. Without the guard the call throws instead, which would
        // stop a night on a name it could say nothing about.
        var gaps = TradingCalendar.Check([new DateOnly(2024, 3, 4), new DateOnly(2024, 3, 6)]);

        Assert.False(gaps.Checked);
        Assert.Empty(gaps.Missing);
    }

    [Fact]
    public void TheReportIsEmptyWhereNothingWasStoppedAndNothingWentUnchecked()
    {
        var stop = new SeriesGapStop();

        Assert.False(stop.Stops(ControlName, [WeekMonday, WeekTuesday, WeekWednesday, WeekThursday, WeekFriday]));
        Assert.Equal(string.Empty, stop.Report());
        Assert.Equal(0, stop.Stopped);
        Assert.Equal(0, stop.NotChecked);
    }

    [Fact]
    public void TheReportNamesEveryStoppedNameWithItsEarliestGap()
    {
        var stop = new SeriesGapStop();

        Assert.True(stop.Stops(GappedName, [WeekMonday, WeekTuesday, WeekThursday, WeekFriday]));
        Assert.False(stop.Stops(ControlName, [WeekWednesday]));

        Assert.Equal(1, stop.Stopped);
        Assert.Equal(1, stop.NotChecked);
        Assert.Contains("1 stopped at a gap (AAPL 2026-03-04)", stop.Report());
        Assert.Contains("1 not checked for gaps", stop.Report());
    }

    // The five computed stages, each over a store whose fixture year has one
    // interior session cut out of one name.

    [Fact]
    public async Task TheIndicatorEngineWritesNoRowForAGappedNameAndNamesTheGap()
    {
        using var store = await WithAHoleAsync();
        var cut = CutSession(store);

        await new IndicatorEngine(GapClock(), store.DatabaseFile).RunAsync("gap-indicators");

        Assert.Equal(0, GapCount(store, "SELECT COUNT(*) FROM indicator WHERE ticker = 'AAPL';"));
        Assert.True(GapCount(store, "SELECT COUNT(*) FROM indicator WHERE ticker = 'MSFT';") > 0);
        Assert.Contains($"stopped at a gap (AAPL {cut})", GapDetail(store, IndicatorEngine.Stage));
    }

    [Fact]
    public async Task TheSwingFinderWritesNoRowForAGappedNameAndNamesTheGap()
    {
        using var store = await WithAHoleAsync();
        var cut = CutSession(store);

        await new SwingFinder(GapClock(), store.DatabaseFile).RunAsync("gap-swings");

        Assert.Equal(0, GapCount(store, "SELECT COUNT(*) FROM swing WHERE ticker = 'AAPL';"));
        Assert.True(GapCount(store, "SELECT COUNT(*) FROM swing WHERE ticker = 'MSFT';") > 0);
        Assert.Contains($"stopped at a gap (AAPL {cut})", GapDetail(store, SwingFinder.Stage));
    }

    [Fact]
    public async Task TheVolumeProfileBuilderWritesNoRowForAGappedNameAndNamesTheGap()
    {
        using var store = await WithAHoleAsync();
        var cut = CutSession(store);

        await new VolumeProfileBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-profile");

        Assert.Equal(0, GapCount(store, "SELECT COUNT(*) FROM volume_profile WHERE ticker = 'AAPL';"));
        Assert.True(GapCount(store, "SELECT COUNT(*) FROM volume_profile WHERE ticker = 'MSFT';") > 0);
        Assert.Contains($"stopped at a gap (AAPL {cut})", GapDetail(store, VolumeProfileBuilder.Stage));
    }

    [Fact]
    public async Task TheLevelBuilderWritesNoRowForAGappedNameAndNamesTheGap()
    {
        using var store = await WithAHoleAsync();
        var cut = CutSession(store);

        await new IndicatorEngine(GapClock(), store.DatabaseFile).RunAsync("gap-indicators");
        await new SwingFinder(GapClock(), store.DatabaseFile).RunAsync("gap-swings");
        await new VolumeProfileBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-profile");
        await new LevelBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-levels");

        Assert.Equal(0, GapCount(store, "SELECT COUNT(*) FROM level WHERE ticker = 'AAPL';"));
        Assert.True(GapCount(store, "SELECT COUNT(*) FROM level WHERE ticker = 'MSFT';") > 0);
        Assert.Contains($"stopped at a gap (AAPL {cut})", GapDetail(store, LevelBuilder.Stage));
    }

    [Fact]
    public async Task TheMoveAnnotatorWritesNoRowForAGappedNameAndNamesTheGap()
    {
        using var store = await WithAHoleAsync();
        var cut = CutSession(store);

        await new MoveAnnotator(GapClock(), store.DatabaseFile).RunAsync("gap-moves");

        Assert.Equal(0, GapCount(store, "SELECT COUNT(*) FROM move WHERE ticker = 'AAPL';"));
        Assert.True(GapCount(store, "SELECT COUNT(*) FROM move WHERE ticker = 'MSFT';") > 0);
        Assert.Contains($"stopped at a gap (AAPL {cut})", GapDetail(store, MoveAnnotator.Stage));
    }

    [Fact]
    public async Task TheLadderRowIsStillWrittenAndItsPlanNamesTheGap()
    {
        // Every index member gets a ladder row every night, gap or no gap, and
        // what the reader needs from an empty plan is the reason it is empty.
        // The gap is named rather than the trend being called unclassifiable:
        // both are true and only one says what to do about it.
        // see: A ladder row is written for every index member every night
        using var store = await WithAHoleAsync();
        var cut = CutSession(store);

        await new IndicatorEngine(GapClock(), store.DatabaseFile).RunAsync("gap-indicators");
        await new SwingFinder(GapClock(), store.DatabaseFile).RunAsync("gap-swings");
        await new VolumeProfileBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-profile");
        await new LevelBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-levels");
        await new LadderBuilder(GapClock(), store.DatabaseFile).RunAsync(Index, "gap-ladders");

        var row = GapRows(store, "SELECT trend_state, plan FROM ladder WHERE ticker = 'AAPL';");

        Assert.Single(row);
        Assert.StartsWith(
            Expected("gap-stop").GetProperty("trendStateForAGappedName").GetString() + "|",
            row[0],
            StringComparison.Ordinal);
        Assert.True(Expected("gap-stop").GetProperty("reasonNamesTheGapsDate").GetBoolean());
        Assert.Contains($"the stored series has a gap at {cut}", row[0]);
    }

    [Fact]
    public async Task TheListingRowIsStillWrittenAndNothingFiresForAGappedName()
    {
        // The sharp case. Five of the six reasons read a close and a level and
        // would find nothing for a gapped name anyway, and earnings soon reads
        // the calendar alone, so without the stop a gapped name with a print
        // inside the horizon reaches tonight's list carrying an empty plan and
        // nothing saying why.
        using var store = await WithAHoleAsync();
        var cut = CutSession(store);

        // A print eight sessions out from the fixture's newest session, which is
        // inside the twenty-session horizon the earnings reason fires on.
        store.Execute(
            "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) " +
            "VALUES ('AAPL', '2026-09-17', 'earnings', 'after', '{}', '2026-09-05T21:10:00Z');");

        await new IndicatorEngine(GapClock(), store.DatabaseFile).RunAsync("gap-indicators");
        await new SwingFinder(GapClock(), store.DatabaseFile).RunAsync("gap-swings");
        await new VolumeProfileBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-profile");
        await new LevelBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-levels");
        await new LadderBuilder(GapClock(), store.DatabaseFile).RunAsync(Index, "gap-ladders");
        await new ShortlistBuilder(GapClock(), store.DatabaseFile).RunAsync(Index, "gap-listings");

        var row = GapRows(store, "SELECT fired_count FROM listing WHERE ticker = 'AAPL';");

        Assert.Single(row);
        Assert.Equal(
            Expected("gap-stop").GetProperty("firedCountForAGappedName").GetInt32().ToString(CultureInfo.InvariantCulture),
            row[0]);
        Assert.Contains($"stopped at a gap (AAPL {cut})", GapDetail(store, ShortlistBuilder.Stage));
    }

    [Fact]
    public async Task ACleanNameKeepsEveryComputedRowWhileAnotherNameIsStopped()
    {
        // The control. A hole in one name's series stops that name and nothing
        // else, so a night with two absent members still computes the index.
        using var store = await WithAHoleAsync();

        await new IndicatorEngine(GapClock(), store.DatabaseFile).RunAsync("gap-indicators");
        await new SwingFinder(GapClock(), store.DatabaseFile).RunAsync("gap-swings");
        await new VolumeProfileBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-profile");
        await new LevelBuilder(GapClock(), store.DatabaseFile).RunAsync("gap-levels");
        await new MoveAnnotator(GapClock(), store.DatabaseFile).RunAsync("gap-moves");

        // The population, read off the store this test built rather than from a
        // literal: every name the fixture's backfill stored bars for.
        var names = GapCount(store, "SELECT COUNT(DISTINCT ticker) FROM bar;");

        Assert.True(names >= 4, $"Read {names} stored name(s), expected at least 4.");

        foreach (var table in Strings(Expected("gap-stop").GetProperty("withholdsEveryRow")))
        {
            Assert.True(
                GapCount(store, $"SELECT COUNT(DISTINCT ticker) FROM {table};") == names - 1,
                $"{table} holds rows for a different number of names than the {names - 1} that are not stopped.");
        }
    }

    // A store holding the fixture's year with one interior session cut out of
    // one name. Constructed input written straight into a throwaway store, which
    // is what the suite's exemption from writer ownership is for.
    static async Task<TemporaryStore> WithAHoleAsync()
    {
        var store = new TemporaryStore().Migrated();
        var clock = GapClock();

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json")),
            clock,
            store.DatabaseFile).LoadAsync(Index, "gap-0");

        await new Backfill(
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile).RunAsync(Index, "gap-1");

        var cut = Middle(store);

        store.Execute(
            $"DELETE FROM bar WHERE ticker = '{GappedName}' AND session_date = '{cut}';");

        return store;
    }

    // The session cut, read back off the store rather than remembered, so the
    // expected date is a reading of what was done and not a second statement of
    // it. The hole is the one weekday the calendar says was traded and the name
    // no longer holds.
    static string CutSession(TemporaryStore store)
    {
        var sessions = GapRows(store, $"SELECT session_date FROM bar WHERE ticker = '{GappedName}' ORDER BY session_date;")
            .Select(session => DateOnly.ParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToArray();

        var gaps = TradingCalendar.Check(sessions);

        Assert.True(gaps.HasGap, "The constructed store holds no gap, so nothing here is being tested.");
        Assert.Single(gaps.Missing);

        return gaps.Earliest!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // The middle session of the gapped name's stored year, which is interior by
    // construction and so is a gap rather than a shorter history.
    static string Middle(TemporaryStore store)
    {
        var sessions = GapRows(store, $"SELECT session_date FROM bar WHERE ticker = '{GappedName}' ORDER BY session_date;");

        Assert.True(sessions.Count > 2, $"Read {sessions.Count} stored session(s), expected more than 2.");

        return sessions[sessions.Count / 2];
    }

    // The expectation, read rather than restated. A file nothing reads is the
    // same object as a check that runs nothing, which is what the 1.8 sweep was
    // filed for, and the sweep looks for this call by the stage's own name.
    static JsonElement Expected(string stage) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FixtureFolder(), "expectations", stage + ".json"))).RootElement;

    // Each key is named at its own call site rather than passed in as a
    // variable, because the sweep that asserts every expectation key is read
    // matches the literal inside the call. A helper taking the key reads every
    // one of them and reports all of them unread, which is a coverage report
    // measuring its own indirection.
    static IReadOnlyList<string> Strings(JsonElement element) =>
        [.. element.EnumerateArray().Select(item => item.GetString()!)];

    [Fact]
    public void EveryCaseTheExpectationSeparatesFromAGapHasATestOfItsOwn()
    {
        // Three things that are not a gap and two that are answered as
        // uncheckable rather than clean, each with its own assertion above. The
        // counts are read off the expectation, so a case added to the file with
        // no test behind it fails here rather than sitting in prose.
        Assert.Equal(3, Strings(Expected("gap-stop").GetProperty("notAGap")).Count);
        Assert.Equal(2, Strings(Expected("gap-stop").GetProperty("notCheckedRatherThanClean")).Count);
    }

    [Fact]
    public void TheSplitBetweenWithholdingAndWritingIsTheThingAsserted()
    {
        // Two opposite failures, so the split is asserted rather than a loop run
        // over all seven. A stage computing a figure across the hole and a stage
        // leaving a member without the row every member gets are both defects,
        // and a test written as one loop catches neither: each table satisfies
        // whichever half the loop happens to assert.
        var withholds = Strings(Expected("gap-stop").GetProperty("withholdsEveryRow"));
        var writes = Strings(Expected("gap-stop").GetProperty("writesARowStatingTheReason"));
        var tables = Strings(Expected("gap-stop").GetProperty("tables"));

        Assert.Equal(5, withholds.Count);
        Assert.Equal(2, writes.Count);
        Assert.Equal(tables.Count, withholds.Count + writes.Count);
        Assert.Empty(withholds.Intersect(writes, StringComparer.Ordinal));
        Assert.Equal([.. tables.Order(StringComparer.Ordinal)], [.. withholds.Concat(writes).Order(StringComparer.Ordinal)]);
    }

    static string GapDetail(TemporaryStore store, string stage) =>
        GapRows(store, $"SELECT detail FROM run_log WHERE stage = '{stage}';").Single();

    static int GapCount(TemporaryStore store, string sql) =>
        int.Parse(GapRows(store, sql).Single(), CultureInfo.InvariantCulture);

    static IReadOnlyList<string> GapRows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                .Select(field => reader.IsDBNull(field) ? "null" : reader.GetValue(field).ToString())));
        }

        return rows;
    }
}
