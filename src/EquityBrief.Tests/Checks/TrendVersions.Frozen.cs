using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Rules;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// A version's record over a store rather than over setups constructed in memory.
//
// The half of this check that was missing until the 10.4 correction. The record was asserted
// over eight blocks of `CandidateSetup` built in a list, which is a shape the store cannot
// produce: the scores and the labels a block is computed from are kept one year and a record
// is read over about four years of nights, so a record computed when it was read could hold
// three whole blocks at most against a floor of eight and its verdict was withheld forever.
// The test was well formed and the world was not shaped that way, which is the unreachable
// boundary class, and the remedy is the record read through a store the retention has
// already emptied behind it.
// see: A version's record is read from the blocks frozen as each completed
// see: A version's record belongs to the window its scores were written under and never to the version's name
public sealed partial class TrendVersions
{
    // The window every test here opens, and a second one opened 53 seconds later on the same
    // New York date, which is what the runbook's remedy for a moved pin produces.
    static readonly DateTimeOffset Opened = new(2025, 1, 1, 4, 17, 43, TimeSpan.Zero);

    static readonly DateTimeOffset Reopened = new(2025, 1, 1, 4, 18, 36, TimeSpan.Zero);

    const string Kept = "KEEP";

    const string Removed = "GONE";

    // The blocks the floor is read at, which is the population every store below is built to
    // reach: 8 whole blocks, each holding one setup the version keeps and one it takes away.
    static readonly int[] ToTheFloor = [.. Enumerable.Range(0, Blocks.Floor)];

    [Fact]
    public void ABlockCompletesInsideTheRetentionWindowAndAWindowHoldsAtMostTheLastLooksBlocks()
    {
        var expectation = Expected("trend-versions").GetProperty("frozenBlocks");

        Assert.Equal(Blocks.Sessions, expectation.GetProperty("sessionsPerBlock").GetInt32());
        Assert.Equal(ForwardReturnSeries.SetupSessionCap, expectation.GetProperty("setupWindowSessions").GetInt32());
        Assert.Equal(Looks.Maximum, expectation.GetProperty("looksMaximum").GetInt32());
        Assert.Equal(RuleVersions.MostAtOnce, expectation.GetProperty("windowsAtOnce").GetInt32());

        // The oldest session a block needs, worked from the two constants rather than read back:
        // its last session is 62 after its first and its outcome window closes 63 sessions after
        // that, so the freeze reaches 125 sessions back on the night the block completes.
        var oldest = Blocks.Sessions - 1 + ForwardReturnSeries.SetupSessionCap;

        Assert.Equal(expectation.GetProperty("oldestSessionABlockNeeds").GetInt32(), oldest);
        Assert.Equal(
            expectation.GetProperty("marginSessions").GetInt32(),
            expectation.GetProperty("sessionsInAStoredYear").GetInt32() - oldest);

        Assert.True(
            oldest < expectation.GetProperty("sessionsInAStoredYear").GetInt32(),
            "A block needs a session the retention has already dropped on the night it completes, so it could not be frozen from the store.");

        // A window holds at most the last look's blocks, and the open windows at most that many
        // each. A closed window keeps its own, so the table holds the last look's blocks for every
        // window ever opened and has no bound over the windows open at once.
        Assert.Equal(Looks.Maximum, expectation.GetProperty("rowsPerWindow").GetInt32());
        Assert.Equal(Looks.Maximum * RuleVersions.MostAtOnce, expectation.GetProperty("rowsTheOpenWindowsHold").GetInt32());
    }

