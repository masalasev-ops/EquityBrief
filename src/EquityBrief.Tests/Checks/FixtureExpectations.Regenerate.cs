using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 5.8 correction: a regenerate is held to once a name a day by the pass
// itself, so a request the drain reaches for a name a pass already wrote that day starts nothing,
// fetches nothing and asks no model.
// see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
public partial class FixtureExpectations
{
    [Fact]
    public async Task ARegenerateOnADayAPassForTheNameRanStartsNothingFetchesNothingAndSaysWhy()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        Assert.False(await ResearchRunner.WrittenTodayAsync(store.DatabaseFile, ResearchClock, "KEYS"));

        var first = await FixtureReplay.Researcher(store, ResearchClock, archive: new NoRelease(), news: new NoArticles()).RunAsync("KEYS", "research-before-regenerate");

        Assert.Equal(ResearchRunner.Written, first.Outcome);
        Assert.True(await ResearchRunner.WrittenTodayAsync(store.DatabaseFile, ResearchClock, "KEYS"));

        // The regenerate a press asks for, the paid model asked for every section, on the day the pass
        // ran: nothing fetched, no model asked, and its row says why and counts no request.
        var news = new NoArticles();
        var archive = new NoRelease();
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Pinned());

        var again = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, archive: archive, news: news)
            .RunAsync("KEYS", "research-regenerate-same-day", new ResearchPassRequest(Refresh: true, PaidForLocal: true));

        Assert.Equal(ResearchRunner.NotWarranted, again.Outcome);
        Assert.Equal(ResearchRunner.RegeneratedToday, again.Reason);
        Assert.Equal(0, news.Requests + archive.Requests + paid.Probes + paid.Requests);
        Assert.Equal(
            [$"{ResearchRunner.NotWarranted}|0"],
            Query(store, "SELECT outcome, network_requests FROM run_log WHERE run_id = 'research-regenerate-same-day' AND stage = 'research';"));

        // The next day in New York the check a regenerate asks before it fetches lets it through.
        Assert.False(await ResearchRunner.WrittenTodayAsync(store.DatabaseFile, FixedClock.At(ResearchNight.AddDays(1), SessionZones.UnitedStates), "KEYS"));
    }

    // A call's row as the spend cap writes one the model could not be reached for, under the pass's own run.
    static void Unreached(TemporaryStore store, string runId, string stage) =>
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('{runId}', '{stage}', '2026-09-30T14:40:04Z', '2026-09-30T14:41:05Z', '{SpendCap.Unavailable}', 0, 1, 0, '0', '{{}}');");

    [Fact]
    public async Task APassTheModelCouldNotBeReachedForOnASectionIsNotTheDaysReport()
    {
        // A pass written to the end whose model could not be reached for a section of its report lost that section
        // to the connection, and it is not the day's report: a regenerate the same day runs. A trial's or a
        // review's call that could not be reached is no part of the report and leaves the pass the day's.
        // see: A pass the model could not be reached for on a section of the report is not the day's report
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        var first = await FixtureReplay.Researcher(store, ResearchClock, archive: new NoRelease(), news: new NoArticles()).RunAsync("KEYS", "research-cut-short");

        Assert.Equal(ResearchRunner.Written, first.Outcome);

        Unreached(store, "research-cut-short", SpendCap.StageFor("The two cases", TrialCalls.ReviewRound));
        Unreached(store, "research-cut-short", SpendCap.StageFor("The short version", TrialCalls.Round));

        Assert.True(await ResearchRunner.WrittenTodayAsync(store.DatabaseFile, ResearchClock, "KEYS"));

        Unreached(store, "research-cut-short", SpendCap.StageFor("The two cases", ResearchRunner.SecondRound));

        Assert.False(await ResearchRunner.WrittenTodayAsync(store.DatabaseFile, ResearchClock, "KEYS"));

        // And the pass itself, not the check alone, lets the regenerate through: past the day's gate it asks
        // whether the model answers, which here it does not, so it stops there having asked for nothing.
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Pinned(), unreachable: "the model does not answer");
        var again = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, archive: new NoRelease(), news: new NoArticles())
            .RunAsync("KEYS", "research-regenerate-after-a-cut", new ResearchPassRequest(Refresh: true, PaidForLocal: true));

        Assert.NotEqual(ResearchRunner.NotWarranted, again.Outcome);
        Assert.Equal((1, 0), (paid.Probes, paid.Requests));
    }
}
