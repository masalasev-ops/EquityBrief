using EquityBrief.Core.Configuration;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from 20.9: the idea generator. Each label the local model answers is turned by code into a
// condition at its tenth's upper edge over a fold's own listings, worked by hand and shown to be the level winners
// against losers cuts the reading at; an answer outside the grammar is refused by each of its causes; a fold's prompt is
// made from its own tables alone, worked by hand, a listing or a trade ending in its year or after moving nothing in it;
// the k-th proposal is the k-th label each fold's stored answer holds, a fold with no answer choosing none, and every
// proposal is judged with its family's others by one step-down; over a constructed store the generator asks each prompt
// once under the pin, stores each answer or its cause, asks nothing again on a run again and refuses where the pin does
// not match; and the monthly run puts each index's ideas before its tester runs.
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
public partial class FixtureExpectations
{
    // The rows 20.9 adds that this check reaches: section 18's answer the generator cannot read and its step that cannot
    // ask the local model.
    internal static readonly string[] IdeaGeneratorClaims =
    [
        CheckReach.Key(Scope.FailureTable, "An answer the idea generator cannot read, or one the runtime refuses"),
        CheckReach.Key(Scope.FailureTable, "The idea generator cannot ask the local model in the monthly run"),
    ];

    // Twenty listings reading 1 to 20 at a reading of the catalogue, those above 14 winning a risk and the rest losing one,
    // each on a session of its own, and the two values scaled.
    static ConditionUnit[] TwentyUnits(double scale = 1) =>
        [.. Enumerable.Range(1, 20).Select(value => new ConditionUnit(value, value > 14 ? 1.0 : -1.0, OneReading(0, value * scale)))];

    [Fact]
    public void EachLabelIsTurnedIntoItsConditionAtItsTenthsUpperEdgeOverTheFoldsOwnListings()
    {
        var column = LedgerReadings.All[0].Column;

        // The twenty, and two listings holding no value of the reading that won five risks each, which no level reads.
        ConditionUnit[] units = [.. TwentyUnits(), new ConditionUnit(21, 5, OneReading(0, null)), new ConditionUnit(22, 5, OneReading(0, null))];

        // Above the seventh tenth's upper edge: the 14th of the 20 by its nearest rank, 14, keeping 14 to 20, six winners
        // and the one loser at 14, a mean of five sevenths of a risk.
        var above = IdeaPrompt.Condition(new IdeaLabel(column, true, 7), units)!;

        Assert.Equal((0, true, 14.0, 7), (above.Conditions[0].Reading, above.Conditions[0].Above, above.Conditions[0].Level, above.Kept));
        Assert.Equal(5.0 / 7, above.Score, 12);
        Assert.Equal($"also requires {column} at or above 14", ConditionProcedures.ChangeOf(above));
        Assert.Equal(14.0, above.Parameters()[$"also_{column}_above"]);

        // Below the third tenth's upper edge: the sixth of the 20, 6, keeping 1 to 6, each a loser.
        var below = IdeaPrompt.Condition(new IdeaLabel(column, false, 3), units)!;

        Assert.Equal((false, 6.0, 6, -1.0), (below.Conditions[0].Above, below.Conditions[0].Level, below.Kept, below.Score));

        // At every tenth the level is where winners against losers cuts the reading.
        double[] values = [.. Enumerable.Range(1, 20).Select(value => (double)value)];

        for (var tenth = 1; tenth <= 9; tenth++)
        {
            Assert.Equal(ConditionSearch.Quantile(values, ConditionSearch.Cuts[tenth - 1]), IdeaPrompt.Condition(new IdeaLabel(column, tenth % 2 == 0, tenth), units)!.Conditions[0].Level);
        }

        // A reading no listing holds gives no condition.
        Assert.Null(IdeaPrompt.Condition(new IdeaLabel(LedgerReadings.All[1].Column, true, 5), units));
    }