    [Fact]
    public async Task AVersionsRecordReachesItsFloorOverAStoreTheRetentionHasAlreadyEmptied()
    {
        var expectation = Expected("trend-versions").GetProperty("pairedDifference");

        using var store = Planted();

        Plant(store, TheTrendVersions.BelowBothAverages, Opened, ToTheFloor);

        // Stated in advance: 8 blocks of 2 name-nights under one window is 16 scored rows, and
        // the 8 blocks span 504 sessions from the first, so the retention that runs after the
        // freeze leaves the 6 whole blocks below the one-year boundary with no rows behind them.
        Assert.Equal(16, Count(store, "SELECT COUNT(*) FROM version_score;"));

        var night = Sessions((Blocks.Floor * Blocks.Sessions) + Blocks.Sessions - 1 + Blocks.Sessions);
        var outcome = await Score(store, night);

        Assert.Equal(Blocks.Floor, outcome.BlocksFrozen);
        Assert.Equal(Blocks.Floor, Count(store, "SELECT COUNT(*) FROM version_block;"));

        // The retention has already taken the rows the earliest blocks were computed from, which
        // is the state the old reader could never have held a record in.
        Assert.True(
            Count(store, "SELECT COUNT(*) FROM version_score;") < 16,
            "The retention dropped no score, so this record is not being read over a store the retention has emptied behind it.");

        var record = Assert.Single((await Region(store, night)).Versions).Record;

        Assert.NotNull(record);
        Assert.Equal(Blocks.Floor, record!.Blocks);
        Assert.Equal(expectation.GetProperty("verdict").GetString(), record.Verdict);
        Assert.NotEqual(VersionRecord.BelowTheFloor, record.Verdict);
        Assert.Equal(expectation.GetProperty("pValue").GetDouble(), record.PValue!.Value, 9);
        Assert.Equal(Blocks.Floor * 2, record.LiveSetups);
        Assert.Equal(Blocks.Floor, record.VersionSetups);
        Assert.All(record.Differences, difference => Assert.Equal(expectation.GetProperty("differencePerBlock").GetDouble(), difference, 9));

        // And it is read on the surface, not only in the projection: the region draws the verdict
        // rather than the line that says nothing is compared until every version holds its blocks.
        var drawn = new MarkRenderer().TrendVersions(await Region(store, night));

        Assert.Contains(FormattableString.Invariant($"data-blocks=\"{Blocks.Floor}\""), drawn, StringComparison.Ordinal);
        Assert.Contains(expectation.GetProperty("verdict").GetString()!, drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-reality-check=\"none\"", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryFieldOfARecordIsDerivableFromTheFrozenBlocksAlone()
    {
        using var store = Planted();

        Plant(store, TheTrendVersions.BelowBothAverages, Opened, ToTheFloor);

        var night = Sessions((Blocks.Floor * Blocks.Sessions) + Blocks.Sessions - 1 + Blocks.Sessions);

        await Score(store, night);

        // The frozen rows and nothing else, read as columns rather than through the type that
        // reads them, so the derivation below shares no code with what it is asserted against.
        var rows = Rows(
            store,
            "SELECT block || '|' || version_excess || '|' || version_setups || '|' || live_excess || '|' || live_setups " +
            "FROM version_block ORDER BY block;");

        Assert.Equal(Blocks.Floor, rows.Count);

        var read = rows
            .Select(row => row.Split('|'))
            .Select(cells => (
                Block: int.Parse(cells[0], CultureInfo.InvariantCulture),
                VersionExcess: double.Parse(cells[1], CultureInfo.InvariantCulture),
                VersionSetups: int.Parse(cells[2], CultureInfo.InvariantCulture),
                LiveExcess: double.Parse(cells[3], CultureInfo.InvariantCulture),
                LiveSetups: int.Parse(cells[4], CultureInfo.InvariantCulture)))
            .OrderBy(row => row.Block)
            .ToArray();

        IReadOnlyList<double> differences = [.. read.Select(row => row.VersionExcess - row.LiveExcess)];

        var counted = read.Sum(row => row.VersionSetups);
        var against = read.Sum(row => row.LiveSetups);
        var record = Assert.Single((await Region(store, night)).Versions).Record;

        Assert.NotNull(record);

        // Field by field, every one of the twelve.
        Assert.Equal(TheTrendVersions.BelowBothAverages, record!.Version);
        Assert.Equal(Opened, record.OpenedAt);
        Assert.Equal(read.Length, record.Blocks);
        Assert.Equal(Blocks.Floor, record.Floor);
        Assert.Equal(against, record.LiveSetups);
        Assert.Equal(counted, record.VersionSetups);
        Assert.Equal(
            (read.Sum(row => row.VersionExcess) / counted * 100) - (read.Sum(row => row.LiveExcess) / against * 100),
            record.Excess!.Value,
            9);
        Assert.Equal(SignFlip.Statistic(differences), record.Statistic!.Value, 9);
        Assert.Equal(SignFlip.PValue(differences), record.PValue!.Value, 9);
        Assert.Equal(Looks.CrossedAt(differences, ReasonVerdict.Significance), record.CrossedAt);
        Assert.Equal(
            Looks.CrossedAt(differences, ReasonVerdict.Significance) is not null ? VersionRecord.Crossed : VersionRecord.NotCrossed,
            record.Verdict);
        Assert.Equal(differences, record.Differences);
    }

    [Fact]
    public async Task ABlocksSumsAndItsOriginDoNotMoveWhenRetentionTakesWhatTheyWereComputedFrom()
    {
        using var store = Planted();

        Plant(store, TheTrendVersions.BelowBothAverages, Opened, ToTheFloor);

        var night = Sessions((Blocks.Floor * Blocks.Sessions) + Blocks.Sessions - 1 + Blocks.Sessions);

        await Score(store, night);

        var frozen = Rows(store, Every);

        Assert.Equal(Blocks.Floor, frozen.Count);

        // Everything a block was computed from, gone: the scores the night's own retention did
        // not reach, and the labels and outcomes their own writers drop on the same boundary.
        store.Execute("DELETE FROM version_score; DELETE FROM ladder; DELETE FROM forward_return;");

        var again = await Score(store, night, "emptied");

        Assert.Equal(0, again.BlocksFrozen);
        Assert.Equal(frozen, Rows(store, Every));

        // The origin is the session the blocks were cut from and it does not move. A ninth block
        // is filled with every earlier session gone, and it is block 8 counted from the stored
        // origin, which is not the block the rows the store still holds would have given it.
        var origin = Sessions(0);
        var ninth = Sessions(Blocks.Floor * Blocks.Sessions);

        Assert.Equal([origin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)], Rows(store, "SELECT DISTINCT origin FROM version_block;"));

        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [Blocks.Floor]);

        var added = await Score(store, night, "ninth");

        Assert.Equal(1, added.BlocksFrozen);
        Assert.Equal([FormattableString.Invariant($"{Blocks.Floor}")], Rows(store, $"SELECT block FROM version_block WHERE block >= {Blocks.Floor};"));
        Assert.Equal([origin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)], Rows(store, "SELECT DISTINCT origin FROM version_block;"));

