using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;

namespace EquityBrief.Tests.Reading;

// read-surface, 13.1: Tonight on a night the setup families drew its list, read back off the rendered page
// over constructed stores the lister itself drew: one card a family with its rule's standing, its picks in
// the page's order with each one's trade and why it is listed, the notes on what it passed and the page
// holds back, a card that lists nothing saying why, and an earlier night drawn as it was.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public partial class ReadSurface
{
    // The claims the framework's card makes, which this check reaches: the parts its row in section 15.7
    // enumerates, and section 18's rows about a stock held back by its open trade and a night that lists none.
    internal static readonly string[] FamilyCardClaims =
    [
        CheckReach.Key("15.7 Tonight", "A family's card, one card a family in the page's order"),
        CheckReach.Key("15.7 Tonight", "A family's card, the family's rule in a sentence"),
        CheckReach.Key("15.7 Tonight", "A family's card, the day its rule went live or the words saying it is provisional"),
        CheckReach.Key("15.7 Tonight", "A family's card, how many it lists tonight with how many variants are scored beside it"),
        CheckReach.Key("15.7 Tonight", "A family's card, its place down the page"),
        CheckReach.Key("15.7 Tonight", "A family's card, the state its reported quarters give it"),
        CheckReach.Key("15.7 Tonight", "A family's card, the buy and the stop and the target"),
        CheckReach.Key("15.7 Tonight", "A family's card, where the buy sits between the stop and the target"),
        CheckReach.Key("15.7 Tonight", "A family's card, the reward to risk"),
        CheckReach.Key("15.7 Tonight", "A family's card, why it is listed tonight in the figures its family stored"),
        CheckReach.Key("15.7 Tonight", "A family's card, the positive and negative stories of the thirty days before"),
        CheckReach.Key("15.7 Tonight", "A family's card, a note for each stock it passed that a trade still open holds back"),
        CheckReach.Key("15.7 Tonight", "A family's card, a note counting the stocks past its five"),
        CheckReach.Key("15.7 Tonight", "A family's card, a line saying why where it lists none"),
        CheckReach.Key("15.7 Tonight", "A family's card, a key saying how to read it"),
        CheckReach.Key(Scope.FailureTable, "A stock a family passes while a trade for it is still open"),
        CheckReach.Key(Scope.FailureTable, "A night the families list no stock"),
    ];

    // The two parts and the row that need a second family on the page, which breakouts bring.
    internal static readonly string[] TwoFamilyClaims =
    [
        CheckReach.Key("15.7 Tonight", "A family's card, the stock with a label for each other family it qualified under"),
        CheckReach.Key("15.7 Tonight", "A family's card, a note for each stock the page lists under an earlier family"),
        CheckReach.Key(Scope.FailureTable, "A stock two families pass on one night"),
    ];

    static async Task DrawTheFamilies(TemporaryStore store, string session)
    {
        // The lister reads the night off the newest stored bar, as the swing filter does.
        PickBar(store, "F1", session, "100");

        await new FamilyLister(FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("families-for-the-page");
    }

    // The night the page is read for. Eight pass the pullback's gates, F1 to F7 at ranks 1 to 7, their
    // reward to risk falling from 3 by a quarter, and HELD at rank 8; N1 fails its trade gate. Every plan
    // is entered at 100, stopped at 96 and won at 110. The night before, a night the swing filter listed
    // before the families, HELD passed, and no outcome is stored for that trade, so it is still open.
    //
    // By hand: no business is read, so the pullback's order is its ranks. F1 to F5 take its five places,
    // F6 and F7 are past five, and HELD is held back by its trade from 2026-10-01.
    static async Task<TemporaryStore> FamilyNightStore(bool registered = true)
    {
        var store = new TemporaryStore().Migrated();

        // The version every row is stored under, reading the plan clear of the noise.
        OpenCloseVersion(store);

        Evening(store, BeforeTheSwitch, filter: true, [new Member("HELD", Passed: true, Rank: 1)]);
        Evening(store, TheSwitch, filter: true,
        [
            .. Enumerable.Range(1, 7).Select(at => new Member($"F{at}", Passed: true, Rank: at, RewardToRisk: 3.25 - 0.25 * at)),
            new Member("HELD", Passed: true, Rank: 8, RewardToRisk: 1.5),
            new Member("N1", Trade: false),
        ]);
        store.Execute("UPDATE gate_result SET clear_stop = '96', clear_target = '110';");

        // F1's setup and trigger gates as a night stores them, with the figures its card's words are read from.
        var member = new Member("F1", Passed: true, Rank: 1, RewardToRisk: 3);
        using var document = JsonDocument.Parse(GatesJson(member));
        var gates = document.RootElement.GetProperty("gates").EnumerateArray()
            .Select(one => new
            {
                gate = one.GetProperty("gate").GetString(),
                passed = one.GetProperty("passed").GetBoolean(),
                reason = one.GetProperty("reason").GetString(),
                values = one.GetProperty("gate").GetString() switch
                {
                    "setup" => new Dictionary<string, string> { ["family"] = "pullback", ["depth"] = "2.3456", ["dry-up"] = "0.8", ["band low"] = "95.5", ["band high"] = "97.25" },
                    "trigger" => new Dictionary<string, string> { ["arrived"] = "2026-10-01", ["arrival window"] = "3" },
                    _ => one.GetProperty("values").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString() ?? string.Empty),
                },
            })
            .ToArray();

        store.Execute($"UPDATE gate_result SET gates = '{JsonSerializer.Serialize(new { gates, notes = Array.Empty<string>() })}' WHERE ticker = 'F1' AND session_date = '{TheSwitch}';");

        // F1's business read as improving on the night, which leaves it first, and three labelled stories
        // in the thirty days before it, two positive and one negative.
        Checks.FixtureExpectations.StoreReadings(store, TheSwitch, "F1", EquityBrief.Core.Quarters.QuarterReadings.Of(Checks.FixtureExpectations.Quarters(4), null, null));

        foreach (var (id, published, direction) in new[] { ("f1", "2026-09-28", "positive"), ("f2", "2026-09-29", "negative"), ("f3", "2026-09-30", "positive") })
        {
            store.Execute(
                "INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES " +
                $"('F1', '{id}', 'https://example.invalid/{id}', 'F1 in the news', 'wire.example', '{published}T12:00:00Z', 'The company said so.', 20, '{EquityBrief.Core.Research.Admissibility.Accepted}', '{published}');");
            store.Execute(
                "INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, cause, kind, direction, reason, labelled_at, run_id) VALUES " +
                $"('F1', '{id}', 'deepseek', 1, 'deepseek-flash', '{EquityBrief.Core.News.NewsLabelling.Labelled}', NULL, 'results', '{direction}', 'The company said so.', '{published}T23:10:00Z', 'label-news-test');");
        }

        if (registered)
        {
            // The pullback's live candidate and three variants registered on 2026-09-25, one of them retired
            // the day after: a live rule since that day with two variants standing.
            store.Execute(
                "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
                "(1, 'the live swing filter, version 1', 'rule', 'test', 'swing-filter', '{}', 'v', 'registered', NULL, '2026-09-25T10:39:49Z', NULL), " +
                "(2, 'the swing filter with the market gate off, from version 1', 'rule', 'test', 'swing-filter', '{}', 'v', 'registered', NULL, '2026-09-25T10:39:49Z', NULL), " +
                "(3, 'the swing filter with one-session arrival, from version 1', 'rule', 'test', 'swing-filter', '{}', 'v', 'registered', NULL, '2026-09-25T10:39:49Z', NULL), " +
                "(4, 'the swing filter with strength in the top third, from version 1', 'rule', 'test', 'swing-filter', '{}', 'v', 'registered', NULL, '2026-09-25T10:39:49Z', NULL), " +
                "(5, 'the swing filter with strength in the top third, from version 1', 'rule', 'test', 'swing-filter', '{}', 'v', 'retired', 'the swing filter with strength in the top third, from version 1', '2026-09-26T09:00:00Z', 'constructed');");
        }

        await DrawTheFamilies(store, TheSwitch);

        return store;
    }

    static string FamilyCardOf(string page, string family) =>
        Assert.Single(Blocks(page, $"<section class=\"family-card\" data-family=\"{family}\".*?</section>"));

    static string FamilyRowOf(string card, string ticker)
    {
        var at = card.IndexOf($"<tr data-ticker=\"{ticker}\"", StringComparison.Ordinal);

        Assert.True(at >= 0, $"no row for {ticker} on the card");

        return card[at..card.IndexOf("</tr>", at, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task TonightDrawsACardAFamilyWithItsPicksInThePagesOrderEachWithItsTradeAndWhyItIsListed()
    {
        using var store = await FamilyNightStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = FamilyCardOf(page, SetupFamilies.Pullback);

        // The card: the first of one on the page, five picks, its rule live since the day its candidate was
        // registered, two variants standing beside it. Its heading and its rule in a sentence are the family's.
        Assert.StartsWith("<section class=\"family-card\" data-family=\"pullback\" data-place=\"1\" data-of=\"1\" data-picks=\"5\" data-state=\"live\" data-live-since=\"2026-09-25\" data-variants=\"2\">", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"family-state\">Live rule since <b>2026-09-25</b> · 5 picks tonight · 2 variants scoring in the background</p>", card, StringComparison.Ordinal);
        Assert.Contains("<div class=\"lbl\">Setup 1 of 1 · Pullback in an uptrend</div><h2>Pullbacks to support</h2>", page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"lede\">{SetupFamilies.Pullbacks.Rule}</p>", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, "data-card=\"family\""));

        // The picks, in the page's order, numbered by their place: F1 to F5, and no row for the two past
        // five, the one held back or the one that failed a gate.
        Assert.Equal(
            [("F1", "1"), ("F2", "2"), ("F3", "3"), ("F4", "4"), ("F5", "5")],
            Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\" data-family=\"pullback\".*?<td class=\"place\" data-place=\"(\\d+)\">\\2</td>", RegexOptions.Singleline)
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.All(new[] { "F6", "F7", "HELD", "N1" }, ticker => Assert.DoesNotContain($"data-ticker=\"{ticker}\"", card, StringComparison.Ordinal));
        Assert.Equal(FamilyHeadingsDrawn, Regex.Matches(card, "<th class=\"[^\"]*tipped\"[^>]*data-heading=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        // F1's trade: bought at 100, stopped at 96, won at 110. The buy sits 4 of the 14 from the stop to the
        // target, 0.286 of the way, and the reward to risk is the 3 its trade gate stored.
        var first = FamilyRowOf(card, "F1");

        Assert.Contains("<td class=\"r num plan-buy\" data-buy=\"100\">100.00</td><td class=\"r num plan-stop\" data-stop=\"96\">96.00</td><td class=\"r num plan-target\" data-target=\"110\">110.00</td>", first, StringComparison.Ordinal);
        Assert.Contains("<span class=\"stt\" data-along=\"0.286\" title=\"the buy sits 29% of the way from the stop to the target\"><span class=\"stt-mark\" style=\"left:28.6%\"></span></span>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"3\">3.00</td>", first, StringComparison.Ordinal);

        // Why it is listed, in the figures its gates stored: a depth of 2.3456 typical moves drawn to a
        // tenth, the band's edges as prices, the dry-up to a hundredth, and the session its trigger arrived on.
        Assert.Contains("<td class=\"why-tonight\">Fell 2.3 typical moves into its support band at 95.50 to 97.25 on volume 0.80 times its average, and turned up on 2026-10-01.</td>", first, StringComparison.Ordinal);

        // The state its reported quarters gave it, and its stories of the thirty days before: two positive
        // and one negative. F2 holds no reading and no label, and its row says so.
        Assert.Contains("<td class=\"business-cell\"> <span class=\"business\" tabindex=\"0\" data-state=\"improving\">improving", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"news-counts\" data-positive=\"2\" data-negative=\"1\"><span class=\"np\">+2</span> <span class=\"nn\">-1</span></td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"business-cell\"><span class=\"degraded\" data-state=\"none\">not read</span></td>", FamilyRowOf(card, "F2"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">no label</span></td>", FamilyRowOf(card, "F2"), StringComparison.Ordinal);

        // The key under the card says how to read it.
        Assert.Contains("<b>How to read the card.</b> Each row is one stock this setup lists tonight, bought at the evening's close.", page, StringComparison.Ordinal);

        // A row whose gates stored no depth, band or dry-up says only what they stored: it turned up tonight.
        Assert.Contains("<td class=\"why-tonight\">Fell into a support band, and turned up tonight.</td>", FamilyRowOf(card, "F2"), StringComparison.Ordinal);
        Assert.Contains("data-reward-to-risk=\"2.75\">2.75</td>", FamilyRowOf(card, "F2"), StringComparison.Ordinal);

        // The row selects as a list's row does, and carries the control asking for a report it does not hold.
        Assert.Contains($"data-selects=\"F1\" data-select-href=\"#/night/{TheSwitch}?name=F1\"", first, StringComparison.Ordinal);
        Assert.Contains("data-report-state=\"unwritten\" data-researched=\"false\"", first, StringComparison.Ordinal);
        Assert.Contains("data-kind=\"ask\" data-asks=\"F1\"", first, StringComparison.Ordinal);
        Assert.Contains("data-selected=\"F1\"", page, StringComparison.Ordinal);

        // The notes: the stock held back by its open trade, naming the night that trade was listed on, and
        // the two past the five, named.
        Assert.Equal(
            [
                "HELD qualified again tonight but its trade from 2026-10-01 is still open, so it is not listed. One stock, one trade.",
                "2 more qualified tonight and are not listed here, a setup listing at most 5 a night: F6, F7.",
            ],
            Regex.Matches(card, "<p class=\"family-note\">(.*?)</p>").Select(match => match.Groups[1].Value));

        // The one list a night before the families draws, and its "Still open" card, are not drawn.
        Assert.DoesNotContain("data-list=\"listed\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card=\"still-open\"", page, StringComparison.Ordinal);

        // Read back against the store in both directions: the rows drawn are the stocks the page's list
        // holds as listed, in their places, and no other.
        Assert.Equal(
            Strings(store, $"SELECT ticker FROM family_pick WHERE session_date = '{TheSwitch}' AND state = 'listed' ORDER BY place;"),
            Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        // The night before, which the families did not draw, is drawn as it was listed: the one list, and no card.
        var before = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{BeforeTheSwitch}"));

        Assert.Contains("data-list=\"listed\"", before, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"family-card\"", before, StringComparison.Ordinal);
    }

    static readonly string[] FamilyHeadingsDrawn = ["#", "Stock", "Business", "Buy", "Stop", "Target", "Stop to target", "Reward to risk", "Why tonight", "News, 30 days"];

    [Fact]
    public async Task AFamilyNoFreezeHasRegisteredIsMarkedProvisionalAndACardListingNothingSaysWhy()
    {
        // No registration stands for the pullback: its card says it is provisional, in the ruling's words.
        using (var store = await FamilyNightStore(registered: false))
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var card = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), SetupFamilies.Pullback);

            Assert.Contains("data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\"", card, StringComparison.Ordinal);
            Assert.Contains("<p class=\"family-state\"><b class=\"provisional\">provisional: not yet frozen; its record starts at the freeze</b> · 5 picks tonight · 0 variants scoring in the background</p>", card, StringComparison.Ordinal);
            Assert.Equal("provisional: not yet frozen; its record starts at the freeze", SetupFamilies.Provisional);
        }

        // A night the market check closed: every member fails its market gate, breadth 40% under a floor of
        // 50%, so the family passes none and its card says the check closed every list, with both figures.
        using (var closed = new TemporaryStore().Migrated())
        {
            OpenCloseVersion(closed);
            Evening(closed, TheSwitch, filter: true, [.. Enumerable.Range(0, 6).Select(at => new Member($"M{at}", Market: false))]);
            closed.Execute(
                "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
                $"VALUES ('{TheSwitch}', 6, 6, 2, 0.4, 6, 3, 0.5, 6, 0.9);");
            await DrawTheFamilies(closed, TheSwitch);

            using var host = new Host(closed.Root);
            using var client = host.CreateClient();

            var card = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), SetupFamilies.Pullback);

            Assert.Contains("data-picks=\"0\"", card, StringComparison.Ordinal);
            Assert.Contains("<p class=\"degraded family-empty\" data-picks=\"0\">The market check closed every list tonight: 40.0% of the members closed above their 200-day average, below its floor of 50%.</p>", card, StringComparison.Ordinal);
            Assert.DoesNotContain("<table", card, StringComparison.Ordinal);
        }

        // A night the market is open and no stock passes: T1 fails its trend gate, S1 its setup, G1 its
        // trigger and D1 its trade, so 3 pass the trend gate, 2 the setup, 1 the trigger and none the trade.
        using (var quiet = new TemporaryStore().Migrated())
        {
            OpenCloseVersion(quiet);
            Evening(quiet, TheSwitch, filter: true,
            [
                new Member("T1", Trend: false), new Member("S1", Setup: false), new Member("G1", Trigger: false), new Member("D1", Trade: false),
            ]);
            await DrawTheFamilies(quiet, TheSwitch);

            using var host = new Host(quiet.Root);
            using var client = host.CreateClient();

            var card = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), SetupFamilies.Pullback);

            Assert.Contains("<p class=\"degraded family-empty\" data-picks=\"0\">No stock passed this setup tonight: 3 passed the trend and strength gate, 2 the setup, 1 the trigger and 0 the trade, and none of those past the exclusions.</p>", card, StringComparison.Ordinal);
        }
    }
}
