using System.Globalization;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Reading;

// read-surface, 8.6: the Run page opens on the newest night by the night's own run id. A Sunday holding only runs beside
// the nights, a research pass and its drain, the history's build and its check, the tester, the SEC's facts asked whole,
// and a run under a name no list holds, leaves the page on Friday's night; a night run after them moves it.
// see: The Run page opens on the newest night by the night's own run id, and no run beside the nights moves it
public partial class ReadSurface
{
    [Fact]
    public async Task TheRunPageOpensOnTheNewestNightWhateverRunsBesideTheNightsFollowItOnASunday()
    {
        using var store = await FixtureExpectations.WithListings();

        // The replay's log holds no night, its rows written the day after the listings' night, so the page opens on the
        // night the listings hold and not on the day the replay ran.
        Assert.Equal(DateOnly.Parse(NightIn(store), CultureInfo.InvariantCulture), await Api(store).RunNightAsync());

        foreach (var (id, stage, started) in new[]
        {
            ("night-20261009T230003Z", "close", "2026-10-09T23:10:00Z"),
            ("night-20261009T230003Z", "backup", "2026-10-10T00:30:00Z"),
            ("drain-20261011T153300Z", "drain", "2026-10-11T15:33:00Z"),
            ("research-20261011T153335Z-AAPL", "research", "2026-10-11T15:33:35Z"),
            ("ledger-build-SML-20261011T180022Z", "ledger-build", "2026-10-11T18:00:22Z"),
            ("ledger-check-SML-20261011T190000Z", "ledger-check", "2026-10-11T19:00:00Z"),
            ("loop-test-SML-20261011T193000Z", "loop-test", "2026-10-11T19:30:00Z"),
            ("filings-whole-20261011T200000Z", "filings-whole", "2026-10-11T20:00:00Z"),
            ("a-verb-no-list-names-20261011T210000Z", "anything", "2026-10-11T21:00:00Z"),
        })
        {
            store.Execute(RunRow(id, stage, started, started));
        }

        var api = Api(store);

        Assert.Equal(new DateOnly(2026, 10, 9), await api.RunNightAsync());

        using (var host = new Host(store.Root))
        using (var client = host.CreateClient())
        {
            Assert.Contains("<section class=\"run\" data-night=\"2026-10-09\"", await client.GetStringAsync("/screens/run"), StringComparison.Ordinal);
        }

        store.Execute(RunRow("night-20261012T230002Z", "close", "2026-10-12T23:10:00Z", "2026-10-12T23:12:00Z"));

        Assert.Equal(new DateOnly(2026, 10, 12), await api.RunNightAsync());
    }
}
