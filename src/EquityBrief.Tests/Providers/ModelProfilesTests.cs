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
// see: A paid job names its model profile in one word, and a profile is priced at its configured rates at its call's own timestamp
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
        Assert.Equal(16000, sent.RootElement.GetProperty("max_tokens").GetInt32());

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

        Assert.Equal(((bytes + ResearchPricing.TemplateTokens) * 2.50m + 16000 * 10.00m) / 1_000_000m, settings.Pricing.Ceiling(request, settings.AnswerTokens));
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
