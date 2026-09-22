using System.Globalization;
using System.Text.Json;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Rules;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// What the trend rule's versions label, what they take away, and how a version is judged
// against the live rule.
//
// The labels are put to the rule directly rather than through a store, for the reason the
// conditions' own expectation gives: what is being compared is a derivation against the code,
// and a store in between adds a way for the two to agree by accident. The replay over the
// fixture is asserted through the store, because what it is about is the scorer reading a
// night's own figures.
public sealed class TrendVersions
{
    internal static CheckReach Reach => new(
        "trend-versions",
        ["fixtures/membership-2026-09-05", "docs/ARCHITECTURE.html"],
        [
            CheckReach.Key(Scope.LimitsTable, "Rule versions scored at once"),
        ]);

    static readonly DateTimeOffset NightStart = new(2026, 9, 8, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EachVersionLabelsWhereTheExpectationWorkedByHandSaysItDoes()
    {
        var expectation = Expected("trend-versions");
        var versions = Versions(expectation);
        var nights = expectation.GetProperty("labels").EnumerateArray().ToArray();

        Assert.Equal(3, versions.Count);
        Assert.True(nights.Length >= 8, $"The expectation works {nights.Length} name-night(s), expected at least 8.");

        var moved = versions.ToDictionary(one => one.Key, _ => 0, StringComparer.Ordinal);

        foreach (var night in nights)
        {
            var stored = night.GetProperty("stored").GetString()!;
            var before = night.GetProperty("nightsBefore").EnumerateArray().Select(one => one.GetString()!).ToArray();

            foreach (var (key, rules) in versions)
            {
                var applied = TrendSeries.Applied(
                    stored,
                    night.GetProperty("close").GetDecimal(),
                    Average(night, "shortAverage"),
                    Average(night, "longAverage"),
                    before,
                    rules);

                Assert.Equal(
                    (night.GetProperty("ticker").GetString(), key, night.GetProperty(key).GetString()),
                    (night.GetProperty("ticker").GetString(), key, applied));

                if (applied != stored)
                {
                    moved[key]++;
                }
            }
        }

        // Each version moves a label somewhere and leaves one alone somewhere, so a run where
        // every version happened to change nothing could not pass the loop above by agreeing
        // with an expectation that also changed nothing.
        Assert.All(moved, pair => Assert.InRange(pair.Value, 1, nights.Length - 1));
    }

    [Fact]
    public void TheVersionPopulationsAreCountedByTheCauseThatPutsANameInThem()
    {
        var expectation = Expected("trend-versions");
        var populations = expectation.GetProperty("populationsByCause");

        var above = 0;
        var below = 0;
        var held = 0;

        foreach (var night in expectation.GetProperty("labels").EnumerateArray())
        {
            var close = night.GetProperty("close").GetDecimal();
            var shortAverage = Average(night, "shortAverage");
            var longAverage = Average(night, "longAverage");

            // Counted over the name-night's own figures whatever the night labelled it, so the
            // two averages populations partition the names a version's first arm reaches and the
            // third is the one its second arm holds down.
            if (shortAverage is { } shortMean && longAverage is { } longMean && close < shortMean && close < longMean)
            {
                if (shortMean < longMean)
                {
                    below++;
                }
                else
                {
                    above++;
                }
            }

            if (night.GetProperty("nightsBefore").EnumerateArray().Take(1)
                .Any(one => one.GetString() == TrendState.Downtrend))
            {
                held++;
            }
        }

        var constructed = populations.GetProperty("overTheConstructedNightNights");

        Assert.Equal(
            (constructed.GetProperty("belowBothWithTheShortAverageAbove").GetInt32(),
             constructed.GetProperty("belowBothWithTheShortAverageBelow").GetInt32(),
             constructed.GetProperty("leftADowntrendWithinTheHold").GetInt32()),
            (above, below, held));

        // The same three over the fixture's own night, each nought, which is the derivation the
        // expectation states for no version moving a plan there.
        var overTheFixture = populations.GetProperty("onTheFixtureNight");

        Assert.Equal(
            (0, 0, 0),
            (overTheFixture.GetProperty("belowBothWithTheShortAverageAbove").GetInt32(),
             overTheFixture.GetProperty("belowBothWithTheShortAverageBelow").GetInt32(),
             overTheFixture.GetProperty("leftADowntrendWithinTheHold").GetInt32()));

        Assert.All(
            expectation.GetProperty("overTheFixtureNights").GetProperty("nights").EnumerateArray()
                .SelectMany(one => one.GetProperty("names").EnumerateArray()),
            name => Assert.False(name.GetProperty("belowBoth").GetBoolean()));
    }

    [Fact]
    public void AVersionOfTheTrendRuleOnlyEverTakesASetupAwayAndNeverAddsOne()
    {
        var expectation = Expected("trend-versions");
        var versions = Versions(expectation);

        // The property the paired record rests on: a version writes the label that carries no
        // tranche or leaves the night's own, so its setups are the live rule's less the ones it
        // removes and every outcome is one the store already holds. A version that could turn a
        // downtrend into something else would produce a plan nothing has scored.
        foreach (var night in expectation.GetProperty("labels").EnumerateArray())
        {
            var stored = night.GetProperty("stored").GetString()!;
            var before = night.GetProperty("nightsBefore").EnumerateArray().Select(one => one.GetString()!).ToArray();

            foreach (var (_, rules) in versions)
            {
                var applied = TrendSeries.Applied(
                    stored, night.GetProperty("close").GetDecimal(), Average(night, "shortAverage"),
                    Average(night, "longAverage"), before, rules);

                Assert.True(
                    applied == stored || applied == TrendState.Downtrend,
                    $"A version labelled a name {applied} where the night stored {stored}, which is neither the stored label nor a downtrend.");

                Assert.False(
                    stored == TrendState.Downtrend && applied != TrendState.Downtrend,
                    "A version took a name out of a downtrend, which would add a plan nothing has scored.");
            }
        }
    }

    [Fact]
    public async Task TheLiveValuesReplayEveryStoredPlanAndNoVersionMovesOneOverTheFixture()
    {
        var expectation = Expected("trend-versions").GetProperty("overTheFixtureNights");

        using var store = await FixtureExpectations.WithListings();

        var on = Query(store, "SELECT MAX(as_of) FROM ladder;").Single();
        var session = DateOnly.ParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");

        connection.Open();

        var moved = 0;
        var read = 0;

        foreach (var ticker in Query(store, $"SELECT ticker FROM ladder WHERE as_of = '{on}' ORDER BY ticker;"))
        {
            var inputs = await RuleVersionScorer.InputsAsync(connection, ticker, session, default);

            Assert.NotNull(inputs);

            read++;

            var stored = Query(store, $"SELECT plan FROM ladder WHERE ticker = '{ticker}' AND as_of = '{on}';").Single();

            // The live window's own parameters, which is the rule the night ran.
            var live = RuleVersionScorer.Replayed(
                Row(RuleVersions.Live, RuleVersionScorer.LiveParameters(LadderRules.TrendRule)),
                inputs!.Value);

            Assert.Equal(Tranches(stored), Tranches(live));

            foreach (var version in TheTrendVersions.All)
            {
                var replayed = RuleVersionScorer.Replayed(Row(version.Version, version.Rules.AsParameters), inputs.Value);

                if (Tranches(replayed) != Tranches(live))
                {
                    moved++;
                }
            }
        }

        Assert.Equal(4, read);
        Assert.Equal(expectation.GetProperty("movedByAnyVersion").GetInt32(), moved);

        // The derivation the expectation states, re-read off the bars the fixture commits: no
        // name of the four closes below both of its averages, which is why no version moves a
        // plan there and why the cases each version turns on are constructed.
        foreach (var night in expectation.GetProperty("nights").EnumerateArray())
        {
            foreach (var name in night.GetProperty("names").EnumerateArray())
            {
                Assert.False(
                    name.GetProperty("close").GetDouble() < name.GetProperty("shortAverage").GetDouble()
                    && name.GetProperty("close").GetDouble() < name.GetProperty("longAverage").GetDouble(),
                    $"{name.GetProperty("ticker").GetString()} closes below both averages, which the expectation says no name of the four does.");

                Assert.False(name.GetProperty("belowBoth").GetBoolean());
            }
        }
    }

    [Fact]
    public void APairedDifferenceIsTheVersionsBlockLessTheLiveRulesAndItsPValueIsTheOneWorkedByHand()
    {
        var expectation = Expected("trend-versions").GetProperty("pairedDifference");
        var blocks = expectation.GetProperty("blocks").GetInt32();
        var bar = expectation.GetProperty("nullWin").GetDouble();
        var opened = First;

        var live = new List<CandidateSetup>();
        var version = new List<CandidateSetup>();

        for (var block = 0; block < blocks; block++)
        {
            // One win the version keeps and one loss it takes away, in every block.
            var session = Sessions(block * Blocks.Sessions);

            live.Add(new CandidateSetup(session, ForwardReturnSeries.Win, bar, bar, 0.4, 1, 1, false));
            live.Add(new CandidateSetup(session, ForwardReturnSeries.Loss, bar, bar, 0.4, -1, 1, false));
            version.Add(live[^2]);
        }

        var night = Sessions(blocks * Blocks.Sessions + Blocks.Sessions);
        var differences = VersionRecord.Differences(live, version, opened, night);

        Assert.Equal(blocks, differences.Count);
        Assert.All(differences, difference => Assert.Equal(expectation.GetProperty("differencePerBlock").GetDouble(), difference, 9));

        var record = VersionRecord.For("a version", live, version, opened, night, ReasonVerdict.Significance);

        Assert.Equal(blocks, record.Blocks);
        Assert.Equal(blocks * 2, record.LiveSetups);
        Assert.Equal(blocks, record.VersionSetups);
        Assert.Equal(expectation.GetProperty("pValue").GetDouble(), record.PValue!.Value, 9);
        Assert.Equal(expectation.GetProperty("verdict").GetString(), record.Verdict);

        // Below the floor nothing is read at all, whatever the blocks say.
        var underTheFloor = VersionRecord.For("a version", live.Take(4).ToArray(), version.Take(2).ToArray(), opened, night, ReasonVerdict.Significance);

        Assert.Equal(VersionRecord.BelowTheFloor, underTheFloor.Verdict);
        Assert.Null(underTheFloor.PValue);
    }

    [Fact]
    public void TheBestOfSeveralVersionsIsReadAgainstTheBenchmarkAndNeverAtItsOwnPValue()
    {
        var expectation = Expected("trend-versions").GetProperty("realityCheck");

        var challengers = expectation.GetProperty("challengers").EnumerateArray()
            .Select(one => Measured(
                one.GetProperty("version").GetString()!,
                [.. one.GetProperty("differences").EnumerateArray().Select(value => value.GetDouble())]))
            .ToArray();

        var over = RealityCheck.Over(challengers);

        Assert.NotNull(over);
        Assert.Equal(expectation.GetProperty("best").GetString(), over!.Best);
        Assert.Equal(expectation.GetProperty("statistic").GetDouble(), over.Statistic, 3);
        Assert.Equal(expectation.GetProperty("pValue").GetDouble(), over.PValue, 9);
        Assert.Equal(challengers.Length, over.Challengers);

        // The whole of the reason it exists: the best of several is read above its own figure.
        foreach (var one in expectation.GetProperty("challengers").EnumerateArray())
        {
            var own = SignFlip.PValue([.. one.GetProperty("differences").EnumerateArray().Select(value => value.GetDouble())]);

            Assert.Equal(one.GetProperty("ownPValue").GetDouble(), own, 9);
        }

        Assert.True(
            over.PValue > challengers.Min(one => SignFlip.PValue(one.Differences)),
            "The check came back at or below the best version's own p-value, so picking the best of several cost nothing.");

        // A version below the floor is not read against the benchmark at all.
        Assert.Null(RealityCheck.Over([Measured("short", [1, 2, 1])]));
    }

    [Fact]
    public void TwoVersionsThatBothCrossKeepTheNarrowerUnlessTheWiderIsAheadByTheMargin()
    {
        var wider = Crossing("wider", 9);
        var narrower = Crossing("narrower", 3);

        Assert.Equal(VersionRecord.Crossed, wider.Verdict);
        Assert.Equal(VersionRecord.Crossed, narrower.Verdict);

        // The margin is in points of win share, and it is read at the margin itself.
        Assert.Equal("narrower", VersionRecord.Kept(wider with { Excess = 6 }, narrower with { Excess = 3 }));
        Assert.Equal("wider", VersionRecord.Kept(wider with { Excess = 8 }, narrower with { Excess = 3 }));
        Assert.Equal("wider", VersionRecord.Kept(wider with { Excess = 3 }, narrower with { Excess = 3, Verdict = VersionRecord.NotCrossed }));
        Assert.Null(VersionRecord.Kept(
            wider with { Verdict = VersionRecord.NotCrossed },
            narrower with { Verdict = VersionRecord.NotCrossed }));

        Assert.Equal(5d, VersionRecord.MarginInPoints);
    }

    [Fact]
    public void TheThreeVersionsAreTheOnesTheCodeOffersAndEachOpensAtTheNumbersTheSpecPins()
    {
        var expectation = Expected("trend-versions").GetProperty("versions").EnumerateArray().ToArray();

        Assert.Equal(expectation.Length, TheTrendVersions.All.Count);

        foreach (var one in expectation)
        {
            var version = TheTrendVersions.Named(one.GetProperty("version").GetString()!);

            Assert.NotNull(version);

            foreach (var parameter in one.GetProperty("parameters").EnumerateObject())
            {
                Assert.Equal(
                    (parameter.Name, parameter.Value.GetDouble()),
                    (parameter.Name, version!.Rules.AsParameters[parameter.Name]));
            }

            // A version at the live rule's own values would store the live rule's plans under a
            // version's name, and the open refuses it.
            Assert.Null(RuleVersionScorer.ParameterRefusal(LadderRules.TrendRule, version!.Version, version.Rules.AsParameters));
        }

        Assert.NotNull(RuleVersionScorer.ParameterRefusal(
            LadderRules.TrendRule,
            "a version at the live values",
            TrendRuleSet.Live.AsParameters));

        // The rule is one of the five the register carries, and its versions replay the ladder
        // stage alone, since the label selects the plan's shape and none of its arithmetic.
        Assert.Contains(LadderRules.TrendRule, LadderRules.All);
        Assert.False(LadderRules.ReplaysLevels(LadderRules.TrendRule));
        Assert.False(LadderRules.ReadsMembers(LadderRules.TrendRule));
    }

    [Fact]
    public void TheFullestRegisterProjectsTheNightAtTheFigureTheCapWasSetFrom()
    {
        var expectation = Expected("trend-versions").GetProperty("theCapAtOnce");

        Assert.Equal(RuleVersions.MostAtOnce, expectation.GetProperty("mostAtOnce").GetInt32());
        Assert.Equal(RuleVersions.MostAtOnce, LadderRules.All.Sum(RuleVersions.MostFor));

        // The fullest register the caps admit, each rule at its cap with its live window among
        // them, and what it adds at the stage durations the night measured.
        var fullest = LadderRules.All
            .SelectMany(rule => Enumerable.Range(0, RuleVersions.MostFor(rule)).Select(at => Row(
                at == 0 ? RuleVersions.Live : FormattableString.Invariant($"v{at}"),
                RuleVersionScorer.LiveParameters(rule)) with { Rule = rule }))
            .ToArray();

        Assert.Equal(RuleVersions.MostAtOnce, fullest.Length);

        var levelStage = 143d;
        var ladderStage = 5d;
        var added = RuleVersions.ProjectedSeconds(fullest, levelStage, ladderStage);

        Assert.Equal(expectation.GetProperty("replayedMergeDistance").GetInt32(), fullest.Count(row => row.Version != RuleVersions.Live && LadderRules.ReplaysLevels(row.Rule)));
        Assert.Equal(expectation.GetProperty("replayedOthers").GetInt32(), fullest.Count(row => row.Version != RuleVersions.Live && !LadderRules.ReplaysLevels(row.Rule)));
        Assert.Equal(expectation.GetProperty("addedSeconds").GetDouble(), added, 6);
        Assert.Equal(expectation.GetProperty("addedSeconds").GetDouble(), RuleVersions.WorstCaseSeconds(levelStage, ladderStage), 6);

        var night = expectation.GetProperty("nightSeconds").GetDouble();

        Assert.Equal(night, 495 + added, 6);
        Assert.True(night < expectation.GetProperty("deadlineSeconds").GetDouble());
    }

    [Fact]
    public void TheVersionsRegionStatesWhatEachLabelledAndHowOftenALabelReturned()
    {
        var expectation = Expected("trend-versions");

        var open = new[]
        {
            new OpenVersionRow(RuleVersions.Live, "{}", new DateOnly(2026, 9, 1)),
            new OpenVersionRow(TheTrendVersions.BelowBothAverages, "{\"downtrendFromAverages\": 1}", new DateOnly(2026, 9, 1)),
        };

        var labels = new[]
        {
            new VersionLabelRow(TheTrendVersions.BelowBothAverages, TrendState.Downtrend, 12, 7),
            new VersionLabelRow(TheTrendVersions.BelowBothAverages, TrendState.Range, 88, 0),
        };

        var region = RunScreen.TrendVersions(
            open,
            labels,
            [new VersionLabelRow(string.Empty, TrendState.Downtrend, 5, 0), new VersionLabelRow(string.Empty, TrendState.Range, 95, 0)],
            [],
            new LabelReturns(3523, 210, 35, 51),
            10,
            new DateOnly(2026, 9, 8));

        // The live window is not a version and draws no row of its own.
        Assert.Equal([TheTrendVersions.BelowBothAverages], region.Versions.Select(row => row.Version));
        Assert.Equal(7, region.Versions[0].Moved);
        Assert.Equal(expectation.GetProperty("theCapAtOnce").GetProperty("mostAtOnce").GetInt32(), region.MostAtOnce);

        var drawn = new MarkRenderer().TrendVersions(region);

        Assert.Contains("data-flipped=\"210\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-returned-next=\"35\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-returned-within-two=\"51\"", drawn, StringComparison.Ordinal);
        Assert.Contains("12 downtrend", drawn, StringComparison.Ordinal);
        Assert.Contains("data-moved=\"7\"", drawn, StringComparison.Ordinal);

        // No name reaches it, here or anywhere else: a version is a rule being measured.
        Assert.DoesNotContain("AAPL", drawn, StringComparison.Ordinal);
    }

    static readonly DateOnly First = new(2025, 1, 2);

    // The session that many exchange sessions after the first, which is what a block is counted in.
    static DateOnly Sessions(int sessions)
    {
        var at = First;

        for (var counted = 0; counted < sessions;)
        {
            at = at.AddDays(1);

            if (EquityBrief.Core.Bars.ExchangeClosures.IsSession(at))
            {
                counted++;
            }
        }

        return at;
    }

    static VersionMeasured Measured(string version, IReadOnlyList<double> differences) =>
        new(version, differences.Count, Blocks.Floor, 0, 0, null, null, null, null, VersionRecord.NotCrossed, differences);

    static VersionMeasured Crossing(string version, double excess) =>
        new(version, Blocks.Floor, Blocks.Floor, 16, 8, excess, double.PositiveInfinity, 1d / 256, 0, VersionRecord.Crossed,
            [.. Enumerable.Repeat(0.5, Blocks.Floor)]);

    static RuleVersionRow Row(string version, IReadOnlyDictionary<string, double> parameters) =>
        new(
            LadderRules.TrendRule,
            version,
            RuleVersions.Write(parameters),
            RuleVersions.Hash(parameters, RuleVersionScorer.CodeVersion),
            RuleVersionScorer.CodeVersion,
            NightStart,
            null,
            null);

    static decimal? Average(JsonElement night, string name) =>
        night.GetProperty(name).ValueKind == JsonValueKind.Null ? null : night.GetProperty(name).GetDecimal();

    static IReadOnlyDictionary<string, TrendRuleSet> Versions(JsonElement expectation) =>
        expectation.GetProperty("versions").EnumerateArray().ToDictionary(
            one => Key(one.GetProperty("version").GetString()!),
            one => new TrendRuleSet(
                (int)one.GetProperty("parameters").GetProperty(TrendSeries.DowntrendFromAverages).GetDouble(),
                (int)one.GetProperty("parameters").GetProperty(TrendSeries.NightsTheNewLabelHolds).GetDouble()),
            StringComparer.Ordinal);

    // The key a name-night states each version's label under, which is the version's name in the
    // form the expectation writes a property in.
    static string Key(string version) =>
        string.Concat(version.Split(' ').Select((word, at) => at == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    static string Tranches(string plan) =>
        JsonDocument.Parse(plan).RootElement.TryGetProperty("tranches", out var tranches) ? tranches.GetRawText() : "[]";

    static IReadOnlyList<string> Query(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        var rows = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    static JsonElement Expected(string stage) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Repository.Root, "fixtures", "membership-2026-09-05", "expectations", stage + ".json"))).RootElement;
}
