using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Spending;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Providers;

// The research model's feed, read against the responses captured from the provider the
// shipped configuration names before any of it was written, and priced at the rates that
// configuration states. Nothing here, and nothing it tests, names a provider in code.
public class ResearchModelFeedTests
{
    static string Folder() => Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder);

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    static string ShippedConfiguration => Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "appsettings.json");

    // A key no test sends anywhere. The settings refuse a blank one, so a test that
    // builds settings has to hand it something.
    const string NotAKey = "a key no test sends";

    // The options the second recording was asked with.
    internal const string ThinkingOff = "{\"thinking\":{\"type\":\"disabled\"}}";

    // The settings the shipped configuration gives, with a key that is not one and the
    // options a test asks for, read through the path the worker reads them through.
    internal static ResearchModelSettings Shipped(string? options = null, params (string Key, string Value)[] overrides) =>
        ResearchLane.Settings(new ConfigurationBuilder()
            .AddJsonFile(ShippedConfiguration)
            .AddInMemoryCollection(
                overrides.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))
                    .Append(new KeyValuePair<string, string?>(ResearchModelSettings.ApiKeyKey, NotAKey))
                    .Append(new KeyValuePair<string, string?>(ResearchModelSettings.OptionsKey, options ?? string.Empty)))
            .Build());

    // The request the two recordings answer, as the capture built it.
    internal static ModelRequest Recorded(ResearchModelSettings settings) =>
        new(
            "paid",
            "The two cases",
            settings.Identity,
            [],
            SectionPrompt.Instructions,
            "Company: KEYS\nFacts:\n- close = 333.42\nSay what the close was, in one sentence.");

    static ResearchAnswer RecordedAnswer(ResearchModelSettings settings) =>
        OpenAiCompatibleResearchFeed.Parse(Captured(RecordedResearchModelFeed.FileFor(Recorded(settings))), "The two cases");

    // ---- the parser, over the captures ----

    [Fact]
    public void TheDefaultModeCaptureIsReadAsItsAnswerWithTheReasoningCountedAndNotStored()
    {
        using var capture = JsonDocument.Parse(Captured("research-probe-thinking.json"));

        var root = capture.RootElement;
        var usage = root.GetProperty("usage");
        var answer = OpenAiCompatibleResearchFeed.Parse(Captured("research-probe-thinking.json"), "The two cases");

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
        var answer = OpenAiCompatibleResearchFeed.Parse(Captured("research-probe-answered.json"), "The two cases");

        Assert.Equal(0, answer.ReasoningTokens);
        Assert.Equal(answer.CacheHitTokens + answer.CacheMissTokens, answer.PromptTokens);
        Assert.Equal("stop", answer.FinishReason);
    }

    [Fact]
    public void AProviderReportingItsCachedPromptInTheFormatsOwnFieldIsReadTheSameWay()
    {
        // The captured provider sends its own cache fields and the format's; a provider
        // sending only the format's is read from that, with the uncached prompt the rest.
        var node = JsonNode.Parse(Captured("research-probe-answered.json"))!;
        var usage = node["usage"]!.AsObject();

        usage.Remove("prompt_cache_hit_tokens");
        usage.Remove("prompt_cache_miss_tokens");
        usage["prompt_tokens_details"] = new JsonObject { ["cached_tokens"] = 20 };

        var answer = OpenAiCompatibleResearchFeed.Parse(node.ToJsonString(), "The two cases");

        Assert.Equal(20, answer.CacheHitTokens);
        Assert.Equal(answer.PromptTokens - 20, answer.CacheMissTokens);
    }

    [Fact]
    public void AnAnswerThatCannotBePricedOrWasCutShortIsRefused()
    {
        var noUsage = JsonNode.Parse(Captured("research-probe-answered.json"))!;
        noUsage.AsObject().Remove("usage");

        Assert.Contains("cannot be priced", Assert.Throws<ProviderRefusal>(() => OpenAiCompatibleResearchFeed.Parse(noUsage.ToJsonString(), "The two cases")).Message, StringComparison.Ordinal);

        var noInstant = JsonNode.Parse(Captured("research-probe-answered.json"))!;
        noInstant.AsObject().Remove("created");

        Assert.Contains("creation instant", Assert.Throws<ProviderRefusal>(() => OpenAiCompatibleResearchFeed.Parse(noInstant.ToJsonString(), "The two cases")).Message, StringComparison.Ordinal);

        // An answer cut off at its budget, or with nothing in it, was still counted and
        // billed, so it is refused carrying its counts for the spend cap to price.
        var cut = JsonNode.Parse(Captured("research-probe-answered.json"))!;
        cut["choices"]![0]!["finish_reason"] = "length";

        var stopped = Assert.Throws<UnusableResearchAnswer>(() => OpenAiCompatibleResearchFeed.Parse(cut.ToJsonString(), "The two cases"));

        Assert.Contains("stopped at its budget", stopped.Message, StringComparison.Ordinal);
        Assert.Equal(cut["usage"]!["prompt_cache_miss_tokens"]!.GetValue<int>(), stopped.Answer.CacheMissTokens);
        Assert.Equal(cut["usage"]!["completion_tokens"]!.GetValue<int>(), stopped.Answer.CompletionTokens);

        var empty = JsonNode.Parse(Captured("research-probe-thinking.json"))!;
        empty["choices"]![0]!["message"]!["content"] = "";

        var nothing = Assert.Throws<UnusableResearchAnswer>(() => OpenAiCompatibleResearchFeed.Parse(empty.ToJsonString(), "The two cases"));

        Assert.Contains("returned no answer", nothing.Message, StringComparison.Ordinal);
        Assert.True(nothing.Answer.ReasoningTokens > 0);
    }

    // ---- the price, derived by hand from the configured rates ----

    [Fact]
    public void ACallIsPricedFromItsOwnCountsAtTheConfiguredRatesMultipliedAtPeak()
    {
        // The shipped configuration's rates, from the provider's page read on 2026-09-13:
        // 0.003 a million cached prompt tokens, 0.15 uncached and 0.60 output, doubled at
        // peak. Worked by hand from the recording's own counts rather than read back.
        var settings = Shipped();
        var answer = RecordedAnswer(settings);

        Assert.Equal(DayOfWeek.Sunday, answer.Created.UtcDateTime.DayOfWeek);
        Assert.False(settings.Pricing.IsPeak(answer.Created));

        var byHand = (answer.CacheHitTokens * 0.003m + answer.CacheMissTokens * 0.15m + answer.CompletionTokens * 0.60m) / 1_000_000m;

        Assert.Equal(byHand, settings.Pricing.Price(answer));
        Assert.Equal(0.0001731m, byHand);

        // The same counts stamped inside a peak window cost twice as much.
        Assert.Equal(byHand * 2m, settings.Pricing.Price(answer with { Created = DateTimeOffset.Parse("2026-09-14T01:30:00Z", CultureInfo.InvariantCulture) }));

        // Every captured call arrived with nothing cached, so the cached rate is held over
        // the same answer with part of its prompt served from the cache: 150 of 182 cached
        // is 150 at 0.003 and 32 at 0.15, beside the same 243 of output.
        var cached = answer with { CacheHitTokens = 150, CacheMissTokens = 32 };

        Assert.Equal(0.00015105m, settings.Pricing.Price(cached));

        // A provider with no peak pricing names no windows, and no instant is priced up.
        var flat = new ResearchPricing(0.003m, 0.15m, 0.60m, [], [], 2m);

        Assert.Equal(byHand, flat.Price(answer with { Created = DateTimeOffset.Parse("2026-09-14T01:30:00Z", CultureInfo.InvariantCulture) }));
        Assert.Equal(1m, flat.PeakMultiple);
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
    public void PeakIsTheConfiguredWindowsOnTheConfiguredDaysInUtc(string instant, bool peak)
    {
        // Monday the 14th, Friday the 18th, Saturday the 19th and Sunday the 20th.
        Assert.Equal(peak, Shipped().Pricing.IsPeak(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ACallsCeilingIsEveryByteAsATokenAndTheWholeBudgetAtPeakAndHoldsOverTheRecordings()
    {
        var settings = Shipped();
        var request = Recorded(settings);

        var bytes = Encoding.UTF8.GetByteCount(request.System) + Encoding.UTF8.GetByteCount(request.Prompt);
        var byHand = ((bytes + ResearchPricing.TemplateTokens) * 0.15m + settings.AnswerTokens * 0.60m) * 2m / 1_000_000m;

        Assert.Equal(byHand, settings.Pricing.Ceiling(request, settings.AnswerTokens));

        // A ceiling that is not one would let a call spend past a cap, so it is held above
        // what each recorded call cost as if it had been at peak.
        foreach (var options in new[] { (string?)null, ThinkingOff })
        {
            var asked = Shipped(options);
            var recorded = Recorded(asked);
            var answer = RecordedAnswer(asked);

            Assert.True(answer.PromptTokens <= Encoding.UTF8.GetByteCount(recorded.System) + Encoding.UTF8.GetByteCount(recorded.Prompt) + ResearchPricing.TemplateTokens);
            Assert.True(asked.Pricing.Price(answer) * asked.Pricing.PeakMultiple <= asked.Pricing.Ceiling(recorded, asked.AnswerTokens));
        }
    }

    // ---- the request and the transport ----

    [Fact]
    public void TheRequestAsSentCarriesTheConfiguredModelAndBudgetAndTheProvidersOptionsBesideThem()
    {
        using var plain = JsonDocument.Parse(OpenAiCompatibleResearchFeed.Body(Recorded(Shipped()), Shipped()));
        using var asked = JsonDocument.Parse(OpenAiCompatibleResearchFeed.Body(Recorded(Shipped(ThinkingOff)), Shipped(ThinkingOff)));

        // With no options, the four fields the feed owns and nothing else.
        Assert.Equal(["model", "messages", "max_tokens", "stream"], plain.RootElement.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.Equal(Shipped().Model, plain.RootElement.GetProperty("model").GetString());
        Assert.Equal(Shipped().AnswerTokens, plain.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(plain.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(["system", "user"], plain.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString()!).ToArray());

        // With options, the provider's own fields beside those four, exactly as written.
        Assert.Equal("disabled", asked.RootElement.GetProperty("thinking").GetProperty("type").GetString());

        // And the identity a section stores carries the options, so the two are two
        // writers with two recordings.
        Assert.Equal(Shipped().Model, Shipped().Identity);
        Assert.Equal(Shipped().Model + " " + ThinkingOff, Shipped(ThinkingOff).Identity);
        Assert.NotEqual(Recorded(Shipped()).Key, Recorded(Shipped(ThinkingOff)).Key);
    }

    [Fact]
    public void SwitchingModelIsAChangeToConfigurationAlone()
    {
        // Another provider, another model and other prices, named in configuration and
        // nowhere else: the same feed sends to that address, asks for that model with that
        // provider's options, and prices at those rates.
        var elsewhere = Shipped(
            "{\"reasoning_effort\":\"low\"}",
            (ResearchModelSettings.BaseAddressKey, "https://models.example.test/v1"),
            (ResearchModelSettings.ModelKey, "another-model"),
            (ResearchModelSettings.CacheHitKey, "0.5"),
            (ResearchModelSettings.CacheMissKey, "1.25"),
            (ResearchModelSettings.OutputKey, "10"));

        Assert.Equal("https://models.example.test/v1/", elsewhere.BaseAddress);
        Assert.Equal("another-model {\"reasoning_effort\":\"low\"}", elsewhere.Identity);

        using var body = JsonDocument.Parse(OpenAiCompatibleResearchFeed.Body(Recorded(elsewhere), elsewhere));

        Assert.Equal("another-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("low", body.RootElement.GetProperty("reasoning_effort").GetString());

        var answer = RecordedAnswer(Shipped());

        Assert.Equal((answer.CacheMissTokens * 1.25m + answer.CompletionTokens * 10m) / 1_000_000m, elsewhere.Pricing.Price(answer));

        var live = OpenAiCompatibleResearchFeed.Live(elsewhere);

        Assert.Equal("another-model {\"reasoning_effort\":\"low\"}", live.Identity);
        Assert.IsType<OpenAiCompatibleResearchFeed>(ResearchModelFeeds.Live(elsewhere));
    }

    [Fact]
    public async Task TheLiveFeedSendsTheKeyInTheHeaderToTheConfiguredAddressAndNeverInTheAddress()
    {
        var settings = Shipped();
        var seen = new Seeing(Captured(RecordedResearchModelFeed.FileFor(Recorded(settings))));

        using var client = new HttpClient(seen) { BaseAddress = new Uri(settings.BaseAddress) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NotAKey);

        var feed = new OpenAiCompatibleResearchFeed(client, settings);
        var answer = await feed.CompleteAsync(Recorded(settings));

        Assert.Equal("KEYS closed at 333.42.", answer.Text);
        Assert.Equal(1, feed.Requests);
        Assert.Equal(settings.BaseAddress + OpenAiCompatibleResearchFeed.Path, seen.Address);
        Assert.DoesNotContain(NotAKey, seen.Address, StringComparison.Ordinal);
        Assert.Equal("Bearer", seen.Scheme);

        Assert.DoesNotContain(NotAKey, settings.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingListeningIsItsOwnFailureAndARefusalCarriesTheProvidersWords()
    {
        var settings = Shipped();

        using var gone = new HttpClient(new Unreachable()) { BaseAddress = new Uri(settings.BaseAddress) };

        await Assert.ThrowsAsync<ResearchModelUnavailable>(() => new OpenAiCompatibleResearchFeed(gone, settings).CompleteAsync(Recorded(settings)));

        Assert.Equal(
            "The research model refused The two cases with status 402: Insufficient Balance",
            OpenAiCompatibleResearchFeed.Refused("The two cases", 402, """{"error":{"message":"Insufficient Balance","type":"unknown_error"}}"""));
        Assert.Equal("The research model refused The two cases with status 500.", OpenAiCompatibleResearchFeed.Refused("The two cases", 500, "<html></html>"));
    }

    // ---- the settings ----

    [Fact]
    public void WhatNobodyConfiguredIsRefusedByNameAtStartup()
    {
        var blankKey = Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(new ConfigurationBuilder().AddJsonFile(ShippedConfiguration).Build()));

        Assert.Contains(ResearchModelSettings.ApiKeyKey, blankKey.Message, StringComparison.Ordinal);

        // A key written into a file and left empty, or holding only spaces, is the likelier
        // mistake than no key at all, and it is refused the same way.
        foreach (var blank in new[] { "", "   " })
        {
            var written = new ConfigurationBuilder()
                .AddJsonFile(ShippedConfiguration)
                .AddInMemoryCollection([new KeyValuePair<string, string?>(ResearchModelSettings.ApiKeyKey, blank)])
                .Build();

            Assert.Contains(ResearchModelSettings.ApiKeyKey, Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(written)).Message, StringComparison.Ordinal);
        }

        string Refusal(string key, string value) =>
            Assert.Throws<InvalidOperationException>(() => Shipped(null, (key, value))).Message;

        Assert.Contains("'another format'", Refusal(ResearchModelSettings.FormatKey, "another format"), StringComparison.Ordinal);
        Assert.Contains(ResearchModelSettings.BaseAddressKey, Refusal(ResearchModelSettings.BaseAddressKey, "api.example.test"), StringComparison.Ordinal);
        Assert.Contains("'5m'", Refusal(ResearchModelSettings.TimeoutKey, "5m"), StringComparison.Ordinal);
        Assert.Contains("'about a dollar'", Refusal(ResearchModelSettings.OutputKey, "about a dollar"), StringComparison.Ordinal);
        Assert.Contains("'1am-4am'", Refusal(ResearchModelSettings.PeakHoursKey + ":0", "1am-4am"), StringComparison.Ordinal);
        Assert.Contains("'Someday'", Refusal(ResearchModelSettings.PeakDaysKey + ":0", "Someday"), StringComparison.Ordinal);

        Assert.Contains("not a JSON object", Assert.Throws<InvalidOperationException>(() => Shipped("[\"thinking\"]")).Message, StringComparison.Ordinal);
        Assert.Contains("sets model", Assert.Throws<InvalidOperationException>(() => Shipped("{\"model\":\"something else\"}")).Message, StringComparison.Ordinal);

        // A model with no prices at all is refused rather than called at nothing.
        var unpriced = new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>(ResearchModelSettings.BaseAddressKey, "https://models.example.test/v1"),
            new KeyValuePair<string, string?>(ResearchModelSettings.ModelKey, "another-model"),
            new KeyValuePair<string, string?>(ResearchModelSettings.ApiKeyKey, NotAKey),
        ]).Build();

        Assert.Contains("No prices", Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(unpriced)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedModelIsOneTheProvidersOwnListCarries()
    {
        using var listed = JsonDocument.Parse(Captured("research-probe-models.json"));

        Assert.Contains(
            Shipped().Model,
            listed.RootElement.GetProperty("data").EnumerateArray().Select(model => model.GetProperty("id").GetString()!));
    }

    [Fact]
    public void TheRunbookStatesEachResearchSettingAndCapWithTheValueTheShippedConfigurationHolds()
    {
        var runbook = Corpus.Read("docs/RUNBOOK.md");

        var rows = runbook.Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal)
                && (line.Contains("`EquityBrief:Models:Research:", StringComparison.Ordinal) || line.Contains("`EquityBrief:Spend:", StringComparison.Ordinal)))
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToArray();

        // Twelve research settings, two caps, and the key's row in the secrets table.
        Assert.Equal(15, rows.Length);

        var shipped = new ConfigurationBuilder().AddJsonFile(ShippedConfiguration).Build();

        string Stated(string key) => Assert.Single(rows, row => row[1] == $"`{key}`")[2].Trim('`');

        foreach (var key in new[]
        {
            ResearchModelSettings.FormatKey,
            ResearchModelSettings.BaseAddressKey,
            ResearchModelSettings.ModelKey,
            ResearchModelSettings.TimeoutKey,
            ResearchModelSettings.AnswerTokensKey,
            ResearchModelSettings.CacheHitKey,
            ResearchModelSettings.CacheMissKey,
            ResearchModelSettings.OutputKey,
            ResearchModelSettings.PeakMultipleKey,
        })
        {
            Assert.Equal(shipped[key], Stated(key));
        }

        Assert.Equal(
            string.Join(", ", shipped.GetSection(ResearchModelSettings.PeakHoursKey).GetChildren().Select(child => child.Value)),
            Stated(ResearchModelSettings.PeakHoursKey));
        Assert.Equal(
            string.Join(", ", shipped.GetSection(ResearchModelSettings.PeakDaysKey).GetChildren().Select(child => child.Value)),
            Stated(ResearchModelSettings.PeakDaysKey));

        Assert.Equal(SpendCaps.DefaultDay, decimal.Parse(Stated(SpendCaps.DayKey), CultureInfo.InvariantCulture));
        Assert.Equal(SpendCaps.DefaultMonth, decimal.Parse(Stated(SpendCaps.MonthKey), CultureInfo.InvariantCulture));

        // The options row states none, and the key row names the path the settings read.
        Assert.Equal("none", Stated(ResearchModelSettings.OptionsKey));
        Assert.Contains($"`{ResearchModelSettings.ApiKeyKey}`", runbook, StringComparison.Ordinal);
    }

    // ---- the recording ----

    [Fact]
    public async Task TheRecordingAnswersTheRequestItHoldsAndRefusesByNameTheOneItDoesNot()
    {
        var settings = Shipped();
        var feed = new RecordedResearchModelFeed(Folder(), settings);

        // Each recording answers the request asked the way it was recorded.
        Assert.Equal("KEYS closed at 333.42.", (await feed.CompleteAsync(Recorded(settings))).Text);
        Assert.Equal("The close was 333.42.", (await new RecordedResearchModelFeed(Folder(), Shipped(ThinkingOff)).CompleteAsync(Recorded(Shipped(ThinkingOff)))).Text);

        var unrecorded = Recorded(settings) with { Prompt = "a prompt nobody recorded" };
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
