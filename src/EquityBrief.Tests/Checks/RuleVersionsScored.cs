using System.Globalization;
using EquityBrief.Core;
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

        Assert.Null(await scorer.OpenLiveAsync(LadderRules.NearExitSkip, "versions-test-0"));

        Assert.Null(await scorer.OpenAsync(
            LadderRules.NearExitSkip,
            "three typical days",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 3 },
            "versions-test-1"));

        var first = Assert.Single(await scorer.VersionsAsync(), row => row.Version != RuleVersions.Live);

        Assert.Null(first.ClosedAt);
        Assert.Equal(RuleVersionScorer.CodeVersion, first.CodeVersion);

        // The close writes two fields and no others, and the row stands.
        var later = new RuleVersionScorer(Clock(Opened.AddDays(1)), store.DatabaseFile);

        Assert.Null(await later.CloseAsync(
            LadderRules.NearExitSkip,
            "three typical days",
            "four typical days",
            "versions-test-2"));

        Assert.Null(await later.OpenAsync(
            LadderRules.NearExitSkip,
            "four typical days",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 4 },
            "versions-test-3"));

        var rows = (await scorer.VersionsAsync()).Where(row => row.Version != RuleVersions.Live).ToArray();

        Assert.Equal(2, rows.Length);

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
    public void TheBoundRefusesTheFifteenthVersionAndTheWindowPastEachRulesCap()
    {
        // Each rule's cap, at the value it names and one below it, with the live
        // window counted: a rule at its cap holds its live window and that many
        // less one versions beside it.
        foreach (var rule in LadderRules.All)
        {
            var cap = RuleVersions.MostFor(rule);

            var full = new[] { Row(rule, RuleVersions.Live, Opened) }
                .Concat(Enumerable.Range(1, cap - 1).Select(at => Row(rule, FormattableString.Invariant($"v{at}"), Opened)))
                .ToArray();

            Assert.Equal(cap, full.Length);
            Assert.Null(RuleVersions.Refusal(full[..^1], rule, "another", Opened.AddDays(1)));

            var refused = RuleVersions.Refusal(full, rule, "another", Opened.AddDays(1));

            Assert.NotNull(refused);
            Assert.Contains(FormattableString.Invariant($"the most for this rule of {cap}"), refused, StringComparison.Ordinal);
        }

        // The merge distance's cap is the lower one, because its versions replay
        // the level stage, and the other three share the higher.
        Assert.Equal(2, RuleVersions.MostFor(LadderRules.MergeDistance));
        Assert.All(
            LadderRules.All.Where(rule => rule != LadderRules.MergeDistance),
            rule => Assert.Equal(4, RuleVersions.MostFor(rule)));

        // Fourteen is the sum of the caps, so every rule at its cap is the
        // fullest register the verb can write and any fifteenth window is past a
        // cap.
        var fullest = LadderRules.All
            .SelectMany(rule => new[] { Row(rule, RuleVersions.Live, Opened) }
                .Concat(Enumerable.Range(1, RuleVersions.MostFor(rule) - 1).Select(at => Row(rule, FormattableString.Invariant($"{rule} v{at}"), Opened))))
            .ToArray();

        Assert.Equal(RuleVersions.MostAtOnce, LadderRules.All.Sum(RuleVersions.MostFor));
        Assert.Equal(RuleVersions.MostAtOnce, fullest.Length);
        Assert.All(LadderRules.All, rule => Assert.NotNull(RuleVersions.Refusal(fullest, rule, "the fifteenth", Opened.AddDays(1))));

        // The total holds on its own over a register written by something other
        // than the verb, where one rule sits past its cap and another has room.
        var lopsided = new[] { Row(LadderRules.MergeDistance, RuleVersions.Live, Opened), Row(LadderRules.NearExitSkip, RuleVersions.Live, Opened) }
            .Concat(Enumerable.Range(1, RuleVersions.MostAtOnce - 2).Select(at => Row(LadderRules.NearExitSkip, FormattableString.Invariant($"n{at}"), Opened)))
            .ToArray();

        Assert.Equal(RuleVersions.MostAtOnce, lopsided.Length);
        Assert.Contains(
            "most at once of 14",
            RuleVersions.Refusal(lopsided, LadderRules.MergeDistance, "m1", Opened.AddDays(1))!,
            StringComparison.Ordinal);
        Assert.Null(RuleVersions.Refusal(lopsided[..^1], LadderRules.MergeDistance, "m1", Opened.AddDays(1)));

        // A closed window counts against nothing, which is what makes closing a
        // version the way to make room.
        var closed = fullest.Select(row => row.Version == RuleVersions.Live ? row : row with { ClosedAt = Opened.AddHours(1) }).ToArray();

        Assert.All(LadderRules.All, rule => Assert.Null(RuleVersions.Refusal(closed, rule, "the fifteenth", Opened.AddDays(1))));

        // A rule the build does not carry is refused whatever the counts, and a
        // version already open is refused rather than opened twice.
        Assert.Contains("is not a ladder rule", RuleVersions.Refusal([], "a rule nobody applies", "v1", Opened)!, StringComparison.Ordinal);
        Assert.Contains("already has an open window", RuleVersions.Refusal(fullest, LadderRules.NearExitSkip, RuleVersions.Live, Opened.AddDays(1))!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoundsWorstCaseIsComputedFromTheNightsOwnStageDurationsAndSitsInsideTheDeadline()
    {
        // The figures section 17 states, read off the row rather than written
        // beside it, and put to the code: the night's measured seconds, its two
        // stage durations, the caps, the total, the deadline and the night the
        // fullest register makes.
        var row = ArchitectureTables.In(Corpus.Read("docs/ARCHITECTURE.html"))
            .Single(table => table.Heading == Scope.LimitsTable)
            .Body.Single(cells => cells.Count > 2 && cells[0] == "Rule versions scored at once");

        var stated = string.Join(" ", row);

        double Figure(string pattern) =>
            double.Parse(
                System.Text.RegularExpressions.Regex.Match(stated, pattern).Groups[1].Value,
                CultureInfo.InvariantCulture);

        var night = Figure(@"(\d+) seconds over the steps before the close");
        var levels = Figure(@"a level stage of (\d+) seconds");
        var ladders = Figure(@"a ladder stage of (\d+) at");
        var deadline = Figure(@"against a deadline of (\d+)");
        var worst = Figure(@"puts the night at (\d+) seconds");

        Assert.Equal(RuleVersions.MostOfTheMergeDistance, (int)Figure(@"at most (\d+) windows of the merge distance"));
        Assert.Equal(RuleVersions.MostPerRule, (int)Figure(@"(\d+) of each of the other"));
        Assert.Equal(RuleVersions.MostAtOnce, (int)Figure(@"(\d+) at once"));

        // The deadline the row states is the one the night is bounded by.
        Assert.Equal(Core.Providers.RetryPolicy.Standard.Deadline.TotalSeconds, deadline, 6);

        // One replay of each kind, from the night's own stage durations.
        Assert.Equal(levels + ladders, RuleVersions.ProjectedSeconds([Row(LadderRules.MergeDistance, "v1", Opened)], levels, ladders), 6);
        Assert.Equal(ladders, RuleVersions.ProjectedSeconds([Row(LadderRules.NearExitSkip, "v1", Opened)], levels, ladders), 6);

        // A live window costs nothing, because the night already computed the
        // rule it measures and the scorer replays only the versions beside it.
        Assert.Equal(0, RuleVersions.ProjectedSeconds([.. LadderRules.All.Select(rule => Row(rule, RuleVersions.Live, Opened))], levels, ladders), 6);

        // The fullest register the caps admit, projected window by window, is
        // the worst case, and the night it makes is the one the row states and
        // sits inside the deadline.
        var fullest = LadderRules.All
            .SelectMany(rule => new[] { Row(rule, RuleVersions.Live, Opened) }
                .Concat(Enumerable.Range(1, RuleVersions.MostFor(rule) - 1).Select(at => Row(rule, FormattableString.Invariant($"{rule} v{at}"), Opened))))
            .ToArray();

        Assert.Equal(RuleVersions.WorstCaseSeconds(levels, ladders), RuleVersions.ProjectedSeconds(fullest, levels, ladders), 6);
        Assert.Equal(worst, night + RuleVersions.WorstCaseSeconds(levels, ladders), 6);
        Assert.True(
            night + RuleVersions.WorstCaseSeconds(levels, ladders) <= deadline,
            FormattableString.Invariant($"The fullest register the caps admit makes a night of {night + RuleVersions.WorstCaseSeconds(levels, ladders)} seconds against a deadline of {deadline}."));

        // Which rules replay levels, both ways, because the split is the whole
        // of the arithmetic above and a reader that said yes to everything would
        // pass every assertion in it.
        Assert.True(LadderRules.ReplaysLevels(LadderRules.MergeDistance));
        Assert.All(
            LadderRules.All.Where(rule => rule != LadderRules.MergeDistance),
            rule => Assert.False(LadderRules.ReplaysLevels(rule)));
    }

    [Fact]
    public async Task AVersionIsOpenedOnlyBesideItsRulesLiveWindowAndUnderTheNamesItsRuleIsReplayedFrom()
    {
        using var store = new TemporaryStore().Migrated();

        var scorer = new RuleVersionScorer(Clock(Opened), store.DatabaseFile);
        var three = new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 3 };

        // A version with no live window beside it.
        Assert.Contains(
            "has no open live window",
            await scorer.OpenAsync(LadderRules.NearExitSkip, "three typical days", three, "beside-1"),
            StringComparison.Ordinal);

        // A live window at anything but the build's own parameters.
        Assert.Contains(
            "carries the build's own parameters",
            await scorer.OpenAsync(LadderRules.NearExitSkip, RuleVersions.Live, three, "beside-2"),
            StringComparison.Ordinal);

        Assert.Null(await scorer.OpenLiveAsync(LadderRules.NearExitSkip, "beside-3"));

        // A version under a name its rule is not replayed from, which the replay
        // would read past and replay the live rule under the version's name.
        Assert.Contains(
            "was given 'nearExitDays'",
            await scorer.OpenAsync(
                LadderRules.NearExitSkip,
                "three typical days",
                new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitDays"] = 3 },
                "beside-4"),
            StringComparison.Ordinal);

        Assert.Null(await scorer.OpenAsync(LadderRules.NearExitSkip, "three typical days", three, "beside-5"));

        // Every live window the verb opens hashes to what the night compares it
        // against, so opening one cannot stop the next night.
        var rows = await scorer.VersionsAsync();

        Assert.Empty(RuleVersions.Drifted(RuleVersions.OpenAt(rows, Opened), RuleVersionScorer.HashesNow()));
        Assert.Equal(2, rows.Count);

        // Three refusals and two opens, each its own row under the outcome it had.
        Assert.Equal(
            [RuleVersionScorer.Refused, RuleVersionScorer.Refused, RuleVersionScorer.Ok, RuleVersionScorer.Refused, RuleVersionScorer.Ok],
            Query(store, $"SELECT outcome FROM run_log WHERE stage = '{RuleVersionScorer.Stage}' ORDER BY run_id;"));
    }

    [Fact]
    public async Task ALiveWindowIsNotClosedWhileAVersionOfItsRuleIsOpenBesideIt()
    {
        using var store = new TemporaryStore().Migrated();

        var scorer = new RuleVersionScorer(Clock(Opened), store.DatabaseFile);

        Assert.Null(await scorer.OpenLiveAsync(LadderRules.StopPlacement, "close-1"));
        Assert.Null(await scorer.OpenAsync(
            LadderRules.StopPlacement,
            "stop under the zone",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["stopTrailsTheLastHigherLow"] = 0 },
            "close-2"));

        var later = new RuleVersionScorer(Clock(Opened.AddDays(1)), store.DatabaseFile);

        Assert.Contains(
            "is not closed while 'stop under the zone' is open beside it",
            await later.CloseAsync(LadderRules.StopPlacement, RuleVersions.Live, null, "close-3"),
            StringComparison.Ordinal);

        // A window that is not open is not closed, and says so.
        Assert.Contains(
            "has no open window to close",
            await later.CloseAsync(LadderRules.StopPlacement, "a version nobody opened", null, "close-4"),
            StringComparison.Ordinal);

        // The versions first, then the live window.
        Assert.Null(await later.CloseAsync(LadderRules.StopPlacement, "stop under the zone", null, "close-5"));
        Assert.Null(await later.CloseAsync(LadderRules.StopPlacement, RuleVersions.Live, null, "close-6"));

        Assert.Empty(RuleVersions.OpenAt(await later.VersionsAsync(), Opened.AddDays(2)));
    }

    [Fact]
    public async Task TheVersionVerbOpensAWindowBesideItsLiveOneListsThemAndClosesOneNamingWhatReplacedIt()
    {
        // The verb a person runs, over a store, rather than the scorer's methods
        // under it: what reaches the store is what the command line says.
        using var store = new TemporaryStore().Migrated();

        async Task<(int Code, string Said, string Refused)> Run(DateTimeOffset at, params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var code = await VersionVerb.RunAsync([VersionVerb.Name, .. args], Clock(at), store.DatabaseFile, output, error);

            return (code, output.ToString(), error.ToString());
        }

        var live = await Run(Opened, "--rule", LadderRules.NearExitSkip, "--live-window");

        Assert.Equal(0, live.Code);
        Assert.Contains("opened the live window of 'the near-exit skip'", live.Said, StringComparison.Ordinal);

        var opened = await Run(Opened.AddMinutes(1), "--rule", LadderRules.NearExitSkip, "--version", "three typical days", "--parameters", "nearExitInTypicalDays=3");

        Assert.Equal(0, opened.Code);

        var listed = await Run(Opened.AddMinutes(2), "--list");

        Assert.Contains("2 open window(s) of the 14", listed.Said, StringComparison.Ordinal);
        Assert.Contains("'the near-exit skip' 'three typical days' {\"nearExitInTypicalDays\": 3}", listed.Said, StringComparison.Ordinal);

        // A refusal is a non-zero exit with the scorer's reason on the error
        // stream, and nothing written.
        var refused = await Run(Opened.AddMinutes(3), "--rule", LadderRules.MergeDistance, "--version", "wider", "--parameters", "typicalMoveMultiple=0.75");

        Assert.Equal(1, refused.Code);
        Assert.Contains("has no open live window", refused.Refused, StringComparison.Ordinal);

        var unreadable = await Run(Opened.AddMinutes(4), "--rule", LadderRules.NearExitSkip, "--version", "four", "--parameters", "nearExitInTypicalDays=four");

        Assert.Equal(1, unreadable.Code);
        Assert.Contains("is not a name and a number", unreadable.Refused, StringComparison.Ordinal);

        var closed = await Run(Opened.AddDays(1), "--rule", LadderRules.NearExitSkip, "--close", "three typical days", "--replaced-by", "four typical days");

        Assert.Equal(0, closed.Code);

        var rows = new RuleVersionScorer(Clock(Opened.AddDays(1)), store.DatabaseFile);
        var kept = Assert.Single(await rows.VersionsAsync(), row => row.Version == "three typical days");

        Assert.NotNull(kept.ClosedAt);
        Assert.Equal("four typical days", kept.ReplacedBy);
        Assert.Equal("{\"nearExitInTypicalDays\": 3}", kept.Parameters);

        // The backfill form reaches the scorer for the session it names, and a
        // session it cannot read is refused before anything is scored.
        var backfilled = await Run(Opened.AddDays(2), "--backfill", "2026-09-14");

        Assert.Equal(0, backfilled.Code);
        Assert.Contains("version: scored 2026-09-14 under 1 open window(s)", backfilled.Said, StringComparison.Ordinal);
        Assert.Equal(1, (await Run(Opened.AddDays(2), "--backfill", "14/09/2026")).Code);

        // And a form the verb does not have says which forms it does.
        Assert.Equal(1, (await Run(Opened, "--rule", LadderRules.NearExitSkip)).Code);
        Assert.Equal(1, (await Run(Opened, "--live-window")).Code);
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

        Assert.Null(await scorer.OpenLiveAsync(LadderRules.NearExitSkip, "versions-backfill-live"));

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

    [Fact]
    public void TheLadderRulesCodeVersionIsThePinOfTheSourcesAReplayRunsThrough()
    {
        // The code half of what stops the night. A window's hash carries the code
        // version, so the check can only see the code move if the version moves
        // with it, and a version somebody raises by hand is the thing that does
        // not. It is recomputed here from the files and held to the constant.
        var sources = RuleVersionScorer.CodeVersionSources
            .Select(path => File.ReadAllText(Path.Combine(Repository.Root, path)))
            .ToArray();

        Assert.Equal(3, sources.Length);
        Assert.All(sources, source => Assert.True(source.Length > 1_000, "A source the pin is taken over read as nearly empty."));

        Assert.Equal(
            RuleVersionScorer.CodeVersion,
            SourcePin.Of(sources, RuleVersionScorer.CodeVersionDeclaration));

        // Every one of the three moves it, the declaring line moves nothing, and a
        // checkout's line endings and byte order mark move nothing either.
        for (var at = 0; at < sources.Length; at++)
        {
            var moved = sources.ToArray();
            moved[at] += "\n// a changed line";

            Assert.NotEqual(RuleVersionScorer.CodeVersion, SourcePin.Of(moved, RuleVersionScorer.CodeVersionDeclaration));
        }

        var redeclared = sources.ToArray();
        redeclared[2] = redeclared[2].Replace(
            $"{RuleVersionScorer.CodeVersionDeclaration} \"{RuleVersionScorer.CodeVersion}\";",
            $"{RuleVersionScorer.CodeVersionDeclaration} \"ffffffffffff\";",
            StringComparison.Ordinal);

        Assert.NotEqual(sources[2], redeclared[2]);
        Assert.Equal(RuleVersionScorer.CodeVersion, SourcePin.Of(redeclared, RuleVersionScorer.CodeVersionDeclaration));

        var checkedOut = sources.Select(source => "﻿" + source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)).ToArray();

        Assert.Equal(RuleVersionScorer.CodeVersion, SourcePin.Of(checkedOut, RuleVersionScorer.CodeVersionDeclaration));
    }

    [Fact]
    public async Task ABackfillScoresAPastNightAgainstThatNightsBandsAndNotTheNewest()
    {
        // A past night replayed against bands computed after it would be a plan
        // no night could have produced, stored as that night's score.
        using var store = await FixtureExpectations.WithListings();

        var night = Query(store, "SELECT MAX(session_date) FROM bar;").Single();
        var session = DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var later = session.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var at = new DateTimeOffset(session.AddDays(3).ToDateTime(new TimeOnly(21, 0)), TimeSpan.Zero);

        var scorer = new RuleVersionScorer(Clock(at), store.DatabaseFile);

        Assert.Null(await scorer.OpenLiveAsync(LadderRules.NearExitSkip, "as-of-live"));
        Assert.Null(await scorer.OpenAsync(
            LadderRules.NearExitSkip,
            "three typical days",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 3 },
            "as-of-open"));

        await scorer.RunAsync(session, "as-of-1");

        var first = Query(store, $"SELECT ticker || ' ' || plan FROM version_score WHERE session_date = '{night}' ORDER BY ticker;");

        Assert.True(first.Count >= 3, $"The backfill scored {first.Count} name(s), expected at least 3.");

        // A band set two sessions later with every edge half as high again, and
        // that later night's bars and indicators, so the later night has bands of
        // its own that would change any plan read against them.
        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"SELECT ticker, '{later}', printf('%.4f', CAST(low_edge AS REAL) * 1.5), printf('%.4f', CAST(high_edge AS REAL) * 1.5), " +
            $"role, immediate, strength, has_non_average_anchor, members FROM level WHERE as_of = (SELECT MAX(as_of) FROM level);");

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"SELECT ticker, '{later}', open, high, low, close, volume, source, observed_at, raw_close FROM bar WHERE session_date = '{night}';");

        store.Execute(
            "INSERT INTO indicator (ticker, session_date, name, value, bar_count) " +
            $"SELECT ticker, '{later}', name, value, bar_count FROM indicator WHERE session_date = '{night}';");

        store.Execute("DELETE FROM version_score;");

        await new RuleVersionScorer(Clock(at.AddMinutes(1)), store.DatabaseFile).RunAsync(session, "as-of-2");

        // The past night scored again reads its own bands, so the plans are the
        // ones it wrote before the later bands existed.
        Assert.Equal(first, Query(store, $"SELECT ticker || ' ' || plan FROM version_score WHERE session_date = '{night}' ORDER BY ticker;"));

        // And the later night, over the same closes, reads the later bands, which
        // is what makes the equality above a statement about the read.
        await new RuleVersionScorer(Clock(at.AddMinutes(2)), store.DatabaseFile).RunAsync(DateOnly.ParseExact(later, "yyyy-MM-dd", CultureInfo.InvariantCulture), "as-of-3");

        var laterPlans = Query(store, $"SELECT ticker || ' ' || plan FROM version_score WHERE session_date = '{later}' ORDER BY ticker;");

        Assert.Equal(first.Count, laterPlans.Count);
        Assert.NotEqual(
            first.Select(row => row[(row.IndexOf(' ', StringComparison.Ordinal) + 1)..]),
            laterPlans.Select(row => row[(row.IndexOf(' ', StringComparison.Ordinal) + 1)..]));
    }

    [Fact]
    public async Task ANightWithNoVersionOpenReadsNoBandsAndCompletesOverLevelRowsWithoutMemberSources()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = Query(store, "SELECT MAX(session_date) FROM bar;").Single();
        var session = DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Level rows in the shape stored before member sources were written.
        store.Execute("UPDATE level SET members = (SELECT json_group_array(json_remove(value, '$.source')) FROM json_each(level.members));");

        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM level WHERE members LIKE '%\"source\"%';"));
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM level WHERE json_type(members, '$[0]') <> 'object';"));
        Assert.NotEqual(
            "0",
            Query(store, $"SELECT COUNT(*) FROM level l WHERE EXISTS (SELECT 1 FROM bar b WHERE b.ticker = l.ticker AND b.session_date = '{night}');").Single());

        var outcome = await new RuleVersionScorer(Clock(Opened), store.DatabaseFile).RunAsync(session, "no-version-open");

        Assert.Equal(0, outcome.Versions);
        Assert.Equal(0, outcome.RowsWritten);
        Assert.True(outcome.NamesScored > 0, $"Read {outcome.NamesScored} name(s) with a bar on {night}, expected at least 1.");
        Assert.Equal(
            [RuleVersionScorer.Ok],
            Query(store, $"SELECT outcome FROM run_log WHERE run_id = 'no-version-open' AND stage = '{RuleVersionScorer.Stage}';"));
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
