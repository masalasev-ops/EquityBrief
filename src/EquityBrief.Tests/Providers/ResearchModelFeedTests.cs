using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Providers;

// The research model's feed, read against the responses captured from the provider
// before any of it was written, and priced by the rates read off the provider's page.
public class ResearchModelFeedTests
{
    static string Folder() => Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder);

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    // A key no test sends anywhere. The settings refuse a blank one, so a test that
    // builds settings has to hand it something.
    const string NotAKey = "a key no test sends";

    static ResearchModelSettings Settings(string? thinking = null, string? model = null) =>
        new(null, model, thinking, null, NotAKey);

    // The request the two recordings answer, as the capture built it.
    internal static ModelRequest Recorded(ResearchModelSettings settings) =>
        new(
            "paid",
            "The two cases",
            settings.Identity,
            [],
            SectionPrompt.Instructions,
            "Company: KEYS\nFacts:\n- close = 333.42\nSay what the close was, in one sentence.");

    // ---- the parser, over the captures ----

    [Fact]
    public void TheDefaultModeCaptureIsReadAsItsAnswerWithTheReasoningCountedAndNotStored()
    {
        using var capture = JsonDocument.Parse(Captured("research-probe-thinking.json"));

        var root = capture.RootElement;
        var usage = root.GetProperty("usage");
        var answer = DeepSeekModelFeed.Parse(Captured("research-probe-thinking.json"), "The two cases");

        Assert.Equal(root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString(), answer.Text);
        Assert.DoesNotContain(root.GetProperty("choices")[0].GetProperty("message").GetProperty("reasoning_content").GetString()!, answer.Text, StringComparison.Ordinal);
        Assert.Equal(usage.GetProperty("prompt_tokens").GetInt32(), answer.PromptTokens);
        Assert.Equal(usage.GetProperty("prompt_cache_hit_tokens").GetInt32(), answer.CacheHitTokens);
        Assert.Equal(usage.GetProperty("prompt_cache_miss_tokens").GetInt32(), answer.CacheMissTokens);
        Assert.Equal(usage.GetProperty("completion_tokens").GetInt32(), answer.CompletionTokens);
        Assert.Equal(usage.GetProperty("completion_tokens_details").GetProperty("reasoning_tokens").GetInt32(), answer.ReasoningTokens);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("created").GetInt64()), answer.Created);
        Assert.Equal(root.GetProperty("model").GetString(), answer.Model);

        // The reasoning is inside the completion count, which is what the provider
        // bills, so the prompt and the completion are the whole of the total.
        Assert.Equal(usage.GetProperty("total_tokens").GetInt32(), answer.PromptTokens + answer.CompletionTokens);
    }

    [Fact]
    public void TheThinkingOffCaptureCarriesNoReasoningAndIsReadWithNone()
    {
        var answer = DeepSeekModelFeed.Parse(Captured("research-probe-answered.json"), "The two cases");

        Assert.Equal(0, answer.ReasoningTokens);
        Assert.Equal(answer.CacheHitTokens + answer.CacheMissTokens, answer.PromptTokens);
        Assert.Equal("stop", answer.FinishReason);
    }

    [Fact]
    public void AnAnswerThatCannotBePricedOrWasCutShortIsRefused()
    {
        // Each is the answered capture with one thing changed, keeping the shape the
        // provider sends.
        var noUsage = JsonNode.Parse(Captured("research-probe-answered.json"))!;
        noUsage.AsObject().Remove("usage");

        Assert.Contains("cannot be priced", Assert.Throws<ProviderRefusal>(() => DeepSeekModelFeed.Parse(noUsage.ToJsonString(), "The two cases")).Message, StringComparison.Ordinal);

        var noInstant = JsonNode.Parse(Captured("research-probe-answered.json"))!;
        noInstant.AsObject().Remove("created");

        Assert.Contains("creation instant", Assert.Throws<ProviderRefusal>(() => DeepSeekModelFeed.Parse(noInstant.ToJsonString(), "The two cases")).Message, StringComparison.Ordinal);

        var cut = JsonNode.Parse(Captured("research-probe-answered.json"))!;
        cut["choices"]![0]!["finish_reason"] = "length";

        Assert.Contains("stopped at its budget", Assert.Throws<ProviderRefusal>(() => DeepSeekModelFeed.Parse(cut.ToJsonString(), "The two cases")).Message, StringComparison.Ordinal);

        var empty = JsonNode.Parse(Captured("research-probe-thinking.json"))!;
        empty["choices"]![0]!["message"]!["content"] = "";

        Assert.Contains("returned no answer", Assert.Throws<ProviderRefusal>(() => DeepSeekModelFeed.Parse(empty.ToJsonString(), "The two cases")).Message, StringComparison.Ordinal);
    }

    // ---- the price, derived by hand from the page ----

    [Fact]
    public void ACallIsPricedFromItsOwnCountsAtThePagesRatesDoubledAtPeak()
    {
        // The page, read on 2026-09-13: deepseek-flash at 0.003 a million cached prompt
        // tokens, 0.15 uncached and 0.60 output, off-peak, and twice that at peak.
        // Worked by hand from the recording's own counts rather than read back.
        var thinking = DeepSeekModelFeed.Parse(Captured(RecordedResearchModelFeed.FileFor(Recorded(Settings()))), "The two cases");

        Assert.Equal(DayOfWeek.Sunday, thinking.Created.UtcDateTime.DayOfWeek);
        Assert.False(DeepSeekModelFeed.IsPeak(thinking.Created));

        var byHand = (thinking.CacheHitTokens * 0.003m + thinking.CacheMissTokens * 0.15m + thinking.CompletionTokens * 0.60m) / 1_000_000m;

        Assert.Equal(byHand, DeepSeekModelFeed.PriceOf("deepseek-flash", thinking));
        Assert.Equal(0.0001395m, byHand);

        // The same counts stamped inside a peak window cost twice as much, and the
        // other model's rates apply to it where it was the model asked for.
        var atPeak = thinking with { Created = DateTimeOffset.Parse("2026-09-14T01:30:00Z", CultureInfo.InvariantCulture) };

        Assert.Equal(byHand * 2m, DeepSeekModelFeed.PriceOf("deepseek-flash", atPeak));
        Assert.Equal(
            (thinking.CacheHitTokens * 0.022m + thinking.CacheMissTokens * 0.66m + thinking.CompletionTokens * 1.98m) / 1_000_000m,
            DeepSeekModelFeed.PriceOf("deepseek-v4-pro", thinking));

        Assert.Throws<InvalidOperationException>(() => DeepSeekModelFeed.PriceOf("a model nobody priced", thinking));
    }

    [Theory]
    [InlineData("2026-09-14T00:59:59Z", false)]
    [InlineData("2026-09-14T01:00:00Z", true)]
    [InlineData("2026-09-14T03:59:59Z", true)]
    [InlineData("2026-09-14T04:00:00Z", false)]
    [InlineData("2026-09-14T05:59:59Z", false)]
    [InlineData("2026-09-14T06:00:00Z", true)]
    [InlineData("2026-09-14T09:59:59Z", true)]
    [InlineData("2026-09-14T10:00:00Z", false)]
    [InlineData("2026-09-18T02:00:00Z", true)]
    [InlineData("2026-09-19T02:00:00Z", false)]
    [InlineData("2026-09-20T07:00:00Z", false)]
    public void PeakIsTheTwoWeekdayWindowsThePageStatesInUtc(string instant, bool peak)
    {
        // Monday the 14th, Friday the 18th, Saturday the 19th and Sunday the 20th.
        Assert.Equal(peak, DeepSeekModelFeed.IsPeak(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ACallsCeilingIsEveryByteAsATokenAndTheWholeBudgetAtPeakAndHoldsOverTheRecordings()
    {
        var settings = Settings();
        var request = Recorded(settings);

        var bytes = Encoding.UTF8.GetByteCount(request.System) + Encoding.UTF8.GetByteCount(request.Prompt);
        var byHand = ((bytes + DeepSeekModelFeed.TemplateTokens) * 0.15m + DeepSeekModelFeed.AnswerTokens * 0.60m) * 2m / 1_000_000m;

        Assert.Equal(byHand, DeepSeekModelFeed.CeilingOf("deepseek-flash", request));

        // A ceiling that is not one would let a call spend past a cap, so it is held
        // above what each recorded call cost, in both modes, as if it had been at peak.
        foreach (var mode in new[] { ResearchModelSettings.Enabled, ResearchModelSettings.Disabled })
        {
            var recorded = Recorded(Settings(mode));
            var answer = DeepSeekModelFeed.Parse(Captured(RecordedResearchModelFeed.FileFor(recorded)), recorded.Section);

            Assert.True(answer.PromptTokens <= Encoding.UTF8.GetByteCount(recorded.System) + Encoding.UTF8.GetByteCount(recorded.Prompt) + DeepSeekModelFeed.TemplateTokens);
            Assert.True(DeepSeekModelFeed.PriceOf("deepseek-flash", answer) * DeepSeekModelFeed.PeakMultiple <= DeepSeekModelFeed.CeilingOf("deepseek-flash", recorded));
        }
    }

    // ---- the request and the transport ----

    [Fact]
    public void TheRequestAsSentNamesTheProvidersModelAndItsModeAndTakesATemperatureOnlyWithoutThinking()
    {
        using var thinking = JsonDocument.Parse(DeepSeekModelFeed.Body(Recorded(Settings()), Settings()));
        using var plain = JsonDocument.Parse(DeepSeekModelFeed.Body(Recorded(Settings(ResearchModelSettings.Disabled)), Settings(ResearchModelSettings.Disabled)));

        Assert.Equal("deepseek-flash", thinking.RootElement.GetProperty("model").GetString());
        Assert.Equal(ResearchModelSettings.Enabled, thinking.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(thinking.RootElement.TryGetProperty("temperature", out _));

        Assert.Equal(ResearchModelSettings.Disabled, plain.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(0, plain.RootElement.GetProperty("temperature").GetInt32());

        Assert.All(new[] { thinking, plain }, body =>
        {
            Assert.Equal(DeepSeekModelFeed.AnswerTokens, body.RootElement.GetProperty("max_tokens").GetInt32());
            Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal(["system", "user"], body.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString()!).ToArray());
        });

        // The identity a section stores carries the mode; the model sent does not.
        Assert.Equal("deepseek-flash thinking", Settings().Identity);
        Assert.Equal("deepseek-flash", Settings(ResearchModelSettings.Disabled).Identity);
        Assert.NotEqual(Recorded(Settings()).Key, Recorded(Settings(ResearchModelSettings.Disabled)).Key);
    }

    [Fact]
    public async Task TheLiveFeedSendsTheKeyInTheHeaderToTheCompletionsPathAndNeverInTheAddress()
    {
        var seen = new Seeing(Captured(RecordedResearchModelFeed.FileFor(Recorded(Settings()))));

        using var client = new HttpClient(seen) { BaseAddress = new Uri(ResearchModelSettings.DefaultBaseAddress) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NotAKey);

        var feed = new DeepSeekModelFeed(client, Settings());
        var answer = await feed.CompleteAsync(Recorded(Settings()));

        Assert.Equal("The close was 333.42.", answer.Text);
        Assert.Equal(1, feed.Requests);
        Assert.Equal("https://api.deepseek.com/chat/completions", seen.Address);
        Assert.DoesNotContain(NotAKey, seen.Address, StringComparison.Ordinal);
        Assert.Equal("Bearer", seen.Scheme);

        // And the settings never render the key.
        Assert.DoesNotContain(NotAKey, Settings().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingListeningIsItsOwnFailureAndARefusalCarriesTheProvidersWords()
    {
        using var gone = new HttpClient(new Unreachable()) { BaseAddress = new Uri(ResearchModelSettings.DefaultBaseAddress) };

        await Assert.ThrowsAsync<ResearchModelUnavailable>(() => new DeepSeekModelFeed(gone, Settings()).CompleteAsync(Recorded(Settings())));

        Assert.Equal(
            "The research model refused The two cases with status 402: Insufficient Balance",
            DeepSeekModelFeed.Refused("The two cases", 402, """{"error":{"message":"Insufficient Balance","type":"unknown_error"}}"""));
        Assert.Equal("The research model refused The two cases with status 500.", DeepSeekModelFeed.Refused("The two cases", 500, "<html></html>"));
    }

    // ---- the settings ----

    [Fact]
    public void ABlankKeyIsRefusedByNameAtStartupAndSoIsAProviderOrAModelNothingImplements()
    {
        var blank = Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(new ConfigurationBuilder().Build()));

        Assert.Contains(ResearchModelSettings.ApiKeyName, blank.Message, StringComparison.Ordinal);

        IConfiguration With(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(
                values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))
                    .Append(new KeyValuePair<string, string?>(ResearchModelSettings.ApiKeyName, NotAKey))).Build();

        Assert.Contains("'another provider'", Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(With((ResearchModelSettings.ProviderKey, "another provider")))).Message, StringComparison.Ordinal);
        Assert.Contains("carries no price", Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(With((ResearchModelSettings.ModelKey, "deepseek-reasoner")))).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(With((ResearchModelSettings.ThinkingKey, "sometimes"))));
        Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(With((ResearchModelSettings.TimeoutKey, "10m"))));

        // Named, the provider is read without regard to case, and every model the page
        // prices is one a setting may name.
        var named = ResearchLane.Settings(With((ResearchModelSettings.ProviderKey, "DeepSeek"), (ResearchModelSettings.ModelKey, "deepseek-v4-pro"), (ResearchModelSettings.ThinkingKey, "Disabled")));

        Assert.Equal("deepseek-v4-pro", named.Identity);
        Assert.Equal(["deepseek-flash", "deepseek-v4-pro"], DeepSeekModelFeed.Rates.Keys.Order(StringComparer.Ordinal).ToArray());

        // The models the build prices are the models the provider's own list carries.
        using var listed = JsonDocument.Parse(Captured("research-probe-models.json"));

        Assert.Equal(
            listed.RootElement.GetProperty("data").EnumerateArray().Select(model => model.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray(),
            DeepSeekModelFeed.Rates.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void TheRunbookStatesEachResearchSettingAndCapWithItsDefaultAsTheCodeHoldsThem()
    {
        var runbook = Checks.Corpus.Read("docs/RUNBOOK.md");

        var rows = runbook.Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal)
                && (line.Contains("`EquityBrief:Models:Research:", StringComparison.Ordinal) || line.Contains("`EquityBrief:Spend:", StringComparison.Ordinal)))
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToArray();

        string Default(string key) => Assert.Single(rows, row => row[1] == $"`{key}`")[2].Trim('`');

        Assert.Equal(6, rows.Length);
        Assert.Equal(ResearchModelSettings.DeepSeek, Default(ResearchModelSettings.ProviderKey));
        Assert.Equal(ResearchModelSettings.DefaultModel, Default(ResearchModelSettings.ModelKey));
        Assert.Equal(ResearchModelSettings.Enabled, Default(ResearchModelSettings.ThinkingKey));
        Assert.Equal(ResearchModelSettings.DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture), Default(ResearchModelSettings.TimeoutKey));
        Assert.Equal(Core.Spending.SpendCaps.DefaultDay, decimal.Parse(Default(Core.Spending.SpendCaps.DayKey), CultureInfo.InvariantCulture));
        Assert.Equal(Core.Spending.SpendCaps.DefaultMonth, decimal.Parse(Default(Core.Spending.SpendCaps.MonthKey), CultureInfo.InvariantCulture));

        // The key row names the path the settings read.
        Assert.Contains($"| DeepSeek | `{ResearchModelSettings.ApiKeyName}` | `EquityBrief.Worker` |", runbook, StringComparison.Ordinal);
    }

    // ---- the recording ----

    [Fact]
    public async Task TheRecordingAnswersTheRequestItHoldsAndRefusesByNameTheOneItDoesNot()
    {
        var feed = new RecordedResearchModelFeed(Folder(), Settings());

        Assert.Equal("The close was 333.42.", (await feed.CompleteAsync(Recorded(Settings()))).Text);

        var unrecorded = Recorded(Settings()) with { Prompt = "a prompt nobody recorded" };
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => feed.CompleteAsync(unrecorded));

        Assert.Contains(unrecorded.Key, refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, feed.Requests);

        Assert.DoesNotContain(
            typeof(RecordedResearchModelFeed).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            field => typeof(HttpClient).IsAssignableFrom(field.FieldType) || typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType));
    }

    sealed class Seeing(string body) : HttpMessageHandler
    {
        public string Address { get; private set; } = string.Empty;

        public string Scheme { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri!.ToString();
            Scheme = request.Headers.Authorization?.Scheme ?? string.Empty;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
    }
}
