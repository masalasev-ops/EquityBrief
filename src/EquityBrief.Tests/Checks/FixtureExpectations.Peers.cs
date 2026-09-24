using System.Globalization;
using EquityBrief.Core.Moves;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 11.6: the peers table's two readings, worked by hand over constructed bars
// at their edges and over the bars the fixture's replay stores.
public partial class FixtureExpectations
{
    [Fact]
    public void APeerReadingIsWorkedByHandAndASeriesOneShortOfTheWindowHasNoReturn()
    {
        // Sixty-one sessions closing at 100 but for the newest at 120, with the highest high, 150, on
        // the eleventh. Worked by hand: the return is 120 against the close sixty sessions before it,
        // 100, which is +20 per cent, and the close sits 30 below a high of 150, which is 20 per cent.
        var first = new DateOnly(2026, 1, 1);
        PeerBar[] bars =
        [
            .. Enumerable.Range(0, 60).Select(day => new PeerBar(first.AddDays(day), day == 10 ? 150m : 110m, 100m)),
            new PeerBar(first.AddDays(60), 121m, 120m),
        ];

        var reading = PeerReadings.Of(bars)!;

        Assert.Equal(60, PeerReadings.ReturnWindow);
        Assert.Equal((first.AddDays(60), 150m, 61), (reading.Session, reading.YearHigh, reading.Bars));
        Assert.Equal(20.0, reading.BelowHighPct, 9);
        Assert.Equal(20.0, reading.ReturnPct!.Value, 9);

        // Sixty bars is one short of the window and the close it is measured from: no return, and
        // the count says why, where a return over fifty-nine sessions would read as the same figure.
        var shorter = PeerReadings.Of(bars[1..])!;

        Assert.Null(shorter.ReturnPct);
        Assert.Equal(60, shorter.Bars);
        Assert.Equal(20.0, shorter.BelowHighPct, 9);

        // A close at the high sits none below it, and a name holding no bars has no readings.
        Assert.Equal(0.0, PeerReadings.Of([new PeerBar(first, 50m, 50m)])!.BelowHighPct, 9);
        Assert.Null(PeerReadings.Of([]));
    }

    [Fact]
    public async Task TheFixturesPeerReadingsAreTheOnesWorkedByHandFromTheCapturedBars()
    {
        var expected = Expected("peers");

        Assert.Equal(PeerReadings.ReturnWindow, expected.GetProperty("returnWindow").GetInt32());

        using var store = await FixtureReplay.ReplayedAsync();

        var read = 0;

        foreach (var name in expected.GetProperty("readings").EnumerateObject())
        {
            var want = name.Value;
            var stored = Assert.Single(Query(
                store,
                $"SELECT session_date || '|' || bars || '|' || year_high || '|' || below_high_pct || '|' || IFNULL(return_pct, '') FROM peer_reading WHERE ticker = '{name.Name}';")).Split('|');

            Assert.Equal(want.GetProperty("session").GetString(), stored[0]);
            Assert.Equal(want.GetProperty("bars").GetInt32(), int.Parse(stored[1], CultureInfo.InvariantCulture));
            Assert.InRange(
                Math.Abs(decimal.Parse(stored[2], CultureInfo.InvariantCulture) - decimal.Parse(want.GetProperty("yearHigh").GetString()!, CultureInfo.InvariantCulture)),
                0m,
                0.0001m);
            Assert.InRange(Math.Abs(double.Parse(stored[3], CultureInfo.InvariantCulture) - want.GetProperty("belowHighPct").GetDouble()), 0, 0.0001);

            if (want.GetProperty("returnPct").ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                Assert.Equal(string.Empty, stored[4]);
            }
            else
            {
                Assert.InRange(Math.Abs(double.Parse(stored[4], CultureInfo.InvariantCulture) - want.GetProperty("returnPct").GetDouble()), 0, 0.0001);
            }

            // The close the readings end on and the session the return is measured from, each read
            // off the bars the replay stored, so the two ends the figures rest on are asserted too.
            Assert.Equal(
                decimal.Parse(want.GetProperty("close").GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(Query(store, $"SELECT close FROM bar WHERE ticker = '{name.Name}' ORDER BY session_date DESC LIMIT 1;")[0], CultureInfo.InvariantCulture));
            Assert.Equal(
                want.GetProperty("returnFrom").GetString(),
                Query(store, $"SELECT session_date FROM bar WHERE ticker = '{name.Name}' ORDER BY session_date DESC LIMIT 1 OFFSET {PeerReadings.ReturnWindow};")[0]);

            read++;
        }

        Assert.Equal(4, read);

        // One row per name the store holds bars for, and none for any other.
        Assert.Equal(
            Query(store, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;"),
            Query(store, "SELECT ticker FROM peer_reading ORDER BY ticker;"));
    }
}
