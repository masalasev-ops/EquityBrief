using System.Net;
using System.Text.Json.Nodes;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from the second 20.3 correction of 2026-10-22: a reading is taken only at the window its pin
// names. A run refuses where its profile's window is not the pin's; the reading feed keeps a model the runtime's list
// holds once at the profile's window, or at one the list does not state, and unloads and loads it again at that window
// wherever the list holds it at another or more than once; the history read reads the list before each release, so a
// load LM Studio made at its own saved window is undone before the next release; and a refusal naming a window other
// than the pin's ends the run with nothing stored for its release and no cut.
// see: A reading is taken only at the window its pin names, and the model is loaded again at it wherever it is held at another
public partial class FixtureExpectations
{
    // The captured list holding Gemma loaded once at 50,176, its instances given the windows named in turn, a window of
    // none an instance whose settings state no context.
    static string GemmaHeldAt(params int?[] windows)
    {
        var list = JsonNode.Parse(Runtime("local-runtime-models-gemma-loaded.json"))!;
        var gemma = list["models"]!.AsArray().Single(model => (string?)model!["key"] == Gemma)!;
        var captured = gemma["loaded_instances"]![0]!;
        var instances = new JsonArray();

        for (var at = 0; at < windows.Length; at++)
        {
            var instance = captured.DeepClone();

            instance["id"] = at == 0 ? Gemma : $"{Gemma}:{at + 1}";

            if (windows[at] is { } tokens)
            {
                instance["config"]!["context_length"] = tokens;
            }
            else
            {
                instance["config"]!.AsObject().Remove("context_length");
            }

            instances.Add(instance);
        }

        gemma["loaded_instances"] = instances;

        return list.ToJsonString();
    }

    // A runtime answering its list with each list given in turn and the last after them, and each reading with the
    // answer given.
    static ScriptedRuntime ListingInTurn(Func<HttpResponseMessage> reading, params string[] lists)
    {
        var listed = 0;

        return new ScriptedRuntime((path, _) => Task.FromResult(path switch
        {
            "/api/v1/models" => Answered(HttpStatusCode.OK, lists[Math.Min(listed++, lists.Length - 1)]),
            "/api/v1/models/load" => Answered(HttpStatusCode.OK, Runtime("local-runtime-load-answered.json")),
            "/api/v1/models/unload" => Answered(HttpStatusCode.OK, Runtime("local-runtime-unload-answered.json")),
            "/api/v0/chat/completions" => reading(),
            _ => Answered(HttpStatusCode.NotFound, "{}"),
        }));
    }

    // Each request a scripted runtime was sent by its method and path, a load with the window it asked for.
    static string[] Requests(ScriptedRuntime runtime)
    {
        const string Load = "POST /api/v1/models/load ";

        return
        [
            .. runtime.Asked.Select(asked => asked.StartsWith(Load, StringComparison.Ordinal)
                ? Load + JsonNode.Parse(asked[Load.Length..])!["context_length"]
                : string.Join(' ', asked.Split(' ').Take(2))),
        ];
    }

    [Fact]
    public void ARunRefusesWhereItsProfilesWindowIsNotThePins()
    {
        var pin = Pinned();

        Assert.Null(ReadingPins.Differs(pin, Gemma, pin.Sha256, pin.Size, pin.Modified, ReleaseQuestions.Version, 50176));
        Assert.Equal(ReadingPins.WindowPart, ReadingPins.Differs(pin, Gemma, pin.Sha256, pin.Size, pin.Modified, ReleaseQuestions.Version, 59136));
        Assert.Equal(ReadingPins.WindowPart, ReadingPins.Differs(pin, Gemma, pin.Sha256, pin.Size, pin.Modified, ReleaseQuestions.Version, 50175));

        // The parts compared in their order: a run differing in its prompt and its window named by its prompt.
        Assert.Equal(ReadingPins.PromptPart, ReadingPins.Differs(pin, Gemma, pin.Sha256, pin.Size, pin.Modified, "000000000000", 59136));
    }

