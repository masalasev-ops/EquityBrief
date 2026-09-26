using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: a name's page draws no listing history. The evenings a name was
// listed on stay stored, since the run page's records read them, and the page a reader opens states
// none of them.
// see: A name's page states no listing history, since what an earlier evening's list said is not what the page is read for
public partial class ReadSurface
{
    [Fact]
    public async Task ANamesPageDrawsNoListingHistoryThoughTheEveningsItWasListedOnAreStored()
    {
        // Over the fixture's night, MSFT is given an earlier evening it was listed on with a matured
        // horizon, so a page drawing no history is not a page with none to draw.
        using var store = await FixtureReplay.ReplayedAsync();

        const string Reasons = "[{\"name\":\"at entry zone\",\"fired\":true,\"values\":{\"close\":\"1.00\"}}]";

        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES " +
            $"('MSFT', '2026-09-04', '{Reasons}', 1, '[]', '[]');");
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) VALUES " +
            "('MSFT', '2026-09-04', '5', 'win', '2026-09-11', 2.5, 40.0, NULL);");

        Assert.Equal(["1"], Rows(store, "SELECT fired_count FROM listing WHERE ticker = 'MSFT' AND session_date = '2026-09-04';").Select(row => row[0]));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync("/screens/name/MSFT");

        // The page is drawn, its contents listed, and neither the region nor its entry in the
        // contents is on it.
        Assert.Contains("<nav class=\"contents\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("listing-history", page, StringComparison.Ordinal);
        Assert.DoesNotContain("listing-evenings", page, StringComparison.Ordinal);
        Assert.DoesNotContain("The evenings it was on the list", page, StringComparison.Ordinal);
    }
}
