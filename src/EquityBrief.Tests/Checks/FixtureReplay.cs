using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Fundamentals;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Membership;
using EquityBrief.Worker.News;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Returns;
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
    // record of having produced them, every component that writes appends to it, and no
    // expectation would be diffing anything about the fixture by naming it.
    // Named here rather than filtered silently, because an exclusion nobody
    // states is an exclusion nobody can argue with.
    static readonly string[] NotAFigure = ["run_log"];

    // Every stage that exists, in the order the night runs them. A stage added
    // to the night and not here is a stage this check does not replay, which
    // the count below is what catches.
    internal static async Task<TemporaryStore> ReplayedAsync(RecordedLocalModelFeed? model = null)
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
        await new ForwardReturnFiller(night, store.DatabaseFile).RunAsync("replay-returns");
        await new NewsPulseCounter(
            RecordedNewsFeed.FromFolder(Folder()),
            night,
            store.DatabaseFile).RunAsync(Index, new DateOnly(2026, 9, 8), "replay-pulse");

        // The one stage here that is not the night's. The fundamentals fetcher runs
        // when a name is opened, so it is replayed after the night rather than
        // inside it, and it is replayed at all because a table nothing populates is
        // a table no expectation can be read against. The ordering says which kind
        // of stage it is: a night that fetched fundamentals would be a night making
        // a per-name request, which is the one thing the limits table forbids.
        //
        // The archive is handed over for the names it holds a capture for, from 6.6,
        // because the segment commentary is written from the segment table the archive
        // supplies and a replay without it could never write one. Withheld for the
        // others, since asking the recording for a capture nobody committed refuses by
        // name rather than reading as an archive with nothing in it.
        var archived = new RecordedFilingsArchiveFeed(Folder()).Names;

        foreach (var ticker in RecordedFundamentalsFeed.FromFolder(Folder()).Names)
        {
            await new FundamentalsFetcher(
                RecordedFundamentalsFeed.FromFolder(Folder()),
                night,
                store.DatabaseFile,
                archived.Contains(ticker, StringComparer.OrdinalIgnoreCase)
                    ? new RecordedFilingsArchiveFeed(Folder())
                    : null).RunAsync(ticker, null, "replay-fundamentals-" + ticker);
        }

        // The prose writer and the claim checker, from 6.6, on demand and after the
        // night for the reason the fundamentals fetch is. Over the recorded local model,
        // which holds no client, so the replay reaches no network.
        //
        // Handed no documents, because the store holds none before 6.8 fetches one
        // through admissibility, so what it writes is the section the lane holds that
        // rests on the facts file alone and what it records is the three that were
        // handed nothing. For every name holding a facts file tonight except the one
        // the research expectations construct their own sections for, whose versions
        // start at one and would collide with a version this wrote.
        foreach (var ticker in ProsePassNames(store))
        {
            await new ProseWriter(
                model ?? new RecordedLocalModelFeed(Folder()),
                new LocalModelSettings(null, null, null, null, null),
                ProseWriter.DefaultLane,
                night,
                store.DatabaseFile).WriteAsync(ticker, new Dictionary<string, IReadOnlyList<StoredDocument>>(), "replay-prose-" + ticker);
        }

        await new ClaimChecker(night, store.DatabaseFile).RunAsync("replay-claims");

        return store;
    }

    // The name the fixture's research pass is recorded for.
    internal const string ResearchName = "KEYS";

    // The whole pipeline a fixture can replay, the stages an open runs included: the
    // night, the fundamentals the opens fetched, the night's facts assembled again once
    // those are stored as the research verb does after a fetch, and one research pass for
    // the name the recordings are for, over the recorded news, filings and both models.
    // see: A name's facts file is assembled again for its night when an open fetches its fundamentals
    //
    // Apart from the replay above rather than inside it, because the claim and staleness
    // expectations construct that name's sections from version one over the replayed
    // store, and a pass that had written them first would collide with every one. The
    // lane and the option are the comparison's: the same evidence asked with every
    // section paid, or every section local, reads recordings of its own.
    internal static async Task<TemporaryStore> ResearchedAsync(
        IReadOnlyList<string>? lane = null,
        bool paidForLocal = false,
        RecordedLocalModelFeed? local = null,
        RecordedResearchModelFeed? paid = null)
    {
        var store = await ReplayedForResearchAsync();

        await Researcher(store, FixedClock.At(Night, SessionZones.UnitedStates), lane, local, paid).RunAsync(ResearchName, "replay-research", new ResearchPassRequest(PaidForLocal: paidForLocal));

        return store;
    }

    // The store a research pass over the fixture starts from: the replay, and the night's
    // facts assembled again and its changes read again once the opens' fundamentals are
    // stored, which is what the research verb does after a fetch.
    internal static async Task<TemporaryStore> ReplayedForResearchAsync()
    {
        var store = await ReplayedAsync();
        var night = FixedClock.At(Night, SessionZones.UnitedStates);

        await new FactsAssembler(night, store.DatabaseFile).RunAsync("replay-facts-after-fundamentals");
        await new ChangeDetector(night, store.DatabaseFile).RunAsync("replay-changes-after-fundamentals");

        return store;
    }

    // The research runner over the fixture's recordings, as the verb composes it.
    internal static ResearchRunner Researcher(
        TemporaryStore store,
        IClock clock,
        IReadOnlyList<string>? lane = null,
        RecordedLocalModelFeed? local = null,
        IResearchModelFeed? paid = null,
        Core.Spending.SpendCaps? caps = null,
        ILocalModelFeed? localModel = null,
        LocalModelSettings? localSettings = null,
        IFilingsArchiveFeed? archive = null,
        INameNewsFeed? news = null) =>
        new(
            new StalenessJudge(clock, store.DatabaseFile),
            sections => new ProseWriter(localModel ?? local ?? new RecordedLocalModelFeed(Folder()), localSettings ?? new LocalModelSettings(null, null, null, null, null), sections, clock, store.DatabaseFile),
            new SpendCap(paid ?? new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped()), caps ?? Core.Spending.SpendCaps.Default, clock, store.DatabaseFile),
            new ClaimChecker(clock, store.DatabaseFile),
            archive ?? new RecordedFilingsArchiveFeed(Folder()),
            news ?? new RecordedNameNewsFeed(Folder()),
            lane ?? ProseWriter.DefaultLane,
            clock,
            store.DatabaseFile);

    // The names the replay's prose pass is for: those holding a facts file on the
    // replayed night, less the name the prose fixture writes its own sections for.
    // Read from the store and from that fixture rather than listed, so a name added
    // to the fixture is a name the pass reaches.
    internal static IReadOnlyList<string> ProsePassNames(TemporaryStore store)
    {
        using var prose = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), "research-prose.json")));

        var constructed = prose.RootElement.GetProperty("ticker").GetString()!;

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker FROM facts WHERE session_date = $night ORDER BY ticker;";
        command.Parameters.AddWithValue(
            "$night",
            ((IClock)FixedClock.At(Night, SessionZones.UnitedStates)).SessionDateAt(Night).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var names = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (!string.Equals(reader.GetString(0), constructed, StringComparison.Ordinal))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
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

    // The table an expectation is waiting for a writer for, where it names one.
    //
    // A store can be built, read and drawn a checkpoint before anything writes to
    // it, and the source documents store is the first of those here: SCHEMA gives
    // Insert on it to the two research runners and to nobody else, so the
    // statement cannot exist before the first runner does. An expectation about
    // what those rows will say is a real expectation, and naming its table in
    // `tables` would claim the replay populates it.
    //
    // So it goes in `awaits` instead, with the checkpoint whose writer will fill
    // it, and the exemption is asserted rather than tolerated: the table has to be
    // one SCHEMA declares, the replay has to leave it empty, and the checkpoint has
    // to be one the plan has and the record does not. The day something writes it,
    // the emptiness fails and the only repair is to move the table into `tables`,
    // which is what keeps this from becoming a way to name a table nothing checks.
    static IReadOnlyDictionary<string, (string Table, string WrittenAt)> Awaited()
    {
        var awaited = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(Path.Combine(Folder(), "expectations"), "*.json"))
        {
            var document = JsonDocument.Parse(File.ReadAllText(file)).RootElement;

            if (!document.TryGetProperty("awaits", out var awaits))
            {
                continue;
            }

            awaited[Path.GetFileNameWithoutExtension(file)] = (
                awaits.GetProperty("table").GetString()!,
                awaits.GetProperty("writtenAt").GetString()!);

            Assert.False(
                string.IsNullOrWhiteSpace(awaits.GetProperty("why").GetString()),
                $"{Path.GetFileNameWithoutExtension(file)}.json awaits a table and does not say why nothing writes it.");
        }

        return awaited;
    }

    [Fact]
    public async Task AnExpectationCoversATableTheReplayWritesOrOneNoWriterExistsFor()
    {
        // The two halves in one place, because what they partition is every
        // expectation: a file that named neither would cover nothing and pass
        // both directions of the checks above by having nothing in either list.
        var named = Named();
        var awaited = Awaited();

        Assert.True(named.Count >= 5, $"Read {named.Count} expectations, expected at least 5.");

        // Empty from 6.8, which wrote the one table an expectation awaited, and the loop
        // below still holds any added later to the terms it held that one to.

        var covering = named
            .Where(entry => entry.Value.Length == 0 && !awaited.ContainsKey(entry.Key))
            .Select(entry => $"{entry.Key} names no table and awaits none")
            .ToArray();

        Assert.DoesNotContain(covering, _ => true);

        using var store = await ResearchedAsync();
        var populated = Populated(store).ToHashSet(StringComparer.Ordinal);
        var declared = StoreSchema.DeclaredTables(Corpus.Read("docs/SCHEMA.md"));
        var plan = Corpus.Read("docs/BUILD_PLAN.md");
        var progress = Corpus.Read("docs/PROGRESS.md");

        foreach (var (stage, (table, writtenAt)) in awaited)
        {
            Assert.Contains(table, declared);

            Assert.DoesNotContain(table, populated);

            Assert.True(
                DuePoints.InThePlan(writtenAt, plan),
                $"{stage}.json says {table} is written at {writtenAt}, which the plan has neither as a checkpoint nor as a phase.");

            Assert.False(
                DuePoints.HasLanded(writtenAt, progress),
                $"{stage}.json says {table} is written at {writtenAt}, which the record shows as landed. " +
                "A table the replay leaves empty after its writer's checkpoint has landed is a writer " +
                "nothing runs, and the expectation has to move its table into the covered list.");
        }
    }

    [Fact]
    public async Task EveryTableTheReplayPopulatesIsNamedByAnExpectation()
    {
        var named = Named();
        var covered = named.Values.SelectMany(tables => tables).ToHashSet(StringComparer.Ordinal);

        using var store = await ResearchedAsync();

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

        using var store = await ResearchedAsync();
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
        using var store = await ResearchedAsync();

        var covered = Named().Values.SelectMany(tables => tables).ToHashSet(StringComparer.Ordinal);
        var populated = Populated(store).Except(NotAFigure, StringComparer.Ordinal).ToArray();

        Assert.DoesNotContain(populated, table => !covered.Contains(table));

        // Now with one name removed from what the expectations cover, the same
        // reading reports it. Nothing on disk is touched.
        var narrowed = covered.Except(["indicator"], StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(populated, table => !narrowed.Contains(table));
    }
}
