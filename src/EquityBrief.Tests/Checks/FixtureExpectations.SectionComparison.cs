using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from 20.6: the report sections compared on the local model. 6.8's comparison over the fixture's
// evidence set run again with every section in the local lane under the model the comparison's own folder names, its
// answers read from that folder's recordings and from no other, beside the paid side read from the fixture's own
// recordings: each section's outcome on both sides through the claim checker, and no store written but the replay's own.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
public partial class FixtureExpectations
{
    // The local side's recordings, in a folder of their own that no replay of the pipeline reads, and the variable that
    // has this test record them again live, by hand, with LM Studio holding the model and the history read stopped.
    internal const string ComparisonFolderName = "local-comparison";

    internal const string RecordComparisonVariable = "EQUITYBRIEF_RECORD_LOCAL_COMPARISON";

    static string ComparisonFolder() => Path.Combine(Folder(), ComparisonFolderName);

    [Fact]
    public async Task TheReportSectionsAreComparedOnTheLocalModelOverTheFixturesEvidenceSet()
    {
        var gemma = LocalLane.OfFixture(ComparisonFolder());
        var recording = string.Equals(Environment.GetEnvironmentVariable(RecordComparisonVariable), "1", StringComparison.Ordinal);
        var recorded = new RecordedLocalModelFeed(ComparisonFolder());
        ILocalModelFeed local = recording ? new RecordingLocalModelFeed(gemma, ComparisonFolder()) : recorded;

        // The local side: every section in the local lane under the comparison's model, over the evidence the pass hands.
        using var localStore = await FixtureReplay.ReplayedForResearchAsync();

        await FixtureReplay.Researcher(localStore, ResearchClock, lane: ClaimRules.Sections, localModel: local, localSettings: gemma)
            .RunAsync(FixtureReplay.ResearchName, "replay-research", new ResearchPassRequest(PaidForLocal: false));

        // A recording made by hand writes what it found to the machine's temporary folder for the expectation and the
        // manifest to be written from, and asserts nothing.
        if (local is RecordingLocalModelFeed made)
        {
            File.WriteAllLines(
                Path.Combine(Path.GetTempPath(), "equitybrief-local-comparison.txt"),
                [.. Outcomes(localStore), FormattableString.Invariant($"calls {made.Requests}"), .. made.Kept.Select(kept => kept.File + " " + kept.Section)]);

            return;
        }

        // The paid side: every section in the paid lane, from the fixture's own recordings, as 6.8's comparison read it.
        using var paidStore = await FixtureReplay.ResearchedAsync(lane: [], paidForLocal: true);

        var expected = Expected("local-comparison").GetProperty("local");

        // Each section's outcome on both sides: the version the checker accepted, or the code's own words where it left
        // the model's draft out.
        Assert.Equal(Outcomes(Expected("research-record").GetProperty("comparison").GetProperty("paid")), Outcomes(paidStore));
        Assert.Equal(Outcomes(expected), Outcomes(localStore));
        Assert.Equal(expected.GetProperty("calls").GetInt32(), recorded.Requests);

        // And why the checker left each local draft out, in its own words.
        Assert.Equal(
            expected.GetProperty("reasons").EnumerateObject().Select(reason => $"{reason.Name}|{reason.Value.GetString()}").Order(StringComparer.Ordinal),
            Query(localStore, "SELECT section, reject_reason FROM research_section WHERE ticker = 'KEYS' AND status = 'fallback' ORDER BY section;"));

        // Every local answer read from the comparison's own folder under its model, and the folder holding those alone.
        Assert.All(recorded.Asked, request => Assert.Equal(gemma.Model, request.Model));
        Assert.Equal(
            Directory.GetFiles(ComparisonFolder(), RecordedLocalModelFeed.FilePrefix + "*.json").Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal),
            recorded.Asked.Select(RecordedLocalModelFeed.FileFor).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheRecorderKeepsEachAnswerAsSentAndAnswersARequestAskedAgainFromItsFirstRecording()
    {
        using var folder = new TemporaryDirectory();

        var settings = new LocalModelSettings("gemma-4", "http://127.0.0.1:1234/v1/", Gemma, 30, 50176, 30);
        var answers = new Queue<string>(["first", "second", "third"]);

        static string Body(string text) =>
            JsonSerializer.Serialize(new { model = Gemma, choices = new[] { new { message = new { role = "assistant", content = text }, finish_reason = "stop" } }, usage = new { prompt_tokens = 10, completion_tokens = 2 } });

        // A runtime holding the model loaded, which answers each completion with the next of three answers.
        var runtime = new ScriptedRuntime((path, _) => Task.FromResult(path switch
        {
            "/api/v1/models" => Answered(System.Net.HttpStatusCode.OK, Runtime("local-runtime-models-gemma-loaded.json")),
            "/v1/chat/completions" => Answered(System.Net.HttpStatusCode.OK, Body(answers.Dequeue())),
            _ => Answered(System.Net.HttpStatusCode.NotFound, "{}"),
        }));
        var recorder = new RecordingLocalModelFeed(settings, folder.Path, runtime);
        var once = new ModelRequest("local", "What the company sells", Gemma, [], "system", "a prompt");
        var other = once with { Prompt = "another prompt" };

        // Worked by hand: the first request answered first and kept as sent; the second request answered second; the
        // first asked again answered from its recording, the runtime not asked, and its file left as first written.
        Assert.Equal("first", (await recorder.CompleteAsync(once)).Text);
        Assert.Equal("second", (await recorder.CompleteAsync(other)).Text);
        Assert.Equal("first", (await recorder.CompleteAsync(once)).Text);
        Assert.Equal(3, recorder.Requests);
        Assert.Equal(2, runtime.Asked.Count(asked => asked.StartsWith("POST /v1/chat/completions", StringComparison.Ordinal)));
        Assert.Equal(Body("first"), File.ReadAllText(Path.Combine(folder.Path, RecordedLocalModelFeed.FileFor(once))));
        Assert.Equal([RecordedLocalModelFeed.FileFor(once), RecordedLocalModelFeed.FileFor(other)], recorder.Kept.Select(kept => kept.File));

        // And the recorded feed reads back what the recorder kept.
        Assert.Equal("second", (await new RecordedLocalModelFeed(folder.Path).CompleteAsync(other)).Text);
    }

    // Each section's outcome as a line: accepted at its version, or written by code where the checker left the model's
    // draft out, read off a store or off an expectation's two lists.
    static string[] Outcomes(TemporaryStore store) =>
    [
        .. Query(store, "SELECT section, version FROM research_section WHERE ticker = 'KEYS' AND status = 'accepted' ORDER BY section;").Select(row => "accepted " + row),
        .. Query(store, "SELECT section FROM research_section WHERE ticker = 'KEYS' AND status = 'fallback' ORDER BY section;").Select(row => "fallback " + row),
    ];

    static string[] Outcomes(JsonElement side) =>
    [
        .. side.GetProperty("accepted").EnumerateObject().Select(section => $"accepted {section.Name}|{section.Value.GetInt32()}").Order(StringComparer.Ordinal),
        .. Listed(side.GetProperty("fallback")).Order(StringComparer.Ordinal).Select(section => "fallback " + section),
    ];
}
