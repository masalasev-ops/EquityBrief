using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Providers;

// The local lane's feed, read against the responses captured from the operator's
// runtime before any of it was written: the thinking model's empty answer, the
// answer once reasoning was turned off, and the refusal of a prompt too large for
// the loaded context.
public class LocalModelFeedTests
{
    static string Folder() => Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder);

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    static LocalModelSettings Settings() => new(null, null, null, null, null);

    static ModelRequest Request(string prompt = "Keysight closed at 333.42.") =>
        new(SectionPrompt.Lane, "The key under each figure", LocalModelSettings.DefaultModel, [], SectionPrompt.Instructions, prompt);

    // ---- the parser, over the captures ----

    [Fact]
    public void TheThinkingCaptureIsRefusedWithItsFinishReasonRatherThanStoredEmpty()
    {
        var refused = Assert.Throws<ProviderRefusal>(() =>
            OpenAiCompatibleModelFeed.Parse(Captured("model-probe-thinking.json"), "The key under each figure"));

        // The capture spent its budget in reasoning and stopped on length, and the
        // refusal says both, because "empty" alone would not say why.
        using var capture = JsonDocument.Parse(Captured("model-probe-thinking.json"));

        var choice = capture.RootElement.GetProperty("choices")[0];
        var reasoning = choice.GetProperty("message").GetProperty("reasoning_content").GetString()!.Length;

        Assert.Equal(OpenAiCompatibleModelFeed.CutOff, choice.GetProperty("finish_reason").GetString());
        Assert.Contains($"the finish reason was {OpenAiCompatibleModelFeed.CutOff}", refused.Message, StringComparison.Ordinal);
        Assert.Contains($"{reasoning} character(s) of reasoning", refused.Message, StringComparison.Ordinal);
        Assert.False(refused.Transient);
    }

    [Fact]
    public void TheAnsweredCaptureIsReadAsTheAnswerTheModelAndTheTokenCounts()
    {
        var captured = Captured("model-probe-answered.json");

        using var capture = JsonDocument.Parse(captured);

        var root = capture.RootElement;
        var answer = OpenAiCompatibleModelFeed.Parse(captured, "The key under each figure");

        Assert.Equal(root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!.Trim(), answer.Text);
        Assert.Equal(root.GetProperty("model").GetString(), answer.Model);
        Assert.Equal(root.GetProperty("usage").GetProperty("prompt_tokens").GetInt32(), answer.PromptTokens);
        Assert.Equal(root.GetProperty("usage").GetProperty("completion_tokens").GetInt32(), answer.CompletionTokens);
        Assert.Equal("stop", answer.FinishReason);
        Assert.NotEmpty(answer.Text);
    }

    [Fact]
    public void AnAnswerThatStoppedOnItsBudgetIsRefusedRatherThanStoredCutShort()
    {
        // The answered capture with its finish reason set to what a budget running out
        // sends, which keeps the shape the runtime delivers and changes one value.
        var node = JsonNode.Parse(Captured("model-probe-answered.json"))!;

        node["choices"]![0]!["finish_reason"] = OpenAiCompatibleModelFeed.CutOff;

        var refused = Assert.Throws<ProviderRefusal>(() =>
            OpenAiCompatibleModelFeed.Parse(node.ToJsonString(), "The key under each figure"));

        Assert.Contains("stopped at its budget", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuntimeThatAnsweredWithReasoningInlineHasItTakenOut()
    {
        var node = JsonNode.Parse(Captured("model-probe-answered.json"))!;
        var text = node["choices"]![0]!["message"]!["content"]!.GetValue<string>();

        node["choices"]![0]!["message"]!["content"] = "<think>the close is listed</think>\n" + text;

        Assert.Equal(text.Trim(), OpenAiCompatibleModelFeed.Parse(node.ToJsonString(), "The key under each figure").Text);
    }

    // ---- the refusals the transport produces ----

    [Fact]
    public async Task APromptTooLargeForTheContextIsRefusedInTheRuntimesOwnWords()
    {
        var feed = new OpenAiCompatibleModelFeed(
            new HttpClient(new Answering(HttpStatusCode.BadRequest, Captured("model-probe-overflow.json")))
            {
                BaseAddress = new Uri(LocalModelSettings.DefaultBaseAddress),
            },
            Settings());

        var refused = await Assert.ThrowsAsync<ProviderRefusal>(() => feed.CompleteAsync(Request()));

        Assert.StartsWith("The local model refused The key under each figure with status 400: ", refused.Message, StringComparison.Ordinal);
        Assert.Contains("exceeds the available context size (50176 tokens)", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, feed.Requests);
    }

    [Fact]
    public void ARefusalWithNoWordsOfItsOwnSaysOnlyItsStatus()
    {
        Assert.Equal(
            "The local model refused The key under each figure with status 503.",
            OpenAiCompatibleModelFeed.Refused("The key under each figure", 503, "<html>Service Unavailable</html>"));

        Assert.Equal(
            "The local model refused The key under each figure with status 400: the prompt is too long",
            OpenAiCompatibleModelFeed.Refused("The key under each figure", 400, """{"error":{"message":"the prompt is too long"}}"""));
    }

    [Fact]
    public async Task NothingListeningIsItsOwnFailureRatherThanARefusalWithParticularWords()
    {
        var feed = new OpenAiCompatibleModelFeed(
            new HttpClient(new Unreachable()) { BaseAddress = new Uri(LocalModelSettings.DefaultBaseAddress) },
            Settings());

        var gone = await Assert.ThrowsAsync<LocalModelUnavailable>(() => feed.CompleteAsync(Request()));

        Assert.Contains("could not be reached for The key under each figure", gone.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestAsSentTurnsReasoningOffAndAsksForOneAnswerAtTemperatureZero()
    {
        using var body = JsonDocument.Parse(OpenAiCompatibleModelFeed.Body(Request()));

        var root = body.RootElement;

        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetInt32());
        Assert.Equal(OpenAiCompatibleModelFeed.AnswerTokens, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(["system", "user"], root.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString()!).ToArray());
    }

    // ---- the recording ----

    [Fact]
    public async Task ARequestTheRecordingDoesNotHoldIsRefusedByNameAndReachesNothing()
    {
        var feed = new RecordedLocalModelFeed(Folder());

        var request = Request("a prompt nobody recorded");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => feed.CompleteAsync(request));

        Assert.Contains(request.Key, refused.Message, StringComparison.Ordinal);
        Assert.Contains(request.Section, refused.Message, StringComparison.Ordinal);
        Assert.Contains(request.Model, refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, feed.Requests);

        // It holds nothing that could reach a network in place of the recording.
        Assert.DoesNotContain(
            typeof(RecordedLocalModelFeed).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            field => typeof(HttpClient).IsAssignableFrom(field.FieldType) || typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void TheKeyIsTheWholeRequestSoAnyFieldThatChangesIsARequestNobodyRecorded()
    {
        var request = Request();

        Assert.Equal(32, request.Key.Length);
        Assert.Equal(request.Key, Request().Key);

        Assert.NotEqual(request.Key, (request with { Section = "What the company sells" }).Key);
        Assert.NotEqual(request.Key, (request with { Model = "another/model" }).Key);
        Assert.NotEqual(request.Key, (request with { DocumentIds = ["d3f0e89926ea336cadd5fe9951d434e7"] }).Key);
        Assert.NotEqual(request.Key, (request with { System = SectionPrompt.Instructions + " " + SectionPrompt.Citing }).Key);
        Assert.NotEqual(request.Key, (request with { Prompt = request.Prompt + " " }).Key);
        Assert.NotEqual(request.Key, (request with { Lane = "paid" }).Key);
    }

    // ---- the key ----

    [Fact]
    public void AKeyConfiguredForTheLocalLaneIsRefusedWhereverTheLaneIsRead()
    {
        var direct = Assert.Throws<InvalidOperationException>(() => new LocalModelSettings(null, null, null, null, "sk-local"));

        Assert.Contains(LocalModelSettings.ApiKeyKey, direct.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-local", direct.Message, StringComparison.Ordinal);

        var configured = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>(LocalModelSettings.ApiKeyKey, "sk-local")]).Build();

        Assert.Throws<InvalidOperationException>(() => LocalLane.Settings(configured));

        // A fixture run refuses it too, because the refusal is about the configuration.
        Assert.Throws<InvalidOperationException>(() =>
            OnDemandFeeds.Resolve("fixture", Folder(), null, null, null, null, LocalLane.Settings(configured), ResearchModelFeedTests.Shipped()));

        // And a number that is not one is refused rather than read as the default.
        var mistyped = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>(LocalModelSettings.TimeoutKey, "5m")]).Build();

        Assert.Contains("'5m'", Assert.Throws<InvalidOperationException>(() => LocalLane.Settings(mistyped)).Message, StringComparison.Ordinal);

        // Blank, the defaults this machine was measured with.
        var defaults = LocalLane.Settings(new ConfigurationBuilder().Build());

        Assert.Equal(LocalModelSettings.DefaultBaseAddress, defaults.BaseAddress);
        Assert.Equal(LocalModelSettings.DefaultModel, defaults.Model);
        Assert.Equal(LocalModelSettings.DefaultContextTokens, defaults.ContextTokens);
    }

    [Fact]
    public void TheRunbookStatesEachLocalSettingAndItsDefaultAsTheCodeHoldsThem()
    {
        // Pinned rather than restated: each row of RUNBOOK's table names the key the
        // code reads and the default it falls back to, read off the document.
        var rows = Corpus.Read("docs/RUNBOOK.md").Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal)
                && (line.Contains("`EquityBrief:Models:Local:", StringComparison.Ordinal) || line.Contains("`EquityBrief:Models:LocalLane`", StringComparison.Ordinal)))
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToArray();

        string Default(string key) =>
            Assert.Single(rows, row => row[1] == $"`{key}`")[2].Trim('`');

        Assert.Equal(LocalModelSettings.DefaultBaseAddress, Default(LocalModelSettings.BaseAddressKey));
        Assert.Equal(LocalModelSettings.DefaultModel, Default(LocalModelSettings.ModelKey));
        Assert.Equal(LocalModelSettings.DefaultTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), Default(LocalModelSettings.TimeoutKey));
        Assert.Equal(LocalModelSettings.DefaultContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture), Default(LocalModelSettings.ContextTokensKey));

        // The lane's row names its key first and the default sections in the order the
        // code holds them, in the figure's names with the first letter lowered.
        var lane = Assert.Single(rows, row => row[1].StartsWith($"`{LocalLane.SectionsKey}`", StringComparison.Ordinal));

        Assert.Equal(
            string.Join(", ", ProseWriter.DefaultLane.Select(section => char.ToLowerInvariant(section[0]) + section[1..])),
            lane[2]);

        // And the refused key is the one the settings refuse.
        Assert.Contains($"`{LocalModelSettings.ApiKeyKey}` is refused", Corpus.Read("docs/RUNBOOK.md"), StringComparison.Ordinal);

        Assert.Equal(5, rows.Length);
    }

    sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
    }
}
