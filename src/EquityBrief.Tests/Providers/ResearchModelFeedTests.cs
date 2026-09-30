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
// fixture's pinned profile names before any of it was written, and priced at the rates that
// profile states. Nothing here, and nothing it tests, names a provider in code.
public class ResearchModelFeedTests
{
    static string Folder() => Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder);

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    internal static string ShippedConfiguration => Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "appsettings.json");

    // The fixture's own profiles and each paid job's `Use`, which its recordings were made
    // under, read over the shipped settings so a switch the operator makes there moves no
    // recorded test.
    internal static string PinnedModels => Path.Combine(Folder(), "models.json");

    // The profile the fixture pins the research job to, and the secrets section its key sits in.
    internal const string PinnedProfile = "deepseek";
    internal const string PinnedKey = "Research";

    // A key no test sends anywhere. The settings refuse a blank one, so a test that
    // builds settings has to hand it something.
    internal const string NotAKey = "a key no test sends";

    // The options the second recording was asked with.
    internal const string ThinkingOff = "{\"thinking\":{\"type\":\"disabled\"}}";

    // The settings the fixture's recordings were made under, with a key that is not one and
    // the options a test asks for, read through the path the worker reads them through.
    internal static ResearchModelSettings Pinned(string? options = null, params (string Key, string Value)[] overrides) =>
        ResearchLane.Settings(new ConfigurationBuilder()
            .AddJsonFile(ShippedConfiguration)
            .AddJsonFile(PinnedModels)
            .AddInMemoryCollection(
                overrides.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))
                    .Append(new KeyValuePair<string, string?>(ModelProfiles.KeyPath(PinnedKey), NotAKey))
                    .Append(new KeyValuePair<string, string?>(ModelProfiles.Field(PinnedProfile, ModelProfiles.OptionsField), options ?? string.Empty)))
            .Build());

    // The fixture's settings with the research job on its claude-sonnet profile, which the fixture's
    // recordings of Claude Sonnet 5.5 were made under.
    internal const string ClaudeProfile = "claude-sonnet";

    internal static ResearchModelSettings PinnedClaude() =>
        Pinned(null, (ModelProfiles.Use(ModelProfiles.ResearchJob), ClaudeProfile), (ModelProfiles.KeyPath("Claude"), NotAKey));

    // A key of the pinned profile's prices.
    static string Price(string field) => ModelProfiles.Prices(PinnedProfile) + ":" + field;

    // A read surface the suite hosts, pinned to the fixture's profiles over the shipped settings
    // it reads beside the worker, so the peak windows its pages state are the recorded profile's
    // whatever profile the operator's switch names.
    internal static void PinModels(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddJsonFile(PinnedModels));

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
        var settings = Pinned();
        var answer = RecordedAnswer(settings);

        // Recorded on a weekday outside both peak windows.
        Assert.Equal(DayOfWeek.Tuesday, answer.Created.UtcDateTime.DayOfWeek);
        Assert.False(settings.Pricing.IsPeak(answer.Created));

        var byHand = (answer.CacheHitTokens * 0.003m + answer.CacheMissTokens * 0.15m + answer.CompletionTokens * 0.60m) / 1_000_000m;

        Assert.Equal(byHand, settings.Pricing.Price(answer));
        Assert.Equal(0.0000717m, byHand);

        // The same counts stamped inside a peak window cost twice as much.
        Assert.Equal(byHand * 2m, settings.Pricing.Price(answer with { Created = DateTimeOffset.Parse("2026-09-14T01:30:00Z", CultureInfo.InvariantCulture) }));

        // The captured call arrived with nothing cached, so the cached rate is held over the
        // same answer with part of its prompt served from the cache: 200 of 246 cached is 200
        // at 0.003 and 46 at 0.15, beside the same 58 of output.
        var cached = answer with { CacheHitTokens = 200, CacheMissTokens = 46 };

        Assert.Equal(0.0000423m, settings.Pricing.Price(cached));

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
        Assert.Equal(peak, Pinned().Pricing.IsPeak(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ACallsCeilingIsEveryByteAsATokenAndTheWholeBudgetAtPeakAndHoldsOverTheRecordings()
    {
        var settings = Pinned();
        var request = Recorded(settings);

        var bytes = Encoding.UTF8.GetByteCount(request.System) + Encoding.UTF8.GetByteCount(request.Prompt);
        var byHand = ((bytes + ResearchPricing.TemplateTokens) * 0.15m + settings.AnswerTokens * 0.60m) * 2m / 1_000_000m;

        Assert.Equal(byHand, settings.Pricing.Ceiling(request, settings.AnswerTokens));

        // A ceiling that is not one would let a call spend past a cap, so it is held above
        // what each recorded call cost as if it had been at peak.
        foreach (var options in new[] { (string?)null, ThinkingOff })
        {
            var asked = Pinned(options);
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
        using var plain = JsonDocument.Parse(OpenAiCompatibleResearchFeed.Body(Recorded(Pinned()), Pinned()));
        using var asked = JsonDocument.Parse(OpenAiCompatibleResearchFeed.Body(Recorded(Pinned(ThinkingOff)), Pinned(ThinkingOff)));

        // With no options, the four fields the feed owns and nothing else.
        Assert.Equal(["model", "messages", "max_tokens", "stream"], plain.RootElement.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.Equal(Pinned().Model, plain.RootElement.GetProperty("model").GetString());
        Assert.Equal(Pinned().AnswerTokens, plain.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(plain.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(["system", "user"], plain.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString()!).ToArray());

        // With options, the provider's own fields beside those four, exactly as written.
        Assert.Equal("disabled", asked.RootElement.GetProperty("thinking").GetProperty("type").GetString());

        // And the identity a section stores carries the options, so the two are two
        // writers with two recordings.
        Assert.Equal(Pinned().Model, Pinned().Identity);
        Assert.Equal(Pinned().Model + " " + ThinkingOff, Pinned(ThinkingOff).Identity);
        Assert.NotEqual(Recorded(Pinned()).Key, Recorded(Pinned(ThinkingOff)).Key);
    }

    [Fact]
    public void SwitchingModelIsAChangeToConfigurationAlone()
    {
        // Another provider, another model and other prices, named in configuration and
        // nowhere else: the same feed sends to that address, asks for that model with that
        // provider's options, and prices at those rates.
        var elsewhere = Pinned(
            "{\"reasoning_effort\":\"low\"}",
            (ModelProfiles.Field(PinnedProfile, ModelProfiles.BaseAddressField), "https://models.example.test/v1"),
            (ModelProfiles.Field(PinnedProfile, ModelProfiles.ModelField), "another-model"),
            (Price(ResearchPricing.CacheHitField), "0.5"),
            (Price(ResearchPricing.CacheMissField), "1.25"),
            (Price(ResearchPricing.OutputField), "10"));

        Assert.Equal("https://models.example.test/v1/", elsewhere.BaseAddress);
        Assert.Equal("another-model {\"reasoning_effort\":\"low\"}", elsewhere.Identity);

        using var body = JsonDocument.Parse(OpenAiCompatibleResearchFeed.Body(Recorded(elsewhere), elsewhere));

        Assert.Equal("another-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("low", body.RootElement.GetProperty("reasoning_effort").GetString());

        var answer = RecordedAnswer(Pinned());

        Assert.Equal((answer.CacheMissTokens * 1.25m + answer.CompletionTokens * 10m) / 1_000_000m, elsewhere.Pricing.Price(answer));

        var live = OpenAiCompatibleResearchFeed.Live(elsewhere);

        Assert.Equal("another-model {\"reasoning_effort\":\"low\"}", live.Identity);
        Assert.IsType<OpenAiCompatibleResearchFeed>(ResearchModelFeeds.Live(elsewhere));
    }

    [Fact]
    public async Task TheLiveFeedSendsTheKeyInTheHeaderToTheConfiguredAddressAndNeverInTheAddress()
    {
        var settings = Pinned();
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
    public async Task TheProbeAsksForTheModelListWithTheKeyInTheHeaderAndReadsOnlyAnAnswerAsAnswering()
    {
        // What a pass asks before it fetches anything. The 6.8 sweep found no test calling
        // it on the live feed, so a probe that read every response as an answer passed.
        var settings = Pinned();

        async Task<(string? Line, Seeing Seen, OpenAiCompatibleResearchFeed Feed)> Probe(HttpStatusCode status, string body)
        {
            var seen = new Seeing(body, status);
            var client = new HttpClient(seen) { BaseAddress = new Uri(settings.BaseAddress) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NotAKey);

            var feed = new OpenAiCompatibleResearchFeed(client, settings);

            return (await feed.UnreachableAsync(), seen, feed);
        }

        var answered = await Probe(HttpStatusCode.OK, Captured("research-probe-models.json"));

        Assert.Null(answered.Line);
        Assert.Equal(settings.BaseAddress + OpenAiCompatibleResearchFeed.ModelsPath, answered.Seen.Address);
        Assert.DoesNotContain(NotAKey, answered.Seen.Address, StringComparison.Ordinal);
        Assert.Equal("Bearer", answered.Seen.Scheme);

        // A probe is not a call: it is counted as a probe and bills nothing.
        Assert.Equal(1, answered.Feed.Probes);
        Assert.Equal(0, answered.Feed.Requests);

        // A provider refusing the list is a model that does not answer, in its own words.
        var refused = await Probe(HttpStatusCode.Unauthorized, """{"error":{"message":"Authentication Fails, Your api key is invalid","type":"authentication_error"}}""");

        Assert.Equal("The research model refused the model list with status 401: Authentication Fails, Your api key is invalid", refused.Line);

        // And nothing listening says so.
        using var gone = new HttpClient(new Unreachable()) { BaseAddress = new Uri(settings.BaseAddress) };

        Assert.StartsWith("The research model could not be reached: ", await new OpenAiCompatibleResearchFeed(gone, settings).UnreachableAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingListeningIsItsOwnFailureAndARefusalCarriesTheProvidersWords()
    {
        var settings = Pinned();

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
        // The fixture's profile with no key: the refusal names the profile, the path the key
        // is read from, and that no other profile answers instead.
        var blankKey = Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(
            new ConfigurationBuilder().AddJsonFile(ShippedConfiguration).AddJsonFile(PinnedModels).Build()));

        Assert.Contains($"'{PinnedProfile}'", blankKey.Message, StringComparison.Ordinal);
        Assert.Contains(ModelProfiles.KeyPath(PinnedKey), blankKey.Message, StringComparison.Ordinal);
        Assert.Contains("no other profile answers", blankKey.Message, StringComparison.Ordinal);

        // A key written into a file and left empty, or holding only spaces, is the likelier
        // mistake than no key at all, and it is refused the same way.
        foreach (var blank in new[] { "", "   " })
        {
            var written = new ConfigurationBuilder()
                .AddJsonFile(ShippedConfiguration)
                .AddJsonFile(PinnedModels)
                .AddInMemoryCollection([new KeyValuePair<string, string?>(ModelProfiles.KeyPath(PinnedKey), blank)])
                .Build();

            Assert.Contains(ModelProfiles.KeyPath(PinnedKey), Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(written)).Message, StringComparison.Ordinal);
        }

        string Refusal(string key, string value) =>
            Assert.Throws<InvalidOperationException>(() => Pinned(null, (key, value))).Message;

        Assert.Contains("'another format'", Refusal(ModelProfiles.Field(PinnedProfile, ModelProfiles.FormatField), "another format"), StringComparison.Ordinal);
        Assert.Contains(
            ModelProfiles.Field(PinnedProfile, ModelProfiles.BaseAddressField),
            Refusal(ModelProfiles.Field(PinnedProfile, ModelProfiles.BaseAddressField), "api.example.test"),
            StringComparison.Ordinal);
        Assert.Contains("'5m'", Refusal(ModelProfiles.JobField(ModelProfiles.ResearchJob, ModelProfiles.TimeoutField), "5m"), StringComparison.Ordinal);
        Assert.Contains("'about a dollar'", Refusal(Price(ResearchPricing.OutputField), "about a dollar"), StringComparison.Ordinal);
        Assert.Contains("'1am-4am'", Refusal(Price(ResearchPricing.PeakHoursField) + ":0", "1am-4am"), StringComparison.Ordinal);
        Assert.Contains("'Someday'", Refusal(Price(ResearchPricing.PeakDaysField) + ":0", "Someday"), StringComparison.Ordinal);
        Assert.Contains("'soon'", Refusal(ModelProfiles.Field(PinnedProfile, ModelProfiles.RetiresField), "soon"), StringComparison.Ordinal);

        // A job naming no profile, or one the profiles do not hold, is refused by name and no
        // other profile is taken in its place.
        Assert.Contains("names no profile", Refusal(ModelProfiles.Use(ModelProfiles.ResearchJob), " "), StringComparison.Ordinal);
        Assert.Contains("'a-profile-nobody-wrote'", Refusal(ModelProfiles.Use(ModelProfiles.ResearchJob), "a-profile-nobody-wrote"), StringComparison.Ordinal);

        Assert.Contains("not a JSON object", Assert.Throws<InvalidOperationException>(() => Pinned("[\"thinking\"]")).Message, StringComparison.Ordinal);
        Assert.Contains("sets model", Assert.Throws<InvalidOperationException>(() => Pinned("{\"model\":\"something else\"}")).Message, StringComparison.Ordinal);

        // A model with no prices at all is refused rather than called at nothing.
        var unpriced = new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>(ModelProfiles.Use(ModelProfiles.ResearchJob), "unpriced"),
            new KeyValuePair<string, string?>(ModelProfiles.Field("unpriced", ModelProfiles.BaseAddressField), "https://models.example.test/v1"),
            new KeyValuePair<string, string?>(ModelProfiles.Field("unpriced", ModelProfiles.ModelField), "another-model"),
            new KeyValuePair<string, string?>(ModelProfiles.Field("unpriced", ModelProfiles.KeyField), "Elsewhere"),
            new KeyValuePair<string, string?>(ModelProfiles.KeyPath("Elsewhere"), NotAKey),
        ]).Build();

        Assert.Contains("No prices", Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(unpriced)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePinnedModelIsOneTheProvidersOwnListCarries()
    {
        using var listed = JsonDocument.Parse(Captured("research-probe-models.json"));

        Assert.Contains(
            Pinned().Model,
            listed.RootElement.GetProperty("data").EnumerateArray().Select(model => model.GetProperty("id").GetString()!));
    }

    [Fact]
    public void TheRunbookStatesEachJobSettingEachProfileAndEachCapWithTheValueTheShippedConfigurationHolds()
    {
        var runbook = Corpus.Read("docs/RUNBOOK.md");
        var lines = runbook.Split('\n');

        static string[] Cells(string line) => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

        var shipped = new ConfigurationBuilder().AddJsonFile(ShippedConfiguration).Build();

        // A value as the file holds it, a list joined as the runbook writes it, and none where
        // the file holds nothing.
        string Holds(string key)
        {
            var listed = shipped.GetSection(key).GetChildren().Select(child => child.Value).ToArray();
            var value = listed.Length > 0 ? string.Join(", ", listed) : shipped[key];

            return string.IsNullOrWhiteSpace(value) ? "none" : value;
        }

        // The job's settings and the caps, a key to a row.
        var rows = lines
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal)
                && (line.Contains("`EquityBrief:Models:Research:", StringComparison.Ordinal) || line.Contains("`EquityBrief:Spend:", StringComparison.Ordinal))
                && !line.Contains("ApiKey", StringComparison.Ordinal))
            .Select(Cells)
            .ToArray();

        string Stated(string key) => Assert.Single(rows, row => row[1] == $"`{key}`")[2].Trim('`');

        foreach (var field in new[] { ModelProfiles.UseField, ModelProfiles.TimeoutField, ModelProfiles.AnswerTokensField })
        {
            var key = ModelProfiles.JobField(ModelProfiles.ResearchJob, field);

            Assert.Equal(Holds(key), Stated(key));
        }

        // The map of sections, one row standing for every section, which ships naming one profile for all of them.
        var sections = ModelProfiles.JobField(ModelProfiles.ResearchJob, ModelProfiles.SectionsField);

        Assert.Equal(ClaimRules.Sections.Order(StringComparer.Ordinal), shipped.GetSection(sections).GetChildren().Select(child => child.Key).Order(StringComparer.Ordinal));
        Assert.All(shipped.GetSection(sections).GetChildren(), child => Assert.Equal(child.Value, Stated(sections + ":<section>")));

        // The trial's settings, a key to a row.
        foreach (var field in new[] { ModelProfiles.UseField, ModelProfiles.SectionsField, "Reports", "From" })
        {
            var key = ModelProfiles.JobField(ModelProfiles.ResearchJob, ResearchLane.TrialField) + ":" + field;

            Assert.Equal(Holds(key), Stated(key));
        }

        Assert.Equal(SpendCaps.DefaultDay, decimal.Parse(Stated(SpendCaps.DayKey), CultureInfo.InvariantCulture));
        Assert.Equal(SpendCaps.DefaultMonth, decimal.Parse(Stated(SpendCaps.MonthKey), CultureInfo.InvariantCulture));

        // The profiles' table, a column per shipped profile and a row per field, every cell
        // the value the shipped file holds.
        var header = Cells(Assert.Single(lines, line => line.StartsWith("| Field | What it holds |", StringComparison.Ordinal)));
        var profiles = header.Skip(2).Select(cell => cell.Trim('`')).ToArray();

        Assert.Equal(
            shipped.GetSection(ModelProfiles.ProfilesSection).GetChildren().Select(child => child.Key).Order(StringComparer.Ordinal),
            profiles.Order(StringComparer.Ordinal));

        string[] fields =
        [
            ModelProfiles.FormatField, ModelProfiles.BaseAddressField, ModelProfiles.ModelField, ModelProfiles.KeyField,
            ModelProfiles.OptionsField, ModelProfiles.ThinkingField, ModelProfiles.RetiresField, ModelProfiles.RetiresReadOnField,
            .. new[]
            {
                ResearchPricing.CacheHitField, ResearchPricing.CacheWriteField, ResearchPricing.CacheMissField, ResearchPricing.OutputField,
                ResearchPricing.PeakHoursField, ResearchPricing.PeakDaysField, ResearchPricing.PeakMultipleField,
            }.Select(field => ModelProfiles.PricesField + ":" + field),
        ];

        foreach (var field in fields)
        {
            var row = Cells(Assert.Single(lines, line => line.StartsWith($"| `{field}` |", StringComparison.Ordinal)));

            for (var column = 0; column < profiles.Length; column++)
            {
                Assert.Equal(Holds(ModelProfiles.Field(profiles[column], field)), row[column + 2].Trim('`'));
            }
        }

        // And the key rows name the paths the settings read.
        foreach (var keyName in profiles.Select(profile => shipped[ModelProfiles.Field(profile, ModelProfiles.KeyField)]!).Distinct(StringComparer.Ordinal))
        {
            Assert.Contains($"`{ModelProfiles.KeyPath(keyName)}`", runbook, StringComparison.Ordinal);
        }
    }

    // ---- the recording ----

    [Fact]
    public async Task TheRecordingAnswersTheRequestItHoldsAndRefusesByNameTheOneItDoesNot()
    {
        var settings = Pinned();
        var feed = new RecordedResearchModelFeed(Folder(), settings);

        // Each recording answers the request asked the way it was recorded.
        Assert.Equal("KEYS closed at 333.42.", (await feed.CompleteAsync(Recorded(settings))).Text);
        Assert.Equal("The close was 333.42.", (await new RecordedResearchModelFeed(Folder(), Pinned(ThinkingOff)).CompleteAsync(Recorded(Pinned(ThinkingOff)))).Text);

        var unrecorded = Recorded(settings) with { Prompt = "a prompt nobody recorded" };
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => feed.CompleteAsync(unrecorded));

        Assert.Contains(unrecorded.Key, refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, feed.Requests);

        Assert.DoesNotContain(
            typeof(RecordedResearchModelFeed).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            field => typeof(HttpClient).IsAssignableFrom(field.FieldType) || typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType));
    }

    sealed class Seeing(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string Address { get; private set; } = string.Empty;

        public string Scheme { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri!.ToString();
            Scheme = request.Headers.Authorization?.Scheme ?? string.Empty;

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
    }
}
