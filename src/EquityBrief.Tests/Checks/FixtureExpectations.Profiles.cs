using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Tests.Providers;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the model profiles and the one word each paid job names: each shipped profile
// resolved from the shipped settings, the switch changing the model called, the fixture pinned to its own
// profile whatever the shipped switch says, and a key the secrets file does not hold stopping the job with
// no other profile answering for it.
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
public partial class FixtureExpectations
{
    // The shipped settings with the research job switched to one profile, and the keys a test hands the
    // secrets file.
    static IConfiguration WithProfile(string use, params string[] keyNames) =>
        new ConfigurationBuilder()
            .AddJsonFile(ResearchModelFeedTests.ShippedConfiguration)
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(ModelProfiles.Use(ModelProfiles.ResearchJob), use),
                .. keyNames.Select(keyName => new KeyValuePair<string, string?>(ModelProfiles.KeyPath(keyName), ResearchModelFeedTests.NotAKey)),
            ])
            .Build();

    static ResearchModelSettings ResolvedProfile(string use) => ResearchLane.Settings(WithProfile(use, "Research", "Claude"));

    [Fact]
    public void EachShippedProfileResolvesFromTheShippedSettingsWithItsProvidersPricesAndDates()
    {
        // The three profiles as the pricing and models pages read on 2026-09-29 state them, and DeepSeek's as
        // its page read on 2026-09-13 did, worked out here rather than read back.
        var deepseek = ResolvedProfile("deepseek");

        Assert.Equal(("Research", "deepseek", ResearchModelSettings.OpenAiFormat, "deepseek-flash", "Research"), (deepseek.Job, deepseek.Profile, deepseek.Format, deepseek.Model, deepseek.KeyName));
        Assert.Equal((0.003m, 0.15m, 0m, 0.60m, 2m), (deepseek.Pricing.CacheHit, deepseek.Pricing.CacheMiss, deepseek.Pricing.CacheWrite, deepseek.Pricing.Output, deepseek.Pricing.PeakMultiple));
        Assert.Equal([(1, 4), (6, 10)], deepseek.Pricing.PeakHours);
        Assert.Null(deepseek.Retires);

        var haiku = ResolvedProfile("claude-haiku");

        Assert.Equal(("claude-haiku", ResearchModelSettings.AnthropicFormat, "claude-haiku-4-5-20251001", "Claude"), (haiku.Profile, haiku.Format, haiku.Model, haiku.KeyName));
        Assert.Equal((0.10m, 1.00m, 1.25m, 5.00m, 1m), (haiku.Pricing.CacheHit, haiku.Pricing.CacheMiss, haiku.Pricing.CacheWrite, haiku.Pricing.Output, haiku.Pricing.PeakMultiple));
        Assert.Empty(haiku.Pricing.PeakHours);
        Assert.Equal((new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 29)), (haiku.Retires, haiku.RetiresReadOn));

        var sonnet = ResolvedProfile("claude-sonnet");

        Assert.Equal(("claude-sonnet", ResearchModelSettings.AnthropicFormat, "claude-sonnet-5-5", "Claude"), (sonnet.Profile, sonnet.Format, sonnet.Model, sonnet.KeyName));
        Assert.Equal((0.20m, 2.00m, 2.50m, 10.00m, 1m), (sonnet.Pricing.CacheHit, sonnet.Pricing.CacheMiss, sonnet.Pricing.CacheWrite, sonnet.Pricing.Output, sonnet.Pricing.PeakMultiple));
        Assert.Equal((new DateOnly(2027, 9, 28), new DateOnly(2026, 9, 29)), (sonnet.Retires, sonnet.RetiresReadOn));

        // The research job's own budget and timeout whichever profile it names, and the shipped switch naming
        // DeepSeek with the budget it ran on, as the operator last switched it.
        Assert.All(new[] { deepseek, haiku, sonnet }, settings => Assert.Equal((32768, TimeSpan.FromSeconds(600)), (settings.AnswerTokens, settings.Timeout)));

        var shipped = new ConfigurationBuilder().AddJsonFile(ResearchModelFeedTests.ShippedConfiguration).Build();

        Assert.Equal("deepseek", shipped[ModelProfiles.Use(ModelProfiles.ResearchJob)]);
    }

    [Fact]
    public void SwitchingTheOneWordChangesTheModelCalledAndNothingElse()
    {
        // Each profile the research job can name reaches the feed of its format, which asks for that profile's
        // model, and the recording key each asks under is its own.
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (use, feed, model) in new[]
        {
            ("deepseek", typeof(OpenAiCompatibleResearchFeed), "deepseek-flash"),
            ("claude-haiku", typeof(AnthropicMessagesFeed), "claude-haiku-4-5-20251001"),
            ("claude-sonnet", typeof(AnthropicMessagesFeed), "claude-sonnet-5-5"),
        })
        {
            var settings = ResolvedProfile(use);
            var live = ResearchModelFeeds.Live(settings);

            Assert.IsType(feed, live);
            Assert.Equal(model, live.Identity);

            var request = ResearchModelFeedTests.Recorded(settings);
            var body = live is AnthropicMessagesFeed ? AnthropicMessagesFeed.Body(request, settings) : OpenAiCompatibleResearchFeed.Body(request, settings);

            using var sent = JsonDocument.Parse(body);

            Assert.Equal(model, sent.RootElement.GetProperty("model").GetString());
            Assert.True(keys.Add(request.Key));
        }
    }

    [Fact]
    public void TheFixtureIsPinnedToItsOwnProfileWhateverTheShippedSwitchSays()
    {
        // The operator's switch sits beneath the fixture's models file, so no recorded test moves with it,
        // whichever profile it names.
        foreach (var use in new[] { "claude-haiku", "claude-sonnet", "deepseek" })
        {
            var settings = ResearchLane.Settings(new ConfigurationBuilder()
                .AddJsonFile(ResearchModelFeedTests.ShippedConfiguration)
                .AddInMemoryCollection([new KeyValuePair<string, string?>(ModelProfiles.Use(ModelProfiles.ResearchJob), use)])
                .AddJsonFile(ResearchModelFeedTests.PinnedModels)
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(ModelProfiles.KeyPath("Research"), ResearchModelFeedTests.NotAKey),
                    new KeyValuePair<string, string?>(ModelProfiles.KeyPath("Claude"), ResearchModelFeedTests.NotAKey),
                ])
                .Build());

            Assert.Equal((ResearchModelFeedTests.PinnedProfile, "deepseek-flash", 32768), (settings.Profile, settings.Identity, settings.AnswerTokens));
        }

        Assert.Equal("deepseek-flash", ResearchModelFeedTests.Pinned().Identity);
    }

    [Fact]
    public async Task AKeyTheSecretsFileDoesNotHoldStopsTheJobWithAPlainLineAndNoOtherProfileAnswers()
    {
        // DeepSeek's key is there and Claude's is not, with the research job naming Claude.
        var refused = Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(WithProfile("claude-sonnet", "Research")));

        Assert.Equal(
            "The Research job uses the profile 'claude-sonnet', whose key 'Claude' the secrets file does not hold. Set " +
            "'EquityBrief:Models:Claude:ApiKey' in appsettings.Secrets.json beside appsettings.json, or in the environment. The " +
            "job stops here and no other profile answers for it, because a section whose model is not the one configured is a " +
            "section nobody can say who wrote.",
            refused.Message);

        // The line is the pass's own row, which the drain settles the request under and the run page reads,
        // rather than a line on an error stream nobody reads.
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES ('MDT', '2026-09-29T01:07:26Z', 'night', 'paid', 'outstanding');");

        await using var connection = store.Open();

        var claimed = DateTimeOffset.Parse("2026-09-29T04:00:00Z", CultureInfo.InvariantCulture);
        var request = Assert.IsType<TakenRequest>(await RequestDrain.ClaimAsync(connection, claimed));
        var clock = new FixedClock(claimed.AddSeconds(1), SessionZones.ResolveSessionZone(SessionZones.UnitedStates));
        var runId = PassRun.IdFor(clock.UtcNow, "MDT");

        await ResearchRunner.RefusedBySettingsAsync(clock, store.DatabaseFile, "MDT", runId, refused.Message);

        var (found, outcome) = await RequestDrain.PassAsync(connection, request);

        Assert.Equal((runId, ResearchRunner.Unconfigured), (found, outcome));
        Assert.Equal(("refused", "the pass came to unconfigured, and its own run says why"), RequestDrain.SettlementFor(outcome));

        await using var read = connection.CreateCommand();

        read.CommandText = "SELECT detail, model_calls, network_requests, spend FROM run_log WHERE run_id = $run;";
        read.Parameters.AddWithValue("$run", runId);

        await using var row = await read.ExecuteReaderAsync();

        Assert.True(await row.ReadAsync());

        using var detail = JsonDocument.Parse(row.GetString(0));

        Assert.Equal(refused.Message, detail.RootElement.GetProperty("reason").GetString());
        Assert.Equal((0L, 0L, "0"), (row.GetInt64(1), row.GetInt64(2), row.GetString(3)));
    }
}
