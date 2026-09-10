using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Membership;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-replay. The pipeline over the committed fixture matches every
// expectation, with every figure it produces named by one.
//
// The first half is what fixture-expectations asserts, stage by stage. This is
// the second half, and it runs the other way: after replaying every stage that
// exists, every table the pipeline wrote rows into has to be named by some
// expectation. A stage that produces a figure nothing expected is the same
// object as an expectation nothing reads, seen from the other end, and neither
// direction catches the other.
//
// It is rostered from 3.1 because that is the checkpoint at which the pipeline
// first has a stage downstream of the ones phase 1 built, so the backward
// direction has something to be wrong about.
public class FixtureReplay
{
    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    static readonly DateTimeOffset Backfilled = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    static string Folder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // The run log is not a figure the pipeline produces. It is the operational
    // record of having produced them, every component appends to it, and no
    // expectation would be diffing anything about the fixture by naming it.
    // Named here rather than filtered silently, because an exclusion nobody
    // states is an exclusion nobody can argue with.
    static readonly string[] NotAFigure = ["run_log"];

    // Every stage that exists, in the order the night runs them. A stage added
    // to the night and not here is a stage this check does not replay, which
    // the count below is what catches.
    static async Task<TemporaryStore> ReplayedAsync()
    {
        var store = new TemporaryStore().Migrated();
        var backfill = FixedClock.At(Backfilled, SessionZones.UnitedStates);
        var night = FixedClock.At(Night, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(Folder(), "index-constituents.json")),
            backfill,
            store.DatabaseFile).LoadAsync(Index, "replay-membership");

        await new Backfill(
            RecordedHistoricalBarFeed.FromFolder(Folder()),
            backfill,
            store.DatabaseFile).RunAsync(Index, "replay-backfill");

        await new BarFetcher(
            RecordedBulkPriceFeed.FromFolder(Folder()),
            night,
            store.DatabaseFile).RunAsync(Index, "replay-fetch");

        await new CorporateActionChecker(
            RecordedCorporateActionFeed.FromFolder(Folder()),
            RecordedHistoricalBarFeed.FromFolder(Folder()),
            FixedClock.At(new DateTimeOffset(2026, 8, 10, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "replay-actions");

        await new CalendarFetcher(
            RecordedEarningsCalendarFeed.FromFolder(Folder()),
            night,
            store.DatabaseFile).RunAsync(Index, new DateOnly(2026, 9, 8), "replay-calendar");

        await new IndicatorEngine(night, store.DatabaseFile).RunAsync("replay-indicators");
        await new SwingFinder(night, store.DatabaseFile).RunAsync("replay-swings");
        await new VolumeProfileBuilder(night, store.DatabaseFile).RunAsync("replay-profile");
        await new LevelBuilder(night, store.DatabaseFile).RunAsync("replay-levels");
        await new LadderBuilder(night, store.DatabaseFile).RunAsync(Index, "replay-ladders");
        await new MoveAnnotator(night, store.DatabaseFile).RunAsync("replay-moves");
        await new FactsAssembler(night, store.DatabaseFile).RunAsync("replay-facts");
        await new ChangeDetector(night, store.DatabaseFile).RunAsync("replay-changes");
        await new ShortlistBuilder(night, store.DatabaseFile).RunAsync(Index, "replay-listings");

        // The detector again, in the order the night takes: the shortlist is
        // step 12 and the facts file is step 13, and the detector's retention
        // reads the listings there were none of the first time it ran.
        await new ChangeDetector(night, store.DatabaseFile).RunAsync("replay-changes-again");

        return store;
    }

    static IReadOnlyList<string> Populated(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        var populated = new List<string>();

        foreach (var table in StoreSchema.Tables(connection))
        {
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {table};";

            if (Convert.ToInt64(count.ExecuteScalar()) > 0)
            {
                populated.Add(table);
            }
        }

        return populated;
    }

    // What each expectation says it covers, read from the files rather than kept
    // beside this check, so an expectation added without saying what it names is
    // an expectation this check reports rather than one it silently trusts.
    static IReadOnlyDictionary<string, string[]> Named()
    {
        var named = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(Path.Combine(Folder(), "expectations"), "*.json"))
        {
            var document = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
            var stage = Path.GetFileNameWithoutExtension(file);

            Assert.True(
                document.TryGetProperty("tables", out var tables),
                $"{stage}.json names no tables, so nothing can say which figures it covers.");

            named[stage] = tables.EnumerateArray().Select(table => table.GetString()!).ToArray();
        }

        return named;
    }

    [Fact]
    public async Task EveryTableTheReplayPopulatesIsNamedByAnExpectation()
    {
        var named = Named();
        var covered = named.Values.SelectMany(tables => tables).ToHashSet(StringComparer.Ordinal);

        using var store = await ReplayedAsync();

        var populated = Populated(store).Except(NotAFigure, StringComparer.Ordinal).ToArray();

        // The scope carrying the property is the populated tables, and it is
        // floored. A replay that wrote nothing would satisfy every assertion
        // below by having nothing to check.
        Assert.True(populated.Length >= 4, $"The replay populated {populated.Length} tables, expected at least 4.");

        var unnamed = populated.Where(table => !covered.Contains(table)).ToArray();

        Assert.DoesNotContain(unnamed, _ => true);
    }

    [Fact]
    public async Task EveryTableAnExpectationNamesIsOneTheReplayPopulates()
    {
        // The other direction, and it is not symmetric with the one above. That
        // one catches a figure nothing expected; this one catches an expectation
        // naming a table no stage writes, which is an expectation about
        // something that does not happen.
        var named = Named();

        Assert.True(named.Count >= 4, $"Read {named.Count} expectations, expected at least 4.");

        using var store = await ReplayedAsync();
        var populated = Populated(store).ToHashSet(StringComparer.Ordinal);

        var missing = named
            .SelectMany(entry => entry.Value.Select(table => (Stage: entry.Key, Table: table)))
            .Where(pair => !populated.Contains(pair.Table))
            .Select(pair => $"{pair.Stage} names {pair.Table} and the replay does not populate it")
            .ToArray();

        Assert.DoesNotContain(missing, _ => true);
    }

    [Fact]
    public async Task ATableTheReplayWritesAndNoExpectationNamesIsReported()
    {
        // The permanent proof that the forward direction can fail, over
        // constructed input rather than by breaking the fixture. A table nobody
        // named is exactly what this check exists to find, so it is planted.
        using var store = await ReplayedAsync();

        var covered = Named().Values.SelectMany(tables => tables).ToHashSet(StringComparer.Ordinal);
        var populated = Populated(store).Except(NotAFigure, StringComparer.Ordinal).ToArray();

        Assert.DoesNotContain(populated, table => !covered.Contains(table));

        // Now with one name removed from what the expectations cover, the same
        // reading reports it. Nothing on disk is touched.
        var narrowed = covered.Except(["indicator"], StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(populated, table => !narrowed.Contains(table));
    }
}
