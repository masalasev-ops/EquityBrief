using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, an industry cycle the model declined: named as declined for lack of industry sources with
// what the searches found, the sector named where the list carries no site of its own for it, a draft citing none
// of the pages handed declined and never stored, a theme search adding its sector's sites and no other sector's,
// and the rule a proposed site joins the list by.
// see: An industry cycle the model declined is named as declined for lack of industry sources
// see: A theme search adds its sector's sites, and a site joins the list only where a measurement found industry material on it
public partial class FixtureExpectations
{
    // A search tool that finds nothing and keeps each query it was asked.
    sealed class AskedSearch : ISearchFeed
    {
        public List<SearchQuery> Asked { get; } = [];

        public int Requests => Asked.Count;

        public Task<SearchAnswer> SearchAsync(SearchQuery query, CancellationToken cancellation = default)
        {
            Asked.Add(query);

            return Task.FromResult(new SearchAnswer([]));
        }
    }

    [Fact]
    public async Task TheFixturesCycleTheModelAnsweredWithNothingIsNamedAsDeclinedWithThePagesItWasHanded()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        // The theme's row keeps what the call was handed, each page with its site and its density.
        var theme = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'replay-research' AND stage = 'theme research';").Single()).RootElement;
        var handed = theme.GetProperty("handed").EnumerateArray().ToArray();

        Assert.Equal(Expected("research-record").GetProperty("theme").GetProperty("handed").GetInt32(), handed.Length);
        Assert.All(handed, page => Assert.True(page.GetProperty("mentions").GetDouble() >= ThemeSearch.MentionsPerTenThousand));

        var sites = handed.Select(page => page.GetProperty("site").GetString()!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // The pass's line for the cycle: declined, the pages about the industry counted and their sites named, and
        // the member's sector named, since the list carries no site of its own for Technology.
        var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'replay-research' AND stage = 'research';").Single()).RootElement;
        var line = detail.GetProperty("notWritten").EnumerateArray().Single(one => one.GetProperty("section").GetString() == ClaimRules.CycleSection).GetProperty("reason").GetString()!;

        Assert.Equal(
            ResearchRunner.ThemeNotRefreshed
                + $"{ThemeResearchRunner.Declined}: the searches found {handed.Length} page(s) about {FixtureReplay.KeysIndustry}, from {string.Join(" and ", sites)}, and the model wrote nothing from them"
                + "; the source list carries no site of its own for the Technology sector",
            line);
        Assert.Empty(Query(store, "SELECT theme FROM theme_section;"));
    }

    [Fact]
    public async Task ACycleDraftCitingNoPageItWasHandedIsDeclinedNotStoredAndNotAskedForAgain()
    {
        using var store = new TemporaryStore().Migrated();
        using var folder = new TemporaryDirectory();

        OnePageAboutTheIndustry(folder.Path);

        // A draft saying it cannot write the section, which cites nothing it was handed.
        var model = new ScriptedModel("Nothing in the documents supports a view of where the industry's prices are.");
        var cap = new SpendCap(model, Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);

        var outcome = await FixtureReplay.Themer(store, ResearchClock, cap, new ClaimChecker(ResearchClock, store.DatabaseFile), new RecordedSearchFeed(folder.Path)).RunAsync(FixtureReplay.RecordedTheme, "theme-uncited", sector: "Technology");

        Assert.Equal(1, model.Requests);
        Assert.Empty(Query(store, "SELECT version FROM theme_section;"));
        Assert.Equal(
            $"{ThemeResearchRunner.Declined}: the searches found 1 page(s) about {FixtureReplay.RecordedTheme}, from semiconductors.org, and the model wrote nothing from them; the source list carries no site of its own for the Technology sector",
            Assert.Single(outcome.NotWritten, one => one.Section == ClaimRules.CycleSection).Reason);
    }

    [Fact]
    public async Task AThemeSearchAddsItsSectorsSitesAndNoOtherSectorsAndNamesASectorWithNone()
    {
        using var store = new TemporaryStore().Migrated();

        var sectors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Healthcare"] = ["cms.gov", "fda.gov"],
            ["Basic Materials"] = ["cefic.org"],
        };

        IReadOnlyList<string> list = SourceLists.Read(Path.Combine(Repository.Root, SourceLists.FileName)).Industry;

        // Each theme searched once, since a theme is researched once a day.
        async Task<(AskedSearch Search, ThemePassOutcome Outcome)> Searched(string theme, string? sector, string runId)
        {
            var search = new AskedSearch();
            var cap = new SpendCap(new ScriptedModel(), Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);

            var outcome = await FixtureReplay.Themer(store, ResearchClock, cap, new ClaimChecker(ResearchClock, store.DatabaseFile), search, sectors)
                .RunAsync(theme, runId, sector: sector);

            return (search, outcome);
        }

        // A Healthcare member's theme: the list's sites and Healthcare's, one search each, and no Basic Materials site.
        var (healthcare, searched) = await Searched("Medical Devices", "Healthcare", "theme-healthcare");

        Assert.Equal(ThemeResearchRunner.Written, searched.Outcome);
        Assert.All(healthcare.Asked, query => Assert.Single(query.Domains));
        Assert.Equal([.. list, "cms.gov", "fda.gov"], [.. healthcare.Asked.SelectMany(query => query.Domains)]);

        // A member of a sector the list carries no site of its own for: the list's sites alone, and its line says so.
        var (energy, found) = await Searched("Oil & Gas Integrated", "Energy", "theme-energy");

        Assert.Equal(list, [.. energy.Asked.SelectMany(query => query.Domains)]);
        Assert.EndsWith("; the source list carries no site of its own for the Energy sector", Assert.Single(found.NotWritten).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AProposedSiteJoinsItsSectorOnlyWhereItReturnedAnAdmittedPageAboutADeclinedIndustry()
    {
        string[] declined = ["Medical Devices", "Healthcare Plans"];

        static MeasuredPage Page(string site, string industry, bool admitted, double mentions) =>
            new(site, industry, "https://" + site + "/a-page", "A page", admitted, mentions);

        // In: one admitted page about a declined industry, at the density the pass hands a page at.
        Assert.True(SourceMeasurement.Joins("advamed.org", [Page("advamed.org", "Medical Devices", true, ThemeSearch.MentionsPerTenThousand)], declined));

        // Out: a refused page however dense, an admitted page only mentioning the industry, a dense page about an
        // industry the sector did not decline, and another site's page.
        Assert.False(SourceMeasurement.Joins("advamed.org", [Page("advamed.org", "Medical Devices", false, 40)], declined));
        Assert.False(SourceMeasurement.Joins("advamed.org", [Page("advamed.org", "Medical Devices", true, ThemeSearch.MentionsPerTenThousand - 0.01)], declined));
        Assert.False(SourceMeasurement.Joins("advamed.org", [Page("advamed.org", "Diagnostics & Research", true, 40)], declined));
        Assert.False(SourceMeasurement.Joins("advamed.org", [Page("cms.gov", "Medical Devices", true, 40)], declined));
        Assert.False(SourceMeasurement.Joins("iata.org", [], declined));
    }
}
