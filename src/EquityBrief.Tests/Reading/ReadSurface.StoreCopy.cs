using System.Net;
using EquityBrief.Core.Configuration;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Reading;

// read-surface, the operator's ruling of 2026-10-02: the run page draws the store's newest copy beneath anything to
// worry about, its time, its folder read back against the surface's own data root and the copies kept, a newer
// attempt that made none with why, and that none is recorded where the store holds no copy's row.
// see: The store is copied once the night and every process it started have finished, and the newest three copies are kept after each is opened and read
public partial class ReadSurface
{
    static void CopyRowOf(TemporaryStore store, string runId, string outcome, string endedAt, string detail) =>
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('{runId}', '{StoreCopies.Stage}', '{endedAt}', '{endedAt}', '{outcome}', 0, 0, 0, '0', '{detail}');");

    [Fact]
    public async Task TheRunPageDrawsTheStoresNewestCopyWithItsTimeAndFolderBeneathAnythingToWorryAbout()
    {
        using var store = await FixtureExpectations.FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Line() =>
            Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16")), "<p class=\"store-copy\".*?</p>"));

        // No copy's row yet.
        var none = await Line();

        Assert.Contains("data-copies=\"none\"", none, StringComparison.Ordinal);
        Assert.Contains("No copy of the store is recorded yet.", none, StringComparison.Ordinal);

        // A copy made at 00:41:12 on the 17th into a folder beside the data root, three kept, read back against
        // this surface's own data root.
        CopyRowOf(store, "backup-20260917T003000Z", "ok", "2026-09-17T00:45:00Z",
            "{\"copy\":\"equitybrief-20260917T004112Z.db\",\"folder\":\"../copies\",\"elsewhere\":null,\"kept\":[\"equitybrief-20260917T004112Z.db\",\"equitybrief-20260916T004000Z.db\",\"equitybrief-20260915T003900Z.db\"],\"removed\":[],\"unread\":[],\"waited\":[\"the drain\"]}");

        var folder = Path.GetFullPath(Path.Combine(store.Root, "..", "copies"));
        var made = await Line();

        Assert.Contains("data-made=\"2026-09-17T00:41:12Z\" data-kept=\"3\" data-failed=\"false\"", made, StringComparison.Ordinal);
        Assert.Contains($"The newest copy of the store was made at 00:41 UTC on 2026-09-17, in {folder} as equitybrief-20260917T004112Z.db, and opened and read; 3 copies are kept.", made, StringComparison.Ordinal);

        // A newer attempt that made none is drawn beside it with why, and an older one is not.
        CopyRowOf(store, "backup-20260916T003000Z", "failed", "2026-09-16T20:30:00Z", "{\"reason\":\"an older attempt\",\"waited\":[]}");
        CopyRowOf(store, "backup-20260918T003000Z", "failed", "2026-09-18T20:30:00Z", "{\"reason\":\"no copy was made, since the drain still held the queue after 20 hours\",\"waited\":[\"the drain\"]}");

        var failed = await Line();

        Assert.Contains("data-failed=\"true\"", failed, StringComparison.Ordinal);
        Assert.Contains($"in {folder} as equitybrief-20260917T004112Z.db", failed, StringComparison.Ordinal);
        Assert.Contains("The copy tried at 20:30 UTC on 2026-09-18 was not made: no copy was made, since the drain still held the queue after 20 hours.", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("an older attempt", failed, StringComparison.Ordinal);
    }
}
