using System.Text.Json;
using EquityBrief.Api.Cloudflare;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;
using EquityBrief.Worker.Cards;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From 24.50, on the operator's order of 2026-11-28: Home, both of Tonight's tabs, both of Paper trading's and a plan, laid out in
// the machine's browser at 1440 and at a true 390 in both palettes over constructed stores, every block on its page column or its
// card's inner edges within a pixel, a running paragraph held to a reading width excepted; each block that is not is named. Where
// EQUITYBRIEF_LAYOUT_SHOTS names a folder, each page laid out is saved there as a screenshot.
// see: Every block sits on its page column or its card's inner edges as a browser lays the pages out at 1440 and 390
public partial class ReadSurface
{
    const string PlansOrigin = "http://plans.equitybrief.invalid";

    const string PickerOrigin = "http://picker.equitybrief.invalid";

    [Fact]
    public async Task EveryBlockOfHomeTonightPaperTradingAndAPlanSitsOnItsColumnInBothWindowsAndBothPalettes()
    {
        using var plans = await FamilyPagesStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), plans.DatabaseFile).RunAsync("cards");

        using var picker = PickerTabsStore();
        using var plansHost = new Host(plans.Root);
        using var plansClient = plansHost.CreateClient();
        using var pickerHost = new Host(picker.Root);
        using var pickerClient = pickerHost.CreateClient();

        async Task<(int Status, string Type, byte[] Body)?> Answer(string url)
        {
            var client = url.StartsWith(PlansOrigin + "/", StringComparison.Ordinal) ? plansClient
                : url.StartsWith(PickerOrigin + "/", StringComparison.Ordinal) ? pickerClient
                : null;

            if (client is null)
            {
                return null;
            }

            using var response = await client.GetAsync(new Uri(url).PathAndQuery);

            return ((int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? "text/html; charset=utf-8", await response.Content.ReadAsByteArrayAsync());
        }

        var pages = new (string Page, string Url)[]
        {
            ("Home", PlansOrigin + "/" + SinglePageApp.HomeRoute),
            ("Tonight", PlansOrigin + "/#/tonight"),
            ("Tonight's AI tab", PickerOrigin + "/" + SinglePageApp.PickerTabRoute),
            ("Paper trading", PlansOrigin + "/#/paper"),
            ("Paper trading's AI tab", PickerOrigin + "/" + SinglePageApp.PickerTradesRoute),
            ("F3's plan", PlansOrigin + "/#/tonight/plan/F3"),
        };
        var shots = Environment.GetEnvironmentVariable("EQUITYBRIEF_LAYOUT_SHOTS");
        var faults = new List<string>();

        await using var browser = await PageBrowser.OpenAsync(Answer);

        Assert.True(browser is not null, "no browser was found to lay the pages out, so their alignment went unread");

        foreach (var (page, url) in pages)
        {
            foreach (var window in SiteCopy.Windows)
            {
                foreach (var dark in new[] { false, true })
                {
                    var named = $"{page} at {window.Name} in the {(dark ? "dark" : "light")} palette";
                    var laid = JsonSerializer.Deserialize<PageColumn.Laid>(await browser.EvaluateAsync(url, window, PageColumn.Script, dark))!;

                    faults.AddRange(PageColumn.Faults(named, laid));

                    if (shots is { Length: > 0 })
                    {
                        Directory.CreateDirectory(shots);
                        await File.AppendAllTextAsync(Path.Combine(shots, "blocks.txt"), $"{named}: {laid.Blocks.Count} block(s) read, {laid.Blocks.Count(block => block.Within != "the page column")} of them inside a card{Environment.NewLine}");
                        await File.WriteAllBytesAsync(Path.Combine(shots, $"{Slug(page)}-{window.Name}-{(dark ? "dark" : "light")}.png"), await browser.ScreenshotAsync(window));
                    }
                }
            }
        }

        Assert.True(faults.Count == 0, string.Join(Environment.NewLine, faults));
    }

    // From 24.50 a plan says each thing once: the card's sentence and the traded plan's reward line are not drawn in it, the
    // header, the buy condition, the price line's caption and why it is here saying their facts; the stock's own page, which
    // counts as a plan view, keeps its full card.
    // see: A plan view says each thing once, the card's sentence and the traded plan's reward line left to the parts above that say them
    [Fact]
    public async Task APlanSaysEachThingOnceItsCardsSentenceAndRewardLineLeftToThePartsAboveThatSayThem()
    {
        using var store = await FamilyPagesStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?name=F3"));
        var opened = System.Text.RegularExpressions.Regex.Match(page, "<article class=\"pl-plan[^\"]*\" id=\"[^\"]*\" data-plan=\"F3\"");

        Assert.True(opened.Success, "F3's plan is not drawn.");

        var plan = page[opened.Index..page.IndexOf("</article>", opened.Index, StringComparison.Ordinal)];
        Assert.DoesNotContain("Picked by the", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("pp-said", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("The plan it trades:", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("You risk $1 to make", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("card-break-even", plan, StringComparison.Ordinal);

        // Each fact the sentence said stands above the tabs: the setup in the header, the buy in the buy condition, and what the
        // setup buys in why it is here; the reward against the risk in the price line's caption.
        Assert.Contains("<span class=\"pp-fam\">", plan, StringComparison.Ordinal);
        Assert.Contains("<p class=\"pp-cond\">Buys at the next open, at whatever price it opens.", plan, StringComparison.Ordinal);
        Assert.Contains("<b>Why it's here.</b>", plan, StringComparison.Ordinal);
        Assert.Matches("<figcaption data-reward-to-risk=\"2.50\">From the last close, the stop is \\$4.00 down and the take-profit \\$10.00 up: 2.50 times the risk.", plan);
        Assert.True(plan.IndexOf("<b>Why it's here.</b>", StringComparison.Ordinal) < plan.IndexOf("<nav class=\"pp-tabs\"", StringComparison.Ordinal));

        // The card drawn anywhere else keeps its sentence first.
        var card = System.Net.WebUtility.HtmlDecode(new EquityBrief.Web.Marks.MarkRenderer().DecisionCard(new EquityBrief.Web.Marks.DecisionCardView("GSPC", new DateOnly(2026, 10, 2), "pullback", "F3", "the pullback rule", 100m, 96m, 110m, [], null)));

        Assert.Contains("<p class=\"card-summary\" data-summary>Picked by the", card, StringComparison.Ordinal);
    }

    static string Slug(string page) => new([.. page.ToLowerInvariant().Select(one => char.IsLetterOrDigit(one) ? one : '-')]);

    // The rule by hand over constructed placements: a block a pixel off passes, two off is named, a running paragraph held short
    // passes where one wider does not, and a page wider than its window is named.
    [Fact]
    public void TheColumnRuleNamesABlockOffItsEdgesAndLetsARunningParagraphStopShort()
    {
        PageColumn.Placed Block(string name, double left, double right, bool reading = false) => new(name, "the page column", left, right, 20, 1420, reading);

        Assert.Empty(PageColumn.Faults("Tonight", new(1440, 1440, [Block("nav.folder-tabs", 20, 1420), Block("section.tonight", 19.2, 1420.8)])));
        Assert.Equal(
            ["Tonight: nav.folder-tabs in the page column runs 40 to 1420, where its edges are 20 to 1420"],
            PageColumn.Faults("Tonight", new(1440, 1440, [Block("nav.folder-tabs", 40, 1420)])));
        Assert.Equal(
            ["Tonight: div.decision-card in the page column runs 20 to 1348, where its edges are 20 to 1420"],
            PageColumn.Faults("Tonight", new(1440, 1440, [Block("div.decision-card", 20, 1348)])));
        Assert.Empty(PageColumn.Faults("Tonight", new(1440, 1440, [Block("p.lede", 20, 900, reading: true)])));
        Assert.Single(PageColumn.Faults("Tonight", new(1440, 1440, [Block("p.lede", 24, 900, reading: true)])));
        Assert.Single(PageColumn.Faults("Tonight", new(1440, 1440, [Block("p.lede", 20, 1430, reading: true)])));
        // A table in a box scrolling sideways may run wider than the box, never narrower and never off its left edge.
        Assert.Empty(PageColumn.Faults("Tonight", new(390, 390, [new("table.list-table", "section.card", 31, 851, 31, 359, false, Scrolls: true)])));
        Assert.Single(PageColumn.Faults("Tonight", new(390, 390, [new("table.list-table", "section.card", 31, 300, 31, 359, false, Scrolls: true)])));
        Assert.Single(PageColumn.Faults("Tonight", new(390, 390, [new("table.list-table", "section.card", 40, 851, 31, 359, false, Scrolls: true)])));
        Assert.Equal(["Tonight: the page runs 1500 px wide in a window of 1440 px"], PageColumn.Faults("Tonight", new(1440, 1500, [Block("section.tonight", 20, 1420)])));
        Assert.Equal(["Tonight: the page runs 1500 px wide in a window of 1440 px, past it span.tag to 1500"], PageColumn.Faults("Tonight", new(1440, 1500, [Block("section.tonight", 20, 1420)], ["span.tag to 1500"])));
        Assert.Equal(["Tonight: the script read no block"], PageColumn.Faults("Tonight", new(1440, 1440, [])));
    }
}