    [Fact]
    public void AnAnswerOutsideTheGrammarIsRefusedWithItsCause()
    {
        var column = LedgerReadings.All[0].Column;

        static string Idea(string reading, string side, string tenth) => "{\"reading\": " + reading + ", \"side\": " + side + ", \"tenth\": " + tenth + "}";

        static string Ideas(params string[] ideas) => "{\"ideas\": [" + string.Join(", ", ideas) + "]}";

        var good = Idea($"\"{column}\"", "\"above\"", "7");

        // Two ideas read in the answer's order, a key it adds passed over, and a list of none read as no idea.
        var read = IdeaPrompt.Read("{\"ideas\": [" + good + ", " + Idea($"\"{column}\"", "\"below\"", "2") + "], \"why\": \"a key the answer adds\"}", "stop");

        Assert.Equal([new IdeaLabel(column, true, 7), new IdeaLabel(column, false, 2)], read.Labels);
        Assert.Null(read.Cause);
        Assert.Empty(IdeaPrompt.Read(Ideas(), "stop").Labels!);

        // Each answer outside the grammar refused with its cause, each a cause read off the answer and asked once more.
        foreach (var (answer, finish, cause) in new (string?, string, string)[]
        {
            (Ideas(good), "length", ReleaseAnswers.CutOff),
            ("  ", "stop", ReleaseAnswers.Empty),
            (null, "stop", ReleaseAnswers.Empty),
            ("```json\n" + Ideas(good) + "\n```", "stop", ReleaseAnswers.NotOneObject),
            ("[" + good + "]", "stop", ReleaseAnswers.NotOneObject),
            ("{\"idea\": []}", "stop", IdeaPrompt.NoIdeas),
            ("{\"ideas\": \"none\"}", "stop", IdeaPrompt.NoIdeas),
            (Ideas(good, good, good, good), "stop", IdeaPrompt.TooMany),
            (Ideas(Idea("\"no_such_reading\"", "\"above\"", "7")), "stop", IdeaPrompt.ReadingNotListed),
            (Ideas(Idea("7", "\"above\"", "7")), "stop", IdeaPrompt.ReadingNotListed),
            (Ideas("\"a reading\""), "stop", IdeaPrompt.ReadingNotListed),
            (Ideas(Idea($"\"{column}\"", "\"Above\"", "7")), "stop", IdeaPrompt.SideNotListed),
            (Ideas("{\"reading\": \"" + column + "\", \"tenth\": 7}"), "stop", IdeaPrompt.SideNotListed),
            (Ideas(Idea($"\"{column}\"", "\"above\"", "0")), "stop", IdeaPrompt.TenthOutside),
            (Ideas(Idea($"\"{column}\"", "\"above\"", "10")), "stop", IdeaPrompt.TenthOutside),
            (Ideas(Idea($"\"{column}\"", "\"above\"", "7.5")), "stop", IdeaPrompt.TenthOutside),
            (Ideas(Idea($"\"{column}\"", "\"above\"", "\"7\"")), "stop", IdeaPrompt.TenthOutside),
        })
        {
            var refused = IdeaPrompt.Read(answer, finish);

            Assert.Null(refused.Labels);
            Assert.Equal(cause, refused.Cause);
            Assert.Contains(cause, IdeaPrompt.AnswerCauses);
        }

        // Every cause the generator stores is one of the stated ones, the runtime's own two among them.
        Assert.Equal([.. IdeaPrompt.AnswerCauses, ReleaseAnswers.RuntimeRefused, ReleaseAnswers.NotAnsweredInTime], IdeaPrompt.Causes);
    }

