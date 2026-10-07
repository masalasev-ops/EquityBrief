using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 14.3: the sector heavyweights on the pages, read back off the rendered pages over constructed books.
// Tonight's card after the families' with every holding open at the night's close and no other, each with its sector,
// the day it was bought, its lead at the last rebalance and its close against its 200-day average, what that
// rebalance bought, what was sold at it or since, and the next rebalance; drawn on a night the market check closed
// the lists, and saying why where it holds nothing. Past picks draws each holding in percent beside its size cut, and
// a name's page says the heavyweights hold it.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
// see: The market check closes every swing family's list together, and the sector heavyweights read none
// see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
public partial class ReadSurface
{
    // The parts section 15.7's card enumerates, and section 15.17's holdings, each a claim of its own.
    internal static readonly string[] HeavyweightCardParts =
    [
        "after the setup families' cards on a night they drew the page's list, whatever the market check read",
        "the family's rule in a sentence and the day its live rule registered with the variants kept beside it or the words saying it is provisional where no freeze registered it",
        "one row a holding at the night's close",
        "the sectors in order",
        "each with its sector",
        "the session it was bought on",
        "its lead over its sector at the rebalance that last read it",
        "its close against its 200-day average",
        "held while leading in place of a stop and a target",
        "the stocks the last rebalance bought",
        "each holding sold at it or since, with the session and why",
        "the next rebalance's date",
        "a line saying why where it holds none",
    ];

    internal static readonly string[] HeavyweightPicksParts =
    [
        "beneath the trades",
        "every holding the sector heavyweights bought",
        "newest first",
        "its sector",
        "the session it was bought on with its close",
        "the session it was sold on with its close, or held",
        "why it ended",
        "its result in percent of the buy",
        "its size cut's return over the same sessions and the difference between them",
        "the words saying these are the page's own book and each registered rule's record is its own",
        "nothing where the book has bought none",
    ];

    // The rows the heavyweights' pages add that this check reaches: section 15.7's card as the parts its row
    // enumerates, section 15.9's line and section 15.17's holdings as the parts theirs enumerates.
    internal static readonly string[] HeavyweightPageClaims =
    [
        .. HeavyweightCardParts.Select(part => CheckReach.Key("15.7 Tonight", "The sector heavyweights' card, " + part)),
        CheckReach.Key("15.9 Name", "Held by the sector heavyweights"),
        .. HeavyweightPicksParts.Select(part => CheckReach.Key("15.17 Past picks", "The sector heavyweights' holdings, " + part)),
    ];

    // A book as it stood on the family night: the rebalance of 2026-10-01 bought F1, leading Energy by 0.123, and X3,
    // leading Utilities by 0.05; Y1, bought on 2026-09-01, was sold at that rebalance; Y2 was sold under its average on
    // the night itself; Y3, sold on 2026-10-06, after the night, was held on it; and Z9 was bought after it. F1 closes at
    // 100 tonight over its 200-day average of 90.5, X3 at 30 under its 35.25, and Y3 stores no bar tonight.
    static void HeavyweightBookOn(TemporaryStore store)
    {
        store.Execute(
            "INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader) VALUES " +
            $"('{BeforeTheSwitch}', 'Energy', 1, 'F1', 'CIK 0000000101', '5000', 0.25, 0.127, 0.123, 1, 1), " +
            $"('{BeforeTheSwitch}', 'Utilities', 1, 'X2', 'CIK 0000000102', '4000', 0.01, 0.01, 0.0, 1, 0), " +
            $"('{BeforeTheSwitch}', 'Utilities', 2, 'X3', 'CIK 0000000103', '3000', 0.06, 0.01, 0.05, 1, 1), " +
            "('2026-09-01', 'Energy', 1, 'Y1', 'CIK 0000000104', '6000', 0.3, 0.1, 0.2, 1, 1);");
        store.Execute(
            "INSERT INTO heavyweight_holding (ticker, entered_on, sector, company, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return) VALUES " +
            $"('F1', '{BeforeTheSwitch}', 'Energy', 'CIK 0000000101', '98', 1.02, '[]', '{TheSwitch}', NULL, NULL, NULL, NULL, NULL), " +
            $"('X3', '{BeforeTheSwitch}', 'Utilities', 'CIK 0000000103', '31', 0.97, '[]', '{TheSwitch}', NULL, NULL, NULL, NULL, NULL), " +
            $"('Y1', '2026-09-01', 'Energy', 'CIK 0000000104', '40', 1.05, '[]', '{BeforeTheSwitch}', '{BeforeTheSwitch}', '42', 'no longer the leader', 0.05, 0.02), " +
            $"('Y2', '2026-09-01', 'Health Care', 'CIK 0000000105', '50', 0.96, '[]', '{TheSwitch}', '{TheSwitch}', '48', 'a close under its 200-day average', -0.04, 0.01), " +
            "('Y3', '2026-09-01', 'Financials', 'CIK 0000000106', '60', 1.1, '[]', '2026-10-06', '2026-10-06', '66', 'no longer the leader', 0.1, 0.03), " +
            "('Z9', '2026-10-05', 'Energy', 'CIK 0000000107', '70', 1.0, '[]', '2026-10-05', NULL, NULL, NULL, NULL, NULL);");
        PickBar(store, "X3", TheSwitch, "30");
        store.Execute(
            "INSERT OR REPLACE INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
            $"('F1', '{TheSwitch}', 'sma200', 90.5, 200), ('X3', '{TheSwitch}', 'sma200', 35.25, 200);");
    }

