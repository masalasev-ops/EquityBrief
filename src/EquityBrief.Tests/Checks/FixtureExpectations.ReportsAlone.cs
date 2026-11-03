using System.Text.Json;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Shortlist;

namespace EquityBrief.Tests.Checks;

// see: Earnings soon, the sessions to earnings and a page's next report read the calendar's earnings alone
// see: The second book is keyed to the next report the calendar holds, and an ex-dividend date beside it is no key
public partial class FixtureExpectations
{
    // Worked by hand from the fixture's calendar, which files AAPL's next report and none for KEYS. An ex-dividend date one
    // session after the night, nearer than AAPL's report, and another before the night, among its past prints, leave its
    // plan and its reason as they stood; one inside the horizon for KEYS leaves it with no report, no setup and no reason fired.
    [Fact]
    public async Task EarningsSoonAndTheSecondBookReadTheCalendarsEarningsAlone()
    {
        using var store = await WithListings();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        string Plan(string name) => Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single();

        const string Nearer = "2026-09-08";

        Assert.True(
            string.CompareOrdinal(Query(store, "SELECT MIN(event_date) FROM calendar WHERE ticker = 'AAPL' AND kind = 'earnings' AND event_date >= '2026-09-04';").Single(), Nearer) > 0,
            "AAPL's report has to fall after the ex-dividend date for the date to be nearer.");
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM calendar WHERE ticker = 'KEYS' AND event_date >= '2026-09-04';"));

        var planBefore = Plan("AAPL");
        var valuesBefore = ReasonValues(store, "AAPL", ShortlistSeries.EarningsSoon);
        var firedBefore = ReasonFired(store, "AAPL", ShortlistSeries.EarningsSoon);

        Insert(
            store,
            "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES " +
            $"('AAPL', '{Nearer}', 'ex-dividend', 'unstated', '{{}}', '2026-09-05T21:10:00Z'), " +
            "('AAPL', '2026-08-20', 'ex-dividend', 'unstated', '{}', '2026-09-05T21:10:00Z'), " +
            $"('KEYS', '{Nearer}', 'ex-dividend', 'unstated', '{{}}', '2026-09-05T21:10:00Z');");
        Insert(store, "DELETE FROM listing;");

        await new LadderBuilder(clock, store.DatabaseFile).RunAsync(Index, "reports-alone-ladders");
        await new ShortlistBuilder(clock, store.DatabaseFile).RunAsync(Index, "reports-alone-listings", Instant);

        Assert.Equal(planBefore, Plan("AAPL"));
        Assert.Equal(firedBefore, ReasonFired(store, "AAPL", ShortlistSeries.EarningsSoon));
        Assert.Equal(valuesBefore, ReasonValues(store, "AAPL", ShortlistSeries.EarningsSoon));

        var keys = JsonDocument.Parse(Plan("KEYS")).RootElement;

        Assert.Empty(keys.GetProperty("events").EnumerateArray());
        Assert.Contains("no report is dated", keys.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        Assert.False(ReasonFired(store, "KEYS", ShortlistSeries.EarningsSoon));
        Assert.Equal(ShortlistSeries.NotOnFile, ReasonValues(store, "KEYS", ShortlistSeries.EarningsSoon)[ShortlistSeries.NextDatedEventValue]);
    }
}