    [Fact]
    public void AFoldsPromptIsMadeFromItsOwnTablesAloneWithNoTickerAndNoDate()
    {
        var calendar = Weekdays(new DateOnly(2021, 1, 4), new DateOnly(2023, 3, 10));
        var folds = LoopFolds.Of(calendar, calendar[^1]);
        var column = LedgerReadings.All[0].Column;

        int At(int year, int month, int day) => Array.IndexOf(calendar, new DateOnly(year, month, day));

        Assert.Equal([2022, 2023], folds.Select(fold => fold.Year));

        // Four listings whose trades ended in 2021, reading 1 to 4 and losing a risk, losing half, winning half and
        // winning one; one entered in 2021 whose trade ended in 2022; one entered and ended in 2022; and one still open.
        List<RuleListing> listings =
        [
            new(0, 10, At(2021, 3, 1), At(2021, 3, 10), -1),
            new(1, 20, At(2021, 5, 3), At(2021, 5, 12), -0.5),
            new(2, 30, At(2021, 7, 1), At(2021, 7, 12), 0.5),
            new(3, 40, At(2021, 9, 1), At(2021, 9, 10), 1),
            new(4, 50, At(2021, 12, 20), At(2022, 1, 5), 2),
            new(5, 60, At(2022, 3, 1), At(2022, 3, 10), 3),
            new(6, 70, At(2022, 12, 1), null, null),
        ];
        double[] values = [1, 2, 3, 4, 10, 20, 30];

        // A trade stopped in 2021 after closing a risk up first and peaking two sessions after its buy, and one its target
        // ended in 2022.
        List<(DateOnly Ended, TradePath Path)> finished =
        [
            (new DateOnly(2021, 3, 10), new TradePath([10, 11, 12, 9], 10, 1, SetupEnds.Stop)),
            (new DateOnly(2022, 3, 10), new TradePath([10, 13, 14], 10, 2, SetupEnds.Target)),
        ];

        IdeaProcedures.FoldAsk Ask(LoopFold fold) => IdeaProcedures.Ask("drift", fold, calendar, listings, at => OneReading(0, values[at]), finished);

        // The 2022 fold's prompt worked by hand: the autopsy over the one trade that ended before 2022, and the reading over
        // the four listings that did, the winners' median 3.5, the losers' 1.5, and the mean edge of each tenth of four
        // listings, a tenth holding none stated as none; every other reading holds no listing and is left out.
        var first = Ask(folds[0]);

        Assert.Equal(
            "Rule: the drift\n\n" +
            "How its finished trades went:\n" +
            "- the median trade's best close was 2.00 risks above its buy before it ended, over 1 trades\n" +
            "- its worst close 1.00 risks under its buy, over 1 trades\n" +
            "- its best close came a median 2 sessions after the buy, over 1 trades\n" +
            "- 1 of the 1 trades the stop ended had first closed a risk up, over 1 trades\n" +
            "- no trade ended at its target, over 0 trades\n\n" +
            "Each reading over the listings whose trades had finished, a winner one whose edge was above nothing:\n" +
            "reading | listings | winners | losers | winners' median | losers' median | mean edge by tenth, lowest first\n" +
            $"{column} | 4 | 2 | 2 | 3.5 | 1.5 | none none -1 none -0.5 none none 0.5 none 1\n",
            first.Prompt);
        Assert.Equal(IdeaPrompt.Key(first.Prompt), first.Key);
        Assert.Equal(4, first.Units.Count);

        // A listing and a trade that ended in 2022 or later, changed, move nothing in the 2022 fold's prompt, and move the
        // 2023 fold's, which learns on them.
        var second = Ask(folds[1]);

        listings[4] = listings[4] with { Edge = -2 };
        listings[5] = listings[5] with { Edge = -3 };
        values[4] = 11;
        finished[1] = (finished[1].Ended, new TradePath([10, 8, 14], 10, 2, SetupEnds.Target));

        Assert.Equal(first.Key, Ask(folds[0]).Key);
        Assert.NotEqual(second.Key, Ask(folds[1]).Key);
        Assert.Contains($"{column} | 6 | 2 | 4 |", Ask(folds[1]).Prompt, StringComparison.Ordinal);

        // A listing that ended before 2022, changed, moves it.
        listings[0] = listings[0] with { Edge = 1 };

        Assert.NotEqual(first.Key, Ask(folds[0]).Key);

        // No prompt carries a date or a year of the calendar.
        Assert.DoesNotMatch(@"\b20\d\d\b", Ask(folds[0]).Prompt + Ask(folds[1]).Prompt);
    }

