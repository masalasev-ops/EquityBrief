using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 24.48: on the operator's order of 2026-11-28 every setup row on Tonight starts closed and opens a panel of its
// own on a click, and a stock's full card is drawn only in its plan view, every other page showing it as one line opening it.
// see: Tonight's setup rows start closed and each opens a panel of its own on a click
// see: A stock's full card is drawn only in its plan view on Tonight, and every other page shows it as one line that opens the plan
public partial class ReadSurface
{
    // The rows 24.48 adds that this check reaches.
    internal static string[] SetupPanelRows =>
    [
        CheckReach.Key("15.7 Tonight", "A setup's panel"),
    ];

    // Every page the order names, for each night and index the store holds a card on: Tonight, Home, the AI stock picker,
    // Paper trading and Past picks. None draws a full card outside a plan's article or opens a setup or a fold unclicked; the
    // plan view of a stock a card is stored for draws it, so the check reads at least one card it lets stand.
    [Fact]
    public async Task NoPageDrawsAFullCardOutsideThePlanViewOrOpensASetupOrAFoldWithoutAClick()
    {
        using var plans = PlanTradesStore();

        MetAgainCard(plans);

        // The S&P 500's families night with the night's cards written over it, as the cards test stores them.
        using var families = await FamilyPagesStore();

        await new EquityBrief.Worker.Cards.DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), families.DatabaseFile).RunAsync("cards");

        var found = new List<string>();
        var (read, cards, nights) = (0, 0, 0);

        foreach (var (store, newest) in new[] { (plans, "MID|" + IndexNight), (families, "GSPC|" + TheSwitch) })
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            foreach (var key in Query(store, "SELECT DISTINCT index_code || '|' || session_date FROM decision_card ORDER BY 1;").Append(newest).Distinct())
            {
                nights++;
                await Walk(store, client, key);
            }
        }

        Assert.True(read >= nights * 3, $"Only {read} of the pages were read.");
        Assert.True(cards > 0, "No page read draws a full card, so the check let none stand.");
        Assert.Empty(found);

        async Task Walk(TemporaryStore store, HttpClient client, string key)
        {
            var (code, night) = (key.Split('|')[0], key.Split('|')[1]);
            var word = (Universes.ByCode(code) ?? Universes.Large).Word;
            var named = Query(store, $"SELECT DISTINCT ticker FROM decision_card WHERE index_code = '{code}' AND session_date = '{night}' ORDER BY 1;").Select(ticker => $"/screens/tonight/{night}?universe={word}&plan={ticker}");

            foreach (var route in named.Concat([$"/screens/tonight/{night}?universe={word}", $"/screens/home/{night}?universe={word}", $"/screens/judge/{night}?universe={word}", $"/screens/paper?universe={word}", $"/screens/picks?universe={word}"]))
            {
                using var answer = await client.GetAsync(route);

                if (!answer.IsSuccessStatusCode)
                {
                    continue;
                }

                var page = WebUtility.HtmlDecode(await answer.Content.ReadAsStringAsync());

                read++;
                cards += Regex.Matches(page, "<div class=\"decision-card").Count;
                found.AddRange(MarkRenderer.CardsOutsideThePlan(page).Select(said => route + ": " + said));
            }
        }
    }

    // Planted by hand: a full card under a setup's row, a panel drawn open and a fold drawn open inside it are each named; the
    // same card inside a plan's article and the panel hidden are not.
    [Fact]
    public void TheCheckNamesAFullCardOutsideThePlanAndAPanelOrFoldDrawnOpen()
    {
        const string card = "<div class=\"decision-card\" data-index=\"MID\" data-family=\"breakout\" data-ticker=\"M2\" data-night=\"2026-10-02\"><p>card</p></div>";
        const string fold = "<details class=\"fold fam-rule\" open><summary>The full rule</summary><div class=\"fold-b\"></div></details>";

        var planted = $"<table><tbody><tr class=\"st-open\" data-for=\"breakout\"><td colspan=\"6\"><div class=\"st-panel fam-edge\" data-family=\"breakout\"><div class=\"setup-panel\">{card}{fold}</div></div></td></tr></tbody></table>";

        Assert.Equal(
            ["a full card outside the plan view: M2", "a setup panel open without a click: breakout", "a fold open without a click: breakout"],
            MarkRenderer.CardsOutsideThePlan(planted));

        var kept = $"<article class=\"pl-plan fam-edge on\" id=\"plan-held-breakout-m2\"><div><p>head</p></div>{card}</article><table><tbody><tr class=\"st-open\" data-for=\"breakout\" hidden><td><div class=\"st-panel fam-edge\" data-family=\"breakout\"><details class=\"fold\"><summary>x</summary></details></div></td></tr></tbody></table>";

        Assert.Empty(MarkRenderer.CardsOutsideThePlan(kept));
        Assert.Equal(["a setup panel open without a click: breakout"], MarkRenderer.CardsOutsideThePlan(kept.Replace(" hidden>", ">", StringComparison.Ordinal)));
        Assert.Empty(MarkRenderer.CardsOutsideThePlan(kept.Replace(" hidden>", ">", StringComparison.Ordinal), setupNamed: true));
    }

    // Worked by hand over the S&P 400's night of 2 Oct: the breakout's panel draws no heading, revision line or description of
    // its own; its pick M1 as one line opening its plan; M2, whose rules met it again while its order of 30 Sep waits, as one
    // line under "Met the rules again (1)" saying so and opening its plan, with no note repeating it; and its folds closed.
    [Fact]
    public async Task ASetupsPanelDrawsItsPicksAndTheStocksItMetAgainAsLinesOpeningTheirPlans()
    {
        using var store = PlanTradesStore();

        MetAgainCard(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        Assert.Contains("<tr class=\"st-open\" data-for=\"breakout\" hidden><td colspan=\"6\"><div class=\"st-panel fam-edge\" data-family=\"breakout\"><section class=\"card setup-card\" id=\"setup-breakout\" data-card=\"family\"", page, StringComparison.Ordinal);

        var whole = SetupCardOf(page, "breakout");
        var panel = whole[whole.IndexOf("<section class=\"family-card\"", StringComparison.Ordinal)..];

        Assert.DoesNotMatch("<tr class=\"st-open\"[^>]*data-for=\"[^\"]+\"(?![^>]* hidden)[^>]*>", page);
        Assert.DoesNotContain("st-about", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"fam-head\"", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"decision-card", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("qualified again tonight but", panel, StringComparison.Ordinal);
        Assert.Contains("data-summary>", panel, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"sp-line\" data-plan-line data-ticker=\"M1\" href=\"{MarkRenderer.PlanHref("M1", "400")}\">", panel, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"sp-h\">{MarkRenderer.MetAgainHeading} (1)</p>", panel, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"sp-line\" data-plan-line data-ticker=\"M2\" href=\"{MarkRenderer.PlanHref("M2", "400")}\"><b class=\"sp-t\">M2</b><span class=\"sp-why\">order of 30 Sep still waits</span></a>", panel, StringComparison.Ordinal);
        Assert.Contains("<details class=\"fold fam-record\"><summary>How this setup has done</summary>", panel, StringComparison.Ordinal);
        Assert.DoesNotMatch("<details\\b[^>]*\\sopen", panel);
    }
}
