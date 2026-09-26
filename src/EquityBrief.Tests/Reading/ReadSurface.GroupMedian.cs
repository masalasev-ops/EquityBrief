using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 11.5: each move on a name's page beside its group's median move over the same
// sessions, as the annotator stored it.
public partial class ReadSurface
{
    // The group cell on a move's own row, read inside the table of the biggest moves alone, since
    // other tables on the page carry rows dated by a session too.
    static string GroupCellOf(string page, string session)
    {
        var table = Regex.Match(page, "<table class=\"moves-table\".*?</table>", RegexOptions.Singleline).Value;
        var cell = Regex.Match(table, $"<tr id=\"move-[^\"]+\" data-session-date=\"{session}\"[^>]*>.*?(<td class=\"group-median\"[^>]*>[^<]*</td>)", RegexOptions.Singleline);

        Assert.True(cell.Success, $"no group is drawn beside the move ending {session}");

        return cell.Groups[1].Value;
    }

    [Fact]
    public void AMoveIsDrawnBesideItsGroupsMedianOrSaysWhyThereIsNone()
    {
        var on = new DateOnly(2026, 9, 1);
        MoveCell[] moves =
        [
            new(on, 1, 5.5, 1, Group: new MoveGroup("sector", "Technology", 3, 2, 1.25)),
            new(on.AddDays(1), 1, -4, 2, Group: new MoveGroup("industry", "Utilities - Regulated Electric", 6, 6, -0.5)),
            new(on.AddDays(2), 1, 3, 3, Group: new MoveGroup("sector", "Communication Services", 0, 0, null)),
            new(on.AddDays(3), 1, 2, 4, Group: new MoveGroup("sector", "Energy", 2, 0, null)),
            new(on.AddDays(4), 1, 1, 5),
        ];

        var table = WebUtility.HtmlDecode(new MarkRenderer().MovesTable("ZZZA", moves, []));

        Assert.Contains("<th>Its group</th>", table, StringComparison.Ordinal);
        Assert.EndsWith(">1.25%, the median of 2 of the 3 other members of the Technology sector, 1 holding no close on one of the two sessions</td>", GroupCellOf(table, "2026-09-01"), StringComparison.Ordinal);
        Assert.EndsWith(">-0.5%, the median of 6 of the 6 other members of the Utilities - Regulated Electric industry</td>", GroupCellOf(table, "2026-09-02"), StringComparison.Ordinal);
        Assert.EndsWith(">the Communication Services sector holds no other member, so no median is drawn</td>", GroupCellOf(table, "2026-09-03"), StringComparison.Ordinal);
        Assert.EndsWith(">none of the 2 other members of the Energy sector held a close on both sessions</td>", GroupCellOf(table, "2026-09-04"), StringComparison.Ordinal);
        Assert.EndsWith(">no group median is stored for this move</td>", GroupCellOf(table, "2026-09-05"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachMoveOnANamesPageIsDrawnBesideItsGroupsMedianAsTheStoreHoldsIt()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        var read = 0;

        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            foreach (var row in Rows(store, $"SELECT session_date, group_kind, IFNULL(group_name, ''), group_members, group_counted, CASE WHEN group_median IS NULL THEN '' ELSE printf('%.6f', group_median) END FROM move WHERE ticker = '{ticker}' ORDER BY rank;"))
            {
                var cell = Regex.Match(GroupCellOf(page, row[0]), "data-group-kind=\"([^\"]*)\" data-group-name=\"([^\"]*)\" data-group-members=\"([^\"]*)\" data-group-counted=\"([^\"]*)\" data-group-median=\"([^\"]*)\"");

                Assert.True(cell.Success);
                Assert.Equal([row[1], row[2], row[3], row[4]], [cell.Groups[1].Value, cell.Groups[2].Value, cell.Groups[3].Value, cell.Groups[4].Value]);

                // The median as the annotator stored it, drawn to the two places a move is drawn to.
                if (row[5].Length == 0)
                {
                    Assert.Equal(string.Empty, cell.Groups[5].Value);
                }
                else
                {
                    Assert.InRange(
                        Math.Abs(double.Parse(row[5], CultureInfo.InvariantCulture) - double.Parse(cell.Groups[5].Value, CultureInfo.InvariantCulture)),
                        0,
                        0.005 + 1e-9);
                }

                read++;
            }
        }

        Assert.True(read >= 20, $"Read {read} moves' groups off the pages, expected at least 20.");

        // And the card's key says how the median is read.
        Assert.Contains("Beside each move is its group's median move over the same sessions", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/KEYS")), StringComparison.Ordinal);
    }
}
