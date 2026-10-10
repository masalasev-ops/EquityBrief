using EquityBrief.Core.Research;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the risks as fields: the fixture's accepted risks are the prose code composed from the fields
// the row stores, and the page draws them a risk to a row, each beside what would confirm it as its fields say.
// see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
public partial class ReadSurface
{
    [Fact]
    public async Task TheRisksAreComposedFromTheFieldsTheirRowStoresAndDrawnARiskToARow()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var written = WrittenOnThePage(page, MarkRenderer.TheRisks);

        Assert.True(written.Success, "KEYS draws no risks, and this is what reads them.");

        var stored = Rows(
            store,
            $"SELECT prose, parts FROM research_section r WHERE ticker = 'KEYS' AND section = '{MarkRenderer.TheRisks}' AND status = 'accepted' " +
            "AND version = (SELECT MAX(version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted');")
            .Single();

        var risks = RiskFields.Read(stored[1]);
        var columns = RiskFields.Columns(stored[1]);

        // From 18.4 the fields carry the two columns beside the risks, two lines each, which code composes after them.
        Assert.NotNull(risks);
        Assert.NotNull(columns);
        Assert.Equal((2, 2), (columns.Value.Up.Count, columns.Value.Worse.Count));
        Assert.Equal(RiskFields.Compose(risks, columns.Value.Up, columns.Value.Worse), stored[0]);

        // A risk to a row, in the order the fields hold them, each confirmation opening as its fields say.
        var table = RiskRows(written.Value);

        Assert.Equal(risks.Count, table.Count);

        for (var at = 0; at < risks.Count; at++)
        {
            Assert.StartsWith(RiskFields.ConfirmedBy(risks[at]) + ", because " + risks[at].Why, table[at].Confirmation, StringComparison.Ordinal);
        }

        // Where a fact confirms a risk it is one the night's facts file lists, and no two risks share one.
        var facts = Rows(store, "SELECT payload FROM facts WHERE ticker = 'KEYS' ORDER BY session_date DESC LIMIT 1;").Single()[0];

        Assert.All(risks.Where(risk => !risk.IsEvent), risk => Assert.Contains("\"" + risk.Fact + "\"", facts, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(risks.Count(risk => !risk.IsEvent), risks.Where(risk => !risk.IsEvent).Select(risk => risk.Fact!.ToLowerInvariant()).Distinct().Count());
    }
}
