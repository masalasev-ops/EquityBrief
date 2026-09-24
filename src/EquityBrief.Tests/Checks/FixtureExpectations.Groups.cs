using EquityBrief.Core.Moves;
using EquityBrief.Tests.Harness;

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
