using EquityBrief.Core.Moves;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Moves;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 11.5: a name's group and its median move over a move's own sessions.
public partial class FixtureExpectations
{
    [Fact]
    public void AnIndustryOfFiveOtherMembersIsTheGroupAndOneOfFourGivesWayToTheSectorAndTheNameIsNeverInIt()
    {
        // Six names in one industry and two in another, all in one sector, and one name alone in
        // a sector of its own.
        GroupMember[] members =
        [
            new("ZA", "Tech", "Chips"),
            new("ZB", "Tech", "Chips"),
            new("ZC", "Tech", "Chips"),
            new("ZD", "Tech", "Chips"),
            new("ZE", "Tech", "Chips"),
            new("ZF", "Tech", "Chips"),
            new("ZG", "Tech", "Software"),
            new("ZH", "Tech", "Software"),
            new("ZI", "Energy", "Oil"),
        ];

        // Five others share the industry, which is the floor, so the industry is the group.
        Assert.Equal(5, Groups.Floor);
        Assert.Equal((Group.Industry, "Chips"), (Groups.Of("ZA", members).Kind, Groups.Of("ZA", members).Name));
        Assert.Equal(["ZB", "ZC", "ZD", "ZE", "ZF"], Groups.Of("ZA", members).Members);

        // One short of it, and the industry gives way to the sector, every other member of it.
        var short1 = members.Where(member => member.Ticker != "ZF").ToArray();
        var fallen = Groups.Of("ZA", short1);

        Assert.Equal((Group.Sector, "Tech"), (fallen.Kind, fallen.Name));
        Assert.Equal(["ZB", "ZC", "ZD", "ZE", "ZG", "ZH"], fallen.Members);

        // An industry of one other gives way too, and a name alone in its sector holds nobody.
        Assert.Equal(["ZA", "ZB", "ZC", "ZD", "ZE", "ZF", "ZH"], Groups.Of("ZG", members).Members);
        Assert.Empty(Groups.Of("ZI", members).Members);
        Assert.Equal((Group.Sector, "Energy"), (Groups.Of("ZI", members).Kind, Groups.Of("ZI", members).Name));

        // The name is never in its own group, whichever group it is.
        Assert.All(members, member => Assert.DoesNotContain(member.Ticker, Groups.Of(member.Ticker, members).Members));
    }

