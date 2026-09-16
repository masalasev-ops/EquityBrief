using System.Globalization;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Rules;

namespace EquityBrief.Tests.Checks;

// rule-versions-scored.
//
// A version change opens a new window and keeps the previous one, a score
// written for a night before its window opened counts toward nothing, and the
// bound is refused at the version past it.
//
// The window is the unit rather than the version name, and that is the whole
// point of the table. Scores belong to a window, so a version closed and opened
// again is two measurements and not one, and a row edited after its scores were
// written would make them scores of something else.
// see: Adding a candidate later restarts the clock
public class RuleVersionsScored
{
    internal static CheckReach Reach => new(
        "rule-versions-scored",
        ["docs/ARCHITECTURE.html", "docs/SCHEMA.md"],
        [
            CheckReach.Key(Scope.StoresTable, "Rule versions"),
            CheckReach.Key(Scope.StoresTable, "Version scores"),
            CheckReach.Key(Scope.LimitsTable, "Rule versions scored at once"),
        ]);

    static readonly DateTimeOffset Opened = new(2026, 9, 16, 21, 0, 0, TimeSpan.Zero);

    static FixedClock Clock(DateTimeOffset at) => FixedClock.At(at, SessionZones.UnitedStates);

    static RuleVersionRow Row(string rule, string version, DateTimeOffset opened, DateTimeOffset? closed = null) =>
        new(rule, version, "{}", "000000000000", "8.6", opened, closed, null);

    [Fact]
    public async Task AVersionChangeOpensANewWindowAndKeepsThePreviousOne()
    {
        using var store = new TemporaryStore().Migrated();

        var scorer = new RuleVersionScorer(Clock(Opened), store.DatabaseFile);

        Assert.Null(await scorer.OpenAsync(
            LadderRules.NearExitSkip,
            "three typical days",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 3 },
            "versions-test-1"));

        var first = Assert.Single(await scorer.VersionsAsync());

        Assert.Null(first.ClosedAt);
        Assert.Equal("8.6", first.CodeVersion);

        // The close writes two fields and no others, and the row stands.
        var later = new RuleVersionScorer(Clock(Opened.AddDays(1)), store.DatabaseFile);

        Assert.True(await later.CloseAsync(
            LadderRules.NearExitSkip,
            "three typical days",
            first.OpenedAt,
            "four typical days",
            "versions-test-2"));