        // The other direction, so the assertion above is not one the arithmetic would have
        // reached either way: the earliest session the store still holds is not the origin, and
        // the block that session's own count would put the ninth in is not the eighth.
        var earliest = Rows(store, "SELECT MIN(session_date) FROM version_score WHERE sample = 'scored';").Single();
        var surviving = DateOnly.ParseExact(earliest, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.NotEqual(origin, surviving);
        Assert.NotEqual(Blocks.Floor, Blocks.Of(surviving, ninth));
    }

    [Fact]
    public async Task AnInSampleScoreChangesNoFrozenSum()
    {
        using var store = Planted();
        using var beside = Planted();

        Plant(store, TheTrendVersions.BelowBothAverages, Opened, ToTheFloor);
        Plant(beside, TheTrendVersions.BelowBothAverages, Opened, ToTheFloor);

        // The same store but for one name-night inside block 0 that a backfill wrote for a
        // session on or before the date the window opened on. It is a win the version keeps, so
        // counting it would move block 0's sums on both sides and nothing else.
        PlantOne(beside, TheTrendVersions.BelowBothAverages, Opened, 0, "BACK", TrendState.Range, ForwardReturnSeries.Win, RuleVersions.InSample);

        Assert.Equal(16, Count(store, "SELECT COUNT(*) FROM version_score;"));
        Assert.Equal(17, Count(beside, "SELECT COUNT(*) FROM version_score;"));

        var night = Sessions((Blocks.Floor * Blocks.Sessions) + Blocks.Sessions - 1 + Blocks.Sessions);

        await Score(store, night);
        await Score(beside, night);

        Assert.Equal(Rows(store, Every), Rows(beside, Every));
    }

    [Fact]
    public async Task TheLabelsRegionDrawsEveryScoreOnTheNightAWindowOpens()
    {
        using var store = Planted();

        // The night a window opens, where every score under it is in sample by the rule that
        // counts a score only for a session after the New York date the window opened on. The
        // record counts none of them and the region still says what the version labelled,
        // because what it draws is a description of tonight rather than evidence.
        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [0], RuleVersions.InSample);

        var night = Sessions(0);
        var region = await Region(store, night);
        var version = Assert.Single(region.Versions);

        Assert.Null(version.Record);
        Assert.Equal(2, version.Labels.Sum(label => label.Names));
        Assert.Equal(1, version.Moved);

        var drawn = new MarkRenderer().TrendVersions(region);

