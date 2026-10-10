using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;
using ResearchFeeds = EquityBrief.Tests.Providers.ResearchModelFeedTests;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, a model per section and the trial: a section the research job's map names another profile
// for asked of that model while the rest stay on the job's, a section or a profile the settings do not hold refused
// by name, a Claude profile's thinking sent off or at an effort beside the answer's format, and a trial asking a
// second profile for named sections after a pass, writing no section row, recording both models, stopping at the
// reports it names and its calls left out of a report's count; and no research setting as shipped naming a Claude
// profile.
// see: Research names a profile per section as well as per job, and a Claude profile states its thinking
// see: A trial asks a second profile for named sections beside a report, and ships naming none
// see: Report generation asks DeepSeek alone, and no research setting names a Claude profile
public partial class FixtureExpectations
{
    // Every profile word the research job's settings name as shipped: the job's own, one for each section, the
    // trial's and the review's, where a blank word names none.
    static IReadOnlyList<(string Setting, string Profile)> ResearchProfileWords(IConfiguration shipped)
    {
        var words = new List<(string, string)> { (ModelProfiles.Use(ModelProfiles.ResearchJob), shipped[ModelProfiles.Use(ModelProfiles.ResearchJob)] ?? string.Empty) };

        words.AddRange(ClaimRules.Sections.Select(section => (SectionKey(section), shipped[SectionKey(section)] ?? string.Empty)));

        foreach (var field in new[] { ResearchLane.TrialField, ResearchLane.ReviewField })
        {
            var key = ModelProfiles.JobField(ModelProfiles.ResearchJob, field) + ":" + ModelProfiles.UseField;

            words.Add((key, shipped[key] ?? string.Empty));
        }

        return words;
    }

    [Fact]
    public void NoResearchSettingAsShippedNamesAClaudeProfile()
    {
        var shipped = new ConfigurationBuilder().AddJsonFile(ResearchFeeds.ShippedConfiguration).Build();
        var words = ResearchProfileWords(shipped);

        // Stated in advance: the job's word, eight sections' and the review's name DeepSeek, ten, and the trial's
        // names none.
        Assert.Equal(11, words.Count);
        Assert.Equal(10, words.Count(word => word.Profile == "deepseek"));
        Assert.Equal([ModelProfiles.JobField(ModelProfiles.ResearchJob, ResearchLane.TrialField) + ":" + ModelProfiles.UseField], words.Where(word => word.Profile.Length == 0).Select(word => word.Setting));

        // No word names a profile answered on Claude's wire format, read off the profile's own format rather than its
        // name, so a Claude profile renamed is found too; and the file still holds the Claude profiles a later ruling
        // may name.
        Assert.All(
            words.Where(word => word.Profile.Length > 0),
            word => Assert.NotEqual(ResearchModelSettings.AnthropicFormat, shipped[ModelProfiles.Field(word.Profile, ModelProfiles.FormatField)]));
        Assert.Contains(
            shipped.GetSection(ModelProfiles.ProfilesSection).GetChildren(),
            profile => profile[ModelProfiles.FormatField] == ResearchModelSettings.AnthropicFormat);
    }

