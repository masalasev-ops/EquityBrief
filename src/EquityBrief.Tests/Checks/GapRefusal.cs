using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
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
        [CheckReach.Key(Scope.FailureTable, "A gap in one name's series, chart")]);

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
        Assert.Contains(removed.ToString("yyyy-MM-dd"), detail, StringComparison.Ordinal);
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
        Assert.DoesNotContain($"data-session=\"{removed:yyyy-MM-dd}\"", svg, StringComparison.Ordinal);
    }
}
