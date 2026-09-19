using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Fundamentals;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: a pass the page started, watched until it ends. The line
// states the step the pass's own rows are on, the page asks again while it runs, and the
// sections arrive as they are written.
// see: A pass the page starts is watched until it ends and the page redraws as each section lands
public partial class ReadSurface
{
    const string Watching = "2026-09-08T21:30:00Z";

    static string Row(string runId, string stage, string started, string? ended) =>
        "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
        $"('{runId}', '{stage}', '{started}', {(ended is null ? "NULL" : $"'{ended}'")}, 'ok', 0, 0, 0, '0', NULL);";

    [Fact]
    public async Task APassThePageStartedIsWatchedByItsOwnRowsAndSaysWhatItIsDoing()
    {
        using var store = await FixtureExpectations.WithListings();

        var name = FiredNamesOn(store, NightIn(store))[0];
        var watching = DateTimeOffset.Parse(Watching, CultureInfo.InvariantCulture);
        var run = PassRun.IdFor(watching.AddSeconds(4), name);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Before the pass writes a row there is no run, and the page says the pass has started
        // rather than reading an earlier pass's rows as this one's.
        store.Execute(Row(PassRun.IdFor(watching.AddDays(-1), name), ReadApi.ProseStage, "2026-09-07T21:30:00Z", "2026-09-07T21:34:00Z"));

        var starting = await client.GetStringAsync($"{SinglePageApp.PassRoute}{name}?since={Watching}");

        Assert.Contains("data-state=\"starting\"", starting, StringComparison.Ordinal);
        Assert.Contains("the pass has started and has written nothing yet", starting, StringComparison.Ordinal);

        // While it runs, the step is the newest row that has not ended, in the reader's words
        // rather than in the stage's own name, and a paid call names the section it asked for.
        store.Execute(
            Row(run, FundamentalsFetcher.Stage, "2026-09-08T21:30:04Z", "2026-09-08T21:30:09Z") +
            Row(run, ProseWriter.Stage, "2026-09-08T21:30:09Z", "2026-09-08T21:31:30Z") +
            Row(run, SpendCap.Stage + ": The two cases", "2026-09-08T21:31:30Z", null));

        var running = await client.GetStringAsync($"{SinglePageApp.PassRoute}{name}?since={Watching}");

        Assert.Contains("data-state=\"running\"", running, StringComparison.Ordinal);
        Assert.Contains("asking the research model for The two cases", running, StringComparison.Ordinal);

        // The count of sections is the name's own, read off the store by the test's own query,
        // and it is what the page watches to know one more has landed.
        var written = Rows(store, $"SELECT section FROM research_section WHERE ticker = '{name}' AND status = 'accepted';").Count;

        Assert.Contains($"data-sections=\"{written}\"", running, StringComparison.Ordinal);

        // The pass's own row is written last, and that is what ends the watch.
        store.Execute(Row(run, ResearchRunner.Stage, "2026-09-08T21:33:00Z", "2026-09-08T21:33:01Z"));

        var ended = await client.GetStringAsync($"{SinglePageApp.PassRoute}{name}?since={Watching}");

        Assert.Contains("data-state=\"ended\"", ended, StringComparison.Ordinal);
        Assert.Contains("the pass has ended", ended, StringComparison.Ordinal);

        // A request naming no instant watches nothing rather than whatever ran last.
        var unasked = await client.GetAsync($"{SinglePageApp.PassRoute}{name}");

        Assert.Equal(400, (int)unasked.StatusCode);
        Assert.Contains("data-state=\"unasked\"", await unasked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheStepsThePageNamesAreTheStagesAPassWritesAndThePageAsksAgainUntilItEnds()
    {
        // The read surface holds no reference to the worker, so the stages it names are stated
        // there and read against the worker's own here, as the prose and research stages are.
        Assert.Equal(FundamentalsFetcher.Stage, NameScreen.PassSteps.Keys.First());
        Assert.Equal(ClaimChecker.Stage, NameScreen.PassSteps.Keys.Last());

        foreach (var (stage, words) in NameScreen.PassSteps)
        {
            Assert.NotEqual(stage, words);
        }

        foreach (var stage in new[]
        {
            FundamentalsFetcher.Stage, FactsAssembler.Stage, ChangeDetector.Stage, StalenessJudge.Stage,
            ThemeResearchRunner.Stage, ProseWriter.Stage, SpendCap.Stage, ClaimChecker.Stage,
        })
        {
            Assert.True(NameScreen.PassSteps.ContainsKey(stage), $"The page names no words for the stage {stage}.");
        }

        // And the run a pass writes under is the one the surface looks for.
        var run = PassRun.IdFor(DateTimeOffset.Parse("2026-09-08T21:30:04Z", CultureInfo.InvariantCulture), "NVDA");

        Assert.True(PassRun.IsFor(run, "NVDA"));
        Assert.False(PassRun.IsFor(run, "VDA"));
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T21:30:04Z", CultureInfo.InvariantCulture), PassRun.StartedAt(run));

        // The page watches until the pass ends, redraws when one more section has landed, and
        // stops asking at the bound. Read off the shell's own script, the suite having no
        // browser to run it in.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains($"tick < {SinglePageApp.PassTicks}", shell, StringComparison.Ordinal);
        Assert.Contains($"setTimeout(wait, {SinglePageApp.PassTickMillis})", shell, StringComparison.Ordinal);
        Assert.Contains("data-watch-from", shell, StringComparison.Ordinal);
        Assert.Contains("if (location.hash !== hash) { return; }", shell, StringComparison.Ordinal);
        Assert.Contains("if ((written !== null && sections !== written) || ended) {", shell, StringComparison.Ordinal);
        Assert.Contains("scrollTo(0, kept);", shell, StringComparison.Ordinal);
    }
}
