using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from 20.1: the local run, pinned, checked and visible. The four questions and their words as the
// decision writes them, each word turned into a reading by code; an answer read into its words or refused with its
// cause; the fixture release's captured answer read by hand; the runtime's refusal and its answer to a model it does not
// hold read as captured; the pin refusing on each of its parts and the model's file hashed whole as its rule says; a
// release readable from the first night whose late slot starts after it was accepted, over a constructed store; and the
// local run over a constructed store and a runtime answering as the captures show, writing a pin, checking it before a
// slot, reading the stored releases twice, ending where the runtime is not running or does not hold the model, ending
// at an answer the pin did not ask for, and waiting to write while the night holds its lock.
// see: The local run is a process of its own at two slots outside the night and refuses any model file or prompt but its pin, whatever runtime answers
// see: The release reading asks four closed questions written before any release is read, and code turns each answer into a reading
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public partial class FixtureExpectations
{
    // The rows 20.1 adds that this check reaches: section 17's row and section 18's four.
    internal static readonly string[] LocalRunClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Local reading's settings"),
        CheckReach.Key(Scope.FailureTable, "LM Studio is not running when a local run starts"),
        CheckReach.Key(Scope.FailureTable, "The model the local profile names is not held"),
        CheckReach.Key(Scope.FailureTable, "The model, its file, the prompt or the window is not the pin's"),
        CheckReach.Key(Scope.FailureTable, "An answer the local run cannot read, or one the runtime refuses"),
    ];

    const string Runtime256 = "llama.cpp-win-x86_64-nvidia-cuda12-avx2";

    const string RuntimeVersion = "2.56.0";

    [Fact]
    public void TheFourQuestionsAreTheDecisionsAndCodeTurnsEachWordIntoAReading()
    {
        // The instruction is the one 20.0's sample was read under, word for word, built from the four questions.
        Assert.Equal(
            "You read a company's quarterly results release. Answer four questions about what the release itself says, " +
            "from its text alone. Reply with one JSON object and nothing else, with exactly these four keys, each holding " +
            "exactly one of its listed words: " +
            "\"results\": \"better\", \"in line\", \"worse\" or \"not stated\", for how the release describes the quarter against what was expected; " +
            "\"outlook\": \"raised\", \"kept\", \"lowered or withdrawn\" or \"none given\", for what the release does to the outlook; " +
            "\"tone\": \"confident\", \"neutral\" or \"cautious\", for the tone of management's own words; " +
            "\"demand\": \"stronger\", \"steady\", \"weaker\" or \"not stated\", for how the release describes demand or orders.",
            ReleaseQuestions.Instruction);

        // Its version, the first twelve hex characters of the SHA-256 of its bytes, worked out here with the suite's own hash.
        // From 20.3 the anonymiser's own version on a line after it, so a rule of what a release is read without moved is
        // another version, and from the 20.3 correction of 2026-10-22 the fit's on a line after that.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ReleaseQuestions.Instruction + "\n" + ReleaseAnonymiser.Version + "\n" + ReleaseFit.Version)))[..12], ReleaseQuestions.Version);

        // Each word and its reading as the decision writes them, a word saying nothing read as none and never nought.
        Assert.Equal(
            ["results better 1", "results in line 0", "results worse -1", "results not stated none",
             "outlook raised 1", "outlook kept 0", "outlook lowered or withdrawn -1", "outlook none given none",
             "tone confident 1", "tone neutral 0", "tone cautious -1",
             "demand stronger 1", "demand steady 0", "demand weaker -1", "demand not stated none"],
            ReleaseQuestions.All.SelectMany(question => question.Words.Select(word => $"{question.Key} {word.Word} {(word.Reading is { } reading ? reading.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}")));

        var answer = new ReleaseAnswer("not stated", "none given", "cautious", "steady");

        Assert.Equal(new int?[] { null, null, -1, 0 }, ReleaseQuestions.All.Select(question => answer.Reading(question.Key)));
    }

    [Fact]
    public void AnAnswerIsReadIntoItsFourWordsOrRefusedWithItsCause()
    {
        const string Whole = "{\"results\": \"better\", \"outlook\": \"kept\", \"tone\": \"neutral\", \"demand\": \"weaker\", \"note\": \"ignored\"}";

        Assert.Equal(new ReleaseAnswer("better", "kept", "neutral", "weaker"), ReleaseAnswers.Read(Whole, "stop").Answer);

        // An answer stopped at its budget is cut off whatever it holds; an empty one, one that is no object and one that is
        // an array are refused, as are a missing key, a key holding no word, and a word its question does not list,
        // written in another case among them.
        Assert.Equal(ReleaseAnswers.CutOff, ReleaseAnswers.Read(Whole, ReleaseAnswers.BudgetReached).Cause);
        Assert.Equal(ReleaseAnswers.Empty, ReleaseAnswers.Read("  ", "stop").Cause);
        Assert.Equal(ReleaseAnswers.Empty, ReleaseAnswers.Read(null, "stop").Cause);
        Assert.Equal(ReleaseAnswers.NotOneObject, ReleaseAnswers.Read("better, raised, confident, stronger", "stop").Cause);
        Assert.Equal(ReleaseAnswers.NotOneObject, ReleaseAnswers.Read("[\"better\"]", "stop").Cause);
        Assert.Equal(ReleaseAnswers.MissingAnAnswer, ReleaseAnswers.Read("{\"results\": \"better\", \"outlook\": \"kept\", \"tone\": \"neutral\"}", "stop").Cause);
        Assert.Equal(ReleaseAnswers.MissingAnAnswer, ReleaseAnswers.Read("{\"results\": \"better\", \"outlook\": \"kept\", \"tone\": 1, \"demand\": \"weaker\"}", "stop").Cause);
        Assert.Equal(ReleaseAnswers.WordNotListed, ReleaseAnswers.Read("{\"results\": \"In line\", \"outlook\": \"kept\", \"tone\": \"neutral\", \"demand\": \"weaker\"}", "stop").Cause);
        Assert.Equal(ReleaseAnswers.WordNotListed, ReleaseAnswers.Read("{\"results\": \"better\", \"outlook\": \"withdrawn\", \"tone\": \"neutral\", \"demand\": \"weaker\"}", "stop").Cause);

        // The causes are the decision's, in its order.
        Assert.Equal(
            ["empty", "cut off", "not one object", "missing an answer", "a word not listed", "the runtime refused", "not answered in time", "not the pin's model"],
            ReleaseAnswers.Causes);
    }

    [Fact]
    public async Task TheFixtureReleasesRecordedAnswerIsReadByHand()
    {
        var expected = Expected("local-run");

        Assert.Equal("local-run", expected.GetProperty("stage").GetString());
        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Contains("read by hand", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);
        Assert.Equal(["source_document"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()));

        var reply = LmStudioReadingFeed.Parse(File.ReadAllText(Path.Combine(Folder(), expected.GetProperty("capture").GetString()!)), 2.44);
        var read = ReleaseAnswers.Read(reply.Content, reply.FinishReason);

        Assert.Equal(expected.GetProperty("model").GetString(), reply.Model);
        Assert.Equal((expected.GetProperty("runtime").GetString(), expected.GetProperty("runtimeVersion").GetString()), (reply.RuntimeName, reply.RuntimeVersion));
        Assert.Equal((expected.GetProperty("promptTokens").GetInt32(), expected.GetProperty("completionTokens").GetInt32(), 0), (reply.PromptTokens, reply.CompletionTokens, reply.ReasoningTokens));
        Assert.Equal("stop", reply.FinishReason);
        Assert.Null(read.Cause);
        Assert.Equal(expected.GetProperty("words").EnumerateArray().Select(word => word.GetString()), ReleaseQuestions.All.Select(question => read.Answer!.Word(question.Key)));
        Assert.Equal(expected.GetProperty("readings").EnumerateArray().Select(reading => (int?)reading.GetInt32()), ReleaseQuestions.All.Select(question => read.Answer!.Reading(question.Key)));

        // The text the capture was asked over is the fixture's KEYS exhibit as the archive's reader makes it text, its
        // envelope's own words included, its length the one the capture's manifest states, and the replay's research pass
        // stores it as KEYS's results release.
        var text = SecEdgarArchive.Plain(File.ReadAllText(Path.Combine(Folder(), "release-KEYS.htm")));

        Assert.Equal(expected.GetProperty("characters").GetInt32(), text.Length);

        using var store = await FixtureReplay.ResearchedAsync();
        var releases = new List<string>();

        using (var connection = store.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT body FROM source_document WHERE body IS NOT NULL AND (title LIKE 'Results release, %' OR title LIKE 'Previous results release, %') ORDER BY id;";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                releases.Add(reader.GetString(0));
            }
        }

        Assert.Contains(text, releases);

        // And the local run reads it: the twice-read over the replayed store asks the reading of KEYS's release once in
        // each pass, the runtime answering as the capture records, and of every other stored release the same way.
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        var asked = new List<string>();
        var runtime = AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("answered by the body below"));
        var feed = new BodyAnsweringFeed(ReadingFeedOver(runtime, settings), body =>
        {
            asked.Add(body);

            return body == text ? reply : Reply("{\"results\": \"in line\", \"outlook\": \"kept\", \"tone\": \"neutral\", \"demand\": \"steady\"}");
        });

        var outcome = await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).TwiceAsync(TextWriter.Null);

        Assert.Equal((LocalRun.Finished, releases.Count, releases.Count, releases.Count), (outcome.Outcome, outcome.Read, outcome.Compared, outcome.Identical));
        Assert.Equal(2, asked.Count(body => body == text));
        Assert.Equal(2 * releases.Count, asked.Count);
    }

    [Fact]
    public async Task TheRuntimesRefusalAndItsAnswerToAModelItDoesNotHoldAreReadAsTheCapturesShow()
    {
        var settings = ReadingProfile(Path.GetTempPath());

        // A prompt past the loaded context: the runtime's 400 is a reading refused for the runtime's cause, with its words.
        var runtime = new ScriptedRuntime((path, _) => Task.FromResult(Answered(HttpStatusCode.BadRequest, Runtime("local-reading-past-context-refused.json"))));
        var refused = await Assert.ThrowsAsync<ReadingRefused>(() => ReadingFeedOver(runtime, settings).AskAsync(new ReadingRequest(Gemma, ReleaseQuestions.Instruction, "a release", ReadingSettings.Seed, ReadingSettings.AnswerTokens)));

        Assert.Equal(ReleaseAnswers.RuntimeRefused, refused.Cause);
        Assert.Contains("exceeds the available context size (50176 tokens)", refused.Message, StringComparison.Ordinal);

        // A request naming a model the runtime does not hold is answered by the model it has loaded, and named so: an
        // answer the pin's model did not give is told apart by the model it names.
        var stranger = LmStudioReadingFeed.Parse(Runtime("local-reading-unknown-model-answered.json"), 0.3);
        var qwenPin = Pinned(model: Qwen);

        Assert.Equal(Gemma, stranger.Model);
        Assert.Equal(ReadingPins.ModelPart, ReadingPins.AnswerDiffers(qwenPin, stranger.Model));
        Assert.Null(ReadingPins.AnswerDiffers(Pinned(), stranger.Model));

        // The runtime that served it is no part: the pin's model's answer is the pin's under a pin naming another
        // runtime's version or name.
        Assert.Null(ReadingPins.AnswerDiffers(Pinned() with { RuntimeVersion = "2.57.0" }, stranger.Model));
        Assert.Null(ReadingPins.AnswerDiffers(Pinned() with { RuntimeName = "llama.cpp-cuda" }, stranger.Model));

        // The request the reading sends: temperature zero, the seed, the budget, no stream and reasoning off, at the
        // native chat answer's path.
        var body = JsonDocument.Parse(LmStudioReadingFeed.Body(new ReadingRequest(Gemma, "instruction", "text", ReadingSettings.Seed, ReadingSettings.AnswerTokens))).RootElement;

        Assert.Equal((0.0, 20261018, 256, false, "none"), (body.GetProperty("temperature").GetDouble(), body.GetProperty("seed").GetInt32(), body.GetProperty("max_tokens").GetInt32(), body.GetProperty("stream").GetBoolean(), body.GetProperty("reasoning_effort").GetString()));
        Assert.Equal(["system instruction", "user text"], body.GetProperty("messages").EnumerateArray().Select(message => $"{message.GetProperty("role").GetString()} {message.GetProperty("content").GetString()}"));
        Assert.Equal("../api/v0/chat/completions", LmStudioReadingFeed.ChatPath);
    }

    [Fact]
    public void ThePinRefusesOnEachOfItsPartsAndHashesTheModelsFileWholeAsItsRuleSays()
    {
        var pin = Pinned();
        var modified = pin.Modified;

        // Each part compared before a run asks anything, in order: the model, the file by its hash where hashed whole and
        // by its size and modified instant otherwise, the instruction's version, and from the second 20.3 correction of
        // 2026-10-22 the profile's window.
        Assert.Null(ReadingPins.Differs(pin, Gemma, pin.Sha256, pin.Size, modified, ReleaseQuestions.Version, 50176));
        Assert.Null(ReadingPins.Differs(pin, Gemma, null, pin.Size, modified, ReleaseQuestions.Version, 50176));
        Assert.Equal(ReadingPins.ModelPart, ReadingPins.Differs(pin, Qwen, pin.Sha256, pin.Size, modified, ReleaseQuestions.Version, 50176));
        Assert.Equal(ReadingPins.FilePart, ReadingPins.Differs(pin, Gemma, new string('0', 64), pin.Size, modified, ReleaseQuestions.Version, 50176));
        Assert.Equal(ReadingPins.FilePart, ReadingPins.Differs(pin, Gemma, null, pin.Size + 1, modified, ReleaseQuestions.Version, 50176));
        Assert.Equal(ReadingPins.FilePart, ReadingPins.Differs(pin, Gemma, null, pin.Size, modified.AddTicks(1), ReleaseQuestions.Version, 50176));
        Assert.Equal(ReadingPins.PromptPart, ReadingPins.Differs(pin, Gemma, pin.Sha256, pin.Size, modified, "000000000000", 50176));

        // Hashed whole where the size or the modified instant differs from the pin's, and on a run in an ISO week of UTC
        // no run hashed it whole in: Sunday 2026-10-18 and Monday 2026-10-19 sit in two weeks.
        var sunday = new DateTimeOffset(2026, 10, 18, 23, 59, 59, TimeSpan.Zero);
        var monday = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

        Assert.False(ReadingPins.HashesWhole(pin, pin.Size, modified, new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), sunday));
        Assert.True(ReadingPins.HashesWhole(pin, pin.Size, modified, sunday, monday));
        Assert.True(ReadingPins.HashesWhole(pin, pin.Size, modified, null, sunday));
        Assert.True(ReadingPins.HashesWhole(pin, pin.Size + 1, modified, sunday, sunday));
        Assert.True(ReadingPins.HashesWhole(pin, pin.Size, modified.AddTicks(1), sunday, sunday));
    }

    [Fact]
    public void AReleaseIsReadableFromTheFirstNightWhoseLateSlotStartsAfterItWasAccepted()
    {
        using var store = new TemporaryStore().Migrated();

        // The nights' sessions the store holds: Friday 2026-10-16, Monday 10-19 and Tuesday 10-20.
        foreach (var session in new[] { "2026-10-16", "2026-10-19", "2026-10-20" })
        {
            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('KEYS', '{session}', '1', '1', '1', '1', 1, 'eod', '{session}T23:00:00Z', '1');");
        }

        var sessions = new List<DateOnly>();

        using (var connection = store.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT DISTINCT session_date FROM bar ORDER BY session_date;";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                sessions.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        static DateTimeOffset At(int month, int day, int hour, int minute, int second) => new(2026, month, day, hour, minute, second, TimeSpan.Zero);

        // A second before the late slot's start on Friday is Friday's night; at it, Monday's; a Saturday acceptance and
        // one a second before Monday's slot are Monday's; one past Tuesday's slot is no night's the store holds yet.
        Assert.Equal(new DateOnly(2026, 10, 16), ReleaseAvailability.FirstNight(At(10, 16, 21, 39, 59), sessions));
        Assert.Equal(new DateOnly(2026, 10, 19), ReleaseAvailability.FirstNight(At(10, 16, 21, 40, 0), sessions));
        Assert.Equal(new DateOnly(2026, 10, 19), ReleaseAvailability.FirstNight(At(10, 17, 15, 0, 0), sessions));
        Assert.Equal(new DateOnly(2026, 10, 19), ReleaseAvailability.FirstNight(At(10, 19, 21, 39, 59), sessions));
        Assert.Null(ReleaseAvailability.FirstNight(At(10, 20, 21, 40, 0), sessions));

        // Read from that night on and on no night before it.
        Assert.True(ReleaseAvailability.ReadableOn(At(10, 16, 21, 39, 59), new DateOnly(2026, 10, 20)));
        Assert.False(ReleaseAvailability.ReadableOn(At(10, 16, 21, 40, 0), new DateOnly(2026, 10, 16)));

        // The constant is the late slot's start, and the slots' instants are the decision's.
        Assert.Equal(
            ["12:05", "21:30", "21:40", "22:50", "22:55"],
            new[] { LocalSlots.EarlyStart, LocalSlots.EarlyAsksNothingAfter, LocalSlots.LateStart, LocalSlots.LateAsksNothingAfter, LocalSlots.LateRowBy }
                .Select(time => time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task ThePinIsWrittenFromTheFileHashedWholeAndTheProbesAnswerAndTheRuntimeIsLeftAsFound()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var runtime = AnsweringRuntime(loaded: false, chat: _ => Runtime("local-reading-probe-answered.json"));
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        var outcome = await new LocalRun(ReadingFeedOver(runtime, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        Assert.Equal((LocalRun.Finished, ReadingPins.WholeHash), (outcome.Outcome, outcome.FileCheck));

        using (var connection = store.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, model, model_file, sha256, size, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, history_completed_at FROM reading_pin;";

            using var reader = command.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal((Gemma, ModelsFolder.File, models.Sha256, models.Size), (reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4)));
            Assert.Equal((Runtime256, RuntimeVersion, ReleaseQuestions.Version), (reader.GetString(5), reader.GetString(6), reader.GetString(7)));
            Assert.Equal((0.0, 20261018L, 256L, 50176L, "2025-01-31"), (reader.GetDouble(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11), reader.GetString(12)));
            Assert.True(reader.IsDBNull(13));
            Assert.False(reader.Read());
        }

        // The model was not loaded, so the run loaded it at its profile's context, asked the probe and unloaded it.
        Assert.Equal(
            ["GET /api/v1/models", "POST /api/v1/models/load", "POST /api/v0/chat/completions", "POST /api/v1/models/unload"],
            runtime.Asked.Select(asked => string.Join(' ', asked.Split(' ').Take(2))));
        Assert.Contains("\"context_length\":50176", runtime.Asked[1], StringComparison.Ordinal);

        // Written again over the same file, runtime and prompt, nothing differs and no version is written; and again after
        // LM Studio updated its runtime by itself, which is no part of a version, so none is written then either.
        var again = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        Assert.Equal(LocalRun.PinUnchanged, again.Outcome);

        var updated = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json").Replace($"\"{RuntimeVersion}\"", "\"2.63.0\"", StringComparison.Ordinal)), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        Assert.Equal(LocalRun.PinUnchanged, updated.Outcome);
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM reading_pin;"));

        // One run log row a run, under the local run's stage, with no network request and no spend.
        Assert.Equal(3L, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'local-run' AND network_requests = 0 AND spend = '0';"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'local-run' AND outcome = 'finished' AND rows_written = 1 AND model_calls = 1;"));
    }

    [Fact]
    public async Task ASlotChecksThePinBeforeItReadsAndHashesTheFileWholeOnlyAsTheRuleSays()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var thursday = FixedClock.At(new DateTimeOffset(2026, 10, 15, 21, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // No pin written yet: the slot reads nothing and says why.
        var unpinned = await SlotOver(store, settings, thursday, AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no reading is asked")));

        Assert.Equal(LocalRun.NoPin, unpinned.Outcome);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, thursday, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // The same ISO week as the pin's whole hash: the size and modified instant compared alone, nothing asked of the
        // runtime since nothing waits to be read, and the slot's row names its kind and its check.
        var untouched = AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no reading is asked"));
        var friday = await SlotOver(store, settings, FixedClock.At(new DateTimeOffset(2026, 10, 16, 12, 5, 0, TimeSpan.Zero), SessionZones.UnitedStates), untouched);

        Assert.Equal((LocalRun.EarlySlot, LocalRun.NothingToRead, ReadingPins.SizeAndTime), (friday.Kind, friday.Outcome, friday.FileCheck));
        Assert.Empty(untouched.Asked);

        // The next ISO week's first run hashes it whole again, and finds it the pin's.
        var monday = await SlotOver(store, settings, FixedClock.At(new DateTimeOffset(2026, 10, 19, 21, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), untouched, LocalSlots.Late);

        Assert.Equal((LocalRun.LateSlot, LocalRun.NothingToRead, ReadingPins.WholeHash), (monday.Kind, monday.Outcome, monday.FileCheck));

        // A file written again refuses, named as the model's file; a profile naming another model refuses, named as the
        // model; and a profile naming no file refuses before the pin is read.
        models.Change();

        var changed = await SlotOver(store, settings, FixedClock.At(new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), untouched);

        Assert.Equal((LocalRun.PinRefused, ReadingPins.WholeHash), (changed.Outcome, changed.FileCheck));
        Assert.Equal("the model's file is not pin 1's", changed.Why);

        var otherModel = new LocalModelSettings("qwen-3.5", "http://127.0.0.1:1234/v1/", Qwen, 30, 50176, 30) { ModelFile = ModelsFolder.File, ModelsFolder = models.Root, KnowledgeCutoff = new DateOnly(2025, 1, 31) };
        var refused = await SlotOver(store, otherModel, FixedClock.At(new DateTimeOffset(2026, 10, 19, 22, 1, 0, TimeSpan.Zero), SessionZones.UnitedStates), untouched);

        Assert.Equal((LocalRun.PinRefused, "the model is not pin 1's"), (refused.Outcome, refused.Why));

        var noFile = new LocalModelSettings("gemma-4", "http://127.0.0.1:1234/v1/", Gemma, 30, 50176, 30) { KnowledgeCutoff = new DateOnly(2025, 1, 31) };

        Assert.Equal(LocalRun.NotSet, (await SlotOver(store, noFile, thursday, untouched)).Outcome);
        Assert.Empty(untouched.Asked);
    }

    [Fact]
    public async Task ARunRefusesWhereLmStudiosSavedStructuredOutputDiffersFromTheOneTheHistoryReadStartedUnder()
    {
        // From the 20.1 correction of 2026-10-19, on the operator's ruling of that evening: an LM Studio home of the
        // suite's own, its models folder holding the model's file and beside it the folder of saved model settings, and a
        // profile stating the structured output the history read started under, json.
        using var store = new TemporaryStore().Migrated();
        using var home = new TemporaryDirectory();
        var modelsRoot = Path.Combine(home.Path, "models");

        Directory.CreateDirectory(Path.Combine(modelsRoot, "pub", "model"));
        System.IO.File.WriteAllBytes(Path.Combine(modelsRoot, "pub", "model", "m.gguf"), Encoding.UTF8.GetBytes("a model file of the suite's own"));

        var settings = ReadingProfile(modelsRoot) with { StructuredOutput = "json" };
        var saved = Path.Combine(home.Path, ".internal", "user-concrete-model-default-config", "google", "gemma-4-26b-a4b-qat.json");
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 20, 12, 5, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var untouched = AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no reading is asked"));

        void Saved(string fields)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            System.IO.File.WriteAllText(saved, "{\"preset\":\"\",\"operation\":{\"fields\":[" + fields + "]},\"load\":{\"fields\":[]}}");
        }

        string Detail() => Query(store, "SELECT detail FROM run_log WHERE stage = 'local-run' ORDER BY rowid DESC LIMIT 1;").Single();

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // Worked by hand. LM Studio's saved settings for the model hold the structured output as json, the one the read
        // started under: the slot goes on, nothing waiting to be read, and its row names what it read.
        Saved("{\"key\":\"llm.prediction.temperature\",\"value\":0.69},{\"key\":\"llm.prediction.structured\",\"value\":{\"type\":\"json\"}}");

        Assert.Equal(LocalRun.NothingToRead, (await SlotOver(store, settings, clock, untouched)).Outcome);
        Assert.Contains("\"structuredOutput\":\"json\"", Detail(), StringComparison.Ordinal);

        // The setting taken off, which LM Studio saves as no field: the slot refuses before anything is asked of the
        // runtime, saying so on its row in these words.
        Saved("{\"key\":\"llm.prediction.temperature\",\"value\":0.69}");

        var refused = await SlotOver(store, settings, clock, untouched);

        Assert.Equal(
            (LocalRun.SettingDiffers, "LM Studio's saved structured output for google/gemma-4-26b-a4b-qat reads off, not json, the one the history read started under"),
            (refused.Outcome, refused.Why));
        Assert.Equal(["setting differs"], Query(store, "SELECT outcome FROM run_log WHERE stage = 'local-run' ORDER BY rowid DESC LIMIT 1;"));
        Assert.Contains("\"structuredOutput\":\"off\"", Detail(), StringComparison.Ordinal);
        Assert.Empty(untouched.Asked);

        // A home holding the model's file as the first holds it, its size and modified instant the pin's, and no folder of
        // saved settings to read: the slot says so on its row with the setting the read started under, and goes on.
        using var bare = new TemporaryDirectory();
        var bareFile = Path.Combine(bare.Path, "models", "pub", "model", "m.gguf");

        Directory.CreateDirectory(Path.GetDirectoryName(bareFile)!);
        System.IO.File.Copy(Path.Combine(modelsRoot, "pub", "model", "m.gguf"), bareFile);
        System.IO.File.SetLastWriteTimeUtc(bareFile, System.IO.File.GetLastWriteTimeUtc(Path.Combine(modelsRoot, "pub", "model", "m.gguf")));

        Assert.Equal(LocalRun.NothingToRead, (await SlotOver(store, settings with { ModelsFolder = Path.Combine(bare.Path, "models") }, clock, untouched)).Outcome);
        Assert.Contains("\"structuredOutput\":\"not read: LM Studio keeps no folder of saved model settings beside its models folder; the history read started under json\"", Detail(), StringComparison.Ordinal);

        // A profile stating no setting holds the saved settings to nothing, and its row carries no reading of them.
        Assert.Equal(LocalRun.NothingToRead, (await SlotOver(store, settings with { StructuredOutput = null }, clock, untouched)).Outcome);
        Assert.DoesNotContain("structuredOutput", Detail(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRuntimeNotRunningAndTheModelNotHeldEndTheRunAndSaySo()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);
        StoreRelease(store, "a", "Results release, a.htm", "The quarter beat.");

        // Nothing listens at the address.
        var gone = new ScriptedRuntime((_, _) => throw new HttpRequestException("connection refused"));
        var notRunning = await new LocalRun(ReadingFeedOver(gone, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).TwiceAsync(TextWriter.Null);

        Assert.Equal(ReadingRuntimeUnavailable.NotRunning, notRunning.Outcome);
        Assert.Contains("LM Studio is not running", notRunning.Why, StringComparison.Ordinal);

        // The runtime lists no such model.
        var other = new ScriptedRuntime((path, _) => Task.FromResult(Answered(HttpStatusCode.OK, "{\"models\":[{\"key\":\"qwen/qwen3.5-9b\",\"loaded_instances\":[]}]}")));
        var notHeld = await new LocalRun(ReadingFeedOver(other, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).TwiceAsync(TextWriter.Null);

        Assert.Equal(ReadingRuntimeUnavailable.ModelNotHeld, notHeld.Outcome);

        // No file stands under the models folder: the model is not held, and nothing is asked of the runtime.
        var asked = AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no reading is asked"));
        var missing = new LocalModelSettings("gemma-4", "http://127.0.0.1:1234/v1/", Gemma, 30, 50176, 30) { ModelFile = "pub/none/gone.gguf", ModelsFolder = models.Root, KnowledgeCutoff = new DateOnly(2025, 1, 31) };
        var noFile = await new LocalRun(ReadingFeedOver(asked, missing), missing, clock, store.DatabaseFile, store.Root, NoWait).TwiceAsync(TextWriter.Null);

        Assert.Equal(ReadingRuntimeUnavailable.ModelNotHeld, noFile.Outcome);
        Assert.Empty(asked.Asked);
        Assert.Equal(4L, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'local-run';"));
    }

    [Fact]
    public async Task TheStoredReleasesAreReadTwiceAndAnAnswerThatCannotBeReadIsAskedOnceMore()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // Three stored results releases and one document that is none: A answers alike, B answers what cannot be read and
        // then a reading each time, and C past the loaded context is refused by the runtime each time.
        StoreRelease(store, "a", "Results release, a.htm", "Release A beat its quarter.");
        StoreRelease(store, "b", "Previous results release, b.htm", "Release B held its outlook.");
        StoreRelease(store, "c", "Results release, c.htm", "Release C runs long.");
        StoreRelease(store, "d", "A story about the company", "Not a release.");

        var asks = new Dictionary<string, int>(StringComparer.Ordinal);
        var runtime = new ScriptedRuntime(async (path, cancellation) =>
        {
            await Task.Yield();

            return path switch
            {
                "/api/v1/models" => Answered(HttpStatusCode.OK, Runtime("local-runtime-models-gemma-loaded.json")),
                "/api/v0/chat/completions" => throw new InvalidOperationException("answered by the body below"),
                _ => Answered(HttpStatusCode.NotFound, "{}"),
            };
        });

        var feed = new BodyAnsweringFeed(ReadingFeedOver(runtime, settings), text =>
        {
            var key = text.Contains("Release A", StringComparison.Ordinal) ? "A" : text.Contains("Release B", StringComparison.Ordinal) ? "B" : text.Contains("Release C", StringComparison.Ordinal) ? "C" : "D";

            asks[key] = asks.GetValueOrDefault(key) + 1;

            return key switch
            {
                "A" => Reply("{\"results\": \"better\", \"outlook\": \"raised\", \"tone\": \"confident\", \"demand\": \"stronger\"}"),
                "B" when asks[key] % 2 == 1 => Reply("The release reads well."),
                "B" => Reply("{\"results\": \"in line\", \"outlook\": \"kept\", \"tone\": \"neutral\", \"demand\": \"steady\"}"),
                "C" => throw new ReadingRefused(ReleaseAnswers.RuntimeRefused, "the runtime refused the reading: past the context"),
                _ => throw new InvalidOperationException("a document that is no results release is read"),
            };
        });

        var outcome = await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).TwiceAsync(TextWriter.Null);

        // Each release read in both passes, B asked once more each time, C refused at once and not asked again, D never:
        // two releases read, and C's two refusals counted by their cause.
        Assert.Equal((LocalRun.Finished, 3, 3), (outcome.Outcome, outcome.Compared, outcome.Identical));
        Assert.Equal(2, outcome.Read);
        Assert.Equal(new Dictionary<string, int> { [ReleaseAnswers.RuntimeRefused] = 2 }, outcome.Refused);
        Assert.Equal(new Dictionary<string, int> { ["A"] = 2, ["B"] = 4, ["C"] = 2 }, asks);
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'local-run' AND json_extract(detail, '$.kind') = 'twice' AND json_extract(detail, '$.identical') = 3;"));

        // An answer alike is the same four words or the same cause, and no other pair is.
        var words = new ReleaseAnswerRead(new ReleaseAnswer("better", "kept", "neutral", "steady"), null);

        Assert.True(LocalRun.Alike(words, words with { }));
        Assert.False(LocalRun.Alike(words, new ReleaseAnswerRead(new ReleaseAnswer("worse", "kept", "neutral", "steady"), null)));
        Assert.True(LocalRun.Alike(new ReleaseAnswerRead(null, ReleaseAnswers.CutOff), new ReleaseAnswerRead(null, ReleaseAnswers.CutOff)));
        Assert.False(LocalRun.Alike(new ReleaseAnswerRead(null, ReleaseAnswers.CutOff), new ReleaseAnswerRead(null, ReleaseAnswers.Empty)));
        Assert.False(LocalRun.Alike(words, new ReleaseAnswerRead(null, ReleaseAnswers.Empty)));
    }

    [Fact]
    public async Task AnAnswerFromAModelThePinDoesNotNameEndsTheRunAndOneUnderAnotherRuntimeIsReadAsThePins()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);
        StoreRelease(store, "a", "Results release, a.htm", "Release A beat its quarter.");

        // The pin's model answering under a runtime LM Studio updated by itself is read as the pin's.
        var upgraded = Runtime("local-reading-KEYS-answered.json").Replace($"\"{RuntimeVersion}\"", "\"2.57.0\"", StringComparison.Ordinal);
        var read = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => upgraded), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).TwiceAsync(TextWriter.Null);

        Assert.Equal((LocalRun.Finished, 1, 1), (read.Outcome, read.Compared, read.Identical));

        // Another model's answer ends the run with nothing compared.
        var stranger = Runtime("local-reading-KEYS-answered.json").Replace($"\"{Gemma}\"", $"\"{Qwen}\"", StringComparison.Ordinal);
        var outcome = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => stranger), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).TwiceAsync(TextWriter.Null);

        Assert.Equal(LocalRun.PinRefused, outcome.Outcome);
        Assert.Contains("an answer came from the model the pin does not name", outcome.Why, StringComparison.Ordinal);
        Assert.Equal(0, outcome.Compared);
    }

    [Fact]
    public async Task TheRunWaitsToWriteWhileTheNightHoldsItsLockAndASlotGivesUpOnTheLocalLockPastItsLastInstant()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 19, 21, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // The night holds its lock as the slot comes to write: the slot waits, writes nothing until the night lets go,
        // and then writes its row.
        var night = NightLock.Take(store.Root, "night-20261019T230000Z-for-2026-10-19");
        var waits = 0;

        Assert.NotNull(night);
        Assert.Equal("night-20261019T230000Z-for-2026-10-19", LocalLocks.Holder(store.Root));

        var slot = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no reading is asked")), settings), settings, clock, store.DatabaseFile, store.Root, (_, _) =>
        {
            waits++;
            Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'local-run';"));
            night!.Dispose();

            return Task.CompletedTask;
        }).SlotAsync(LocalSlots.Late, TextWriter.Null);

        Assert.Equal((1, LocalRun.NothingToRead), (waits, slot.Outcome));
        Assert.Null(LocalLocks.Holder(store.Root));
        Assert.Equal(2L, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'local-run';"));

        // Another local process holds the local model's lock past the late slot's last instant: the slot gives up and
        // says so.
        var folder = Path.Combine(store.Root, EquityBrief.Core.Research.WorkerDrainLauncher.CopiesFolder);

        Directory.CreateDirectory(folder);

        using (new FileStream(Path.Combine(folder, LocalLocks.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var late = FixedClock.At(new DateTimeOffset(2026, 10, 19, 22, 51, 0, TimeSpan.Zero), SessionZones.UnitedStates);
            var held = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no reading is asked")), settings), settings, late, store.DatabaseFile, store.Root, NoWait).SlotAsync(LocalSlots.Late, TextWriter.Null);

            Assert.Equal(LocalRun.LockHeld, held.Outcome);
        }
    }

    // ---- the local run's own helpers ----

    static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    static LocalModelSettings ReadingProfile(string modelsRoot) =>
        new("gemma-4", "http://127.0.0.1:1234/v1/", Gemma, 30, 50176, 30)
        {
            ModelFile = ModelsFolder.File,
            ModelsFolder = modelsRoot,
            KnowledgeCutoff = new DateOnly(2025, 1, 31),
        };

    static ReadingPin Pinned(string model = Gemma) =>
        new(1, model, ModelsFolder.File, new string('a', 64), 1024, new DateTimeOffset(2026, 9, 20, 15, 19, 25, TimeSpan.Zero),
            Runtime256, RuntimeVersion, ReleaseQuestions.Version, ReadingSettings.Seed, ReadingSettings.AnswerTokens, 50176,
            new DateOnly(2025, 1, 31), new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero), null);

    static LmStudioReadingFeed ReadingFeedOver(ScriptedRuntime runtime, LocalModelSettings settings) =>
        new(new HttpClient(runtime) { BaseAddress = new Uri(settings.BaseAddress), Timeout = TimeSpan.FromMinutes(2) }, settings);

    // A runtime answering as the captures show: its list of models with the model loaded or not, a load and an unload,
    // and each chat call by the answer the test hands back for the request's body.
    static ScriptedRuntime AnsweringRuntime(bool loaded, Func<string, string> chat) => new((path, _) => Task.FromResult(path switch
    {
        "/api/v1/models" => Answered(HttpStatusCode.OK, Runtime(loaded ? "local-runtime-models-gemma-loaded.json" : "local-runtime-models-none-loaded.json")),
        "/api/v1/models/load" => Answered(HttpStatusCode.OK, Runtime("local-runtime-load-answered.json")),
        "/api/v1/models/unload" => Answered(HttpStatusCode.OK, Runtime("local-runtime-unload-answered.json")),
        "/api/v0/chat/completions" => Answered(HttpStatusCode.OK, chat(path)),
        _ => Answered(HttpStatusCode.NotFound, "{}"),
    }));

    static Task<LocalRunOutcome> SlotOver(TemporaryStore store, LocalModelSettings settings, IClock clock, ScriptedRuntime runtime, string slot = LocalSlots.Early) =>
        new LocalRun(ReadingFeedOver(runtime, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).SlotAsync(slot, TextWriter.Null);

    static void StoreRelease(TemporaryStore store, string id, string title, string body) =>
        store.Execute($"INSERT INTO source_document (id, url, title, published_on, fetched_at, body, admissibility) VALUES ('{id}', 'https://www.sec.gov/Archives/edgar/data/1/{id}.htm', '{title}', '2026-10-01', '2026-10-02T00:00:00Z', '{body}', 'accepted');");

    static ReadingReply Reply(string content) =>
        new(Gemma, content, "stop", 100, 20, 0, Runtime256, RuntimeVersion, 1.5);

    // A feed whose list, load and unload go to the scripted runtime and whose readings the test answers by the
    // document's text, so each document's answers can be told apart.
    sealed class BodyAnsweringFeed(LmStudioReadingFeed inner, Func<string, ReadingReply> answer) : IReadingModelFeed
    {
        public int Requests { get; private set; }

        public Task<bool> PrepareAsync(CancellationToken cancellation = default) => inner.PrepareAsync(cancellation);

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default) => inner.ReleaseAsync(loadedHere, cancellation);

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(answer(request.Text));
        }
    }

    // A models folder of the suite's own holding one small file in place of the model's, its hash worked out here.
    sealed class ModelsFolder : IDisposable
    {
        public const string File = "pub/model/m.gguf";

        readonly TemporaryDirectory folder = new();

        public ModelsFolder()
        {
            Directory.CreateDirectory(Path.Combine(Root, "pub", "model"));
            System.IO.File.WriteAllBytes(Path.Combine(Root, "pub", "model", "m.gguf"), Encoding.UTF8.GetBytes("a model file of the suite's own"));
        }

        public string Root => folder.Path;

        public string Sha256 => Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(Path.Combine(Root, "pub", "model", "m.gguf"))));

        public long Size => new FileInfo(Path.Combine(Root, "pub", "model", "m.gguf")).Length;

        // The file written again with one byte more, its modified instant moving with it.
        public void Change() => System.IO.File.AppendAllText(Path.Combine(Root, "pub", "model", "m.gguf"), "!");

        public void Dispose() => folder.Dispose();
    }
}