        Assert.Contains("1 downtrend", drawn, StringComparison.Ordinal);
        Assert.Contains("data-moved=\"1\"", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneNameWithTwoWindowsCountsASharedSessionOnceUnderEach()
    {
        using var store = Planted();

        // The runbook's remedy for a moved pin, run twice in one night: the window is closed and
        // opened again under the same name, and the session scored under both belongs to each.
        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [0], RuleVersions.InSample);
        Plant(store, TheTrendVersions.BelowBothAverages, Reopened, [0], RuleVersions.InSample);

        var night = Sessions(0);
        var labels = await new ReadApi(store.DatabaseFile, Clock).VersionLabelsAsync(LadderRules.TrendRule, night);

        // Four rows, two per window, each counting one name. Keyed on the version's name alone
        // they were two rows of two names, which counts one name-night twice.
        Assert.Equal(4, labels.Count);
        Assert.Equal([1, 1, 1, 1], labels.Select(label => label.Names));
        Assert.Equal([Opened, Opened, Reopened, Reopened], labels.Select(label => label.OpenedAt));
        Assert.Equal(2, labels.Count(label => label.Label == TrendState.Downtrend));

        var region = await Region(store, night);

        Assert.Equal(2, region.Versions.Count);
        Assert.Equal([Opened, Reopened], region.Versions.Select(row => row.OpenedAt));
        Assert.All(region.Versions, row => Assert.Equal(1, row.Moved));

        var drawn = new MarkRenderer().TrendVersions(region);

        Assert.Contains(FormattableString.Invariant($"data-opened=\"{RuleVersions.Stored(Opened)}\""), drawn, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"data-opened=\"{RuleVersions.Stored(Reopened)}\""), drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoWindowsOfOneNameAreTwoRecordsAndNeverOne()
    {
        using var store = Planted();

        // One window with the blocks a verdict is read at, and one opened 53 seconds later with
        // a single block. Merged on the name they would be one record over nine blocks; kept
        // apart they are a record that is read and a record under the floor.
        Plant(store, TheTrendVersions.BelowBothAverages, Opened, ToTheFloor);
        Plant(store, TheTrendVersions.BelowBothAverages, Reopened, [0]);

        var night = Sessions((Blocks.Floor * Blocks.Sessions) + Blocks.Sessions - 1 + Blocks.Sessions);

        await Score(store, night);

        Assert.Equal(
            [
                FormattableString.Invariant($"{RuleVersions.Stored(Opened)}|{Blocks.Floor}"),
                FormattableString.Invariant($"{RuleVersions.Stored(Reopened)}|1"),
            ],
            Rows(store, "SELECT opened_at || '|' || COUNT(*) FROM version_block GROUP BY opened_at ORDER BY opened_at;"));

        var region = await Region(store, night);

        Assert.Equal(2, region.Versions.Count);

        var read = region.Versions.Single(row => row.OpenedAt == Opened).Record;
        var under = region.Versions.Single(row => row.OpenedAt == Reopened).Record;

        Assert.Equal(Blocks.Floor, read!.Blocks);
        Assert.Equal(1, under!.Blocks);
        Assert.Equal(VersionRecord.BelowTheFloor, under.Verdict);
        Assert.NotEqual(VersionRecord.BelowTheFloor, read.Verdict);

        // And the benchmark names the window as well as the name, because two challengers
        // sharing a name cannot be told apart by one. The window under the floor is not read
        // against the benchmark at all, so one challenger stands and it is the one that is.
        var best = RealityCheck.Over([read, under]);

        Assert.NotNull(best);
        Assert.Equal(TheTrendVersions.BelowBothAverages, best!.Best);
        Assert.Equal(Opened, best.BestOpenedAt);
        Assert.Equal(1, best.Challengers);
    }

    [Fact]
    public async Task NoBlockPastTheLastLookIsFrozenWhateverCompletesAfterIt()
    {
        using var store = Planted();

        var night = Sessions(CompletesAt(Blocks.Floor));

        // Block 0 frozen as a night freezes it, which fixes the window's origin.
        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [0]);

        Assert.Equal(1, (await Score(store, night)).BlocksFrozen);

        // Then copies of it standing for the blocks a window holds by its last look but one. The
        // closure table's calendar holds ten whole blocks from the first session these tests count
        // from and the last look is at sixteen, so the count the cap reads is reached with frozen
        // rows numbered past the two planted below. The cap reads how many blocks a window holds
        // and never which.
        var copies = Enumerable.Range(3, Looks.Maximum - 2).ToArray();

        foreach (var block in copies)
        {
            store.Execute(
                "INSERT INTO version_block SELECT rule, version, opened_at, " +
                FormattableString.Invariant($"{block}, ") +
                "origin, version_excess, version_setups, live_excess, live_setups, " +
                "version_null_sum, version_null_spread, live_null_sum, live_null_spread, frozen_at " +
                "FROM version_block WHERE block = 0;");
        }

        Assert.Equal(Looks.Maximum - 1, Count(store, "SELECT COUNT(*) FROM version_block;"));

        // Two whole blocks more, one inside the last look and one past it.
        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [1, 2]);