    static IConfiguration ShippedWith(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddJsonFile(ResearchFeeds.ShippedConfiguration)
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(ModelProfiles.KeyPath("Research"), ResearchFeeds.NotAKey),
                new KeyValuePair<string, string?>(ModelProfiles.KeyPath("Claude"), ResearchFeeds.NotAKey),
                .. values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)),
            ])
            .Build();

    static string SectionKey(string section) =>
        ModelProfiles.JobField(ModelProfiles.ResearchJob, ModelProfiles.SectionsField) + ":" + section;

    [Fact]
    public async Task ASectionMappedToAnotherProfileIsAskedOfThatModelWhileTheRestStayOnTheJobs()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // The short version already written today, so the pass writes the sections it summarises and not a
        // summary of a set no recording was made over.
        store.Execute("INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES ('KEYS', 'The short version', 1, '2026-09-08', 'a writer', 'accepted', 'prose', '[]', NULL);");

        var paid = new RecordedResearchModelFeed(Folder(), ResearchFeeds.Pinned());
        var claude = new RecordedResearchModelFeed(Folder(), ResearchFeeds.PinnedClaude());

        await FixtureReplay.Researcher(store, ResearchClock, paid: paid, sectionFeeds: new Dictionary<string, IResearchModelFeed> { [ClaimRules.CalendarSection] = claude })
            .RunAsync("KEYS", "research-sections");

        // The calendar asked of Claude alone, and stored under its identity; every other paid section of the job's.
        Assert.Equal([ClaimRules.CalendarSection], claude.Asked.Select(request => request.Section).Distinct());
        Assert.Equal(ClaimRules.CalendarSection, Assert.Single(claude.Asked).Section);
        Assert.DoesNotContain(paid.Asked, request => request.Section == ClaimRules.CalendarSection);
        Assert.Equal([claude.Identity], Query(store, $"SELECT model FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.CalendarSection}';"));
        Assert.All(
            Query(store, $"SELECT model FROM research_section WHERE ticker = 'KEYS' AND section IN ('The cause of each large move', 'The two cases', 'The risks, each with what would confirm it');"),
            model => Assert.Equal(paid.Identity, model));
        Assert.NotEqual(paid.Identity, claude.Identity);
    }

    [Fact]
    public void ASectionOrAProfileTheSettingsDoNotHoldRefusesTheJobByName()
    {
        var job = ResearchLane.Settings(ShippedWith());

        // As shipped, every section names the job's own profile, so nothing moves until the operator changes a word.
        var shipped = ResearchLane.Sections(ShippedWith(), job);

        Assert.Equal(ClaimRules.Sections.Order(StringComparer.Ordinal), shipped.Keys.Order(StringComparer.Ordinal));
        Assert.All(shipped.Values, settings => Assert.Same(job, settings));

        var section = Assert.Throws<InvalidOperationException>(() => ResearchLane.Sections(ShippedWith((SectionKey("The weather"), "deepseek")), job));

        Assert.Contains("'The weather'", section.Message, StringComparison.Ordinal);

        var profile = Assert.Throws<InvalidOperationException>(() => ResearchLane.Sections(ShippedWith((SectionKey(ClaimRules.TwoCasesSection), "a-model-nobody-set")), job));

        Assert.Contains("'a-model-nobody-set'", profile.Message, StringComparison.Ordinal);

        // A section naming another profile resolves to that profile's settings.
        var mapped = ResearchLane.Sections(ShippedWith((SectionKey(ClaimRules.TwoCasesSection), "claude-sonnet-no-thinking")), job);

        Assert.Equal("claude-sonnet-no-thinking", mapped[ClaimRules.TwoCasesSection].Profile);
        Assert.Same(job, mapped[ClaimRules.CauseSection]);
    }

    [Fact]
    public void APassWaitsOutThePeakWindowsOfEveryProfileItsSectionsName()
    {
        static IReadOnlyList<ResearchPricing> Prices(IConfiguration configuration) =>
            ModelProfiles.PricesFor(key => configuration[key], key => configuration.GetSection(key).GetChildren().Select(child => child.Value), ModelProfiles.ResearchJob);

        // As shipped, the job and every section on DeepSeek: one set of windows.
        Assert.Single(Prices(ShippedWith()));

        // The job on Claude, which names no window, and the two cases on DeepSeek, whose windows are 01 to 04 and
        // 06 to 10 UTC on weekdays.
        var pricings = Prices(ShippedWith(
            (ModelProfiles.Use(ModelProfiles.ResearchJob), "claude-sonnet"),
            (SectionKey(ClaimRules.TwoCasesSection), "deepseek")));

        Assert.Equal(2, pricings.Count);

        // A Monday at 02:00, inside DeepSeek's first window: the job's own profile alone would start then, and the
        // pass waits for the window's end; a pass of three hours from there would run into the second window, so it
        // waits for that one's end as well.
        var monday = DateTimeOffset.Parse("2026-09-28T02:00:00Z", CultureInfo.InvariantCulture);

        Assert.Equal(monday, pricings[0].StartFor(monday, TimeSpan.Zero));
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T04:00:00Z", CultureInfo.InvariantCulture), ResearchPricing.StartFor(pricings, monday, TimeSpan.Zero));
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T10:00:00Z", CultureInfo.InvariantCulture), ResearchPricing.StartFor(pricings, monday, TimeSpan.FromHours(3)));
    }

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    [Fact]
    public void AClaudeProfileWithThinkingOffSendsThinkingBetweenToolsAndIsNamedForIt()
    {
        var settings = ResearchLane.Settings(ShippedWith((ModelProfiles.Use(ModelProfiles.ResearchJob), "claude-sonnet-no-thinking")));

        Assert.Equal(ResearchModelSettings.ThinkingOff, settings.Thinking);
        Assert.Equal("claude-sonnet-5-5 thinking off", settings.Identity);

        using var sent = JsonDocument.Parse(AnthropicMessagesFeed.Body(ResearchFeeds.Recorded(settings), settings));

        Assert.Equal("between_tools", sent.RootElement.GetProperty(AnthropicMessagesFeed.ThinkingField).GetProperty("type").GetString());

        // What Sonnet 5.5 answered on 2026-09-30: thinking disabled refused with a 400 naming the form sent, and that
        // form answered with no thinking at all.
        Assert.Contains(
            "send \"thinking\": {\"type\": \"between_tools\"} instead of {\"type\": \"disabled\"}",
            AnthropicMessagesFeed.Refused("Research", "The close", 400, Captured("anthropic-thinking-disabled-refused.json")),
            StringComparison.Ordinal);

        var off = AnthropicMessagesFeed.ParseRecorded(Captured("anthropic-thinking-off.json"), "The close");

        Assert.Equal(("end_turn", 0), (off.FinishReason, off.ReasoningTokens));

        // The provider's default where the profile names none, and a thinking setting refused for the other format.
        var plain = ResearchLane.Settings(ShippedWith((ModelProfiles.Use(ModelProfiles.ResearchJob), "claude-sonnet")));

        using (var sentPlain = JsonDocument.Parse(AnthropicMessagesFeed.Body(ResearchFeeds.Recorded(plain), plain)))
        {
            Assert.False(sentPlain.RootElement.TryGetProperty(AnthropicMessagesFeed.ThinkingField, out _));
        }

        var openai = Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(ShippedWith((ModelProfiles.Field("deepseek", ModelProfiles.ThinkingField), "off"))));

        Assert.Contains("'openai'", openai.Message, StringComparison.Ordinal);

        // A token budget is not a setting: the models this build calls refuse one with a 400, as Sonnet 5.5 did.
        var budget = Assert.Throws<InvalidOperationException>(() => ResearchLane.Settings(ShippedWith(
            (ModelProfiles.Use(ModelProfiles.ResearchJob), "claude-sonnet"),
            (ModelProfiles.Field("claude-sonnet", ModelProfiles.ThinkingField), "1024"))));

        Assert.Contains("400", budget.Message, StringComparison.Ordinal);
        Assert.Contains(
            "\"thinking.type.enabled\" is not supported for this model",
            AnthropicMessagesFeed.Refused("Research", "The close", 400, Captured("anthropic-thinking-budget-refused.json")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEffortLevelIsSentBesideTheAnswersFormatInOneOutputSetting()
    {
        var settings = ResearchLane.Settings(ShippedWith(
            (ModelProfiles.Use(ModelProfiles.ResearchJob), "claude-sonnet"),
            (ModelProfiles.Field("claude-sonnet", ModelProfiles.ThinkingField), "high")));

        Assert.Equal("claude-sonnet-5-5 effort high", settings.Identity);

        // A request listing documents asks for its answer's format, and the effort sits beside it.
        var request = SectionPrompt.PaidRequest(settings.Identity, "KEYS", ClaimRules.TwoCasesSection, [], [new PromptDocument("d1", "A release", new DateOnly(2026, 9, 1), "Revenue rose.")]);

        using var sent = JsonDocument.Parse(AnthropicMessagesFeed.Body(request, settings));
        var output = sent.RootElement.GetProperty(AnthropicMessagesFeed.OutputConfigField);

        Assert.Equal("json_schema", output.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("high", output.GetProperty(AnthropicMessagesFeed.EffortField).GetString());
        Assert.False(sent.RootElement.TryGetProperty(AnthropicMessagesFeed.ThinkingField, out _));

        // An effort level Sonnet 5.5 answered on 2026-09-30.
        Assert.Equal("end_turn", AnthropicMessagesFeed.ParseRecorded(Captured("anthropic-thinking-effort.json"), "The close").FinishReason);
    }

    // A second model for a trial, answering each call with the next of its texts under an identity of its own.
    sealed class TrialModel(ResearchModelSettings settings, params string[] texts) : IResearchModelFeed
    {
        public int Requests { get; private set; }

        public int Probes => 0;

        public string Identity => settings.Identity;

        public List<ModelRequest> Asked { get; } = [];

        public Task<string?> UnreachableAsync(CancellationToken cancellation = default) => Task.FromResult<string?>(null);

        public Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);

            return Task.FromResult(new ResearchAnswer(settings.Model, texts[Requests++ % texts.Length], 100, 0, 100, 50, 0, "end_turn", DateTimeOffset.Parse("2026-09-08T21:30:00Z", CultureInfo.InvariantCulture)));
        }

        public decimal Price(ResearchAnswer answer) => settings.Pricing.Price(answer);

        public decimal Ceiling(ModelRequest request) => settings.Pricing.Ceiling(request, settings.AnswerTokens);
    }

    static ResearchModelSettings TrialSettings() =>
        ResearchFeeds.Pinned(null, (ModelProfiles.Use(ModelProfiles.ResearchJob), ResearchFeeds.ClaudeProfile), (ModelProfiles.KeyPath("Claude"), ResearchFeeds.NotAKey), (ModelProfiles.Field(ResearchFeeds.ClaudeProfile, ModelProfiles.ThinkingField), "off"));

    [Fact]
    public async Task ATrialWritesNoSectionRowRecordsBothModelsAndStopsAtTheReportsItNames()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var settings = TrialSettings();
        var trial = new ResearchTrial(settings, [ClaimRules.TwoCasesSection, ClaimRules.Sections[^1]], 3, new DateOnly(2026, 9, 8));

        // Each answer in the shape its section is asked for, resting on the first document it was handed.
        var model = new TrialModel(settings, "The bull case is that orders keep rising [D1].\n\nThe bear case is that supply stays short [D1].");
        var cap = new SpendCap(model, Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);
        var sectionsBefore = Query(store, "SELECT COUNT(*) FROM research_section;").Single();

        var outcomes = await new SectionTrial(cap, trial, ResearchClock, store.DatabaseFile).RunAsync("KEYS", "replay-research");

        // Nothing written to the report: no section row.
        Assert.Equal(sectionsBefore, Query(store, "SELECT COUNT(*) FROM research_section;").Single());

        // A row a section beside the pass's own, naming the trial's model and the pass's draft it was asked beside.
        var rows = Query(store, $"SELECT detail FROM run_log WHERE run_id = 'replay-research' AND stage LIKE '{SectionTrial.Stage}:%' ORDER BY rowid;");

        Assert.Equal(outcomes.Count, rows.Count);
        Assert.Contains(outcomes, outcome => outcome.Section == ClaimRules.TwoCasesSection && outcome.Outcome == SectionTrial.FirstTime);
        Assert.All(rows, row =>
        {
            using var detail = JsonDocument.Parse(row);

            Assert.Equal(settings.Identity, detail.RootElement.GetProperty("model").GetString());
            Assert.Equal(ResearchFeeds.Pinned().Identity, detail.RootElement.GetProperty("compared").GetProperty("model").GetString());
        });

        // Its calls stand under the trial's round, and a report's count and cost leave them out.
        Assert.All(model.Asked, request => Assert.Equal(settings.Identity, request.Model));
        var trialCalls = Query(store, $"SELECT stage FROM run_log WHERE run_id = 'replay-research' AND stage LIKE 'research call:%{TrialCalls.Round}%';");

        Assert.Equal(model.Requests, trialCalls.Count);
        Assert.All(trialCalls, stage => Assert.True(TrialCalls.Is(stage)));

        decimal Sum(IEnumerable<string> spends) => spends.Sum(spend => decimal.Parse(spend, CultureInfo.InvariantCulture));

        var passSpend = Sum(Query(store, $"SELECT spend FROM run_log WHERE run_id = 'replay-research' AND stage LIKE 'research call%' AND stage NOT LIKE '%, {TrialCalls.Round}%';"));
        var priced = await new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, ResearchClock).PaidCallSpendsAsync();

        var trialSpend = Sum(Query(store, $"SELECT spend FROM run_log WHERE run_id = 'replay-research' AND stage LIKE '%, {TrialCalls.Round}%';"));

        Assert.True(trialSpend > 0m);
        Assert.Equal(passSpend, priced.Where(call => call.RunId == "replay-research").Sum(call => call.Spend));

        // Each call's spend stands once on the run, on the call's own row, which the caps sum: the trial's rows carry
        // their cost in their detail alone.
        Assert.Equal(passSpend + trialSpend, Sum(Query(store, "SELECT spend FROM run_log WHERE run_id = 'replay-research';")));
        Assert.Equal(trialSpend, rows.Sum(row => decimal.Parse(JsonDocument.Parse(row).RootElement.GetProperty("cost").GetString()!, CultureInfo.InvariantCulture)));

        // Three reports and no fourth: the pass's row copied under two more runs, each tried, and a fourth tried to no end.
        foreach (var run in (string[])["research-trial-2", "research-trial-3", "research-trial-4"])
        {
            store.Execute($"INSERT INTO run_log SELECT '{run}', stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail FROM run_log WHERE run_id = 'replay-research' AND stage = 'research';");
        }

        Assert.NotEmpty(await new SectionTrial(cap, trial, ResearchClock, store.DatabaseFile).RunAsync("KEYS", "research-trial-2"));
        Assert.NotEmpty(await new SectionTrial(cap, trial, ResearchClock, store.DatabaseFile).RunAsync("KEYS", "research-trial-3"));

        var asked = model.Requests;

        Assert.Empty(await new SectionTrial(cap, trial, ResearchClock, store.DatabaseFile).RunAsync("KEYS", "research-trial-4"));
        Assert.Equal(asked, model.Requests);
        Assert.Equal("3", Query(store, $"SELECT COUNT(DISTINCT run_id) FROM run_log WHERE stage LIKE '{SectionTrial.Stage}:%';").Single());
    }

    [Fact]
    public void ATrialsCallsAreLeftOutOfTheReportsTheRunPageCounts()
    {
        var night = new DateOnly(2026, 9, 8);

        EquityBrief.Api.Reading.RunStageRow Row(string runId, string stage) =>
            new(runId, stage, DateTimeOffset.Parse("2026-09-08T21:10:00Z", CultureInfo.InvariantCulture), DateTimeOffset.Parse("2026-09-08T21:11:00Z", CultureInfo.InvariantCulture), "ok", 0, 1, 0, "0.01", "{}");

        // One pass whose own call answered, and one run holding a trial's call alone.
        var log = new List<EquityBrief.Api.Reading.RunStageRow>
        {
            Row(PassRun.Prefix + "KEYS-a", "research call: The two cases"),
            Row(PassRun.Prefix + "KEYS-b", "research call: The two cases, " + TrialCalls.Round),
            Row(PassRun.Prefix + "KEYS-b", "research call: The short version, " + TrialCalls.Round + ", round 2"),
        };

        var counted = Assert.Single(EquityBrief.Api.Reading.RunScreen.Research([(night, log)]));

        Assert.Equal(1, counted.PaidPasses);
    }
}