    static string HeavyweightCardOf(string page) =>
        Assert.Single(Blocks(page, "<section class=\"family-card heavyweight-card\".*?</section>"));

    [Fact]
    public async Task TonightDrawsTheSectorHeavyweightsCardAfterTheFamiliesWithEveryHoldingOpenAtTheNightsCloseAndNoOther()
    {
        using var store = await FamilyNightStore();

        HeavyweightBookOn(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = HeavyweightCardOf(page);

        // After every family's card, under its own heading and rule, provisional since no freeze stands in this store, its
        // last rebalance on the month's first session and its next the session after November's first, Monday the 2nd,
        // whose stored year holds 251 closes of the 252 its readings need, its leads read over the twelve months the
        // freeze set.
        Assert.True(page.IndexOf("heavyweight-card", StringComparison.Ordinal) > page.LastIndexOf("data-card=\"family\"", StringComparison.Ordinal));
        Assert.StartsWith(
            "<section class=\"family-card heavyweight-card\" data-family=\"heavyweight\" data-holdings=\"3\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-last-rebalance=\"2026-10-01\" data-next-rebalance=\"2026-11-03\" data-look-back=\"251\">",
            card,
            StringComparison.Ordinal);
        Assert.Contains($"<div class=\"lbl\">Rotation · {SetupFamilies.SectorHeavyweights.Eyebrow}</div><h2>{SetupFamilies.SectorHeavyweights.Heading}</h2>", page, StringComparison.Ordinal);
        // Its rule in words, written from the settings its live rule runs at: each sector's 10 largest, a 251-session
        // look-back, 2 leaders against the sector fund, a beta of at least one, and sold when it no longer leads at a later
        // month's rebalance, the session the book reads it on.
        Assert.Contains("<p class=\"lede\">On each month's first session, or the first after it whose stored year holds the closes its readings need, among each sector's 10 largest companies of the index by value, the 2 whose 251-session returns beat the sector fund's by the most, where they beat it at all, their close above their 50-day average and that above their 200-day, their beta against the index at least one, bought at that close. Held while it leads: sold at the close of a later month's rebalance where the rule would no longer buy it.</p>", page, StringComparison.Ordinal);
        Assert.Contains("It is sold at the close of a later month's rebalance where it no longer leads, or at its last close as a member.", card, StringComparison.Ordinal);
        Assert.DoesNotContain("month's first close", page, StringComparison.Ordinal);
        Assert.Contains(
            $"<p class=\"family-state\"><b class=\"provisional\">{SetupFamilies.ProvisionalStatus}</b> · 3 holdings tonight · held while leading · last rebalance 2026-10-01 · next rebalance <b class=\"next-rebalance\">2026-11-03</b></p>",
            card,
            StringComparison.Ordinal);

        // Both directions: every row a holding open at the night's close, in the order of its sector, and every such
        // holding a row, Y3, sold after the night, among them and Y1, Y2 and Z9 not.
        Assert.Equal(
            Strings(store, $"SELECT ticker FROM heavyweight_holding WHERE entered_on <= '{TheSwitch}' AND (ended_on IS NULL OR ended_on > '{TheSwitch}') ORDER BY sector, ticker;"),
            Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(["F1", "Y3", "X3"], Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(MarkRendererHeadings(), Regex.Matches(card, "<th class=\"[^\"]*tipped\"[^>]*data-heading=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        // F1: Energy, bought on 2026-10-01, leading by 0.123 at that rebalance, closing at 100 over its average of 90.5.
        var f1 = card[card.IndexOf("<tr data-ticker=\"F1\"", StringComparison.Ordinal)..];
        f1 = f1[..f1.IndexOf("</tr>", StringComparison.Ordinal)];

        Assert.StartsWith("<tr data-ticker=\"F1\" data-sector=\"Energy\" data-held-since=\"2026-10-01\" data-lead=\"0.123\" data-close=\"100\" data-average=\"90.5\">", f1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"setup\">Energy</td><td>2026-10-01</td>", f1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num heavyweight-lead\">+12.3 points over 251 sessions, at 2026-10-01</td>", f1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"heavyweight-average\" data-under=\"false\">100.00, at or above its 200-day average of 90.50</td>", f1, StringComparison.Ordinal);
        Assert.Contains("<td class=\"heavyweight-plan\">held while leading</td>", f1, StringComparison.Ordinal);

        // X3 closes under its average, and Y3, whose rebalance stored no row for it and which stores no bar tonight, says
        // so in both cells.
        Assert.Contains("<td class=\"r num heavyweight-lead\">+5.0 points over 251 sessions, at 2026-10-01</td><td class=\"heavyweight-average\" data-under=\"true\">30.00, under its 200-day average of 35.25</td>", card, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"Y3\" data-sector=\"Financials\" data-held-since=\"2026-09-01\" data-lead=\"none\" data-close=\"none\" data-average=\"none\">", card, StringComparison.Ordinal);
        Assert.Contains("<span class=\"degraded\">not read</span></td><td class=\"heavyweight-average\"><span class=\"degraded\">no close or average stored tonight</span></td>", card, StringComparison.Ordinal);

        // What the last rebalance bought, and what was sold at it or since, in the order sold, each with why.
        Assert.Contains("<p class=\"family-note heavyweight-entered\" data-entered=\"F1,X3\">The rebalance of 2026-10-01 bought F1, X3.</p>", card, StringComparison.Ordinal);
        Assert.Equal(
            ["Y1 was sold at the close of 2026-10-01: no longer the leader.", "Y2 was sold at the close of 2026-10-02: a close under its 200-day average."],
            Regex.Matches(card, "<p class=\"family-note heavyweight-ended\"[^>]*>([^<]+)</p>").Select(match => match.Groups[1].Value));

        // F1 stands on the pullback's card as well: each card keeps its own one trade a stock.
        Assert.Contains("data-ticker=\"F1\" data-family=\"pullback\"", page, StringComparison.Ordinal);

        // The key says how to read it.
        Assert.Contains(
            "<b>How to read the card.</b> Each row is a stock the sector heavyweights hold at tonight's close, bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, as one of the two leaders of its sector's largest companies. It has no stop and no target: it is held while it leads, and sold at the close of a later month's rebalance where the rule would no longer buy it, or at its last close as a member of the index.",
            page,
            StringComparison.Ordinal);
    }

    static IReadOnlyList<string> MarkRendererHeadings() =>
        [.. EquityBrief.Web.Marks.MarkRenderer.HeavyweightHeadings.Select(heading => heading.Heading)];

    [Fact]
    public async Task TheHeavyweightsCardHoldsWhatItHoldsOnANightTheMarketCheckClosedAndSaysWhyWhereItHoldsNothing()
    {
        // A night the market check closed every swing list: the cards list nothing, and the heavyweights' card still
        // draws its three holdings.
        using var closed = new TemporaryStore().Migrated();

        OpenCloseVersion(closed);
        Evening(closed, TheSwitch, filter: true, [.. Enumerable.Range(0, 6).Select(at => new Member($"M{at}", Market: false))]);
        closed.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            $"VALUES ('{TheSwitch}', 6, 6, 2, 0.4, 6, 3, 0.5, 6, 0.9);");
        await DrawTheFamilies(closed, TheSwitch);
        HeavyweightBookOn(closed);

        using (var host = new Host(closed.Root))
        using (var client = host.CreateClient())
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

            Assert.Contains("data-open=\"false\"", Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>")), StringComparison.Ordinal);
            Assert.Contains("data-holdings=\"3\"", HeavyweightCardOf(page), StringComparison.Ordinal);
        }

        // A book that has read no rebalance holds nothing, and says why.
        using var empty = await FamilyNightStore();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        var none = HeavyweightCardOf(WebUtility.HtmlDecode(await emptyClient.GetStringAsync($"/screens/tonight/{TheSwitch}")));

        Assert.Contains("data-holdings=\"0\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-last-rebalance=\"none\"", none, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded family-empty\" data-holdings=\"0\">No rebalance has been read yet. The book reads its first on the next night the store holds that night's closes of the index and of each sector's fund and the 252 sessions its readings need, ranking each member by the company and share counts the quarters fetch stores from the answer it already asks for.</p>", none, StringComparison.Ordinal);
        Assert.DoesNotContain("heavyweight-table", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHeavyweightsCardNamesTheNextSessionAsItsNextRebalanceWhereTheBookHasReadNoneInThatMonth()
    {
        // A book that has read no rebalance reads its first on the next night its store's year holds the 252 closes its
        // readings need, so Friday's card names Tuesday, the Monday between holding 251, and not November's first.
        using var none = await FamilyNightStore();

        using (var host = new Host(none.Root))
        using (var client = host.CreateClient())
        {
            var card = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")));

            Assert.Contains("data-last-rebalance=\"none\" data-next-rebalance=\"2026-10-06\"", card, StringComparison.Ordinal);
            Assert.Contains("· no rebalance read yet · next rebalance <b class=\"next-rebalance\">2026-10-06</b></p>", card, StringComparison.Ordinal);
            Assert.DoesNotContain("heavyweight-waits", card, StringComparison.Ordinal);
        }

        // One whose last rebalance was September's, drawn on an October night that read none, rebalances on the same
        // session as well, since no rebalance of October has been read, and says the month's rebalance has not been read.
        using var september = await FamilyNightStore();

        september.Execute(
            "INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader) " +
            "VALUES ('2026-09-01', 'Energy', 1, 'Y1', 'CIK 0000000104', '6000', 0.3, 0.1, 0.2, 1, 0);");

        using (var host = new Host(september.Root))
        using (var client = host.CreateClient())
        {
            var card = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")));

            Assert.Contains("data-last-rebalance=\"2026-09-01\" data-next-rebalance=\"2026-10-06\"", card, StringComparison.Ordinal);
            Assert.Contains("<p class=\"family-note heavyweight-waits\">The rebalance of this month has not been read, and the book tries again on 2026-10-06.</p>", card, StringComparison.Ordinal);
        }
    }

    // The card a book draws on a night, its last rebalance read on the session given and nothing held.
    static string HeavyweightCardOn(DateOnly night, DateOnly last) =>
        new EquityBrief.Web.Marks.MarkRenderer().HeavyweightCard(EquityBrief.Api.Reading.TonightScreen.Heavyweights(
            night,
            [],
            [new EquityBrief.Api.Reading.HeavyweightReadRow(last, "Energy", 1, "F1", "CIK 0000000101", 5000m, 0.25, 0.127, 0.123, true, true)],
            [],
            new Dictionary<string, EquityBrief.Web.Marks.UniverseCell>()));

    [Fact]
    public void TheHeavyweightsCardNamesTheFirstSessionWhoseStoredYearHoldsTheLiveSettingsNeedAndSaysTheMonthsRebalanceWaits()
    {
        var read = new DateOnly(2026, 10, 2);

        // The month's last session: the next month's first is a Monday whose year holds 251 closes, so the card names the
        // Tuesday, whose year holds 252, and says nothing waits.
        var before = HeavyweightCardOn(new DateOnly(2026, 10, 30), read);

        Assert.Contains("data-last-rebalance=\"2026-10-02\" data-next-rebalance=\"2026-11-03\"", before, StringComparison.Ordinal);
        Assert.DoesNotContain("heavyweight-waits", before, StringComparison.Ordinal);

        // On that Monday the month's rebalance has not been read: the card names the Tuesday and says the rebalance waits
        // on the year's 251 closes.
        var monday = HeavyweightCardOn(new DateOnly(2026, 11, 2), read);

        Assert.Contains("data-last-rebalance=\"2026-10-02\" data-next-rebalance=\"2026-11-03\"", monday, StringComparison.Ordinal);
        Assert.Contains(
            "<p class=\"family-note heavyweight-waits\">The rebalance of this month waits: the year of closes the store keeps to 2026-11-02 holds 251 sessions and its readings need 252, so the book reads it on 2026-11-03.</p>",
            monday,
            StringComparison.Ordinal);

        // A month whose first session holds 252 is named as the calendar names it, and nothing waits.
        var november = HeavyweightCardOn(new DateOnly(2026, 11, 30), new DateOnly(2026, 11, 3));

        Assert.Contains("data-last-rebalance=\"2026-11-03\" data-next-rebalance=\"2026-12-01\"", november, StringComparison.Ordinal);
        Assert.DoesNotContain("heavyweight-waits", november, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PastPicksDrawsEachHoldingInPercentBesideItsSizeCutAndANamesPageSaysItIsHeld()
    {
        using var store = await FamilyNightStore();

        HeavyweightBookOn(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));
        var table = Assert.Single(Blocks(picks, "<table class=\"list-table heavyweight-picks\".*?</table>"));

        // Every holding bought by the newest night, newest first and its sectors in order, Z9 bought after it not among
        // them; the two sold with their results in percent beside their size cuts', and the difference in points.
        Assert.Equal(
            ["F1", "X3", "Y1", "Y3", "Y2"],
            Regex.Matches(table, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(
            Strings(store, $"SELECT ticker FROM heavyweight_holding WHERE entered_on <= '{TheSwitch}' ORDER BY entered_on DESC, sector, ticker;"),
            Regex.Matches(table, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<p class=\"list-count\" data-holdings=\"5\" data-ended=\"2\">5 holdings, 2 sold and 3 held</p>", picks, StringComparison.Ordinal);
        Assert.Contains("<p class=\"provisional-count heavyweight-book\">The page's own book, at the setting the family froze at: each registered rule's record is read off a book of its own, on the run page.</p>", picks, StringComparison.Ordinal);
        Assert.Contains(
            "A holding is bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, and sold at the close of a later month's rebalance where it no longer leads its sector, or at its last close as a member of the index.",
            picks,
            StringComparison.Ordinal);
        Assert.DoesNotContain("month's first close", picks, StringComparison.Ordinal);

        Assert.Contains("<td>2026-09-01 at 40.00</td><td>2026-10-01 at 42.00</td><td>no longer the leader</td><td class=\"r num\">+5.0%</td><td class=\"r num\">+2.0%</td><td class=\"r num\">+3.0 points</td>", table, StringComparison.Ordinal);
        Assert.Contains("<td>2026-09-01 at 50.00</td><td>2026-10-02 at 48.00</td><td>a close under its 200-day average</td><td class=\"r num\">-4.0%</td><td class=\"r num\">+1.0%</td><td class=\"r num\">-5.0 points</td>", table, StringComparison.Ordinal);

        // Y3, sold after the night, is held on it, with neither figure yet.
        Assert.Contains("data-ticker=\"Y3\" data-bought=\"2026-09-01\" data-sold=\"open\" data-result=\"none\" data-cut=\"none\" data-edge=\"none\">", table, StringComparison.Ordinal);
        Assert.Contains("<td>2026-09-01 at 60.00</td><td><b>held</b></td><td>still held</td><td class=\"r num\"><span class=\"degraded\">open</span></td>", table, StringComparison.Ordinal);

        // A name's page says the heavyweights hold it; a name they do not hold says nothing of them.
        Assert.Contains(
            "<p class=\"listed-under heavyweight-held\">Held by the sector heavyweights since 2026-10-01 as the leader of Energy, while it leads.</p>",
            WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/F1/{TheSwitch}")),
            StringComparison.Ordinal);
        Assert.DoesNotContain("heavyweight-held", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/F2/{TheSwitch}")), StringComparison.Ordinal);

        // A store whose book has bought nothing draws no table of it.
        using var empty = await FamilyNightStore();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.DoesNotContain("heavyweight-picks", WebUtility.HtmlDecode(await emptyClient.GetStringAsync("/screens/picks")), StringComparison.Ordinal);
    }
}
