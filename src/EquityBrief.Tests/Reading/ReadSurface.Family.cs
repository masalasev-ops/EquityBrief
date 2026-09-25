using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.5: with the swing family registered and phase 10's three retired, the run page's
// shadow region reads six registered and a divisor of 6.
public partial class ReadSurface
{
    [Fact]
    public async Task WithTheSwingFamilyRegisteredTheShadowRegionReadsSixAndADivisorOfSix()
    {
        using var store = await FixtureExpectations.FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, (await FixtureExpectations.RegisterVerbAt(store, new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero), RegisterVerb.TheFamily)).Code);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-08"));
        var region = Regex.Match(page, "<section class=\"shadow-candidates\" data-shadow=\"([0-9]+)\" data-divisor=\"([0-9]+)\" data-maximum=\"([0-9]+)\">");

        // Worked by hand: nine registrations and three retirements leave six standing, all registered at one
        // instant before the page is read, so the divisor is six too.
        Assert.True(region.Success);
        Assert.Equal(("6", "6", "8"), (region.Groups[1].Value, region.Groups[2].Value, region.Groups[3].Value));
        Assert.Contains("6 candidate condition(s) registered as this page is read, of at most 8, so a candidate's threshold is divided by 6", page, StringComparison.Ordinal);
    }
}