    [Fact]
    public void TheKthProposalIsTheKthLabelOfEachFoldsStoredAnswerAndEveryProposalIsJudgedWithItsFamilysOthers()
    {
        var calendar = LoopCalendar();
        var folds = LoopFolds.Of(calendar, calendar[^1]);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<FiledIncome>>(), calendar, 0, folds);
        var column = LedgerReadings.All[0].Column;

        Assert.Equal([2022, 2023, 2024], folds.Select(fold => fold.Year));

        // The 2022 fold learned on the twenty, the 2023 fold on the twenty at twice their values, the 2024 fold's prompt
        // holds no stored answer, and all finished data is the twenty again.
        IdeaProcedures.FoldAsk[] asks =
        [
            new(folds[0], "the 2022 tables", "k2022", TwentyUnits()),
            new(folds[1], "the 2023 tables", "k2023", TwentyUnits(2)),
            new(folds[2], "the 2024 tables", "k2024", TwentyUnits()),
            new(read.AllFinished, "every finished table", "kall", TwentyUnits()),
        ];
        var stored = new Dictionary<string, IReadOnlyList<IdeaLabel>>(StringComparer.Ordinal)
        {
            ["k2022"] = [new(column, true, 7), new(column, false, 3)],
            ["k2023"] = [new(column, true, 7)],
            ["kall"] = [new(column, true, 7), new(column, false, 3), new(column, true, 9)],
        };

        // The rule as it stands earns nothing on every session of the test years; a condition kept above its level earns
        // half a risk a session, and one kept under it six tenths of a risk in every other block and loses as much in
        // the blocks between.
        var (opens, newest) = (read.Opens, read.Newest);
        (int Entry, double? Edge)[] own = [.. Enumerable.Range(opens, newest - opens + 1).Select(at => (at, (double?)0))];
        var walked = new List<string>();

        IReadOnlyList<(int Entry, double? Edge)> UnitsUnder(ConditionFound condition)
        {
            walked.Add(condition.Words());

            return [.. own.Select(one => (one.Entry, (double?)(condition.Conditions[0].Above ? 0.5 : (one.Entry - opens) / 63 % 2 == 0 ? 0.6 : -0.6)))];
        }

        var ideas = IdeaProcedures.Proposals(read, "drift", "the drift as it stands", 10, asks, key => stored.GetValueOrDefault(key), UnitsUnder, own);

        Assert.Equal(["the idea generator, ranked 1", "the idea generator, ranked 2", "the idea generator, ranked 3"], ideas.Select(one => one.Proposal));

