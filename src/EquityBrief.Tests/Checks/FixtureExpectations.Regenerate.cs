using EquityBrief.Core.Providers;
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
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());

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
}
