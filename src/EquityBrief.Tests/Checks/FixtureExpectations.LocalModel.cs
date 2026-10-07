using System.Net;
using System.Text;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the local model chosen by a flag in its settings: the lane calls the one profile the settings
// flag, read whole with no value in the code, a run over the capture calling the profile its recordings were made
// under; and before a pass's first call the model is loaded at its profile's context and waited for, read over the
// answers captured from the operator's runtime: a model not loaded loaded and its load waited for, another model
// unloaded first, one already listed at its profile's context called with the load's allowance, one listed at a
// smaller context unloaded and loaded again at its profile's, and a load refused or past its allowance and a model
// the runtime does not hold each the local model unavailable.
// see: The local lane calls the one model its settings flag as the default, and a profile it cannot read is the local model unavailable
// see: The local lane loads its model at its profile's context before its first call of a night or a pass, and loads again a model held at a smaller one
public partial class FixtureExpectations
{
    // The rows the lane's profiles and its load add that this check reaches.
    internal static readonly string[] LocalModelClaims =
    [
        CheckReach.Key(Scope.FailureTable, "The local model is not loaded or still loading when a pass reaches it"),
        CheckReach.Key(Scope.FailureTable, "The local lane's settings flag no model as the default or more than one"),
    ];

    const string Gemma = "google/gemma-4-26b-a4b-qat";

    const string Qwen = "qwen/qwen3.5-9b";

    static IConfiguration Configured(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

    static (string Key, string Value)[] LocalProfile(string word, string model, string? flagged) =>
    [
        (LocalModelSettings.KeyOf(word, LocalModelSettings.BaseAddressName), "http://127.0.0.1:1234/v1/"),
        (LocalModelSettings.KeyOf(word, LocalModelSettings.ModelName), model),
        (LocalModelSettings.KeyOf(word, LocalModelSettings.TimeoutName), "300"),
        (LocalModelSettings.KeyOf(word, LocalModelSettings.ContextTokensName), "50176"),
        (LocalModelSettings.KeyOf(word, LocalModelSettings.LoadName), "600"),
        .. flagged is null ? Array.Empty<(string, string)>() : [(LocalModelSettings.KeyOf(word, LocalModelSettings.IsDefaultName), flagged)],
    ];

    [Fact]
    public void TheLaneCallsTheOneProfileItsSettingsFlagAndReadsNoModelFromSettingsFlaggingNoneOrTwo()
    {
        // The shipped settings flag Gemma 4, with Qwen 3.5 beside it, every value of the profile read from the file.
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "appsettings.json"), optional: false)
            .Build();
        var flagged = LocalLane.Settings(shipped);

        Assert.Equal(("gemma-4", "http://127.0.0.1:1234/v1/", Gemma), (flagged.Profile, flagged.BaseAddress, flagged.Model));
        Assert.Equal((TimeSpan.FromSeconds(300), 50176, TimeSpan.FromSeconds(600)), (flagged.Timeout, flagged.ContextTokens, flagged.Load));
        Assert.Equal(["gemma-4", "qwen-3.5"], shipped.GetSection(LocalModelSettings.ProfilesKey).GetChildren().Select(profile => profile.Key).Order(StringComparer.Ordinal));

        // Moving the flag moves the model, and a profile with no flag is not the default.
        Assert.Equal(Qwen, LocalLane.Settings(Configured([.. LocalProfile("gemma-4", Gemma, "false"), .. LocalProfile("qwen-3.5", Qwen, "true")])).Model);
        Assert.Equal(Qwen, LocalLane.Settings(Configured([.. LocalProfile("gemma-4", Gemma, null), .. LocalProfile("qwen-3.5", Qwen, "True")])).Model);

        // A run over the capture calls the profile its recordings were made under, whatever the settings flag.
        Assert.Equal(Qwen, LocalLane.For(shipped, FeedSource.Fixture, Folder()).Model);
        Assert.Equal(Gemma, LocalLane.For(shipped, FeedSource.Live, null).Model);

