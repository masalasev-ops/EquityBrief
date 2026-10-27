using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 18.4 correction of the report page: a section the checker left out is written again alone on the
// local model, held to no day, asking the paid model nothing and never moved to it.
// see: A section the checker left out is written again alone on the local model at a press, and the day's one regenerate does not hold it
public partial class FixtureExpectations
{
    [Fact]
    public async Task ASectionAskedForAloneIsAskedOfTheLocalModelOnTheDayAReportWasWrittenAndNeverOfThePaidModel()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A pass that wrote one section asked for alone is no report: the day's one regenerate is still unspent after it.
        await FixtureReplay.Researcher(store, ResearchClock, archive: new NoRelease(), news: new NoArticles(), localModel: new NothingAnsweringLocal())
            .RunAsync("KEYS", "research-first-alone", new ResearchPassRequest(Section: "What the company sells"));
        WroteASection(store, "research-first-alone");

        Assert.False(await ResearchRunner.WrittenTodayAsync(store.DatabaseFile, ResearchClock, "KEYS"));

        await FixtureReplay.Researcher(store, ResearchClock, archive: new NoRelease(), news: new NoArticles()).RunAsync("KEYS", "research-before-alone");
        WroteASection(store, "research-before-alone");

        Assert.True(await ResearchRunner.WrittenTodayAsync(store.DatabaseFile, ResearchClock, "KEYS"));

        // The cause of each large move, a section the paid model writes in a report, asked for alone on the day a report was
        // written: the day's gate does not stop it, the paid model is neither probed nor asked, and the local model is the one
        // asked, which here does not answer, so the section stays not written with the local model's reason and is not
        // moved to the paid path.
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Pinned());
        var local = new NothingAnsweringLocal();
        var alone = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, localModel: local)
            .RunAsync("KEYS", "research-section-alone", new ResearchPassRequest(Section: "The cause of each large move"));

        Assert.Equal(ResearchRunner.Unavailable, alone.Outcome);
        Assert.StartsWith(ProseWriter.Unavailable, alone.Reason, StringComparison.Ordinal);
        Assert.Equal(["The cause of each large move"], alone.Warranted);
        Assert.Equal((0, 0), (paid.Probes, paid.Requests));
        Assert.True(local.Requests > 0, "the local model was not asked for the section");
        Assert.Empty(alone.Written);
        Assert.Contains(alone.NotWritten, line => line.Section == "The cause of each large move" && line.Reason.StartsWith(ProseWriter.Unavailable, StringComparison.Ordinal));
        Assert.Empty(alone.LeftForThePaidPath ?? []);

        // The industry cycle, its theme's and not the name's, and a name no section carries are never asked for alone.
        foreach (var (section, runId) in new[] { (ClaimRules.CycleSection, "research-cycle-alone"), ("Not a section", "research-no-section-alone") })
        {
            var refused = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, localModel: local)
                .RunAsync("KEYS", runId, new ResearchPassRequest(Section: section));

            Assert.Equal((ResearchRunner.NotWarranted, ResearchRunner.NotOwnSection), (refused.Outcome, refused.Reason));
        }

        Assert.Equal((0, 0), (paid.Probes, paid.Requests));
    }
}