    // Every member of every index is grouped by the GICS industry and sector its newest company fetch made before the night
    // started files, both from that fetch, whatever its membership row names, each within its own index, worked by hand
    // over a constructed store.
    // see: Every member is named and grouped by the GICS industry and sector its company fetch files
    [Fact]
    public async Task EveryMemberIsGroupedByTheIndustryAndSectorItsNewestCompanyFetchBeforeTheNightFilesWhateverItsRowNames()
    {
        using var store = new TemporaryStore().Migrated();

        // The S&P 500's G1 to G9 rows naming the index snapshot's words, G9 its sector alone and G10 an industry with no fetch
        // behind it; the S&P 400's M1 to M8 and the S&P 600's S1 naming neither.
        string[] chips = ["G1", "G2", "G3", "G4", "G5", "G6"];
        string[] machinery = ["M1", "M2", "M3", "M4", "M5", "M6"];

        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector, industry) VALUES " +
            string.Join(", ", chips.Select(ticker => $"('GSPC', '{ticker}', NULL, NULL, '2026-09-08T21:00:00Z', 'Technology', 'Snapshot Chips')")) +
            ", ('GSPC', 'G7', NULL, NULL, '2026-09-08T21:00:00Z', 'Technology', 'Snapshot Software'), ('GSPC', 'G8', NULL, NULL, '2026-09-08T21:00:00Z', 'Industrials', 'Snapshot Machinery'), " +
            "('GSPC', 'G9', NULL, NULL, '2026-09-08T21:00:00Z', 'Technology', NULL), ('GSPC', 'G10', NULL, NULL, '2026-09-08T21:00:00Z', 'Technology', 'Snapshot Chips'), " +
            string.Join(", ", machinery.Append("M7").Append("M8").Select(ticker => $"('MID', '{ticker}', NULL, NULL, '2026-09-08T21:00:00Z', NULL, NULL)")) +
            ", ('SML', 'S1', NULL, NULL, '2026-09-08T21:00:00Z', NULL, NULL);");

        // G1 to G6 file Semiconductors, G1 over an older fetch filing Banks; G7 and G9 file Software and G8 Machinery; M1 to
        // M6 file Machinery, M1 over an older fetch filing Banks; M7 files Building Products, and Machinery in a fetch made
        // after the night started, which the night does not read; M8 files no sector and no industry; G10 and S1 have no
        // fetch.
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, sector, industry) VALUES " +
            string.Join(", ", chips.Select(ticker => $"('{ticker}', '2026-09-01T00:00:00.000Z', 'Information Technology', 'Semiconductors & Semiconductor Equipment')")) +
            ", ('G1', '2026-06-01T00:00:00.000Z', 'Financials', 'Banks'), ('G7', '2026-09-01T00:00:00.000Z', 'Information Technology', 'Software')" +
            ", ('G8', '2026-09-01T00:00:00.000Z', 'Industrials', 'Machinery'), ('G9', '2026-09-01T00:00:00.000Z', 'Information Technology', 'Software'), " +
            string.Join(", ", machinery.Select(ticker => $"('{ticker}', '2026-09-01T00:00:00.000Z', 'Industrials', 'Machinery')")) +
            ", ('M1', '2026-06-01T00:00:00.000Z', 'Financials', 'Banks'), ('M7', '2026-09-01T00:00:00.000Z', 'Industrials', 'Building Products')" +
            ", ('M7', '2026-09-09T01:00:00.000Z', 'Industrials', 'Machinery'), ('M8', '2026-09-01T00:00:00.000Z', NULL, NULL);");

        // Every close 100 on 2026-09-03 and 2026-09-04, and on 2026-09-08, the session after Labor Day: G1 120, G2 to G6
        // 101 to 105, M1 130, M2 to M6 101 to 105, G7 90, G8 95, G9 98, G10 97, M7 80, M8 and S1 99.
        var night = new Dictionary<string, int>(StringComparer.Ordinal) { ["G1"] = 120, ["M1"] = 130, ["G7"] = 90, ["G8"] = 95, ["G9"] = 98, ["G10"] = 97, ["M7"] = 80, ["M8"] = 99, ["S1"] = 99 };

        foreach (var (ticker, at) in chips.Skip(1).Select((ticker, at) => (ticker, at)).Concat(machinery.Skip(1).Select((ticker, at) => (ticker, at))))
        {
            night[ticker] = 101 + at;
        }

        foreach (var (ticker, close) in night)
        {
            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('{ticker}', '2026-09-03', '100', '100', '100', '100', 1000, 'test', '2026-09-03T21:00:00Z', '100'), " +
                $"('{ticker}', '2026-09-04', '100', '100', '100', '100', 1000, 'test', '2026-09-04T21:00:00Z', '100'), " +
                $"('{ticker}', '2026-09-08', '{close}', '{Math.Max(close, 100)}', '{Math.Min(close, 100)}', '{close}', 1000, 'test', '2026-09-08T21:00:00Z', '{close}');");
        }

        await new MoveAnnotator(FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("gics-groups");

        string Group(string ticker) => Query(store, $"SELECT group_kind || ' ' || IFNULL(group_name, 'none') || ' ' || group_members FROM move WHERE ticker = '{ticker}' AND session_date = '2026-09-08';").Single();

        // G1 by its newest fetch's industry, five others, and not by its older Banks or its row's words; G7 and G9 one short
        // of an industry, so their fetches' sector with G1 to G6 and each other; G8 alone in its sector, the S&P 400's
        // Industrials in another index; and G10, whose row names an industry and which has no fetch, in no group.
        Assert.Equal("industry Semiconductors & Semiconductor Equipment 5", Group("G1"));
        Assert.Equal("sector Information Technology 7", Group("G7"));
        Assert.Equal("sector Industrials 0", Group("G8"));
        Assert.Equal("sector Information Technology 7", Group("G9"));
        Assert.Equal("sector none 0", Group("G10"));

        // M1 by its newest fetch's Machinery, five others, and not by its older Banks; M7 alone in Building Products, so its
        // fetch's sector with M1 to M6, its Machinery fetched after the night started not read; M8 and S1 in no group.
        Assert.Equal("industry Machinery 5", Group("M1"));
        Assert.Equal("sector Industrials 6", Group("M7"));
        Assert.Equal("sector none 0", Group("M8"));
        Assert.Equal("sector none 0", Group("S1"));

        // The medians over the members' own moves from 2026-09-04 to 2026-09-08: +1 to +5 per cent, +3.
        Assert.Equal(
            ["G1 3.0", "M1 3.0"],
            Query(store, "SELECT ticker || ' ' || ROUND(group_median, 9) FROM move WHERE ticker IN ('G1', 'M1') AND session_date = '2026-09-08' ORDER BY ticker;"));
    }

    [Fact]
    public void AGroupsMedianMoveIsWorkedByHandOverAnEvenCountAndLeavesOutAMemberMissingAClose()
    {
        // Moves of +10, -5, +2 and +4 per cent, and a fifth member holding no close on the span's
        // first session. Worked by hand: sorted -5, 2, 4, 10, so the median of four is the mean of
        // 2 and 4, which is 3, over four members counted and one left out.
        var even = Groups.MedianMove([(100m, 110m), (100m, 95m), (50m, 51m), (25m, 26m), (null, 30m)]);

        Assert.Equal(3.0, even.Median!.Value, 9);
        Assert.Equal((4, 1), (even.Counted, even.Missing));

        // Three members: the middle one, 2.
        Assert.Equal(2.0, Groups.MedianMove([(100m, 110m), (100m, 95m), (50m, 51m)]).Median!.Value, 9);

        // None holding both closes gives no median, every member counted as missing.
        Assert.Equal(new GroupMedian(null, 0, 2), Groups.MedianMove([(null, 1m), (1m, null)]));
    }

    [Fact]
    public async Task TheFixturesGroupsFallBackToTheSectorEachTechnologyNameBesideTheOtherTwoAndNflxBesideNobody()
    {
        var expected = Expected("moves").GetProperty("groups");

        using var store = await FixtureReplay.ReplayedAsync();

        // Every stored close, read off the store the replay wrote, so each move's group median is
        // worked here from the members' own bars rather than through the rule that wrote it.
        var closes = new Dictionary<string, SortedDictionary<string, decimal>>(StringComparer.Ordinal);

        foreach (var row in Query(store, "SELECT ticker || '|' || session_date || '|' || close FROM bar;"))
        {
            var parts = row.Split('|');

            if (!closes.TryGetValue(parts[0], out var byDate))
            {
                closes[parts[0]] = byDate = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            }

            byDate[parts[1]] = decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        }

        var moves = 0;

        foreach (var name in expected.EnumerateObject().Where(entry => entry.Name != "note"))
        {
            var members = name.Value.GetProperty("members").EnumerateArray().Select(member => member.GetString()!).ToArray();

            Assert.Equal(
                [$"{name.Value.GetProperty("kind").GetString()}|{name.Value.GetProperty("name").GetString()}|{members.Length}"],
                Query(store, $"SELECT DISTINCT group_kind || '|' || IFNULL(group_name, '') || '|' || group_members FROM move WHERE ticker = '{name.Name}';"));

            var sessions = closes[name.Name].Keys.ToList();

            foreach (var row in Query(store, $"SELECT session_date || '|' || sessions || '|' || group_counted || '|' || CASE WHEN group_median IS NULL THEN '' ELSE printf('%.9f', group_median) END FROM move WHERE ticker = '{name.Name}';"))
            {
                var parts = row.Split('|');
                var ended = parts[0];
                var from = sessions[sessions.IndexOf(ended) - int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)];

                // Each member's move from the close the name's move was measured from to the close
                // it ended on, a member holding no close on either session left out.
                var held = members
                    .Where(member => closes.TryGetValue(member, out var bars) && bars.ContainsKey(from) && bars.ContainsKey(ended))
                    .Select(member => (double)((closes[member][ended] - closes[member][from]) / closes[member][from]) * 100)
                    .Order()
                    .ToArray();

                Assert.Equal(held.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), parts[2]);

                if (held.Length == 0)
                {
                    Assert.Equal(string.Empty, parts[3]);
                }
                else
                {
                    var median = held.Length % 2 == 1 ? held[held.Length / 2] : (held[(held.Length / 2) - 1] + held[held.Length / 2]) / 2;

                    Assert.Equal(median, double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), 6);
                }

                moves++;
            }
        }

        Assert.True(moves >= 20, $"Worked {moves} moves' group medians by hand, expected at least 20.");
    }
}