        string Refused(IConfiguration configuration) => Assert.Throws<InvalidOperationException>(() => LocalLane.Settings(configuration)).Message;

        // None flagged, two flagged, no profile and a flag neither true nor false, each refused naming what it read.
        Assert.Equal(
            "No local model profile is flagged IsDefault true, of gemma-4, qwen-3.5: flag the one the lane calls.",
            Refused(Configured([.. LocalProfile("gemma-4", Gemma, "false"), .. LocalProfile("qwen-3.5", Qwen, null)])));
        Assert.Equal(
            "2 local model profiles are flagged IsDefault true, gemma-4 and qwen-3.5: the lane calls one, so flag one.",
            Refused(Configured([.. LocalProfile("gemma-4", Gemma, "true"), .. LocalProfile("qwen-3.5", Qwen, "true")])));
        Assert.StartsWith($"The local lane names no model: add a profile under '{LocalModelSettings.ProfilesKey}'", Refused(Configured()), StringComparison.Ordinal);
        Assert.Contains("'yes', which is neither true nor false", Refused(Configured(LocalProfile("gemma-4", Gemma, "yes"))), StringComparison.Ordinal);

        // Every value of the flagged profile is required, and none has a default in the code.
        foreach (var name in new[] { LocalModelSettings.BaseAddressName, LocalModelSettings.ModelName, LocalModelSettings.TimeoutName, LocalModelSettings.ContextTokensName, LocalModelSettings.LoadName })
        {
            var missing = LocalProfile("gemma-4", Gemma, "true").Where(pair => pair.Key != LocalModelSettings.KeyOf("gemma-4", name)).ToArray();

            Assert.Contains($"'{LocalModelSettings.KeyOf("gemma-4", name)}' is blank", Refused(Configured(missing)), StringComparison.Ordinal);
        }

        // An address the runtime cannot be asked at and a bound past a day are refused as a missing value is.
        foreach (var address in new[] { "127.0.0.1:1234/v1/", "http//127.0.0.1:1234/v1/", "ftp://127.0.0.1/v1/" })
        {
            Assert.Contains(
                $"'{LocalModelSettings.KeyOf("gemma-4", LocalModelSettings.BaseAddressName)}' is '{address}', which is not an address over HTTP",
                Refused(Configured([.. LocalProfile("gemma-4", Gemma, "true").Where(pair => pair.Key != LocalModelSettings.KeyOf("gemma-4", LocalModelSettings.BaseAddressName)), (LocalModelSettings.KeyOf("gemma-4", LocalModelSettings.BaseAddressName), address)])),
                StringComparison.Ordinal);
        }

        foreach (var name in new[] { LocalModelSettings.TimeoutName, LocalModelSettings.LoadName })
        {
            var profile = LocalProfile("gemma-4", Gemma, "true").Where(pair => pair.Key != LocalModelSettings.KeyOf("gemma-4", name)).ToArray();

            Assert.Contains("86401 seconds, more than a day", Refused(Configured([.. profile, (LocalModelSettings.KeyOf("gemma-4", name), "86401")])), StringComparison.Ordinal);
            Assert.Null(LocalLane.Settings(Configured([.. profile, (LocalModelSettings.KeyOf("gemma-4", name), "86400")])).Unreadable);
        }

        // A key the lane read before the profiles is refused with where it now goes, never passed over.
        Assert.Contains(
            $"'{LocalModelSettings.Section}:Model' is set, and the lane no longer reads it",
            Refused(Configured([.. LocalProfile("gemma-4", Gemma, "true"), ($"{LocalModelSettings.Section}:Model", Qwen)])),
            StringComparison.Ordinal);

        // On a run, settings the strict reader refuses leave the lane unread with the same line, calling no model.
        var two = Configured([.. LocalProfile("gemma-4", Gemma, "true"), .. LocalProfile("qwen-3.5", Qwen, "true")]);
        var unread = LocalLane.For(two, FeedSource.Live, null);

