using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Judge;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.24: the AI stock picker within each index. The best quarter of the marked, rounded up and at most 5,
// worked by hand on both sides of each bound, none marked picking none; a constructed index's figures placed among its own
// members alone; round two over more sheets than one question holds; "a fixed 5" asking nothing of its own; a short night
// running its versions in the stated order and writing one it cannot hold "not run"; an index with no passed naming test
// of its own members refused alone; the picker asking the local model alone; and no source a rule pins among those 24.24
// writes.
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
public partial class FixtureExpectations
{
    // The rows of sections 17 and 18 24.24 adds that this check reaches.
    internal static readonly string[] JudgeIndexClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The AI stock picker within each index"),
        CheckReach.Key(Scope.FailureTable, "A night the AI stock picker marks no stock of an index, or a version the night cannot hold"),
    ];

    // Every row 24.24 adds: the two claims above and the picker page's four rows.
    internal static string[] JudgeIndexRows => [.. JudgeIndexClaims, .. Reading.ReadSurface.PickerIndexRows];

    const string PickerNight = "2026-11-19";

    // A night of 120 S&P 500 members and 30 S&P 600 members, each sheet holding the facts a pick names, every member closing
    // at 100 with a typical move of 2; a reading pin the model file matches; and a passed naming test of the S&P 500's
    // members alone, or of both where asked.
    static TemporaryStore WithinStore(bool namedSmall = false)
    {
        var store = new TemporaryStore().Migrated();
        var members = Enumerable.Range(0, 120).Select(at => ("GSPC", FormattableString.Invariant($"T{at:000}")))
            .Concat(Enumerable.Range(0, 30).Select(at => ("SML", FormattableString.Invariant($"S{at:000}"))))
            .ToArray();

        store.Execute("INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES "
            + string.Join(", ", members.Select(member => $"('{member.Item1}', '{PickerNight}', '{member.Item2}', '{JudgeFacts.Version}', '{{}}', 'row of member {member.Item2}', 'sheet of member {member.Item2}\n{PickSheet}', '2026-11-20T01:00:00Z', 'r')")) + ";");

        foreach (var index in namedSmall ? new[] { "GSPC", "SML" } : ["GSPC"])
        {
            var naming = new[] { "row", "sheet" }.SelectMany(round => Enumerable.Range(0, 100).Select(at => FormattableString.Invariant(
                $"('naming-{index}', '{PickerNight}', '{JudgeFacts.Version}', '{round}', '{index}', 'N{at:000}', 7, 'answered', '{{}}', 0, 'the model', '2026-11-20T00:30:00Z')")));

            store.Execute("INSERT INTO judge_naming (run_id, session_date, version, round, index_code, ticker, seed, outcome, answer, named, model, asked_at) VALUES " + string.Join(", ", naming) + ";");
        }

        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, run_id) "
            + "VALUES ('the model', 'm/judge.gguf', 'abc', 1000, '2026-11-01T00:00:00Z', 'lm', '1', 'p', 0, 1, 256, 8192, '2025-01-31', '2026-11-01T00:00:00Z', 'r');");
        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES "
            + string.Join(", ", members.Select(member => $"('{member.Item2}', '{PickerNight}', '99', '101', '98', '100', 1000, 'eod', '{PickerNight}T22:00:00Z')")) + ";");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES "
            + string.Join(", ", members.Select(member => $"('{member.Item2}', '{PickerNight}', 'atr14', 2.0, 14)")) + ";");

        return store;
    }

    static JudgePicker IndexPicker(TemporaryStore store, IReadingModelFeed feed, DateTimeOffset at) =>
        new(feed, new LocalModelSettings("local", "http://127.0.0.1:1234/v1/", "the model", 60, 8192, 60), FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile, store.Root, (_, _) => Task.CompletedTask, () => PickFile);

    static DateOnly IndexDay => DateOnly.ParseExact(PickerNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The first `count` of the S&P 500's members, each a target held a week with the whole at the target.
    static (string Ticker, string Hold, string Shape, string Reason)[] Targets(int count, string prefix = "T") =>
        [.. Enumerable.Range(0, count).Select(at => (FormattableString.Invariant($"{prefix}{at:000}"), "1 week", JudgeMenu.Whole, "A steady climb on rising estimates."))];

    [Fact]
    public void TheBestQuarterOfTheMarkedIsRoundedUpAndHeldToFiveAndNoneMarkedPicksNone()
    {
        // By hand: a quarter of 1 to 4 rounds up to 1, of 5 to 8 to 2, of 9 to 12 to 3, of 13 to 16 to 4, of 17 to 20 to 5,
        // and from 21 the cap holds it at 5; none marked picks none.
        Assert.Equal(
            [0, 1, 1, 2, 2, 3, 4, 5, 5, 5, 5],
            new[] { 0, 1, 4, 5, 8, 9, 16, 17, 20, 21, 400 }.Select(JudgePrompt.Quarter));

        // "A fixed 5" takes as many as were marked up to 5, the quarter the first of them.
        Assert.Equal([0, 1, 4, 5, 5, 5], new[] { 0, 1, 4, 5, 6, 400 }.Select(JudgePrompt.Ranked));
        Assert.Equal(["a", "b", "c"], Core.Judge.JudgeNight.Taken(["a", "b", "c", "d", "e"], 12, Core.Judge.JudgeNight.BestQuarter));
        Assert.Equal(["a", "b", "c", "d", "e"], Core.Judge.JudgeNight.Taken(["a", "b", "c", "d", "e"], 12, Core.Judge.JudgeNight.FixedFive));

        // The card's words, the operator's own.
        Assert.Equal("It rated 12 of the S&P 600's stocks worth buying tonight and picked the best quarter: 3.", Core.Judge.JudgeNight.MarkedWords("S&P 600", 12, 3));
        Assert.Equal("It rated none of the S&P 600's stocks worth buying tonight.", Core.Judge.JudgeNight.MarkedWords("S&P 600", 0, 0));
    }

    // As the local model answered on a copy of the store: round one listing a label twice, read once; round two ranking more
    // than it was asked, its first read in order; and fewer than asked, refused and asked once more.
    [Fact]
    public void ALabelMarkedTwiceIsReadOnceAndARankingLongerThanAskedKeepsItsFirst()
    {
        string[] labels = ["Amber Bell", "Blue Kite", "Coral Lamp"];

        var twice = JudgePrompt.ReadScreen("""{"marked":["Coral Lamp","Amber Bell","Coral Lamp"]}""", labels);

        Assert.Equal(["Amber Bell", "Coral Lamp"], twice.Marked);
        Assert.Null(twice.Cause);
        Assert.Equal("a label not shown", JudgePrompt.ReadScreen("""{"marked":["Gray Stone"]}""", labels).Cause);

        string Pick(string label) => $$"""{"label":"{{label}}","hold":"1 week","take_profit":"{{JudgeMenu.Whole}}","reason":"Calm","facts":[]}""";

        var longer = JudgePrompt.ReadPick($$"""{"picks":[{{Pick("Coral Lamp")}},{{Pick("Amber Bell")}},{{Pick("Blue Kite")}}]}""", labels, 2);

        Assert.Equal(["Coral Lamp", "Amber Bell"], longer.Picks.Select(pick => pick.Label));
        Assert.Null(longer.Cause);
        Assert.Equal("1 picks where 2 were asked", JudgePrompt.ReadPick($$"""{"picks":[{{Pick("Coral Lamp")}}]}""", labels, 2).Cause);
    }

    [Fact]
    public async Task AnIndexOfTwelveMarkedPicksItsBestQuarterAndAFixedFiveTakesTheRankingsFirstFiveAskingNothing()
    {
        using var store = WithinStore();

        Assert.Equal([$"GSPC|120|{PickerNight}|{JudgeFacts.Version}", $"SML|30|{PickerNight}|{JudgeFacts.Version}"], FamilyRows(store, "SELECT index_code || '|' || COUNT(*) || '|' || session_date || '|' || version FROM judge_fact GROUP BY index_code ORDER BY index_code;"));

        var feed = new PickFeed(Targets(12));
        var night = await IndexPicker(store, feed, PickEvening).RunAsync(IndexDay, PickOpen, "index-twelve");
        var large = night.Indices.Single(index => index.Index == "GSPC");

        // 120 members in batches of 50, 50 and 20 marking the 12 targets; one question of round two ranking 5, of which the
        // best quarter of 12, 3, is the version's and all 5 the fixed 5's; four questions in all, none for the variant.
        Assert.Equal((JudgePicker.Picked, 120, 12, 4, (string?)null), (large.Outcome, large.Screened, large.Kept, large.Asks, large.Why));
        Assert.Equal(4, feed.Requests);
        Assert.Equal([("T000", true), ("T001", true), ("T002", true), ("T003", false), ("T004", false)], large.Picks.Select(pick => (pick.Ticker, pick.InQuarter)));
        Assert.Equal(["1|T000|1", "2|T001|1", "3|T002|1", "4|T003|0", "5|T004|0"], FamilyRows(store, "SELECT rank || '|' || ticker || '|' || in_quarter FROM judge_pick ORDER BY rank;"));
        Assert.Equal(["4"], FamilyRows(store, "SELECT COUNT(*) FROM judge_ask WHERE run_id = 'index-twelve-500';"));

        // The S&P 600 holds no passed naming test of its own members, so it is refused alone and asked nothing.
        var small = night.Indices.Single(index => index.Index == "SML");

        Assert.Equal(JudgePicker.Refused, small.Outcome);
        Assert.Contains("no naming test of the S&P 600's members has run", small.Why, StringComparison.Ordinal);
        Assert.Empty(FamilyRows(store, "SELECT run_id FROM judge_ask WHERE run_id LIKE '%-600';"));

        // The night's one run log row names each index's run.
        var detail = System.Text.Json.Nodes.JsonNode.Parse(Assert.Single(FamilyRows(store, $"SELECT detail FROM run_log WHERE stage = '{JudgePicker.Stage}';")))!;

        Assert.Equal(
            ["GSPC|picked|12|3", "MID|refused|0|0", "SML|refused|0|0"],
            detail["indices"]!.AsArray().Select(index => $"{index!["index"]}|{index["outcome"]}|{index["marked"]}|{index["quarter"]}"));
    }

    [Fact]
    public async Task AnIndexWhoseNamingTestPassedPicksAmongItsOwnMembersAloneAndNoneMarkedPicksNone()
    {
        using var store = WithinStore(namedSmall: true);

        // The S&P 600's 30 members in one batch, none marked: no question of round two, none picked; the S&P 500's two
        // targets marked and ranked, the best quarter of 2 the first.
        var feed = new PickFeed(Targets(2));
        var night = await IndexPicker(store, feed, PickEvening).RunAsync(IndexDay, PickOpen, "index-none");
        var small = night.Indices.Single(index => index.Index == "SML");

        Assert.Equal((JudgePicker.NonePicked, 30, 0, 1, JudgePicker.NoneMarked), (small.Outcome, small.Screened, small.Kept, small.Asks, small.Why));
        Assert.Empty(small.Picks);
        Assert.Equal(["none|30|0|1"], FamilyRows(store, "SELECT outcome || '|' || screened || '|' || kept || '|' || asks FROM judge_run WHERE index_code = 'SML';"));

        // Each question showed one index's members alone and named that index.
        Assert.All(feed.Texts.Where(text => text.Contains("row of member S", StringComparison.Ordinal)), text =>
        {
            Assert.DoesNotContain("row of member T", text, StringComparison.Ordinal);
            Assert.StartsWith(JudgePrompt.AmongLine("S&P 600"), text, StringComparison.Ordinal);
        });
        Assert.Equal(["T000"], night.Indices.Single(index => index.Index == "GSPC").Picks.Where(pick => pick.InQuarter).Select(pick => pick.Ticker));
    }

    [Fact]
    public async Task RoundTwoOverMoreSheetsThanOneQuestionHoldsRanksEachGroupAndThenTheirBestHeldToFive()
    {
        using var store = WithinStore();
        var feed = new PickFeed(Targets(40));
        var night = await IndexPicker(store, feed, PickEvening).RunAsync(IndexDay, PickOpen, "index-forty");
        var large = night.Indices.Single(index => index.Index == "GSPC");

        // By hand: 40 marked, more than the 16 one question shows, so three questions of 14, 13 and 13 each rank 5 and a
        // fourth ranks the best 5 of those 15; the quarter of 40, 10, is held to 5, so the version and the fixed 5 take the
        // same five.
        Assert.Equal((40, 3 + 4), (large.Kept, large.Asks));
        Assert.Equal([14, 13, 13, 15], feed.Texts.Where(text => text.Contains("Rank ", StringComparison.Ordinal)).Select(text => Regex.Matches(text, "^sheet of member ", RegexOptions.Multiline).Count));
        Assert.Equal(["T000", "T001", "T002", "T003", "T004"], large.Picks.Select(pick => pick.Ticker));
        Assert.All(large.Picks, pick => Assert.True(pick.InQuarter));
        Assert.Equal([2, 2], JudgePicker.Even([1, 2, 3, 4], 2).Select(group => group.Count));
        Assert.Equal([9, 8], JudgePicker.Even([.. Enumerable.Range(0, 17)], JudgePrompt.SheetsPerAsk).Select(group => group.Count));
    }

    [Fact]
    public async Task AShortNightRunsItsVersionsInTheStatedOrderAndWritesOneItCannotHoldNotRun()
    {
        // The order, by hand: each index's running version first, the S&P 500's, 400's and 600's; then the one each most
        // recently replaced; then the rest in the order they were made.
        IReadOnlyList<JudgeSlot> slots =
        [
            new("SML", JudgeStanding.Paper, 2), new("SML", JudgeStanding.Running, 0), new("GSPC", JudgeStanding.Replaced, 0),
            new("MID", JudgeStanding.Running, 0), new("GSPC", JudgeStanding.Running, 0), new("GSPC", JudgeStanding.Paper, 1),
        ];

        Assert.Equal(
            ["GSPC running 0", "MID running 0", "SML running 0", "GSPC replaced 0", "GSPC paper 1", "SML paper 2"],
            Core.Judge.JudgeNight.Order(slots).Select(slot => $"{slot.Index} {JudgePicker.Standing(slot.Standing)} {slot.Made}"));

        // A night past the off-peak run's first slot: the running version runs, and the paper version is written not run and
        // asked nothing.
        using var store = WithinStore();
        var feed = new PickFeed(Targets(2));
        var holds = Core.Judge.JudgeNight.HoldsUntil(IndexDay);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 4, 5, 0, TimeSpan.Zero), holds);

        var night = await IndexPicker(store, feed, holds).RunAsync(IndexDay, PickOpen, "index-short", [new("GSPC", JudgeStanding.Paper, 1), new("GSPC", JudgeStanding.Running, 0)], holds);

        Assert.Equal(["index-short-500|picked|running", $"index-short-500-paper-1|{JudgePicker.NotRun}|paper"], night.Indices.Select(index => $"{index.Run}|{index.Outcome}|{FamilyRows(store, $"SELECT standing FROM judge_run WHERE run_id = '{index.Run}';").Single()}"));
        Assert.Equal(4, feed.Requests);
        Assert.Empty(FamilyRows(store, "SELECT run_id FROM judge_ask WHERE run_id = 'index-short-500-paper-1';"));
        Assert.True(Core.Judge.JudgeNight.MayStart(new("GSPC", JudgeStanding.Paper, 1), holds.AddMinutes(-1), holds));
        Assert.False(Core.Judge.JudgeNight.MayStart(new("GSPC", JudgeStanding.Paper, 1), holds, holds));
        Assert.True(Core.Judge.JudgeNight.MayStart(new("GSPC", JudgeStanding.Running, 0), holds, holds));
    }

    [Fact]
    public async Task TheNamingTestOfAnIndexDrawsItsOwnMembersAloneAndPassesForThatIndexAlone()
    {
        using var store = WithinStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 20, 2, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var settings = new LocalModelSettings("local", "http://127.0.0.1:1234/v1/", "the model", 60, 8192, 60);

        // The S&P 600's 30 members are fewer than the 100 a test draws, so its test stops short and passes nothing.
        var small = await new JudgeNamingTest(new NamingFeed(new HashSet<string>(), new HashSet<string>()), settings, clock, store.DatabaseFile, store.Root, (_, _) => Task.CompletedTask)
            .RunAsync(IndexDay, 7, clock.UtcNow.AddHours(6), "naming-small", "SML");

        Assert.False(small.Passed);
        Assert.Contains("on the S&P 600 under field list", small.Stopped, StringComparison.Ordinal);

        var large = await new JudgeNamingTest(new NamingFeed(new HashSet<string>(), new HashSet<string>()), settings, clock, store.DatabaseFile, store.Root, (_, _) => Task.CompletedTask)
            .RunAsync(IndexDay, 7, clock.UtcNow.AddHours(6), "naming-large", "GSPC");

        Assert.True(large.Passed);
        Assert.Equal(["GSPC"], FamilyRows(store, "SELECT DISTINCT index_code FROM judge_naming WHERE run_id = 'naming-large';"));
        Assert.All(FamilyRows(store, "SELECT ticker FROM judge_naming WHERE run_id = 'naming-large';"), ticker => Assert.StartsWith("T", ticker, StringComparison.Ordinal));

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}");
        await connection.OpenAsync();

        Assert.True((await JudgeNamingTest.MayStartAsync(connection, JudgeFacts.Version, "GSPC")).May);
        Assert.False((await JudgeNamingTest.MayStartAsync(connection, JudgeFacts.Version, "MID")).May);
    }

    [Fact]
    public void AConstructedIndexsFiguresArePlacedAmongItsOwnMembersAlone()
    {
        // Four S&P 500 members' returns of 1 to 4 and one S&P 600 member's of 100, by hand: 1 has none below and itself,
        // half of one in four, an eighth, read up to the 2nd tenth; 2 has one below and half, three eighths, the 4th; 3 five
        // eighths, the 7th; 4 seven eighths, the 9th; and the S&P 600 member alone stands in the middle, the 5th, the S&P
        // 500's figures moving none of it.
        JudgeFact Fact(string index, string ticker) => new(index, ticker, IndexDay, new Dictionary<string, string>(StringComparer.Ordinal) { ["return_month"] = "x", ["sector"] = "Energy" });

        IReadOnlyDictionary<string, double?> Figure(double value) => new Dictionary<string, double?>(StringComparer.Ordinal) { ["return_month"] = value };

        var placed = JudgeFacts.PlacedAmongTheirIndex(
        [
            (Fact("GSPC", "A"), Figure(1)), (Fact("GSPC", "B"), Figure(2)), (Fact("SML", "S"), Figure(100)),
            (Fact("GSPC", "C"), Figure(3)), (Fact("GSPC", "D"), Figure(4)),
        ]);

        Assert.Equal(["A 2", "B 4", "C 7", "D 9", "S 5"], placed.OrderBy(fact => fact.Ticker, StringComparer.Ordinal).Select(fact => $"{fact.Ticker} {fact.Tenths["return_month"]}"));
        Assert.All(placed, fact => Assert.False(fact.Tenths.ContainsKey("sector")));
        Assert.Equal("x (tenth 2 of 10)", placed.Single(fact => fact.Ticker == "A").Placed("return_month"));

        // Four equal figures each stand in the middle, half of four below, the 5th; a figure no member holds is not placed.
        Assert.Equal(5, JudgeFacts.TenthAmong(7, [7, 7, 7, 7]));
        Assert.Null(JudgeFacts.TenthAmong(7, []));

        // The highest of 20 has 19 below and half of itself, 39 fortieths, the 10th; the lowest a fortieth, the 1st.
        Assert.Equal((10, 1), (JudgeFacts.TenthAmong(20, [.. Enumerable.Range(1, 20).Select(at => (double)at)]), JudgeFacts.TenthAmong(1, [.. Enumerable.Range(1, 20).Select(at => (double)at)])));
    }

    [Fact]
    public async Task AReadNightsFiguresArePlacedAmongTheirOwnIndexsMembers()
    {
        using var store = JudgeStore();

        // A second S&P 500 member returning 10% over the month, and an S&P 600 member returning 50%, which moves none of the
        // S&P 500's places.
        store.Execute(
            "INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES "
            + $"('GSPC', '{JudgeNight}', 'AAB', '{JudgeReadings(("return_month", 0.10))}', 'pin'), "
            + $"('SML', '{JudgeNight}', 'BBC', '{JudgeReadings(("return_month", 0.50))}', 'pin');");

        var facts = await JudgeFactReader.ReadAsync(store.DatabaseFile, DateOnly.ParseExact(JudgeNight, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        // By hand: AAA's 5.4% is the lower of the S&P 500's two, half of one of two, a quarter, the 3rd tenth; AAB's the
        // higher, three quarters, the 8th; and BBC alone among the S&P 600's holding a figure, the 5th.
        Assert.Equal(["AAA 3", "AAB 8", "BBC 5"], facts.Where(fact => fact.Tenths.ContainsKey("return_month")).OrderBy(fact => fact.Ticker, StringComparer.Ordinal).Select(fact => $"{fact.Ticker} {fact.Tenths["return_month"]}"));
        Assert.False(facts.Single(fact => fact.Ticker == "BBB").Tenths.ContainsKey("return_month"));
    }

    [Fact]
    public void ThePickerAndItsNamingTestAskTheLocalModelAloneAndNoPaidFeed()
    {
        Assert.Equal([Feed.LocalModel], JudgePicker.Access.Feeds);
        Assert.Equal([Feed.LocalModel], JudgeNamingTest.Access.Feeds);
        Assert.Empty(JudgeFactWriter.Access.Feeds);

        var sources = string.Concat(JudgeIndexSources.Select(path => SourceStatements.WithoutComments(File.ReadAllText(Path.Combine(Repository.Root, path)))));

        Assert.DoesNotContain("IResearchModelFeed", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("Anthropic", sources, StringComparison.Ordinal);
    }

    // The shipped sources 24.24 writes outside their comments.
    static readonly string[] JudgeIndexSources =
    [
        "src/EquityBrief.Core/Judge/JudgeFacts.cs",
        "src/EquityBrief.Core/Judge/JudgeNight.cs",
        "src/EquityBrief.Core/Judge/JudgePrompt.cs",
        "src/EquityBrief.Worker/Judge/JudgeFactReader.cs",
        "src/EquityBrief.Worker/Judge/JudgeFactWriter.cs",
        "src/EquityBrief.Worker/Judge/JudgeNamingTest.cs",
        "src/EquityBrief.Worker/Judge/JudgePicker.cs",
        "src/EquityBrief.Worker/Judge/JudgeReplayRun.cs",
        "src/EquityBrief.Api/Reading/JudgeScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Judge.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Judge.cs",
    ];

    // No source a rule's version pins is one 24.24 writes outside its comments, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneThePickerWithinEachIndexWrites()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(JudgeIndexSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(JudgeIndexSources.Intersect(pinned, StringComparer.Ordinal));
    }
}