        // The first: each fold's first label at its own listings' edge, 14 over the twenty and 28 over the twenty doubled,
        // none where no answer is stored; all finished data names 14, and two folds named its label.
        Assert.Equal(
            [$"also requires {column} at or above 14", $"also requires {column} at or above 28", null],
            ideas[0].Folds.Select(one => one.Chosen));
        Assert.Equal((string?)$"also requires {column} at or above 14", ideas[0].Words);
        Assert.Equal(2, ideas[0].StableFolds);
        Assert.Equal(
            $"the local model named {column} above the upper edge of its tenth 7 over the 20 finished listings; {column} at or above 14 kept 7 of them at +0.714 risks a listing against the rule's -0.400",
            ideas[0].Finding);
        Assert.Equal(LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [$"also_{column}_above"] = 14 }).Json, ideas[0].Change!.Json);

        // The second: the 2022 fold's second label alone, the 2023 answer holding one; the third named by all finished data
        // alone, at 18, and chosen in no fold.
        Assert.Equal([$"also requires {column} at or under 6", null, null], ideas[1].Folds.Select(one => one.Chosen));
        Assert.Equal((1, (string?)$"also requires {column} at or under 6"), (ideas[1].StableFolds, ideas[1].Words));
        Assert.Equal([null, null, null], ideas[2].Folds.Select(one => one.Chosen));
        Assert.Equal((0, (string?)$"also requires {column} at or above 18"), (ideas[2].StableFolds, ideas[2].Words));
        Assert.Equal(["14", "28", "6"], walked.Select(words => words.Split(' ')[^1]));

        // Each fold's year is scored on the condition it chose, or on the rule as it stands where it chose none.
        Assert.All(ideas[0].Evidence.Years.Where(year => year.Year < 2024), year => Assert.True(year.ProposedTotal > 0));
        Assert.Equal(0, ideas[0].Evidence.Years.Single(year => year.Year == 2024).ProposedTotal);

        // The family's proposals are judged together by one step-down, the generator's three among them, and another
        // family's apart. The drift's other proposal is ahead in every block, so alone only the arrangement flipping no
        // sign reaches it; beside the generator's, the second's blocks, flipped to all ahead, reach it too.
        var other = new LoopProposalRead("drift", "winners against losers, ranked 1", "also requires a reading", "the drift as it stands", LoopProcedures.Risks, 10, [.. folds.Select(fold => (fold, (string?)"the change"))], 3,
            LoopProcedures.Evidence(folds, calendar, [.. folds.Select(_ => (IReadOnlyList<(int Entry, double? Edge)>?)[.. own.Select(one => (one.Entry, (double?)((one.Entry - opens) / 63 % 3 == 0 ? 0.9 : 0.05)))])], own));
        var elsewhere = other with { Family = "breakout" };
        var judged = WalkForwardTester.Judge(read, [other, .. ideas, elsewhere]);
        var together = LoopJudge.Judge([other.Evidence, .. ideas.Select(one => one.Evidence)], opens, newest, 10);
        var alone = LoopJudge.Judge([other.Evidence], opens, newest, 10);

        Assert.Equal(together.Select(verdict => verdict.Adjusted), judged.Take(4).Select(one => one.Verdict.Adjusted));
        Assert.Equal(alone[0].Adjusted, judged[4].Verdict.Adjusted);
        Assert.NotEqual(alone[0].Adjusted, together[0].Adjusted);
    }

    [Fact]
    public async Task TheGeneratorAsksEachPromptOnceUnderThePinStoresWhatItWasAnsweredAndARunAgainAsksNothing()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var column = LedgerReadings.All[0].Column;

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // Four prompts, the fourth the first's prompt again from another rule's fold: the first answered at once, the second
        // answering what cannot be read and then an idea, and the third refused by the runtime.
        IdeaAsk[] asks =
        [
            new("drift", 2022, "prompt A", "ka"),
            new("drift", 2023, "prompt B", "kb"),
            new("breakout", 2022, "prompt C", "kc"),
            new("breakout", 2023, "prompt A", "ka"),
        ];
        var idea = "{\"ideas\": [{\"reading\": \"" + column + "\", \"side\": \"above\", \"tenth\": 7}]}";
        var times = new Dictionary<string, int>(StringComparer.Ordinal);
        var runtime = AnsweringRuntime(loaded: false, chat: _ => throw new InvalidOperationException("the answers are the test's own"));
        var feed = new IdeaAnsweringFeed(ReadingFeedOver(runtime, settings), request =>
        {
            var asked = times[request.Text] = times.GetValueOrDefault(request.Text) + 1;

            return request.Text switch
            {
                "prompt A" => Reply(idea),
                "prompt B" when asked == 1 => Reply("I would keep the stronger listings."),
                "prompt B" => Reply(idea),
                _ => throw new ReadingRefused(ReleaseAnswers.RuntimeRefused, "the runtime refused the prompt"),
            };
        });
        var said = new StringWriter();
        var outcome = await new IdeaGenerator(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync("MID", asks, said);

        Assert.Equal((IdeaGenerator.KindOf("MID"), LocalRun.Finished, 2), (outcome.Kind, outcome.Outcome, outcome.Read));
        Assert.Equal(1, outcome.Refused[ReleaseAnswers.RuntimeRefused]);
        Assert.Equal(["prompt A", "prompt B", "prompt B", "prompt C"], feed.Asked.Select(request => request.Text));
        Assert.All(feed.Asked, request => Assert.Equal((Gemma, IdeaPrompt.Instruction, ReadingSettings.Seed, IdeaPrompt.AnswerTokens), (request.Model, request.Instruction, request.Seed, request.AnswerTokens)));
        Assert.Contains(runtime.Asked, request => request.StartsWith("POST /api/v1/models/load", StringComparison.Ordinal));
        Assert.Contains(runtime.Asked, request => request.StartsWith("POST /api/v1/models/unload", StringComparison.Ordinal));
        Assert.Contains("loop-ideas: ideas MID came to finished; 2 prompt(s) answered and 1 refused", said.ToString(), StringComparison.Ordinal);

        // Each prompt stored once under the pin, the second's stored answer the one read, and the run's one row under the
        // local run's stage counting its four calls.
        Assert.Equal(
            ["ka|1|MID|drift|2022|prompt A|answered||" + idea, "kb|1|MID|drift|2023|prompt B|answered||" + idea, "kc|1|MID|breakout|2022|prompt C|refused|" + ReleaseAnswers.RuntimeRefused + "|"],
            Query(store, "SELECT prompt_key || '|' || pin || '|' || index_code || '|' || family || '|' || year || '|' || prompt || '|' || outcome || '|' || COALESCE(cause, '') || '|' || COALESCE(answer, '') FROM loop_idea ORDER BY prompt_key;"));
        Assert.Equal([IdeaPrompt.Version], Query(store, "SELECT DISTINCT version FROM loop_idea;"));
        Assert.Equal(
            ["local-run|finished|3|4"],
            Query(store, "SELECT stage || '|' || outcome || '|' || rows_written || '|' || model_calls FROM run_log WHERE run_id LIKE 'local-run-%-ideas-MID-%';"));

        // The tester reads each prompt's answer through the one reader, the refused one holding none.
        await using (var connection = store.Open())
        {
            var newest = await LoopIdeas.NewestAsync(connection);

            Assert.Equal([new IdeaLabel(column, true, 7)], IdeaPrompt.Read(newest["ka"].Answer, null).Labels);
            Assert.Equal((LoopIdeas.Refused, (string?)ReleaseAnswers.RuntimeRefused), (newest["kc"].Outcome, newest["kc"].Cause));
        }

        // A run again asks nothing, stores nothing and says every prompt is answered.
        var again = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("nothing is asked")), settings), _ => throw new InvalidOperationException("nothing is asked"));
        var second = await new IdeaGenerator(again, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync("MID", asks, TextWriter.Null);

        Assert.Equal((LocalRun.NothingToRead, 0), (second.Outcome, again.Requests));
        Assert.Equal(3L, Scalar(store, "SELECT COUNT(*) FROM loop_idea;"));
    }

    [Fact]
    public async Task APinThatDoesNotMatchRefusesTheGeneratorAndAnAnswerUnderAnotherRuntimeEndsIt()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var idea = "{\"ideas\": [{\"reading\": \"" + LedgerReadings.All[0].Column + "\", \"side\": \"below\", \"tenth\": 4}]}";

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        IdeaAsk[] asks = [new("drift", 2022, "prompt D", "kd"), new("drift", 2023, "prompt E", "ke")];

        // The profile names another model than the pin's: refused before anything is asked, naming the model.
        var otherModel = new LocalModelSettings("qwen-3.5", "http://127.0.0.1:1234/v1/", Qwen, 30, 50176, 30) { ModelFile = ModelsFolder.File, ModelsFolder = models.Root, KnowledgeCutoff = new DateOnly(2025, 1, 31) };
        var unasked = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("nothing is asked")), otherModel), _ => throw new InvalidOperationException("nothing is asked"));
        var refused = await new IdeaGenerator(unasked, otherModel, clock, store.DatabaseFile, store.Root, NoWait).RunAsync("SML", asks, TextWriter.Null);

        Assert.Equal((LocalRun.PinRefused, "the model is not pin 1's", 0), (refused.Outcome, refused.Why, unasked.Requests));
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM loop_idea;"));

        // The second prompt answered under another runtime's version: the run ends refused, keeping the first answer, which
        // the pin's runtime gave.
        var feed = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the answers are the test's own")), settings), request =>
            request.Text == "prompt D" ? Reply(idea) : Reply(idea) with { RuntimeVersion = "2.57.0" });
        var broken = await new IdeaGenerator(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync("SML", asks, TextWriter.Null);

        Assert.Equal(LocalRun.PinRefused, broken.Outcome);
        Assert.Contains("an answer came from the runtime the pin does not name", broken.Why, StringComparison.Ordinal);
        Assert.Equal(["kd"], Query(store, "SELECT prompt_key FROM loop_idea;"));
        Assert.Equal(
            ["pin refused|0", "pin refused|1"],
            Query(store, "SELECT outcome || '|' || rows_written FROM run_log WHERE run_id LIKE 'local-run-%-ideas-SML-%' ORDER BY rowid;"));
    }

    [Fact]
    public async Task TheTestersReadForTheGeneratorRefusesAsItsRunRefusesUnderTheGeneratorsVerb()
    {
        using var store = new TemporaryStore().Migrated();
        var output = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var tester = new WalkForwardTester(clock, store.DatabaseFile, store.Root, output, NoWait);

        // An index no run reads, and a night holding the store, each put no prompt and say so under the generator's verb.
        Assert.Null(await tester.IdeaAsksAsync("NDX"));
        Assert.Contains("loop-ideas: name an index with '--index', GSPC, MID or SML", output.ToString(), StringComparison.Ordinal);

        using (NightLock.Take(store.Root, "night-20261106T230000Z"))
        {
            Assert.Null(await tester.IdeaAsksAsync("MID"));
        }

        Assert.Contains("loop-ideas: the night holds the store (night-20261106T230000Z)", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("loop-test:", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMonthlyRunsStepsAreBuiltInTheOrderItsNamesState()
    {
        // From 20.9 each index's ideas come after which readings go with winners and before its tester runs, and the
        // worker builds its steps in the order the names state, read off its own source.
        Assert.Equal(
            ["facts", "history", "check-GSPC", "check-MID", "check-SML", "links-GSPC", "links-MID", "links-SML", "ideas-GSPC", "ideas-MID", "ideas-SML", "test-GSPC", "test-MID", "test-SML", "rules-GSPC", "rules-MID", "rules-SML", "report"],
            MonthlyRun.StepNames);

        var source = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Program.cs"));
        var month = source[source.IndexOf("static async Task<int> LoopMonthRun", StringComparison.Ordinal)..];

        month = month[..month.IndexOf("\n}", StringComparison.Ordinal)];

        string[] built = ["new(\"facts\"", "\"history\",", "new(\"check-\" + index", "\"links-\" + index", "\"ideas-\" + index", "\"test-\" + index", "\"rules-\" + index", "new(\"report\""];
        var places = built.Select(literal => month.IndexOf(literal, StringComparison.Ordinal)).ToArray();

        Assert.All(places, place => Assert.True(place >= 0));
        Assert.Equal(places.Order(), places);
        Assert.Equal(
            ["facts", "history", "check", "links", "ideas", "test", "rules", "report"],
            MonthlyRun.StepNames.Select(name => name.Split('-')[0]).Distinct());
    }

    static DateOnly[] Weekdays(DateOnly from, DateOnly through)
    {
        var days = new List<DateOnly>();

        for (var day = from; day <= through; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return [.. days];
    }

    // A feed whose list, load and unload go to the scripted runtime and whose answers the test gives by the request,
    // keeping every request it was asked.
    sealed class IdeaAnsweringFeed(LmStudioReadingFeed inner, Func<ReadingRequest, ReadingReply> answer) : IReadingModelFeed
    {
        public List<ReadingRequest> Asked { get; } = [];

        public int Requests => Asked.Count;

        public Task<bool> PrepareAsync(CancellationToken cancellation = default) => inner.PrepareAsync(cancellation);

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default) => inner.ReleaseAsync(loadedHere, cancellation);

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);

            return Task.FromResult(answer(request));
        }
    }
}