        var reaching = await Score(store, night, "reaching");

        Assert.Equal(1, reaching.BlocksFrozen);
        Assert.Equal(Looks.Maximum, Count(store, "SELECT COUNT(*) FROM version_block;"));
        Assert.Equal(1, Count(store, "SELECT COUNT(*) FROM version_block WHERE block = 1;"));
        Assert.Equal(0, Count(store, "SELECT COUNT(*) FROM version_block WHERE block = 2;"));

        // A later night finds the block past the last look still whole and still unwritten. Its
        // rows are planted again because the night above dropped them at the year, and a night
        // with nothing to read past the last look would freeze nothing whatever the cap said.
        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [2]);

        Assert.True(
            Count(store, "SELECT COUNT(*) FROM version_score;") > 0,
            "The night below has no score to read, so its freezing nothing says nothing about the cap.");

        var later = await Score(store, night, "later");

        Assert.Equal(0, later.BlocksFrozen);
        Assert.Equal(Looks.Maximum, Count(store, "SELECT COUNT(*) FROM version_block;"));

        // And the record reads the last look's blocks and no more, in every figure it draws.
        var record = Assert.Single((await Region(store, night)).Versions).Record;

        Assert.NotNull(record);
        Assert.Equal(Looks.Maximum, record!.Blocks);
        Assert.Equal(Looks.Maximum, record.Differences.Count);
        Assert.Equal(Looks.Maximum * 2, record.LiveSetups);
        Assert.Equal(Looks.Maximum, record.VersionSetups);

        var drawn = new MarkRenderer().TrendVersions(await Region(store, night));

        Assert.Contains(FormattableString.Invariant($"data-blocks=\"{Looks.Maximum}\""), drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInSampleScoreBeforeTheFirstScoredSessionIsNotTheOrigin()
    {
        using var store = Planted();

        // A backfill over a night before the window opened leaves an in sample score on a
        // session earlier than any the window's scores count for. Its session is block 0's; the
        // window's own scores start one block later.
        PlantOne(store, TheTrendVersions.BelowBothAverages, Opened, 0, "BACK", TrendState.Range, ForwardReturnSeries.Win, RuleVersions.InSample);
        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [.. Enumerable.Range(1, Blocks.Floor)]);

        var outcome = await Score(store, Sessions(CompletesAt(Blocks.Floor)));

        // Counted from the first scored session, the planted blocks are the window's blocks 0 to
        // 7, and the in sample score's session is behind the origin rather than being it.
        var origin = Sessions(Blocks.Sessions).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Equal(Blocks.Floor, outcome.BlocksFrozen);
        Assert.Equal([origin], Rows(store, "SELECT DISTINCT origin FROM version_block;"));
        Assert.Equal(
            [.. Enumerable.Range(0, Blocks.Floor).Select(block => block.ToString(CultureInfo.InvariantCulture))],
            Rows(store, "SELECT block FROM version_block ORDER BY block;"));
    }

    [Fact]
    public async Task ANightOverBlocksAlreadyFrozenFreezesOnlyTheBlockThatHasJustCompleted()
    {
        // Stated in advance: 9 blocks planted. The first night is one session before the ninth
        // completes and freezes 8; the next is the session it completes on and freezes it alone,
        // while the store still holds rows of blocks already frozen.
        using var store = Planted(CompletesAt(Blocks.Floor) - 1);

        Plant(store, TheTrendVersions.BelowBothAverages, Opened, [.. Enumerable.Range(0, Blocks.Floor + 1)]);

        var first = await Score(store, Sessions(CompletesAt(Blocks.Floor) - 1));

        Assert.Equal(Blocks.Floor, first.BlocksFrozen);

        var frozen = Rows(store, Every);

        NewestAt(store, CompletesAt(Blocks.Floor));

        var ninth = Sessions(Blocks.Floor * Blocks.Sessions).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.True(
            Count(store, $"SELECT COUNT(*) FROM version_score WHERE session_date < '{ninth}';") > 0,
            "The retention has taken every row of the frozen blocks, so the night below re-reads no block it already holds.");

        var next = await Score(store, Sessions(CompletesAt(Blocks.Floor)), "next");

        Assert.Equal(1, next.BlocksFrozen);
        Assert.Equal(Blocks.Floor + 1, Count(store, "SELECT COUNT(*) FROM version_block;"));
        Assert.Equal(frozen, Rows(store, Every).Take(Blocks.Floor));

        // And the step's own row on the run log says so, which is where a night's freeze is read.
        var detail = Rows(
            store,
            FormattableString.Invariant($"SELECT detail FROM run_log WHERE run_id = 'frozen-next-{Sessions(CompletesAt(Blocks.Floor)):yyyyMMdd}';"));

        Assert.EndsWith(", 1 block(s) frozen", Assert.Single(detail), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClosedWindowKeepsItsBlocksSoTheTableGrowsWithEveryWindowEverOpened()
    {
        using var store = Planted();

        var night = Sessions(CompletesAt(Blocks.Floor));

        Plant(store, TheTrendVersions.BelowBothAverages, Opened, ToTheFloor);
        await Score(store, night);

        // The window closed as the runbook's remedy for a moved pin closes it, and opened again
        // under the same name, whose blocks then complete.
        store.Execute(
            "UPDATE rule_version SET closed_at = '2025-01-01T04:18:30Z', evidence = 'a pinned source moved' " +
            $"WHERE opened_at = '{RuleVersions.Stored(Opened)}';");

        Plant(store, TheTrendVersions.BelowBothAverages, Reopened, ToTheFloor);
        await Score(store, night, "reopened");

        // Nothing removes the closed window's blocks, so the table holds the last look's blocks for
        // every window ever opened rather than for the windows open at once.
        Assert.Equal(
            [
                FormattableString.Invariant($"{RuleVersions.Stored(Opened)}|{Blocks.Floor}"),
                FormattableString.Invariant($"{RuleVersions.Stored(Reopened)}|{Blocks.Floor}"),
            ],
            Rows(store, "SELECT opened_at || '|' || COUNT(*) FROM version_block GROUP BY opened_at ORDER BY opened_at;"));

        Assert.Equal(1, Count(store, "SELECT COUNT(*) FROM rule_version WHERE closed_at IS NULL;"));

        // And SCHEMA states that bound, and no bound over the open windows alone.
        var schema = Corpus.Read("docs/SCHEMA.md");
        var paragraph = schema.Split((char)10).Single(line => line.StartsWith("**`version_block` has no updater and no deleter", StringComparison.Ordinal));

        Assert.Contains("for each window ever opened", paragraph, StringComparison.Ordinal);
        Assert.DoesNotContain("bounded by construction", paragraph, StringComparison.Ordinal);
    }

    static readonly string Every =
        "SELECT rule || '|' || version || '|' || opened_at || '|' || block || '|' || origin || '|' || " +
        "version_excess || '|' || version_setups || '|' || live_excess || '|' || live_setups || '|' || " +
        "version_null_sum || '|' || version_null_spread || '|' || live_null_sum || '|' || live_null_spread " +
        "FROM version_block ORDER BY opened_at, block;";

    static FixedClock Clock => FixedClock.At(NightStart, SessionZones.UnitedStates);

    // The session, counted from the first, on which a block counted from that first session
    // completes: its last session is the block's length less one past its first, and its whole
    // outcome window has closed the setup window's length after that.
    static int CompletesAt(int block) =>
        (Blocks.Sessions * (block + 1)) - 1 + ForwardReturnSeries.SetupSessionCap;

    // A migrated store holding one bar, which is what the newest stored session is read off and
    // what the scorer counts names over. It holds no band, so nothing is replayed and the step
    // does its freeze and its drop over the rows planted below.
    static TemporaryStore Planted(int? newest = null)
    {
        var store = new TemporaryStore().Migrated();

        NewestAt(store, newest ?? CompletesAt(Blocks.Floor));

        return store;
    }

    // A bar on a later session, which moves the newest stored session a night is read against.
    static void NewestAt(TemporaryStore store, int position)
    {
        var newest = Sessions(position);

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            FormattableString.Invariant($"('ZZZZ', '{newest:yyyy-MM-dd}', '10', '11', '9', '10', 100, 'probe', '{newest:yyyy-MM-dd}T21:00:00Z', '10');"));
    }

    // One window and the scored name-nights behind it: in each block named, one name-night the
    // version leaves as the night labelled it, which resolved a win, and one it labels downtrend
    // and so takes away, which resolved a loss. Both are judged against the same bar, which is
    // the case the expectation works by hand.
    static void Plant(
        TemporaryStore store,
        string version,
        DateTimeOffset opened,
        IReadOnlyList<int> blocks,
        string sample = RuleVersions.Scored)
    {
        foreach (var block in blocks)
        {
            PlantOne(store, version, opened, block, Kept, TrendState.Range, ForwardReturnSeries.Win, sample);
            PlantOne(store, version, opened, block, Removed, TrendState.Downtrend, ForwardReturnSeries.Loss, sample);
        }
    }

    // One name-night under one window: the label that version gave it, the label the night itself
    // stored, which is always range here, and what the setup came to. A version's label of
    // downtrend against a stored range is what takes the setup away.
    static void PlantOne(
        TemporaryStore store,
        string version,
        DateTimeOffset opened,
        int block,
        string ticker,
        string label,
        string outcome,
        string sample)
    {
        var bar = Expected("trend-versions").GetProperty("pairedDifference").GetProperty("nullWin").GetDouble()
            .ToString(CultureInfo.InvariantCulture);

        var session = Sessions(block * Blocks.Sessions).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute(
            "INSERT OR IGNORE INTO rule_version (rule, version, parameters, parameters_hash, code_version, opened_at, closed_at, replaced_by) VALUES " +
            $"('{LadderRules.TrendRule}', '{version}', '{{}}', 'a hash', '{RuleVersionScorer.CodeVersion}', '{RuleVersions.Stored(opened)}', NULL, NULL);");

        store.Execute(
            "INSERT OR IGNORE INTO ladder (ticker, as_of, trend_state, plan) VALUES " +
            $"('{ticker}', '{session}', '{TrendState.Range}', '{{}}');");

        store.Execute(
            "INSERT OR IGNORE INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even, null_win, null_win_at_sensitivity, planned_risk, on_earnings) VALUES " +
            $"('{ticker}', '{session}', '{ForwardReturnSeries.Setup}', '{outcome}', '{session}', {(outcome == ForwardReturnSeries.Win ? 1 : -1)}, 0.5, 0.4, {bar}, {bar}, 1, 0);");

        store.Execute(
            "INSERT OR IGNORE INTO version_score (ticker, session_date, rule, version, opened_at, plan, sample) VALUES " +
            $"('{ticker}', '{session}', '{LadderRules.TrendRule}', '{version}', '{RuleVersions.Stored(opened)}', '{{\"trend\":\"{label}\"}}', '{sample}');");
    }

    // A run id of its own for each call, because a second run under one id is refused by the
    // run log's own key rather than overwriting the first.
    static Task<VersionScoreOutcome> Score(TemporaryStore store, DateOnly session, string run = "first") =>
        new RuleVersionScorer(Clock, store.DatabaseFile).RunAsync(
            session,
            FormattableString.Invariant($"frozen-{run}-{session:yyyyMMdd}"));

    // The region as the run page builds it, through the read API and nothing else.
    static async Task<TrendVersionRegion> Region(TemporaryStore store, DateOnly night)
    {
        var read = new ReadApi(store.DatabaseFile, Clock);

        return RunScreen.TrendVersions(
            await read.OpenVersionsAsync(LadderRules.TrendRule),
            await read.VersionLabelsAsync(LadderRules.TrendRule, night),
            await read.LiveLabelsAsync(night),
            await read.VersionBlocksAsync(LadderRules.TrendRule),
            new LabelReturns(0, 0, 0, 0),
            1,
            night);
    }

    static IReadOnlyList<string> Rows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        var rows = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(reader.IsDBNull(0) ? string.Empty : reader.GetValue(0).ToString() ?? string.Empty);
        }

        return rows;
    }

    static int Count(TemporaryStore store, string sql) =>
        int.Parse(Rows(store, sql).Single(), CultureInfo.InvariantCulture);
}
