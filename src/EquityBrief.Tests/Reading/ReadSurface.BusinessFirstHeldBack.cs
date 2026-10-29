using System.Globalization;
using System.Net;
using EquityBrief.Core.Families;

namespace EquityBrief.Tests.Reading;

// read-surface, the 22.14 correction of 2026-10-29: the Run page under every index states, one line a night, how many
// picks business first's open trades held back on each of the three indices, read off the rows naming its trade as the
// one still open for a stock; a pick of business first's own held by its own trade is no other family's and is not
// counted, and a night none names states nought on each.
// see: Business first stays in the night unseen until the next change that moves the S&P 400's and 600's pins, and the Run page counts the picks its open trades hold back each night an index
public partial class ReadSurface
{
    static readonly string[] RunRoutesOfEveryIndex = [$"/screens/run/{IndexNight}", $"/screens/run/{IndexNight}?universe=400", $"/screens/run/{IndexNight}?universe=600"];

    [Fact]
    public async Task TheRunPageStatesHowManyPicksBusinessFirstsOpenTradesHeldBackOnEachIndex()
    {
        using var store = UniversesStore();
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var family = SetupFamilies.FundamentalsFirst.Name;

        // M3 is held back by the S&P 500's pullback's trade, which is no hold-back of business first's.
        var none = await Api(store).HeldByBusinessFirstAsync(night);

        Assert.Equal((night, 0, 0, 0), (none.Night, none.Large, none.Mid, none.Small));

        using (var host = new Host(store.Root))
        using (var client = host.CreateClient())
        {
            foreach (var route in RunRoutesOfEveryIndex)
            {
                Assert.Contains(
                    "<p class=\"held-by-business-first\" data-gspc=\"0\" data-mid=\"0\" data-sml=\"0\">Business first's open trades held back 0 picks on the S&P 500, 0 on the S&P 400 and 0 on the S&P 600 tonight.</p>",
                    WebUtility.HtmlDecode(await client.GetStringAsync(route)),
                    StringComparison.Ordinal);
            }
        }

        // One pick on each kind of list held back for business first's trade in a stock, the S&P 500's list's, an index's
        // list's and the dip's on two indices, and one of business first's own held by its own trade.
        store.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night, held_index) VALUES " +
            $"('{IndexNight}', 'AAA', 'pullback', '{FamilyList.OpenTrade}', NULL, '[]', '{family}', '2026-09-30', 'SML');");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('MID', '{IndexNight}', 'M2', 'drift', '{FamilyList.OpenTrade}', NULL, '[]', 'SML', '{family}', '2026-09-30'), " +
            $"('MID', '{IndexNight}', 'M2', '{family}', '{FamilyList.OpenTrade}', NULL, '[]', 'SML', '{family}', '2026-09-30');");
        store.Execute(
            "INSERT INTO dip_pick (index_code, session_date, ticker, state, place, listed_under, held_index, held_family, held_night) VALUES " +
            $"('MID', '{IndexNight}', 'M3', '{FamilyList.OpenTrade}', NULL, NULL, 'SML', '{family}', '2026-09-30'), " +
            $"('SML', '{IndexNight}', 'S1', '{FamilyList.OpenTrade}', NULL, NULL, 'SML', '{family}', '2026-09-30');");

        var held = await Api(store).HeldByBusinessFirstAsync(night);

        Assert.Equal((1, 2, 1), (held.Large, held.Mid, held.Small));

        using (var host = new Host(store.Root))
        using (var client = host.CreateClient())
        {
            foreach (var route in RunRoutesOfEveryIndex)
            {
                Assert.Contains(
                    "<p class=\"held-by-business-first\" data-gspc=\"1\" data-mid=\"2\" data-sml=\"1\">Business first's open trades held back 1 pick on the S&P 500, 2 on the S&P 400 and 1 on the S&P 600 tonight.</p>",
                    WebUtility.HtmlDecode(await client.GetStringAsync(route)),
                    StringComparison.Ordinal);
            }
        }

        // Another night's rows count for nothing on this one.
        Assert.Equal((0, 0, 0), await Api(store).HeldByBusinessFirstAsync(night.AddDays(1)) is { } after ? (after.Large, after.Mid, after.Small) : (-1, -1, -1));
    }
}