        Assert.Null(await later.OpenAsync(
            LadderRules.NearExitSkip,
            "four typical days",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 4 },
            "versions-test-3"));

        var rows = await scorer.VersionsAsync();

        Assert.Equal(2, rows.Count);

        var closed = rows.Single(row => row.Version == "three typical days");

        // Kept, and kept as it was opened. The parameters, the hash, the code
        // version and the opening instant are what the scores under it were of.
        Assert.Equal(first.Parameters, closed.Parameters);
        Assert.Equal(first.ParametersHash, closed.ParametersHash);
        Assert.Equal(first.OpenedAt, closed.OpenedAt);
        Assert.NotNull(closed.ClosedAt);
        Assert.Equal("four typical days", closed.ReplacedBy);

        // And only the new one is open afterwards, at an instant after the close.
        var open = RuleVersions.OpenAt(rows, Opened.AddDays(2));

        Assert.Equal(["four typical days"], [.. open.Select(row => row.Version)]);

        // The closed window is open at an instant inside it, which is what makes
        // a score's window a question about when rather than about which name.
        Assert.Equal(
            ["three typical days"],
            [.. RuleVersions.OpenAt(rows, Opened.AddHours(1)).Select(row => row.Version)]);
    }

    [Fact]
    public void TheBoundRefusesTheFifteenthVersionAndTheFifthOfOneRule()
    {
        // Both caps, at the value each names and one below it. Four of each of
        // four rules is sixteen, so the total binds first and the per-rule cap
        // has to be put to a register that has not reached the total.
        var perRule = Enumerable.Range(1, RuleVersions.MostPerRule)
            .Select(at => Row(LadderRules.NearExitSkip, FormattableString.Invariant($"v{at}"), Opened))
            .ToArray();

        Assert.Null(RuleVersions.Refusal(perRule[..(RuleVersions.MostPerRule - 1)], LadderRules.NearExitSkip, "another", Opened.AddDays(1)));

        var full = RuleVersions.Refusal(perRule, LadderRules.NearExitSkip, "another", Opened.AddDays(1));

        Assert.NotNull(full);
        Assert.Contains("most per rule of 4", full, StringComparison.Ordinal);

        // The total, built from four rules so the per-rule cap is not what
        // refuses: three of each of the four rules is twelve, and two more on a
        // fifth would exceed neither cap, so the fourteen are spread to leave one
        // rule with room.
        var spread = LadderRules.All
            .SelectMany(rule => Enumerable.Range(1, RuleVersions.MostPerRule)
                .Select(at => Row(rule, FormattableString.Invariant($"{rule} v{at}"), Opened)))
            .Take(RuleVersions.MostAtOnce)
            .ToArray();

        Assert.Equal(RuleVersions.MostAtOnce, spread.Length);

        // The rule with room left, so what refuses is the total and not the cap.
        var roomy = spread.GroupBy(row => row.Rule, StringComparer.Ordinal)
            .First(group => group.Count() < RuleVersions.MostPerRule)
            .Key;

        var atOnce = RuleVersions.Refusal(spread, roomy, "the fifteenth", Opened.AddDays(1));

        Assert.NotNull(atOnce);
        Assert.Contains("most at once of 14", atOnce, StringComparison.Ordinal);

        Assert.Null(RuleVersions.Refusal(spread[..(RuleVersions.MostAtOnce - 1)], roomy, "the fourteenth", Opened.AddDays(1)));

        // A closed window does not count against either cap, which is what makes
        // retiring a version the way to make room.
        var closed = spread.Select(row => row with { ClosedAt = Opened.AddHours(1) }).ToArray();

        Assert.Null(RuleVersions.Refusal(closed, roomy, "the fifteenth", Opened.AddDays(1)));

        // A rule the build does not carry is refused whatever the counts, and a
        // version already open is refused rather than opened twice.
        Assert.Contains("is not a ladder rule", RuleVersions.Refusal([], "a rule nobody applies", "v1", Opened)!, StringComparison.Ordinal);
        Assert.Contains("already has an open window", RuleVersions.Refusal(perRule, LadderRules.NearExitSkip, "v1", Opened.AddDays(1))!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProjectedSecondsAreComputedFromTheNightsOwnStageDurations()
    {
        // The bound's arithmetic, put to the figures section 17 states rather
        // than to figures written beside it. A merge distance version costs a
        // level replay and a ladder replay; every other version costs a ladder
        // replay. The night of 2026-09-14 measured 143 and 5 at 504 names.
        const double Levels = 143;
        const double Ladders = 5;

        var one = RuleVersions.ProjectedSeconds([Row(LadderRules.MergeDistance, "v1", Opened)], Levels, Ladders);

        Assert.Equal(148, one, 6);

        var other = RuleVersions.ProjectedSeconds([Row(LadderRules.NearExitSkip, "v1", Opened)], Levels, Ladders);

        Assert.Equal(5, other, 6);

        // Fourteen at the split the document states: two of the merge distance
        // and twelve of the rest is 2 x 148 + 12 x 5 = 356, which is inside the
        // 405 seconds the deadline leaves after the 495 the night took. The
        // document's own 688 is the total, so the two agree.
        var fourteen = new[]
        {
            Row(LadderRules.MergeDistance, "m1", Opened),
            Row(LadderRules.MergeDistance, "m2", Opened),
        }
            .Concat(Enumerable.Range(1, 12).Select(at => Row(LadderRules.NearExitSkip, FormattableString.Invariant($"n{at}"), Opened)))
            .ToArray();

        Assert.Equal(RuleVersions.MostAtOnce, fourteen.Length);
        Assert.Equal(356, RuleVersions.ProjectedSeconds(fourteen, Levels, Ladders), 6);
        Assert.Equal(688, 495 + RuleVersions.ProjectedSeconds(fourteen, Levels, Ladders) - 163, 6);

        // Which rules replay levels, both ways, because the split is the whole
        // of the arithmetic above and a reader that said yes to everything would
        // pass every assertion in it.
        Assert.True(LadderRules.ReplaysLevels(LadderRules.MergeDistance));
        Assert.All(
            LadderRules.All.Where(rule => rule != LadderRules.MergeDistance),
            rule => Assert.False(LadderRules.ReplaysLevels(rule)));
    }

    [Fact]
    public void ALiveRuleThatMovedInsideAnOpenWindowIsFoundAndOneThatHasNotIsNot()
    {
        var now = RuleVersionScorer.HashesNow();

        Assert.Equal(LadderRules.All.Count, now.Count);

        // A live window opened at the hash the build carries is not drifted.
        var steady = LadderRules.All
            .Select(rule => Row(rule, RuleVersions.Live, Opened) with { ParametersHash = now[rule] })
            .ToArray();

        Assert.Empty(RuleVersions.Drifted(steady, now));

        // The same windows against a build whose rules have moved.
        var moved = now.ToDictionary(pair => pair.Key, _ => "ffffffffffff", StringComparer.Ordinal);
        var found = RuleVersions.Drifted(steady, moved);

        Assert.Equal(LadderRules.All.Count, found.Count);
        Assert.All(found, one => Assert.Contains("says nothing about either", one, StringComparison.Ordinal));

        // A window on a version that is not live is not asked about: the rule it
        // measures is the version's own and the build does not apply it.
        var version = new[] { Row(LadderRules.NearExitSkip, "three typical days", Opened) };

        Assert.Empty(RuleVersions.Drifted(version, moved));

        // And a live window on a rule this build applies none of is found, which
        // is the same fault arriving from the other direction.
        Assert.Single(RuleVersions.Drifted(
            [Row(LadderRules.NearExitSkip, RuleVersions.Live, Opened)],
            new Dictionary<string, string>(StringComparer.Ordinal)));

        // The hash moves with the parameters and with the code version, so a
        // code change and a parameter change are the same event to this check.
        var parameters = new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 2 };

        Assert.NotEqual(
            RuleVersions.Hash(parameters, "8.6"),
            RuleVersions.Hash(parameters, "8.7"));

        Assert.NotEqual(
            RuleVersions.Hash(parameters, "8.6"),
            RuleVersions.Hash(new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 3 }, "8.6"));
    }

    [Fact]
    public void AVersionReplaysThePlanAndProducesADifferentOneFromTheLiveRule()
    {
        // The replay itself, over bands written here. The near-exit skip at three
        // typical days rather than two takes an exit out of the traded set, which
        // is a plan a reader can tell from the live one.
        var bands = new[]
        {
            new Core.Levels.Level(90m, 95m, Core.Levels.LevelSeries.Support, false, 3, true, [
                new Core.Levels.LevelMember(Core.Levels.MemberSource.Swing, "low", 90m, new DateOnly(2026, 8, 1)),
                new Core.Levels.LevelMember(Core.Levels.MemberSource.Average, "sma50", 95m, new DateOnly(2026, 9, 1)),
            ]),
            new Core.Levels.Level(101m, 103m, Core.Levels.LevelSeries.Resistance, false, 2, true, [
                new Core.Levels.LevelMember(Core.Levels.MemberSource.Swing, "high", 101m, new DateOnly(2026, 8, 15)),
            ]),
            new Core.Levels.Level(130m, 132m, Core.Levels.LevelSeries.Resistance, false, 2, true, [
                new Core.Levels.LevelMember(Core.Levels.MemberSource.Swing, "high", 130m, new DateOnly(2026, 8, 20)),
            ]),
        };

        var recent = Enumerable.Range(0, 10)
            .Select(at => new LadderBar(new DateOnly(2026, 9, 1).AddDays(at), 101m, 97m, 100m))
            .ToArray();

        var live = LadderSeries.For(bands, 100m, 4m, recent, TrendState.Range);

        // One support band, so the blended entry is that zone's own midpoint of
        // 92.5. The exit at 101 is 8.5 above it, which clears two typical days of
        // 8, so it is traded.
        Assert.Contains(live.Exits, exit => exit.LowEdge == 101m && exit.Traded);

        // At three typical days the same exit is 8.5 against 12, so it is listed
        // and not traded. One rule moved, one plan differs.
        var wider = LadderSeries.For(
            bands, 100m, 4m, recent, TrendState.Range,
            rules: new LadderRuleSet(NearExitInTypicalDays: 3));

        Assert.Contains(wider.Exits, exit => exit.LowEdge == 101m && !exit.Traded);

        // The zone edges version narrows the first tranche to its non-average
        // anchors, which is 90 alone rather than 90 to 95.
        var narrow = LadderSeries.For(
            bands, 100m, 4m, recent, TrendState.Range,
            rules: new LadderRuleSet(ZoneEdgesFromNonAverageAnchorsOnly: true));

        Assert.Equal((90m, 95m), (live.Tranches[0].LowEdge, live.Tranches[0].HighEdge));
        Assert.Equal((90m, 90m), (narrow.Tranches[0].LowEdge, narrow.Tranches[0].HighEdge));

        // And the live rule set is what every caller that names none gets, so the
        // seam changed nothing about what the night computes. Compared part by
        // part rather than record to record, because a record holding lists
        // compares them by reference and would pass on two empty plans.
        var named = LadderSeries.For(bands, 100m, 4m, recent, TrendState.Range, rules: LadderRuleSet.Live);

        Assert.Equal([.. live.Tranches], [.. named.Tranches]);
        Assert.Equal([.. live.Exits], [.. named.Exits]);
        Assert.Equal((live.Invalidation, live.Reason), (named.Invalidation, named.Reason));
        Assert.NotEmpty(live.Tranches);
    }

    [Fact]
    public async Task ABackfilledScoreIsFlaggedInSampleAndAScoreForANightAfterItsWindowIsNot()
    {
        // The rule that keeps a backfill from becoming evidence. A version added
        // later may be scored over the nights before it, because seeing what it
        // would have done is the point; what it may not do is count.
        using var store = await FixtureExpectations.WithListings();

        var night = Query(store, "SELECT MAX(session_date) FROM bar;").Single();
        var session = DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A window opened the day after the night being scored, which is what a
        // backfill is: the version did not exist when that night happened.
        var after = new DateTimeOffset(session.AddDays(1).ToDateTime(new TimeOnly(21, 0)), TimeSpan.Zero);
        var scorer = new RuleVersionScorer(Clock(after), store.DatabaseFile);

        Assert.Null(await scorer.OpenAsync(
            LadderRules.NearExitSkip,
            "three typical days",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 3 },
            "versions-backfill-open"));

        var outcome = await scorer.RunAsync(session, "versions-backfill");

        Assert.True(outcome.RowsWritten > 0, "the scorer wrote no score, so the flag below is asserted over nothing.");

        var flags = Query(store, "SELECT DISTINCT sample FROM version_score;");

        Assert.Equal([RuleVersions.InSample], flags);

        // The same version scoring a night after its window opened counts.
        var later = session.AddDays(2);

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "SELECT ticker, '" + later.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "', open, high, low, close, volume, source, observed_at, raw_close FROM bar WHERE session_date = '" + night + "';");

        store.Execute(
            "INSERT INTO indicator (ticker, session_date, name, value, bar_count) " +
            "SELECT ticker, '" + later.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "', name, value, bar_count FROM indicator WHERE session_date = '" + night + "';");

        await new RuleVersionScorer(Clock(after.AddDays(3)), store.DatabaseFile).RunAsync(later, "versions-scored");

        Assert.Equal(
            [RuleVersions.Scored],
            Query(store, "SELECT DISTINCT sample FROM version_score WHERE session_date = '"
                + later.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "';"));

        // Both are in the table, which is what makes the flag the thing that
        // separates them rather than the row's presence.
        Assert.Equal(
            [RuleVersions.InSample, RuleVersions.Scored],
            Query(store, "SELECT DISTINCT sample FROM version_score ORDER BY sample;"));
    }

    static IReadOnlyList<string> Query(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        var read = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            read.Add(reader.IsDBNull(0) ? string.Empty : reader.GetValue(0).ToString() ?? string.Empty);
        }

        return read;
    }
}