    [Fact]
    public async Task TheReadingFeedKeepsAModelHeldOnceAtThePinsWindowAndLoadsItAgainOtherwise()
    {
        var settings = ReadingProfile(string.Empty);

        async Task<(bool Loaded, string[] Asked)> Prepared(string list)
        {
            var runtime = ListingInTurn(() => Answered(HttpStatusCode.OK, "{}"), list);
            var loaded = await ReadingFeedOver(runtime, settings).PrepareAsync();

            return (loaded, Requests(runtime));
        }

        // Held once at the profile's window, or at one the list does not state: kept, nothing loaded.
        foreach (var kept in new[] { GemmaHeldAt(50176), GemmaHeldAt((int?)null) })
        {
            var (loaded, asked) = await Prepared(kept);

            Assert.False(loaded);
            Assert.Equal(["GET /api/v1/models"], asked);
        }

        // Held at LM Studio's own saved window, at a smaller one or a token under the profile's: the instance unloaded and
        // the model loaded again at the profile's window.
        foreach (var other in new[] { GemmaHeldAt(59136), GemmaHeldAt(32000), GemmaHeldAt(50175) })
        {
            var (loaded, asked) = await Prepared(other);

            Assert.True(loaded);
            Assert.Equal(["GET /api/v1/models", "POST /api/v1/models/unload", "POST /api/v1/models/load 50176"], asked);
        }

        // Held twice at the profile's window: both instances unloaded and one loaded.
        var twice = await Prepared(GemmaHeldAt(50176, 50176));

        Assert.True(twice.Loaded);
        Assert.Equal(["GET /api/v1/models", "POST /api/v1/models/unload", "POST /api/v1/models/unload", "POST /api/v1/models/load 50176"], twice.Asked);
    }

    [Fact]
    public async Task TheHistoryReadReadsTheWindowBeforeEachReleaseAndLoadsTheModelAgainWhereAReloadLeftItAtAnother()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 22, 9, 0, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        foreach (var (accession, day) in new[] { ("0001601046-26-000301", 18), ("0001601046-26-000302", 19), ("0001601046-26-000303", 20) })
        {
            KeepRelease(store, accession, KeysCik, Utc(2026, 8, day, 20, 9, 53), "revenue grew and margins held steady ok\n");
        }

        // LM Studio holds the model at the pin's window for the first release, has loaded it itself at its own saved
        // window of 59,136 before the second, as it did after its update of that morning, and holds it at the pin's
        // window once the read has loaded it again.
        var runtime = ListingInTurn(() => Answered(HttpStatusCode.OK, Runtime("local-reading-KEYS-anonymised.json")), GemmaHeldAt(50176), GemmaHeldAt(59136), GemmaHeldAt(50176));
        var read = await new LocalRun(ReadingFeedOver(runtime, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).ReadHistoryAsync(null, TextWriter.Null);

        Assert.Equal((LocalRun.Finished, 3), (read.Outcome, read.Read));
        Assert.Equal(
            [
                "GET /api/v1/models", "POST /api/v0/chat/completions",
                "GET /api/v1/models", "POST /api/v1/models/unload", "POST /api/v1/models/load 50176", "POST /api/v0/chat/completions",
                "GET /api/v1/models", "POST /api/v0/chat/completions",
                "POST /api/v1/models/unload",
            ],
            Requests(runtime));
    }

    [Fact]
    public async Task ARefusalNamingAWindowOtherThanThePinsEndsTheRunWithNothingStoredForItsReleaseAndNoCut()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 22, 9, 0, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);
        KeepRelease(store, "0001601046-26-000311", KeysCik, Utc(2026, 8, 18, 20, 9, 53), string.Concat(Enumerable.Repeat("revenue grew and margins held steady ok\n", 1000)));

        // The captured refusal of a request past a window of 32,000 tokens, where the pin names 50,176: the model was
        // loaded at another window between the list and the request.
        var runtime = ListingInTurn(
            () => Answered(HttpStatusCode.BadRequest, Runtime("local-reading-past-context-refused.json").Replace("50176", "32000", StringComparison.Ordinal)),
            GemmaHeldAt(50176));
        var read = await new LocalRun(ReadingFeedOver(runtime, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).ReadHistoryAsync(null, TextWriter.Null);

        Assert.Equal(LocalRun.PinRefused, read.Outcome);
        Assert.Contains("a window of 32000 tokens where the pin names 50176", read.Why ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(Chats(runtime));
        Assert.Empty(Query(store, "SELECT accession FROM release_reading;"));
    }
}
