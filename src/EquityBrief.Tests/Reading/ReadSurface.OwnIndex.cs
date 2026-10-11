using System.Net;

namespace EquityBrief.Tests.Reading;

// read-surface, the 15.1 correction of 2026-10-11: an S&P 400 or 600 member's page names the company from its own index's
// membership and reads its group within its own index, where it read the S&P 500's members for both: GTES's page of
// 2026-10-11 drew no company in its masthead and said it was not a member of the index on the night.
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public partial class ReadSurface
{
    [Fact]
    public async Task AnSAndP400MembersPageNamesTheCompanyFromItsOwnIndexAndReadsItsGroupWithinIt()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // M1 is an S&P 400 member named Mid One in the Energy sector, and no S&P 500 member.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/M1"));

        Assert.Contains("<span class=\"m-co\">Mid One</span>", page, StringComparison.Ordinal);
        Assert.Contains("S&P 400", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-peers=\"not-a-member\"", page, StringComparison.Ordinal);

        // An S&P 500 member's page reads the S&P 500's, as before.
        var own = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAA"));

        Assert.Contains("<span class=\"m-co\">Triple A</span>", own, StringComparison.Ordinal);
        Assert.DoesNotContain("data-peers=\"not-a-member\"", own, StringComparison.Ordinal);
    }
}
