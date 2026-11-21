using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Judge;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.4: the AI judge's picks of a constructed night through recorded answers, worked by hand into
// ranked picks with each price; each menu choice priced from a close and from a fill, a choice off the menu refused; a
// reason holding a digit or naming its company refused and asked once more; a night reaching the next open, or still
// waiting on the local model's lock at it, stored as missed with no pick; a run under another prompt than its pin
// refused; the night constructing no reader of the judge's tables; and no source a rule pins among those 24.4 writes.
// see: The AI judge picks straight after the night under the local model's lock, its picks stored before the next open or shown as missed
public partial class FixtureExpectations
{
    // The night's step that starts the judge, as section 14 states it.
    internal const string JudgeStep =
        "From 24.4, start the AI judge, a process of its own that waits until the night has finished, writes the night's facts and picks up " +
        "to five for its own account under the local model's lock, its calls on its own row and never the night's, and stores them before " +
        "the next open or writes the night as missed; a night run again for an earlier session starts none (see: The AI judge picks straight " +
        "after the night under the local model's lock, its picks stored before the next open or shown as missed).";

    // The rows of section 17 and 18 24.4 adds that this check reaches.
    internal static readonly string[] JudgePickClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The AI judge's menu"),
        CheckReach.Key(Scope.FailureTable, "A night the AI judge does not store before the next open, or a question it cannot read"),
        CheckReach.Key(Scope.FailureTable, "An AI judge run under another model, file, window or prompt than its pin"),
    ];

    // From 24.14 section 17's row of the facts a pick names and section 18's row of a pick refused for them.
    internal static readonly string[] JudgeNamedClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The facts an AI judge pick names"),
        CheckReach.Key(Scope.FailureTable, "An AI judge pick naming no fact or one its sheet lacks"),
    ];

    // Every row 24.14 adds: the two claims above and the judge's page's two rows.
    internal static string[] JudgeNamedRows => [.. JudgeNamedClaims, .. Reading.ReadSurface.JudgeNamedRows];

    // Every row 24.4 adds: the picker's catalogue and matrix rows, its four stores, section 14's step, section 17's row and
    // section 18's two rows.
    internal static string[] JudgePickRows =>
        [.. ComponentAccess.JudgePickerRows, .. SchemaColumns.JudgePickStoreRows, CheckReach.Key(NightlyRunSteps.Heading, JudgeStep), .. JudgePickClaims];

    const string PickNight = "2026-11-19";

    // From 24.14 each member's sheet shows four facts, the business score not on file, and a pick names three of the rest.
    const string PickSheet = "Return over 1 month: +5%\nMomentum (RSI): 61 of 100\nVolume against its own average: +40%\nBusiness score: not on file";

    static readonly string[] PickFacts = ["Return over 1 month", "Momentum (RSI)", "Volume against its own average"];

    static readonly ModelFileFacts PickFile = new("m/judge.gguf", 1000, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

    // Answers as a recorded model gives them: round one keeps a target where its batch shows one, and round two picks the
    // targets shown in the order given, each with its hold, its take-profit and its reason.
    sealed class PickFeed(IReadOnlyList<(string Ticker, string Hold, string Shape, string Reason)> targets, Queue<string>? pickAnswers = null) : IReadingModelFeed
    {
        public int Requests { get; private set; }

        public Task<bool> PrepareAsync(CancellationToken cancellation = default) => Task.FromResult(false);

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default) => Task.CompletedTask;

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            Requests++;

            string answer;

            if (request.Instruction == JudgePrompt.ScreenSystem)
            {
                var kept = Regex.Matches(request.Text, @"^(?<label>[A-Za-z]+ [A-Za-z]+): row of member (?<ticker>T[0-9]+)$", RegexOptions.Multiline)
                    .FirstOrDefault(match => targets.Any(target => target.Ticker == match.Groups["ticker"].Value));

                answer = JsonSerializer.Serialize(new { keep = kept?.Groups["label"].Value ?? JudgePrompt.None });
            }
            else if (pickAnswers is { Count: > 0 })
            {
                answer = pickAnswers.Dequeue();
            }
            else
            {
                var shown = Regex.Matches(request.Text, @"^(?<label>[A-Za-z]+ [A-Za-z]+)\nsheet of member (?<ticker>T[0-9]+)$", RegexOptions.Multiline)
                    .ToDictionary(match => match.Groups["ticker"].Value, match => match.Groups["label"].Value);

                answer = JsonSerializer.Serialize(new
                {
                    picks = targets.Where(target => shown.ContainsKey(target.Ticker))
                        .Select(target => new Dictionary<string, object> { ["label"] = shown[target.Ticker], ["hold"] = target.Hold, ["take_profit"] = target.Shape, ["reason"] = target.Reason, ["facts"] = PickFacts }),
                });
            }

            return Task.FromResult(new ReadingReply("the model", answer, "stop", 10, 5, 0, "lm", "1", 0.1));
        }
    }

    // A night of 120 members' facts under the field list's version, a passed naming test of that version, a reading pin
    // the model file matches, and T007 closing at 100 with a typical move of 2 and T093 at 50 with one of 1.
    static TemporaryStore PickStore()
    {
        var store = new TemporaryStore().Migrated();
        var facts = Enumerable.Range(0, 120).Select(at => FormattableString.Invariant(
            $"('GSPC', '{PickNight}', 'T{at:000}', '{JudgeFacts.Version}', '{{}}', 'row of member T{at:000}', 'sheet of member T{at:000}\n{PickSheet}', '2026-11-20T01:00:00Z', 'r')"));

        store.Execute("INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES " + string.Join(", ", facts) + ";");

        var naming = new[] { "row", "sheet" }.SelectMany(round => Enumerable.Range(0, 100).Select(at => FormattableString.Invariant(
            $"('naming', '{PickNight}', '{JudgeFacts.Version}', '{round}', 'GSPC', 'T{at:000}', 7, 'answered', '{{}}', 0, 'the model', '2026-11-20T00:30:00Z')")));

        store.Execute("INSERT INTO judge_naming (run_id, session_date, version, round, index_code, ticker, seed, outcome, answer, named, model, asked_at) VALUES " + string.Join(", ", naming) + ";");
        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, run_id) "
            + "VALUES ('the model', 'm/judge.gguf', 'abc', 1000, '2026-11-01T00:00:00Z', 'lm', '1', 'p', 0, 1, 256, 8192, '2025-01-31', '2026-11-01T00:00:00Z', 'r');");
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES "
            + $"('T007', '{PickNight}', '99', '101', '98', '100', 1000, 'eod', '{PickNight}T22:00:00Z'), "
            + $"('T093', '{PickNight}', '49', '51', '48', '50', 1000, 'eod', '{PickNight}T22:00:00Z');");
        store.Execute(
            "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES "
            + $"('T007', '{PickNight}', 'atr14', 2.0, 14), ('T093', '{PickNight}', 'atr14', 1.0, 14);");
        store.Execute("INSERT INTO listed_name (ticker, name, read_on, run_id) VALUES ('T007', 'Tangerine Holdings', '2026-11-15', 'r');");

        return store;
    }

    static readonly DateTimeOffset PickEvening = new(2026, 11, 20, 2, 0, 0, TimeSpan.Zero);

    static readonly DateTimeOffset PickOpen = JudgePicker.NextOpen(DateOnly.ParseExact(PickNight, "yyyy-MM-dd", CultureInfo.InvariantCulture), SessionZones.ResolveSessionZone(SessionZones.UnitedStates));

    static async Task<JudgeRunOutcome> PickRun(TemporaryStore store, IReadingModelFeed feed, DateTimeOffset at, string run) =>
        await new JudgePicker(
                feed,
                new LocalModelSettings("local", "http://127.0.0.1:1234/v1/", "the model", 60, 8192, 60),
                FixedClock.At(at, SessionZones.UnitedStates),
                store.DatabaseFile,
                store.Root,
                (_, _) => Task.CompletedTask,
                () => PickFile)
            .RunAsync(DateOnly.ParseExact(PickNight, "yyyy-MM-dd", CultureInfo.InvariantCulture), PickOpen, run);

    static readonly (string Ticker, string Hold, string Shape, string Reason)[] PickTargets =
    [
        ("T093", "1 week", JudgeMenu.Whole, "Fell hard on light volume while its business readings held."),
        ("T007", "2 weeks", JudgeMenu.HalfThenGiveUp, "Rising earnings estimates and a breakout on heavy volume."),
    ];

    [Fact]
    public async Task AConstructedNightIsScreenedAndPickedThroughRecordedAnswersIntoRankedPicksWorkedByHand()
    {
        using var store = PickStore();

        var feed = new PickFeed(PickTargets);
        var outcome = await PickRun(store, feed, PickEvening, "judge-picks");

        // 120 members in batches of 50, 50 and 20: three questions in round one and one in round two, each kept member a
        // target, ranked as the answer ranks them.
        Assert.Equal((JudgePicker.Picked, 120, 2, 4, 0), (outcome.Outcome, outcome.Screened, outcome.Kept, outcome.Asks, outcome.AsksFailed));
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 14, 30, 0, TimeSpan.Zero), PickOpen);

        // By hand: T093 closes at 50 with a typical move of 1, held a week, so its stop sits 1 move under, its risk 1, its
        // stop 49 and its target 2 risks over, 52, the whole sold there; T007 closes at 100 with a move of 2, held two weeks,
        // so 1.5 moves, a risk of 3, a stop of 97, a target of 106 for the half and a give-up price 3 behind its highest close.
        Assert.Equal(
            [(1, "T093", "1 week", 50m, 49m, 52m, (decimal?)null), (2, "T007", "2 weeks", 100m, 97m, 106m, (decimal?)3m)],
            outcome.Picks.Select(pick => (pick.Rank, pick.Ticker, pick.Hold, pick.LastClose, pick.Prices.Stop, pick.Prices.TakeProfit, pick.Prices.GiveUpDistance)));

        Assert.Equal(["1|T093|49|52|", "2|T007|97|106|3"], FamilyRows(store, "SELECT rank || '|' || ticker || '|' || stop || '|' || target || '|' || COALESCE(give_up_distance, '') FROM judge_pick ORDER BY rank;"));
        Assert.Equal(["return_month,rsi,volume_ratio", "return_month,rsi,volume_ratio"], FamilyRows(store, "SELECT fields FROM judge_pick ORDER BY rank;"));
        Assert.Equal(["picked|120|2|4|0"], FamilyRows(store, "SELECT outcome || '|' || screened || '|' || kept || '|' || asks || '|' || asks_failed FROM judge_run;"));
        Assert.Equal(["4"], FamilyRows(store, "SELECT COUNT(*) FROM judge_ask WHERE outcome = 'answered';"));

        // Its first pin is written from the reading pin with the prompt's version, and the night is never filled in again.
        Assert.Equal([$"m/judge.gguf|{JudgePrompt.Version}"], FamilyRows(store, "SELECT file || '|' || prompt_version FROM judge_pin;"));

        var again = await PickRun(store, new PickFeed(PickTargets), PickEvening, "judge-picks-again");

        Assert.Equal(JudgePicker.Refused, again.Outcome);
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM judge_run;"));
    }

    [Fact]
    public void EachMenuChoicePlacesItsStopAndTakeProfitFromTheCloseAndFromAFillAndAChoiceOffTheMenuIsRefused()
    {
        // From a close of 100 with a typical move of 2: a week 1 move, two weeks 1.5, four weeks 2, three months 3.
        Assert.Equal(
            [(98m, 104m), (97m, 106m), (96m, 108m), (94m, 112m)],
            JudgeMenu.Holds.Select(hold => JudgeMenu.Prices(100m, 2.0, hold.Words, JudgeMenu.Whole)!).Select(prices => (prices.Stop, prices.TakeProfit)));

        // From a fill of 102.5, four weeks, half then the give-up price: a risk of 4, a stop of 98.5, a target of 110.5.
        Assert.Equal(new JudgePrices(102.5m, 98.5m, 110.5m, 4m), JudgeMenu.Prices(102.5m, 2.0, "4 weeks", JudgeMenu.HalfThenGiveUp));

        // A take-profit or a hold off the menu, or no typical move, prices nothing, so no such pick is stored.
        Assert.Null(JudgeMenu.Prices(100m, 2.0, "4 weeks", "sell at the close"));
        Assert.Null(JudgeMenu.Prices(100m, 2.0, "6 weeks", JudgeMenu.Whole));
        Assert.Null(JudgeMenu.Prices(100m, null, "4 weeks", JudgeMenu.Whole));
    }

    [Fact]
    public async Task AReasonHoldingADigitOrNamingItsCompanyIsRefusedAndAskedOnceMore()
    {
        var labels = new[] { "Amber Bell", "Blue Kite" };

        Assert.Equal("a reason holding a digit or none", JudgePrompt.ReadPick("""{"picks":[{"label":"Amber Bell","hold":"1 week","take_profit":"the whole at 2 times the risk","reason":"Up 12 percent"}]}""", labels).Cause);
        Assert.Equal("a choice not on the menu", JudgePrompt.ReadPick("""{"picks":[{"label":"Amber Bell","hold":"6 weeks","take_profit":"the whole at 2 times the risk","reason":"Calm"}]}""", labels).Cause);
        Assert.Equal("a label picked twice", JudgePrompt.ReadPick("""{"picks":[{"label":"Blue Kite","hold":"1 week","take_profit":"the whole at 2 times the risk","reason":"Calm"},{"label":"Blue Kite","hold":"1 week","take_profit":"the whole at 2 times the risk","reason":"Calm"}]}""", labels).Cause);
        Assert.True(JudgePrompt.NamesTheMember("Alphabet leads its peers", "GOOGL", "Alphabet Inc."));
        Assert.True(JudgePrompt.NamesTheMember("GOOGL keeps rising", "GOOGL", "Alphabet Inc."));
        Assert.False(JudgePrompt.NamesTheMember("A steady climb on rising estimates", "A", "Agilent Technologies"));

        // Round two's first answer names T007's company and is refused; the second is read, so nothing is counted failed.
        using var store = PickStore();

        var outcome = await PickRun(store, new NamingFirstFeed(PickTargets), PickEvening, "judge-named");

        Assert.Equal((JudgePicker.Picked, 5, 0), (outcome.Outcome, outcome.Asks, outcome.AsksFailed));
        Assert.Equal([$"pick|1|unreadable|{JudgePicker.NotNamed}", "pick|2|answered|"], FamilyRows(store, "SELECT round || '|' || attempt || '|' || outcome || '|' || COALESCE(cause, '') FROM judge_ask WHERE round = 'pick' ORDER BY attempt;"));
    }

    // Round two's first answer gives T007 a reason naming its company, and every later one the targets' own.
    sealed class NamingFirstFeed(IReadOnlyList<(string Ticker, string Hold, string Shape, string Reason)> targets) : IReadingModelFeed
    {
        readonly PickFeed inner = new(targets);
        readonly PickFeed naming = new([.. targets.Select(target => target.Ticker == "T007" ? target with { Reason = "Tangerine looks strong." } : target)]);
        bool namedOnce;

        public int Requests => inner.Requests + naming.Requests;

        public Task<bool> PrepareAsync(CancellationToken cancellation = default) => Task.FromResult(false);

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default) => Task.CompletedTask;

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            if (request.Instruction == JudgePrompt.PickSystem && !namedOnce)
            {
                namedOnce = true;

                return naming.AskAsync(request, cancellation);
            }

            return inner.AskAsync(request, cancellation);
        }
    }

    [Fact]
    public async Task ANightReachingTheNextOpenOrWaitingOnTheLockAtItStoresNothingAndReadsMissed()
    {
        // At the open itself the first question is never asked.
        using (var store = PickStore())
        {
            var feed = new PickFeed(PickTargets);
            var outcome = await PickRun(store, feed, PickOpen, "judge-late");

            Assert.Equal((JudgePicker.Missed, 0), (outcome.Outcome, feed.Requests));
            Assert.Empty(FamilyRows(store, "SELECT ticker FROM judge_pick;"));
            Assert.Equal(["missed"], FamilyRows(store, "SELECT outcome FROM judge_run;"));
        }

        // Another local process holding the local model's lock at the open: nothing is asked and nothing picked.
        using (var store = PickStore())
        {
            var folder = Path.Combine(store.Root, WorkerDrainLauncher.CopiesFolder);

            Directory.CreateDirectory(folder);

            using var held = new FileStream(Path.Combine(folder, LocalLocks.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            var feed = new PickFeed(PickTargets);
            var outcome = await PickRun(store, feed, PickOpen, "judge-locked");

            Assert.Equal((JudgePicker.Missed, 0), (outcome.Outcome, feed.Requests));
            Assert.Contains("lock", outcome.Why, StringComparison.Ordinal);
            Assert.Empty(FamilyRows(store, "SELECT ticker FROM judge_pick;"));
        }
    }

    [Fact]
    public async Task ARunUnderAnotherPromptOrModelThanItsPinIsRefused()
    {
        using var store = PickStore();

        store.Execute("INSERT INTO judge_pin (reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at) VALUES (1, 'the model', 'm/judge.gguf', 'abc', 1000, '2026-11-01T00:00:00Z', 8192, 'another0prompt', '2026-11-02T00:00:00Z');");

        var feed = new PickFeed(PickTargets);
        var outcome = await PickRun(store, feed, PickEvening, "judge-pinned");

        Assert.Equal((JudgePicker.Refused, "the prompt is not judge pin 1's", 0), (outcome.Outcome, outcome.Why, feed.Requests));

        store.Execute("DELETE FROM judge_run; DELETE FROM run_log;");
        store.Execute($"UPDATE judge_pin SET prompt_version = '{JudgePrompt.Version}', model = 'another model';");

        Assert.Equal("the model is not judge pin 1's", (await PickRun(store, feed, PickEvening, "judge-pinned-model")).Why);
    }

    // From 24.14, over recorded answers: a pick naming no fact, and one naming a fact its sheet shows as not on file, are each
    // refused, named on the run's row and skipped, and the pick naming three facts its sheet shows is stored with their keys.
    [Fact]
    public async Task APickNamingNoFactOrOneItsSheetLacksIsRefusedLoggedAndSkipped()
    {
        // The rules read by hand off the sheet the store writes: three facts it shows stand; none, two, one twice, a word no
        // field is and a field it shows as not on file are each refused.
        var sheet = "sheet of member T007\n" + PickSheet;

        Assert.Null(JudgePrompt.FieldsRefused(PickFacts, sheet));
        Assert.Equal(JudgePrompt.NamedNone, JudgePrompt.FieldsRefused([], sheet));
        Assert.Equal(JudgePrompt.NamedTooFewOrMany, JudgePrompt.FieldsRefused(["Return over 1 month", "Momentum (RSI)"], sheet));
        Assert.Equal(JudgePrompt.NamedTwice, JudgePrompt.FieldsRefused(["Return over 1 month", "Momentum (RSI)", "Momentum (RSI)"], sheet));
        Assert.Equal(JudgePrompt.NamedLacking, JudgePrompt.FieldsRefused(["Return over 1 month", "Momentum (RSI)", "Its share price"], sheet));
        Assert.Equal(JudgePrompt.NamedLacking, JudgePrompt.FieldsRefused(["Return over 1 month", "Momentum (RSI)", "Business score"], sheet));
        Assert.Equal(JudgePrompt.NamedLacking, JudgePrompt.FieldsRefused(["Return over 1 month", "Momentum (RSI)", "Sector"], sheet));

        // Round two picks the three targets: T093 naming none, T007 naming three its sheet shows, T050 naming the business
        // score its sheet holds none of.
        (string Ticker, string Hold, string Shape, string Reason)[] targets =
        [
            ("T093", "1 week", JudgeMenu.Whole, "Fell hard on light volume."),
            ("T007", "2 weeks", JudgeMenu.HalfThenGiveUp, "Rising estimates on heavy volume."),
            ("T050", "1 week", JudgeMenu.Whole, "A steady climb."),
        ];

        using var store = PickStore();

        store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('T050', '{PickNight}', '20', '20', '20', '20', 1000, 'eod', '{PickNight}T22:00:00Z');");
        store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('T050', '{PickNight}', 'atr14', 1.0, 14);");

        var outcome = await PickRun(store, new FactsFeed(targets), PickEvening, "judge-facts");

        Assert.Equal((JudgePicker.Picked, 0), (outcome.Outcome, outcome.AsksFailed));
        Assert.Equal([(1, "T007")], outcome.Picks.Select(pick => (pick.Rank, pick.Ticker)));
        Assert.Equal(["T007|return_month,rsi,volume_ratio"], FamilyRows(store, "SELECT ticker || '|' || fields FROM judge_pick;"));
        Assert.Equal(
            [$"refused for the facts it named, T093: {JudgePrompt.NamedNone}; T050: {JudgePrompt.NamedLacking}"],
            FamilyRows(store, "SELECT detail FROM judge_run;"));
        Assert.Contains($"T050: {JudgePrompt.NamedLacking}", FamilyRows(store, $"SELECT detail FROM run_log WHERE stage = '{JudgePicker.Stage}';").Single(), StringComparison.Ordinal);
    }

    // Round one keeps each target; round two picks them in order, T093 naming no fact, T050 the business score, and every other
    // the three facts its sheet shows.
    sealed class FactsFeed(IReadOnlyList<(string Ticker, string Hold, string Shape, string Reason)> targets) : IReadingModelFeed
    {
        readonly PickFeed screen = new(targets);

        public int Requests => screen.Requests;

        public Task<bool> PrepareAsync(CancellationToken cancellation = default) => Task.FromResult(false);

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default) => Task.CompletedTask;

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            if (request.Instruction == JudgePrompt.ScreenSystem)
            {
                return screen.AskAsync(request, cancellation);
            }

            var shown = Regex.Matches(request.Text, @"^(?<label>[A-Za-z]+ [A-Za-z]+)\nsheet of member (?<ticker>T[0-9]+)$", RegexOptions.Multiline)
                .ToDictionary(match => match.Groups["ticker"].Value, match => match.Groups["label"].Value);
            var answer = JsonSerializer.Serialize(new
            {
                picks = targets.Where(target => shown.ContainsKey(target.Ticker)).Select(target => new Dictionary<string, object>
                {
                    ["label"] = shown[target.Ticker], ["hold"] = target.Hold, ["take_profit"] = target.Shape, ["reason"] = target.Reason,
                    ["facts"] = target.Ticker switch
                    {
                        "T093" => Array.Empty<string>(),
                        "T050" => ["Return over 1 month", "Momentum (RSI)", "Business score"],
                        _ => PickFacts,
                    },
                }),
            });

            return Task.FromResult(new ReadingReply("the model", answer, "stop", 10, 5, 0, "lm", "1", 0.1));
        }
    }

    // From 24.14 a judge pin under the one earlier prompt version is followed by a new pin under this one, a new judge version;
    // a pin under any other version is refused as before.
    [Fact]
    public async Task APinUnderTheEarlierPromptMovesToANewPinAndAnyOtherIsRefused()
    {
        Assert.NotEqual(JudgePrompt.Previous, JudgePrompt.Version);

        using var store = PickStore();

        store.Execute($"INSERT INTO judge_pin (reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at) VALUES (1, 'the model', 'm/judge.gguf', 'abc', 1000, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Previous}', '2026-11-02T00:00:00Z');");

        var outcome = await PickRun(store, new PickFeed(PickTargets), PickEvening, "judge-moved");

        Assert.Equal(JudgePicker.Picked, outcome.Outcome);
        Assert.Equal([$"1|{JudgePrompt.Previous}", $"2|{JudgePrompt.Version}"], FamilyRows(store, "SELECT id || '|' || prompt_version FROM judge_pin ORDER BY id;"));
        Assert.Equal(["2"], FamilyRows(store, "SELECT pin_id FROM judge_run;"));
    }

    [Fact]
    public void TheNightStartsTheJudgeAndConstructsNoReaderOfItsTables()
    {
        var night = SourceStatements.WithoutComments(File.ReadAllText(Path.Combine(Repository.Root, "src/EquityBrief.Worker/Nightly.cs")));

        Assert.Contains("StartTheJudge()", night, StringComparison.Ordinal);
        Assert.DoesNotContain("new Judge.JudgePicker(", night, StringComparison.Ordinal);
        var named = new[] { "judge_pin", "judge_run", "judge_ask", "judge_pick", "judge_fact", "judge_naming" }
            .Where(table => night.Contains(table, StringComparison.Ordinal))
            .ToArray();

        Assert.True(named.Length == 0, $"The night names the judge's tables: {string.Join(", ", named)}.");
    }

    // The shipped sources 24.4 writes outside their comments.
    static readonly string[] JudgePickSources =
    [
        "src/EquityBrief.Api/Reading/RunScreen.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Core/Judge/JudgeMenu.cs",
        "src/EquityBrief.Core/Judge/JudgePrompt.cs",
        "src/EquityBrief.Core/Research/DrainLauncher.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Worker/Judge/JudgePicker.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Worker/Program.cs",
    ];

    // No source a rule's version pins is one 24.4 writes outside its comments, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheJudgesPicksWrite()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(JudgePickSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(JudgePickSources.Intersect(pinned, StringComparer.Ordinal));
    }
}
