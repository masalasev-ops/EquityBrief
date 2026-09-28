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
    public void ThePeersDrawnAreTheIndustrysFirstThenTheMostAlikeAndTenAtMost()
    {
        // Closes alternating between two prices, so every daily return is one of two figures worked by
        // hand: a member alternating in step with the name moved with it exactly, a likeness of 1, and one
        // alternating against it moved exactly the other way, -1. Seventy sessions give sixty-nine returns.
        var first = new DateOnly(2026, 1, 1);

        IReadOnlyDictionary<DateOnly, decimal> Alternating(decimal low, decimal high, bool inStep, int sessions = 70) =>
            Enumerable.Range(70 - sessions, sessions).ToDictionary(day => first.AddDays(day), day => (day % 2 == 0) == inStep ? low : high);

        GroupMember Member(string ticker, string industry) => new(ticker, "Tech", industry);

        var closes = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.Ordinal)
        {
            ["ZZNM"] = Alternating(100m, 101m, true),
            ["SAME"] = Alternating(100m, 101m, false),
            ["TWIN"] = Alternating(100m, 101m, true),
            ["OPPO"] = Alternating(200m, 202m, false),
            ["SHRT"] = Alternating(10m, 10.1m, true, sessions: 60),
            ["EDGE"] = Alternating(10m, 10.1m, true, sessions: 61),
        };

        // At the floor and one short of it: sixty-one closes share sixty returns with the name and read
        // a likeness, sixty share fifty-nine and read none, with the count.
        Assert.Equal(60, PeerPicks.FewestSessions);
        Assert.Equal((1.0, 60), (Math.Round(PeerPicks.Likeness(closes["ZZNM"], closes["EDGE"]).Likeness!.Value, 9), PeerPicks.Likeness(closes["ZZNM"], closes["EDGE"]).Sessions));
        Assert.Equal(((double?)null, 59), PeerPicks.Likeness(closes["ZZNM"], closes["SHRT"]));
        Assert.Equal(-1.0, PeerPicks.Likeness(closes["ZZNM"], closes["OPPO"]).Likeness!.Value, 9);

        // The name's industry holds one other member, below the group floor, so its group is its sector.
        // The member sharing its industry comes first whatever its likeness, then the members holding one,
        // the higher first, and the member sharing too few sessions last.
        GroupMember[] members =
        [
            Member("ZZNM", "Tools"), Member("SAME", "Tools"), Member("TWIN", "Chips"), Member("OPPO", "Chips"), Member("SHRT", "Chips"),
        ];

        var group = Groups.Of("ZZNM", members);
        var picks = PeerPicks.Of("ZZNM", group, members, closes);

        Assert.Equal(Group.Sector, group.Kind);
        Assert.Equal(["SAME", "TWIN", "OPPO", "SHRT"], picks.Select(pick => pick.Ticker).ToArray());
        Assert.Equal([true, false, false, false], picks.Select(pick => pick.SameIndustry).ToArray());
        Assert.Equal([-1.0, 1.0, -1.0], picks.Take(3).Select(pick => Math.Round(pick.Likeness!.Value, 9)).ToArray());
        Assert.Equal((null, 59), (picks[3].Likeness, picks[3].Sessions));

        // Twelve members closing as the name closes besides, so their likeness is TWIN's to the last digit:
        // ten are drawn, the one sharing its industry first and then nine of the thirteen tied at 1, the tie
        // settled by the ticker.
        foreach (var at in Enumerable.Range(1, 12))
        {
            closes[$"A{at:00}"] = Alternating(100m, 101m, true);
        }

        GroupMember[] crowded = [.. members, .. Enumerable.Range(1, 12).Select(at => Member($"A{at:00}", "Chips"))];
        var drawn = PeerPicks.Of("ZZNM", Groups.Of("ZZNM", crowded), crowded, closes);

        Assert.Equal(10, PeerPicks.Shown);
        Assert.Equal(["SAME", "A01", "A02", "A03", "A04", "A05", "A06", "A07", "A08", "A09"], drawn.Select(pick => pick.Ticker).ToArray());
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

        // The members each name's table draws, in order, as worked by hand from the rebuilt series: the
        // likeness to the sixth place and the returns both hold exactly.
        foreach (var name in expected.GetProperty("picks").EnumerateObject().Where(entry => entry.Name != "note"))
        {
            using var stored = System.Text.Json.JsonDocument.Parse(Assert.Single(Query(store, $"SELECT peers FROM peer_reading WHERE ticker = '{name.Name}';")));

            var want = name.Value.EnumerateArray().ToArray();
            var have = stored.RootElement.EnumerateArray().ToArray();

            Assert.Equal(want.Select(pick => pick.GetProperty("ticker").GetString()), have.Select(pick => pick.GetProperty("ticker").GetString()));

            foreach (var (expectedPick, storedPick) in want.Zip(have))
            {
                Assert.Equal(expectedPick.GetProperty("sameIndustry").GetBoolean(), storedPick.GetProperty("sameIndustry").GetBoolean());
                Assert.Equal(expectedPick.GetProperty("sessions").GetInt32(), storedPick.GetProperty("sessions").GetInt32());
                Assert.InRange(Math.Abs(expectedPick.GetProperty("likeness").GetDouble() - storedPick.GetProperty("likeness").GetDouble()), 0, 0.000001);
            }
        }

        // One row per name the store holds bars for, and none for any other.
        Assert.Equal(
            Query(store, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;"),
            Query(store, "SELECT ticker FROM peer_reading ORDER BY ticker;"));
    }
}
