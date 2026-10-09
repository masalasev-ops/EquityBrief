using System.Globalization;
using System.Net;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Providers;

// The feed over Claude's own messages interface and the profile it is resolved from: the request as it is
// sent, an answer priced from its four counts, the provider's refusal in its own words, the workspace a key
// not scoped to one names, and the retirement warning's reach. The profiles as the fixture's claims read them
// are fixture-expectations'.
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
public class ModelProfilesTests
{
    static string Folder() => Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder);

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    // The shipped settings with the research job switched to one profile, and the keys a test hands the
    // secrets file.
    static IConfiguration Shipped(string use, params string[] keyNames) =>
        new ConfigurationBuilder()
            .AddJsonFile(ResearchModelFeedTests.ShippedConfiguration)
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(ModelProfiles.Use(ModelProfiles.ResearchJob), use),
                .. keyNames.Select(keyName => new KeyValuePair<string, string?>(ModelProfiles.KeyPath(keyName), ResearchModelFeedTests.NotAKey)),
            ])
            .Build();

    static ResearchModelSettings Resolved(string use) => ResearchLane.Settings(Shipped(use, "Research", "Claude"));

    [Fact]
    public void ClaudesRequestCarriesTheInstructionsAsItsSystemPromptAndTheOptionsCannotReplaceWhatTheFeedWrites()
    {
        var settings = Resolved("claude-sonnet");

        using var sent = JsonDocument.Parse(AnthropicMessagesFeed.Body(ResearchModelFeedTests.Recorded(settings), settings));

        Assert.Equal(["model", "max_tokens", "system", "messages", "stream"], sent.RootElement.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.Equal(SectionPrompt.Instructions, sent.RootElement.GetProperty("system").GetString());
        Assert.Equal(["user"], sent.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString()!).ToArray());
        Assert.Equal(32768, sent.RootElement.GetProperty("max_tokens").GetInt32());

        // The system prompt is the feed's own field in this format, and an option setting it is refused at
        // startup as one setting the model is.
        var refused = Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(new ConfigurationBuilder()
            .AddConfiguration(Shipped("claude-sonnet", "Claude"))
            .AddInMemoryCollection([new KeyValuePair<string, string?>(ModelProfiles.Field("claude-sonnet", ModelProfiles.OptionsField), "{\"system\":\"something else\"}")])
            .Build()));

        Assert.Contains("sets system", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClaudeAnswerIsPricedFromItsFourCountsAndItsCeilingHoldsOverACacheWrite()
    {
        // Worked by hand at Claude Sonnet 5.5's rates: 200 prompt tokens read from the cache at 0.20, 1000
        // neither read nor written at 2.00, 100 written at 2.50, and 80 output at 10.00, in dollars a million
        // tokens, with no peak multiple at any hour.
        var settings = Resolved("claude-sonnet");
        var answer = new ResearchAnswer("claude-sonnet-5-5", "KEYS closed at 333.42.", 1300, 200, 1000, 80, 0, "end_turn", DateTimeOffset.Parse("2026-09-29T02:30:00Z", CultureInfo.InvariantCulture), 100);

        Assert.Equal(0.00309m, settings.Pricing.Price(answer));
        Assert.False(settings.Pricing.IsPeak(answer.Created));

        // The ceiling counts every byte of the prompt at the dearer of the uncached and the cache-write rates,
        // so a call whose prompt the provider wrote to its cache is still inside it.
        var request = ResearchModelFeedTests.Recorded(settings);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(request.System) + System.Text.Encoding.UTF8.GetByteCount(request.Prompt);

        Assert.Equal(((bytes + ResearchPricing.TemplateTokens) * 2.50m + 32768 * 10.00m) / 1_000_000m, settings.Pricing.Ceiling(request, settings.AnswerTokens));
    }

    [Fact]
    public async Task ClaudesRefusalIsReadInTheProvidersOwnWordsAndTheWorkspaceIsNamedWhereTheKeyNeedsOne()
    {
        // The body the provider answered on 2026-09-29 to a key not scoped to a workspace, captured.
        var body = Captured("anthropic-refused-no-workspace.json");
        const string Said = "This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.";

        Assert.Equal($"The Research job's model refused The two cases with status 400: {Said}", AnthropicMessagesFeed.Refused("Research", "The two cases", 400, body));
        Assert.Equal("The Research job's model refused the model list with status 500.", AnthropicMessagesFeed.Refused("Research", "the model list", 500, "<html></html>"));

        var settings = Resolved("claude-sonnet");
        var seen = new Answering(body, HttpStatusCode.BadRequest);

        using var client = new HttpClient(seen) { BaseAddress = new Uri(settings.BaseAddress) };

        var feed = new AnthropicMessagesFeed(client, settings);
        var thrown = await Assert.ThrowsAsync<ProviderRefusal>(() => feed.CompleteAsync(ResearchModelFeedTests.Recorded(settings)));

        Assert.EndsWith(Said, thrown.Message, StringComparison.Ordinal);
        Assert.Equal(settings.BaseAddress + AnthropicMessagesFeed.Path, seen.Address);

        var probed = new AnthropicMessagesFeed(new HttpClient(new Answering(body, HttpStatusCode.BadRequest)) { BaseAddress = new Uri(settings.BaseAddress) }, settings);

        Assert.EndsWith(Said, await probed.UnreachableAsync(), StringComparison.Ordinal);
        Assert.Equal((1, 0), (probed.Probes, probed.Requests));

        // A workspace written beside the key is read with it and sent on every request; a key scoped to its own
        // workspace has none written and sends none.
        var named = ResearchLane.Settings(new ConfigurationBuilder()
            .AddConfiguration(Shipped("claude-sonnet", "Claude"))
            .AddInMemoryCollection([new KeyValuePair<string, string?>(ModelProfiles.WorkspacePath("Claude"), "wrkspc_a-workspace-no-test-sends")])
            .Build());

        Assert.Equal(["wrkspc_a-workspace-no-test-sends"], AnthropicMessagesFeed.Headers(named)[AnthropicMessagesFeed.WorkspaceHeader]);
        Assert.False(AnthropicMessagesFeed.Headers(settings).ContainsKey(AnthropicMessagesFeed.WorkspaceHeader));
        Assert.Equal([AnthropicMessagesFeed.Version], AnthropicMessagesFeed.Headers(settings)[AnthropicMessagesFeed.VersionHeader]);
        Assert.DoesNotContain("wrkspc_", named.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudesCapturedAnswersAreReadAsTheyArrivedAndPricedFromTheirCounts()
    {
        var settings = Resolved("claude-sonnet");

        // The section Claude Sonnet 5.5 wrote on 2026-09-29, read at the instant its Date header carries and
        // priced by hand at the profile's rates: 210 prompt tokens neither read from the cache nor written to it
        // at 2.00 and 27 output at 10.00, in dollars a million tokens.
        var answer = AnthropicMessagesFeed.ParseRecorded(Captured("anthropic-section-sonnet.json"), "The close");

        Assert.Equal(
            new ResearchAnswer("claude-sonnet-5-5", "Keysight Technologies (KEYS) closed at $333.42 per share.", 210, 0, 210, 27, 0, "end_turn", DateTimeOffset.Parse("2026-09-29T11:33:21Z", CultureInfo.InvariantCulture), 0),
            answer);
        Assert.Equal(0.00069m, settings.Pricing.Price(answer));

        // An answer stopped at its budget is not stored and is priced at what it was billed: 210 in and 8 out.
        var cut = Assert.Throws<UnusableResearchAnswer>(() => AnthropicMessagesFeed.ParseRecorded(Captured("anthropic-section-budget.json"), "The close"));

        Assert.Equal(("Keysight Technologies", "max_tokens", 210, 8), (cut.Answer.Text, cut.Answer.FinishReason, cut.Answer.PromptTokens, cut.Answer.CompletionTokens));
        Assert.Equal(0.0005m, settings.Pricing.Price(cut.Answer));
        Assert.Contains("stopped at its budget of tokens", cut.Message, StringComparison.Ordinal);

        // A model the provider does not serve is refused in its own words.
        Assert.Equal(
            "The Research job's model refused The close with status 404: model: claude-no-such-model",
            AnthropicMessagesFeed.Refused("Research", "The close", 404, Captured("anthropic-refused-unknown-model.json")));

        // Every model a shipped Claude profile names is one the provider's own list served on that day.
        using var listed = JsonDocument.Parse(Captured("anthropic-probe-models.json"));

        var served = listed.RootElement.GetProperty("data").EnumerateArray().Select(model => model.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var shipped = Shipped("claude-sonnet");
        var claude = new[] { "claude-sonnet", "claude-haiku" };

        Assert.All(claude, profile => Assert.Equal(ResearchModelSettings.AnthropicFormat, shipped[ModelProfiles.Field(profile, ModelProfiles.FormatField)]));
        Assert.All(claude, profile => Assert.Contains(shipped[ModelProfiles.Field(profile, ModelProfiles.ModelField)]!, served));
    }

    [Fact]
    public void EveryResearchModelIsToldToDescribeAndNeverPrescribeAndTheShortVersionAsksForNoPlan()
    {
        // The instructions every section is asked under, as each format's feed sends them: the system prompt
        // in Claude's own interface and the system message in the other format.
        foreach (var use in new[] { "deepseek", "claude-sonnet" })
        {
            var settings = Resolved(use);
            var request = SectionPrompt.PaidRequest(settings.Identity, "KEYS", "The short version", [], [new PromptDocument("a-document", "A release", null, "The release's text.")]);

            using var sent = JsonDocument.Parse(settings.Format == ResearchModelSettings.AnthropicFormat
                ? AnthropicMessagesFeed.Body(request, settings)
                : OpenAiCompatibleResearchFeed.Body(request, settings));

            var system = settings.Format == ResearchModelSettings.AnthropicFormat
                ? sent.RootElement.GetProperty("system").GetString()!
                : sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;

            Assert.Contains("Describe the company and never prescribe: propose no trade, no holding, no adding or trimming and no plan for income", system, StringComparison.Ordinal);
            Assert.Contains("Write no em dash", system, StringComparison.Ordinal);
            Assert.Contains("answer with no text at all", system, StringComparison.Ordinal);
            Assert.Contains("the sentence opening a paragraph as well as the rest", system, StringComparison.Ordinal);
        }

        // The short version asks for what is true and argued about, and for no plan.
        Assert.DoesNotContain("plan therefore", SectionPrompt.Asks["The short version"], StringComparison.Ordinal);
        Assert.Contains("propose nothing", SectionPrompt.Asks["The short version"], StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeIsAskedForSentencesEachNamingItsDocumentWhereDocumentsAreListedAndThePageReadsTheirProse()
    {
        var settings = Resolved("claude-sonnet");
        var listed = SectionPrompt.PaidRequest(settings.Identity, "KEYS", "The short version", [], [new PromptDocument("a-document", "A release", null, "The release's text.")]);

        // Where documents are listed: the answer's shape, paragraphs of sentences, each sentence with the
        // number of the document it rests on and whether it states figures listed under Facts, all required.
        using (var sent = JsonDocument.Parse(AnthropicMessagesFeed.Body(listed, settings)))
        {
            var format = sent.RootElement.GetProperty(AnthropicMessagesFeed.OutputConfigField).GetProperty("format");
            var sentence = format.GetProperty("schema").GetProperty("properties").GetProperty("paragraphs").GetProperty("items").GetProperty("items");

            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.Equal(["sentence", "document", "facts"], sentence.GetProperty("required").EnumerateArray().Select(field => field.GetString()!).ToArray());
            Assert.Equal("integer", sentence.GetProperty("properties").GetProperty("document").GetProperty("type").GetString());
            Assert.Equal("boolean", sentence.GetProperty("properties").GetProperty("facts").GetProperty("type").GetString());
        }

        // A sentence stating the night's figures ends on [N], beside its document's marker or alone where it names
        // document 0, and one naming a document alone ends on that document's marker.
        // see: A sentence names the night's stored figures by [N] and a document by its marker, and a figure in a sentence citing documents alone is one they state
        Assert.Equal(
            "The close was 205.15 [N]. Revenue was $67.2 billion [D2] [N]. It sells fuel [D1].",
            AnthropicMessagesFeed.FromSentences(
                "{\"paragraphs\":[[{\"sentence\":\"The close was 205.15.\",\"document\":0,\"facts\":true}," +
                "{\"sentence\":\"Revenue was $67.2 billion.\",\"document\":2,\"facts\":true}," +
                "{\"sentence\":\"It sells fuel.\",\"document\":1,\"facts\":false}]]}"));

        // Where none is listed there is nothing to name, and the answer is asked for as prose.
        using (var plain = JsonDocument.Parse(AnthropicMessagesFeed.Body(ResearchModelFeedTests.Recorded(settings), settings)))
        {
            Assert.False(plain.RootElement.TryGetProperty(AnthropicMessagesFeed.OutputConfigField, out _));
        }

        // The short version Claude Sonnet 5.5 answered over the fixture as sentences, read back to prose:
        // every sentence ends on the marker of the document its answer named, the paragraphs are kept, and
        // the checker finds no sentence naming no document.
        var recording = Captured(ClaudesShortVersion);
        var answer = AnthropicMessagesFeed.ParseRecorded(recording, listed.Section);

        using var raw = JsonDocument.Parse(recording);

        var asked = JsonDocument.Parse(raw.RootElement.GetProperty(AnthropicMessagesFeed.RecordedResponse).GetProperty("content").EnumerateArray()
            .Single(block => block.GetProperty("type").GetString() == "text").GetProperty("text").GetString()!).RootElement.GetProperty("paragraphs");

        Assert.Equal(asked.GetArrayLength(), answer.Text.Split("\n\n").Length);
        Assert.Equal(asked.EnumerateArray().Sum(paragraph => paragraph.GetArrayLength()), ClaimRules.Sentences(answer.Text).Count);
        Assert.All(ClaimRules.Sentences(answer.Text), sentence => Assert.Matches(@"\[D\d+\]\.$", sentence.Text));
    }

    // The fixture's recording of the short version Claude Sonnet 5.5 wrote over KEYS on the claude-sonnet
    // profile, its first draft, which the checker refused for a figure an article states and for no sentence
    // naming no document.
    const string ClaudesShortVersion = "research-call-fb65e403485478c57c0d3e080bc137da.json";

    [Fact]
    public void AnAnswerHoldingOnlyThinkingIsUnusableNamesWhatItHeldAndIsPricedAtWhatWasBilled()
    {
        var settings = Resolved("claude-sonnet");

        // The industry cycle Claude Sonnet 5.5 was asked for MDT's industry on 2026-09-29, asked again over the
        // pages that pass stored: one thinking block with its text left out, no text block, and the stop reason
        // end_turn after 207 output tokens, 205 of them thinking. Priced by hand: 23617 prompt tokens neither read
        // from the cache nor written to it at 2.00 and 207 output at 10.00, in dollars a million tokens.
        var thrown = Assert.Throws<UnusableResearchAnswer>(() => AnthropicMessagesFeed.ParseRecorded(Captured("anthropic-section-thinking-only.json"), ClaimRules.CycleSection));

        Assert.Contains("it held 1 thinking block, and the stop reason was end_turn after 207 output token(s), 205 of them thinking", thrown.Message, StringComparison.Ordinal);
        Assert.Equal((string.Empty, "end_turn", 23617, 207), (thrown.Answer.Text, thrown.Answer.FinishReason, thrown.Answer.PromptTokens, thrown.Answer.CompletionTokens));
        Assert.Equal(0.049304m, settings.Pricing.Price(thrown.Answer));
    }

    [Fact]
    public void AnAnswerHoldingOnlyAnInvisibleCharacterIsNoAnswerWhicheverFormatCarriesIt()
    {
        // What Claude Sonnet 5.5 answered for MDT's cause of each large move on 2026-09-29, a single zero-width
        // space, which that pass stored as its first draft and the checker refused as a sentence naming no document.
        const string Answered = "​";

        Assert.Equal(string.Empty, AnswerText.Visible(Answered));
        Assert.Equal(string.Empty, AnswerText.Visible(" ​﻿\n"));
        Assert.Equal("A sentence [D1].", AnswerText.Visible("​A sentence [D1].​ "));

        // What DeepSeek answered for the fixture's industry cycle on 2026-09-30, a lone marker, is no answer either,
        // and neither are several; a marker inside a sentence leaves the sentence as it is.
        Assert.Equal(string.Empty, AnswerText.Visible("[D1]"));
        Assert.Equal(string.Empty, AnswerText.Visible(" [D1] [D2]\n[D3] "));
        Assert.Equal("[D1] opens a sentence.", AnswerText.Visible("[D1] opens a sentence."));

        // The captured section with its text replaced by it is no section, and is billed all the same.
        var capture = System.Text.Json.Nodes.JsonNode.Parse(Captured("anthropic-section-sonnet.json"))!;

        capture["response"]!["content"]![0]!["text"] = Answered;

        var thrown = Assert.Throws<UnusableResearchAnswer>(() => AnthropicMessagesFeed.ParseRecorded(capture.ToJsonString(), ClaimRules.CauseSection));

        Assert.Contains("it held 1 text block", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(27, thrown.Answer.CompletionTokens);

        // And over the other format's capture, as DeepSeek would carry it.
        var other = System.Text.Json.Nodes.JsonNode.Parse(Captured("research-probe-thinking.json"))!;

        other["choices"]![0]!["message"]!["content"] = Answered;

        Assert.Throws<UnusableResearchAnswer>(() => OpenAiCompatibleResearchFeed.Parse(other.ToJsonString(), ClaimRules.CauseSection));
    }

    [Theory]
    [InlineData("2026-09-14", false)]
    [InlineData("2026-09-15", true)]
    [InlineData("2026-10-15", true)]
    public void AProfileIsWithinTheWarningFromThirtyDaysBeforeItsRetirementDate(string day, bool soon)
    {
        var haiku = new ModelProfile("News", "claude-haiku", ResearchModelSettings.AnthropicFormat, "claude-haiku-4-5-20251001", null, new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 29));

        Assert.Equal(soon, ModelProfiles.RetiresSoon(haiku, DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Assert.False(ModelProfiles.RetiresSoon(haiku with { Retires = null }, new DateOnly(2026, 10, 15)));
    }

    // A handler that records where a request went and answers with a stated body.
    sealed class Answering(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string Address { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri!.ToString();

            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };

            response.Headers.Date = DateTimeOffset.Parse("2026-09-29T03:48:45Z", CultureInfo.InvariantCulture);

            return Task.FromResult(response);
        }
    }
}