        Assert.Equal(Refused(two), unread.Unreadable);
        Assert.Equal(string.Empty, unread.Model);
        Assert.Equal(Refused(Configured()), LocalLane.For(Configured(), FeedSource.Live, null).Unreadable);

        // A key in a profile is refused as one at the lane is, on a run over the capture too, and never echoed.
        foreach (var run in new[] { FeedSource.Live, FeedSource.Fixture })
        {
            var keyed = Assert.Throws<InvalidOperationException>(() =>
                LocalLane.For(Configured([.. LocalProfile("gemma-4", Gemma, "true"), (LocalModelSettings.KeyOf("gemma-4", "ApiKey"), "sk-local")]), run, Folder())).Message;

            Assert.Contains(LocalModelSettings.KeyOf("gemma-4", "ApiKey"), keyed, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-local", keyed, StringComparison.Ordinal);
        }
    }

    // ---- the load, over the answers captured from the operator's runtime ----

    static string Runtime(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    static LocalModelSettings LoadingProfile(string model, int timeoutSeconds = 30, int loadSeconds = 30) =>
        new("test", "http://127.0.0.1:1234/v1/", model, timeoutSeconds, 50176, loadSeconds);

    static ModelRequest KeyRequest(LocalModelSettings settings) =>
        new(SectionPrompt.Lane, "The key under each figure", settings.Model, [], SectionPrompt.Instructions, "Keysight closed at 333.42.");

    static HttpResponseMessage Answered(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // A runtime answering each request as the test says, noting each one asked: the runtime's own interface with
    // what was sent, and a chat call by its path alone.
    sealed class ScriptedRuntime(Func<string, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null || !path.StartsWith("/api/", StringComparison.Ordinal) ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (Asked)
            {
                Asked.Add(body.Length == 0 ? $"{request.Method} {path}" : $"{request.Method} {path} {body}");
            }

            return await answer(path, cancellationToken);
        }
    }

    static OpenAiCompatibleModelFeed FeedOver(ScriptedRuntime runtime, LocalModelSettings settings) =>
        new(new HttpClient(runtime) { BaseAddress = new Uri(settings.BaseAddress), Timeout = TimeSpan.FromMinutes(2) }, settings);

    [Fact]
    public async Task AModelNotLoadedIsLoadedAtItsProfilesContextAndItsLoadWaitedForBeforeTheFirstCall()
    {
        var settings = LoadingProfile(Gemma);
        var loaded = false;
        var runtime = new ScriptedRuntime(async (path, cancellation) =>
        {
            switch (path)
            {
                case "/api/v1/models":
                    return Answered(HttpStatusCode.OK, Runtime("local-runtime-models-none-loaded.json"));
                case "/api/v1/models/load":
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
                    loaded = true;
                    return Answered(HttpStatusCode.OK, Runtime("local-runtime-load-answered.json"));
                default:
                    Assert.True(loaded, "The section was asked for before the load answered.");
                    return Answered(HttpStatusCode.OK, Runtime("local-gemma-answered.json"));
            }
        });

        var feed = FeedOver(runtime, settings);
        var answer = await feed.CompleteAsync(KeyRequest(settings));
        await feed.CompleteAsync(KeyRequest(settings));

        Assert.Equal(Gemma, answer.Model);
        Assert.Equal(
            [
                "GET /api/v1/models",
                $$"""POST /api/v1/models/load {"model":"{{Gemma}}","context_length":50176}""",
                "POST /v1/chat/completions",
                "POST /v1/chat/completions",
            ],
            runtime.Asked);
        Assert.Equal(2, feed.Requests);
    }

    [Fact]
    public async Task AnotherModelLoadedIsUnloadedFirstSoTheRuntimeHoldsOneModel()
    {
        var settings = LoadingProfile(Qwen);
        var runtime = new ScriptedRuntime((path, _) => Task.FromResult(path switch
        {
            "/api/v1/models" => Answered(HttpStatusCode.OK, Runtime("local-runtime-models-gemma-loaded.json")),
            "/api/v1/models/unload" => Answered(HttpStatusCode.OK, Runtime("local-runtime-unload-answered.json")),
            "/api/v1/models/load" => Answered(HttpStatusCode.OK, Runtime("local-runtime-load-answered.json")),
            _ => Answered(HttpStatusCode.OK, Runtime("local-gemma-answered.json")),
        }));

        await FeedOver(runtime, settings).CompleteAsync(KeyRequest(settings));

        Assert.Equal(
            [
                "GET /api/v1/models",
                $$"""POST /api/v1/models/unload {"instance_id":"{{Gemma}}"}""",
                $$"""POST /api/v1/models/load {"model":"{{Qwen}}","context_length":50176}""",
                "POST /v1/chat/completions",
            ],
            runtime.Asked);
    }

    [Fact]
    public async Task AModelAlreadyListedIsNotLoadedAgainAndItsFirstCallAloneCarriesTheLoadsAllowance()
    {
        // Listed is loaded or loading, so the first call is bounded by its own second and the load's three, and answers
        // after two; the second call, bounded by its own second alone, does not.
        var settings = LoadingProfile(Gemma, timeoutSeconds: 1, loadSeconds: 3);
        var runtime = new ScriptedRuntime(async (path, cancellation) =>
        {
            if (path == "/api/v1/models")
            {
                return Answered(HttpStatusCode.OK, Runtime("local-runtime-models-gemma-loaded.json"));
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellation);

            return Answered(HttpStatusCode.OK, Runtime("local-gemma-answered.json"));
        });

        var feed = FeedOver(runtime, settings);

        Assert.Equal(Gemma, (await feed.CompleteAsync(KeyRequest(settings))).Model);
        Assert.Equal(
            "The local model did not answer The key under each figure within 1 seconds.",
            (await Assert.ThrowsAsync<ProviderRefusal>(() => feed.CompleteAsync(KeyRequest(settings)))).Message);
        Assert.Equal(["GET /api/v1/models", "POST /v1/chat/completions", "POST /v1/chat/completions"], runtime.Asked);

        // A runtime that states no load state, answering the list with something other than LM Studio's, is called the
        // same way: its first call carries the allowance and nothing is loaded.
        var other = new ScriptedRuntime(async (path, cancellation) =>
        {
            if (path == "/api/v1/models")
            {
                return Answered(HttpStatusCode.NotFound, "Not Found");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellation);

            return Answered(HttpStatusCode.OK, Runtime("local-gemma-answered.json"));
        });

        Assert.Equal(Gemma, (await FeedOver(other, settings).CompleteAsync(KeyRequest(settings))).Model);
        Assert.Equal(["GET /api/v1/models", "POST /v1/chat/completions"], other.Asked);
    }

    [Fact]
    public async Task AModelLoadedAtASmallerContextThanItsProfilesIsUnloadedAndLoadedAgainAtItsProfiles()
    {
        // The captured list with Gemma's instance at the runtime's own context of 32,000 and at one token under the
        // profile's: each is unloaded and loaded again at 50,176 before the first call, and the second call asks the
        // runtime nothing more.
        foreach (var held in new[] { 32000, 50175 })
        {
            var settings = LoadingProfile(Gemma);
            var list = System.Text.Json.Nodes.JsonNode.Parse(Runtime("local-runtime-models-gemma-loaded.json"))!;

            list["models"]!.AsArray().Single(model => (string?)model!["key"] == Gemma)!["loaded_instances"]![0]!["config"]!["context_length"] = held;

            var runtime = new ScriptedRuntime((path, _) => Task.FromResult(path switch
            {
                "/api/v1/models" => Answered(HttpStatusCode.OK, list.ToJsonString()),
                "/api/v1/models/unload" => Answered(HttpStatusCode.OK, Runtime("local-runtime-unload-answered.json")),
                "/api/v1/models/load" => Answered(HttpStatusCode.OK, Runtime("local-runtime-load-answered.json")),
                _ => Answered(HttpStatusCode.OK, Runtime("local-gemma-answered.json")),
            }));

            var feed = FeedOver(runtime, settings);

            await feed.CompleteAsync(KeyRequest(settings));
            await feed.CompleteAsync(KeyRequest(settings));

            Assert.Equal(held, OpenAiCompatibleModelFeed.Held(list.ToJsonString(), Gemma)!.Context);
            Assert.Equal(
                [
                    "GET /api/v1/models",
                    $$"""POST /api/v1/models/unload {"instance_id":"{{Gemma}}"}""",
                    $$"""POST /api/v1/models/load {"model":"{{Gemma}}","context_length":50176}""",
                    "POST /v1/chat/completions",
                    "POST /v1/chat/completions",
                ],
                runtime.Asked);
        }

        // An instance whose list states no context is not loaded again, as one at the profile's own is not.
        var unstated = System.Text.Json.Nodes.JsonNode.Parse(Runtime("local-runtime-models-gemma-loaded.json"))!;

        unstated["models"]!.AsArray().Single(model => (string?)model!["key"] == Gemma)!["loaded_instances"]![0]!.AsObject().Remove("config");

        var quiet = new ScriptedRuntime((path, _) => Task.FromResult(path == "/api/v1/models"
            ? Answered(HttpStatusCode.OK, unstated.ToJsonString())
            : Answered(HttpStatusCode.OK, Runtime("local-gemma-answered.json"))));

        await FeedOver(quiet, LoadingProfile(Gemma)).CompleteAsync(KeyRequest(LoadingProfile(Gemma)));

        Assert.Null(OpenAiCompatibleModelFeed.Held(unstated.ToJsonString(), Gemma)!.Context);
        Assert.Equal(["GET /api/v1/models", "POST /v1/chat/completions"], quiet.Asked);
    }

    [Fact]
    public async Task ALoadRefusedOrPastItsAllowanceAndAModelTheRuntimeDoesNotHoldAreTheLocalModelUnavailable()
    {
        // The runtime's own words on a load it refused, and no section asked for.
        var settings = LoadingProfile(Gemma);
        var refusing = new ScriptedRuntime((path, _) => Task.FromResult(path == "/api/v1/models"
            ? Answered(HttpStatusCode.OK, Runtime("local-runtime-models-none-loaded.json"))
            : Answered(HttpStatusCode.NotFound, Runtime("local-runtime-load-refused.json"))));

        var refused = FeedOver(refusing, settings);
        var said = $"The runtime refused to load {Gemma}: Model no-such/model not found in downloaded models";

        Assert.Equal(said, (await Assert.ThrowsAsync<LocalModelUnavailable>(() => refused.CompleteAsync(KeyRequest(settings)))).Message);

        // A later call of the same pass answers with the same reason and asks the runtime nothing more.
        Assert.Equal(said, (await Assert.ThrowsAsync<LocalModelUnavailable>(() => refused.CompleteAsync(KeyRequest(settings)))).Message);
        Assert.Equal(["GET /api/v1/models", $$"""POST /api/v1/models/load {"model":"{{Gemma}}","context_length":50176}"""], refusing.Asked);

        // A load still going when its allowance of a second has passed.
        var slow = LoadingProfile(Gemma, loadSeconds: 1);
        var loading = new ScriptedRuntime(async (path, cancellation) =>
        {
            if (path == "/api/v1/models")
            {
                return Answered(HttpStatusCode.OK, Runtime("local-runtime-models-none-loaded.json"));
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellation);

            return Answered(HttpStatusCode.OK, Runtime("local-runtime-load-answered.json"));
        });

        Assert.Equal(
            $"The local model {Gemma} did not finish loading within 1 seconds, so the lane's sections are not written.",
            (await Assert.ThrowsAsync<LocalModelUnavailable>(() => FeedOver(loading, slow).CompleteAsync(KeyRequest(slow)))).Message);
        Assert.DoesNotContain("POST /v1/chat/completions", loading.Asked);

        // A model the runtime's list does not name, asked for nothing past the list.
        var unheld = LoadingProfile("no-such/model");
        var listing = new ScriptedRuntime((_, _) => Task.FromResult(Answered(HttpStatusCode.OK, Runtime("local-runtime-models-none-loaded.json"))));

        Assert.Equal(
            "The runtime holds no model named no-such/model, which the local profile test names, so the lane's sections are not written.",
            (await Assert.ThrowsAsync<LocalModelUnavailable>(() => FeedOver(listing, unheld).CompleteAsync(KeyRequest(unheld)))).Message);
        Assert.Equal(["GET /api/v1/models"], listing.Asked);
    }

    [Fact]
    public async Task ALoadTheRuntimeRefusesLeavesEverySectionInTheLaneUnwrittenAsTheLocalModelUnavailable()
    {
        var gone = Expected("prose").GetProperty("unavailable");
        var (store, document) = await WithRelease();

        using (store)
        {
            var settings = LocalSettings();
            var refusing = new ScriptedRuntime((path, _) => Task.FromResult(path == "/api/v1/models"
                ? Answered(HttpStatusCode.OK, Runtime("local-runtime-models-none-loaded.json"))
                : Answered(HttpStatusCode.NotFound, Runtime("local-runtime-load-refused.json"))));

            var outcome = await new ProseWriter(FeedOver(refusing, settings), settings, ReleaseLane, ProseClock, store.DatabaseFile)
                .WriteAsync("KEYS", Handed(document), "prose-load-refused");

            Assert.True(outcome.Unavailable);
            Assert.Equal(Listed(gone.GetProperty("notWritten")), outcome.NotWritten.Select(section => section.Section).ToArray());
            Assert.All(outcome.NotWritten, section => Assert.StartsWith(ProseWriter.Unavailable, section.Reason, StringComparison.Ordinal));
            Assert.Empty(Query(store, "SELECT section FROM research_section WHERE ticker = 'KEYS';"));
            Assert.DoesNotContain("POST /v1/chat/completions", refusing.Asked);
        }
    }

    // ---- settings naming no model the lane can call ----

    [Fact]
    public async Task SettingsFlaggingTwoModelsLeaveTheNightRunningEveryStepWithItsQueueRowSayingWhy()
    {
        // A live night's queue as its settings give it, the fixture's arithmetic around it: its lane unread, so its
        // feed reaches no runtime, and the night closes with the queue's row naming the profiles.
        var two = Configured([.. LocalProfile("gemma-4", Gemma, "true"), .. LocalProfile("qwen-3.5", Qwen, "true")]);
        var queue = NightQueue.From(two, FeedSource.Live, null, new RecordingAwake());
        const string line = "2 local model profiles are flagged IsDefault true, gemma-4 and qwen-3.5: the lane calls one, so flag one.";

        Assert.Equal(line, queue.Settings.Unreadable);

        var night = await FixtureReplay.NightAsync(queue);

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        var row = QueueRow(store);

        Assert.Equal(OvernightQueue.Unavailable, row.GetProperty("outcome").GetString());
        Assert.StartsWith(ProseWriter.Unavailable, row.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        Assert.EndsWith(line, row.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        Assert.Empty(row.GetProperty("completed").EnumerateArray());
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM research_section;"));

        // No model is called: the lane's sections are left before any call.
        Assert.Equal(["0"], Query(store, $"SELECT model_calls FROM run_log WHERE run_id = '{FixtureReplay.NightRunId}' AND stage = '{OvernightQueue.Stage}';"));

        // A key on the lane is still refused before the night starts.
        Assert.Throws<InvalidOperationException>(() =>
            NightQueue.From(Configured([.. LocalProfile("gemma-4", Gemma, "true"), (LocalModelSettings.ApiKeyKey, "a-key")]), FeedSource.Live, null, new RecordingAwake()));
    }

    [Fact]
    public async Task APassWhoseLaneIsUnreadWritesThePaidLaneAndLeavesTheLocalLaneAbsentWithTheLine()
    {
        using var fresh = await FixtureReplay.ReplayedForResearchAsync();

        var unread = LocalLane.For(Configured(), FeedSource.Live, null);
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Pinned());
        var outcome = await FixtureReplay.Researcher(
                fresh,
                ResearchClock,
                lane: [.. ProseWriter.DefaultLane, "The short version"],
                paid: paid,
                localModel: OpenAiCompatibleModelFeed.Live(unread),
                localSettings: unread)
            .RunAsync("KEYS", "research-local-unread");

        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(
            [.. ProseWriter.DefaultLane, "The short version"],
            outcome.NotWritten.Where(section => section.Reason.StartsWith(ProseWriter.Unavailable, StringComparison.Ordinal)).Select(section => section.Section).ToArray());
        Assert.All(
            outcome.NotWritten.Where(section => section.Reason.StartsWith(ProseWriter.Unavailable, StringComparison.Ordinal)),
            section => Assert.Contains(unread.Unreadable!, section.Reason, StringComparison.Ordinal));
        Assert.Equal(
            ["The dated calendar items", "The two cases", "The risks, each with what would confirm it"],
            outcome.Written.Select(section => section.Section).ToArray());
        Assert.All(outcome.Written, section => Assert.Equal(paid.Identity, section.Model));
    }

    [Fact]
    public void GemmasCapturedAnswerIsReadAsTheLaneReadsEveryAnswerWithItsReasoningOff()
    {
        var answer = OpenAiCompatibleModelFeed.Parse(Runtime("local-gemma-answered.json"), "The key under each figure");

        Assert.Equal((Gemma, "stop", 33, 35), (answer.Model, answer.FinishReason, answer.PromptTokens, answer.CompletionTokens));
        Assert.StartsWith("{", answer.Text, StringComparison.Ordinal);

        // The list as captured: Gemma 4 loaded and every other language model not, the embedding model never counted.
        var gemma = OpenAiCompatibleModelFeed.Held(Runtime("local-runtime-models-gemma-loaded.json"), Gemma)!;
        var qwen = OpenAiCompatibleModelFeed.Held(Runtime("local-runtime-models-gemma-loaded.json"), Qwen)!;
        var none = OpenAiCompatibleModelFeed.Held(Runtime("local-runtime-models-none-loaded.json"), Qwen)!;

        Assert.Equal((true, true, 50176, Gemma), (gemma.Listed, gemma.Loaded, gemma.Context, Assert.Single(gemma.Instances)));
        Assert.Empty(gemma.Others);
        Assert.Equal((true, false, null, Gemma), (qwen.Listed, qwen.Loaded, qwen.Context, Assert.Single(qwen.Others)));
        Assert.Equal((true, false), (none.Listed, none.Loaded));
        Assert.Empty(none.Others);
        Assert.Null(OpenAiCompatibleModelFeed.Held(Runtime("local-gemma-answered.json"), Gemma));

        // A model of another kind loaded, the captured list with the embedding model's instance added, is among those
        // unloaded, so the runtime is left holding one model.
        var list = System.Text.Json.Nodes.JsonNode.Parse(Runtime("local-runtime-models-none-loaded.json"))!;
        var embedding = list["models"]!.AsArray().Single(model => (string?)model!["type"] == "embedding")!;

        embedding["loaded_instances"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["id"] = "text-embedding-nomic-embed-text-v1.5" });

        Assert.Equal(["text-embedding-nomic-embed-text-v1.5"], OpenAiCompatibleModelFeed.Held(list.ToJsonString(), Gemma)!.Others);
    }
}
