using EquityBrief.Api.Reading;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the operator's orders of 2026-11-25, items 3 and 4: the picks proof worked after the night and kept, its page reading it
// as stored rather than drawing every page again; and Tonight's opening lines held to the width of a phone.
// see: The picks proof is worked after the night and kept, and its page reads it as stored
public partial class ReadSurface
{
    // A proof kept under the data root is what the page serves, in the time a file takes to read; asked fresh, it is worked
    // again.
    [Fact]
    public async Task ThePicksProofsPageReadsTheProofTheNightKept()
    {
        using var store = PlanTradesStore();
        const string kept = "<!doctype html><title>kept</title><p>Worked by the step after the night at 2026-11-25 23:40 UTC and kept.</p>";

        File.WriteAllText(Path.Combine(store.Root, PicksProof.StoredFile), kept);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        Assert.Equal(kept, await client.GetStringAsync(PicksProof.Route));

        var fresh = await client.GetStringAsync(PicksProof.Route + "?fresh=1");

        Assert.Contains("<h1>Picks proof</h1>", fresh, StringComparison.Ordinal);
        Assert.Contains(PicksProof.Tally, await client.GetStringAsync(PicksProof.Route + "?format=text"), StringComparison.Ordinal);
    }

    // Tonight's opening lines sit in one column no wider than the page, so the waiting orders' row of cards scrolls inside it
    // and the page never scrolls sideways on a phone: read on 2026-11-25 at 660 pixels wide at 390.
    [Fact]
    public void TonightsOpeningLinesStayWithinThePagesWidth()
    {
        Assert.Contains(".one-minute{display:grid;grid-template-columns:minmax(0,1fr);gap:14px}", Stylesheet.Css, StringComparison.Ordinal);
    }
}
