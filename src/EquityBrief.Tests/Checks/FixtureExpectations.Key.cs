using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 6.10 correction: the key under each figure handed its facts as a
// reader reads them, and dated by the night of the facts file it was written from.
public partial class FixtureExpectations
{
    // ---- the facts the key is handed ----

    [Fact]
    public void TheKeyIsHandedEachFactAsAReaderReadsIt()
    {
        var worked = Expected("prose").GetProperty("keyFactsAsRead").GetProperty("facts").EnumerateArray().ToArray();

        Assert.NotEmpty(worked);

        foreach (var fact in worked)
        {
            var read = FactReading.Read(new Fact(fact.GetProperty("name").GetString()!, fact.GetProperty("value").GetString()!, "fixture"));

            Assert.Equal(fact.GetProperty("readName").GetString(), read.Name);
            Assert.Equal(fact.GetProperty("readValue").GetString(), read.Value);
        }
    }

    [Fact]
    public async Task EveryFactTheNightWritesIsHandedToTheKeyAsAFigureTheCheckerMatchesToItsStoredValue()
    {
        // The population is every fact of every facts file the fixture's night writes: a reading
        // the claim checker would refuse is a first draft refused for copying what it was handed,
        // so each is the stored value or one figure the checker matches to it.
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = new NothingAnsweringLocal() });

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        var facts = Query(store, "SELECT payload FROM facts WHERE payload != '';").SelectMany(FactsFile.Read).ToArray();

        Assert.True(facts.Length > 100, $"The night wrote {facts.Length} facts, which is too few to read the rule over.");

        var rounded = 0;

        foreach (var fact in facts)
        {
            var read = FactReading.Read(fact);

            if (read.Value == fact.Value)
            {
                continue;
            }

            rounded++;

            var figures = ClaimRules.Figures(read.Value);

            Assert.True(
                figures.Count == 1 && ClaimRules.Matches(figures[0], [fact]),
                $"{fact.Name} = {fact.Value} is handed as {read.Value}, which the checker does not match to it.");
        }

        Assert.True(rounded > 0, "No fact the night wrote is read differently from the way it is stored, so the rule was never reached.");
    }

    [Fact]
    public void OnlyTheKeyIsHandedItsFactsAsAReaderReadsThem()
    {
        // Every other section is asked over the file as stored, since each of its recordings is keyed
        // on the request that carried it.
        Fact[] facts =
        [
            new("close", "333.42", "bar"),
            new("sma200", "289.30685", "indicator"),
            new("latest quarter revenue", "1846000000.00", "fundamental"),
        ];

        var key = SectionPrompt.Prompt("KEYS", ClaimRules.ComputedSection, facts, []);

        Assert.Contains("- close = 333.42\n- 200-day average = 289.31\n- latest quarter revenue = 1.85 billion\n", key, StringComparison.Ordinal);
        Assert.DoesNotContain("sma200", key, StringComparison.Ordinal);

        foreach (var section in ClaimRules.Sections.Where(section => section != ClaimRules.ComputedSection && section != ClaimRules.CycleSection))
        {
            Assert.Contains(
                "- close = 333.42\n- sma200 = 289.30685\n- latest quarter revenue = 1846000000.00\n",
                SectionPrompt.Prompt("KEYS", section, facts, []),
                StringComparison.Ordinal);
        }
    }

    // ---- the night the key is dated by ----

    [Fact]
    public async Task TheKeyIsDatedByTheNightOfItsFactsFileAndPassedOverUntilANewerOneArrives()
    {
        // A pass two days after the fixture's night writes the key from that night's facts file, so
        // its row is dated by that night rather than by the day it was written, and a pass the day
        // after passes it over as written for the newest facts file, asking the model nothing.
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = new NothingAnsweringLocal() });

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        var facts = Query(store, "SELECT MAX(session_date) FROM facts WHERE ticker = 'KEYS' AND payload != '';").Single();
        var local = new RecordedLocalModelFeed(Folder());
        var nothing = new Dictionary<string, IReadOnlyList<StoredDocument>>(StringComparer.Ordinal);

        ProseWriter Writer(DateTimeOffset at) =>
            new(local, new LocalModelSettings(null, null, null, null, null), [ClaimRules.ComputedSection], FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);

        var twoDaysOn = new DateTimeOffset(2026, 9, 10, 16, 0, 0, TimeSpan.Zero);
        var written = await Writer(twoDaysOn).WriteAsync("KEYS", nothing, "key-two-days-on");

        Assert.Equal([ClaimRules.ComputedSection], written.Written.Select(section => section.Section));
        Assert.Equal("2026-09-08", facts);
        Assert.Equal([facts], Query(store, $"SELECT as_of FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.ComputedSection}';"));

        await new ClaimChecker(FixedClock.At(twoDaysOn, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("key-two-days-on-claims");

        Assert.Equal([ClaimChecker.Accepted], Query(store, $"SELECT status FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.ComputedSection}';"));

        var again = await Writer(twoDaysOn.AddDays(1)).WriteAsync("KEYS", nothing, "key-three-days-on");

        Assert.Empty(again.Written);
        Assert.Equal([(ClaimRules.ComputedSection, ProseWriter.WrittenForTheNight)], again.Skipped.Select(section => (section.Section, section.Reason)));
        Assert.Equal(1, local.Requests);
    }

    [Fact]
    public async Task TheKeyThePaidLaneWritesIsDatedByTheNightOfItsFactsFileAndItsRetryIsReadOnThatNight()
    {
        // A pass two days after the fixture's night, the page asking the paid model for the local
        // lane, writes the key from that night's facts file, so its row is dated by that night as the
        // prose writer dates it and not by the day the pass ran. A first draft the checker refuses is
        // written once more inside the pass, the refusal read on the night the draft is dated by. The
        // year holds no article and no release, so the key is the one section a draft is asked for.
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        var paid = new ScriptedModel("The close is 98765.43.", "The key reads the figures drawn above it.");
        var twoDaysOn = FixedClock.At(new DateTimeOffset(2026, 9, 10, 16, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        var pass = await FixtureReplay.Researcher(store, twoDaysOn, paid: paid, archive: new NoRelease(), news: new NoArticles(), search: new NoResults())
            .RunAsync("KEYS", "key-paid-two-days-on", new ResearchPassRequest(PaidForLocal: true));

        Assert.Equal(ResearchRunner.Written, pass.Outcome);
        Assert.Equal(new DateOnly(2026, 9, 10), pass.AsOf);
        Assert.Equal(2, paid.Requests);
        Assert.Equal(
            ["1|2026-09-08|rejected", "2|2026-09-08|accepted"],
            Query(store, $"SELECT version || '|' || as_of || '|' || status FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.ComputedSection}' ORDER BY version;"));
    }
}
