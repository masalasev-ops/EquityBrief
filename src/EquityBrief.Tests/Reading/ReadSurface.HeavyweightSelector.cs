using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.5: the sector heavyweights' card gains the selector the swing cards carry, the live rule first and
// each registered variant by its number, a variant chosen in the link redrawing the card from the rule's own book under
// the band with its words, the default and a link naming no standing rule returning to the live rule with no band; on
// the S&P 400, where no freeze registered a rule, the provisional rule alone with the line that none is registered.
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public partial class ReadSurface
{
    // The row of section 15.7 this check reaches, which is every row 17.5's selector adds.
    internal static readonly string[] HeavyweightSelectorClaims =
    [
        CheckReach.Key("15.7 Tonight", "The sector heavyweights' card's rule"),
    ];

    internal static string[] HeavyweightSelectorRows => [.. HeavyweightSelectorClaims];

    [Fact]
    public async Task TheHeavyweightsCardsSelectorDrawsAChosenVariantsOwnBookUnderTheBandAndTheDefaultIsTheLiveRule()
    {
        using var store = await FamilyNightStore();

        var live = TheSetupFamilies.Heavyweights[0].Candidate;
        var bothExits = TheSetupFamilies.Heavyweights[1].Candidate;

        for (var at = 0; at < TheSetupFamilies.Heavyweights.Count; at++)
        {
            RegisteredRule(store, 201 + at, TheSetupFamilies.Heavyweights[at], "2026-01-02T12:00:00Z");
        }

        // The variant selling on either exit holds H5 at the night's close in its own book; the live rule's book holds
        // H3.
        HeldByRule(store, live, "H3", "2026-09-01", null, null, null);
        HeldByRule(store, bothExits, "H5", "2026-09-01", null, null, null);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // By default the live rule, first among the choices and selected, no band, and the variants by their numbers.
        var page = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")));

        Assert.Contains($"data-rule=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\"", page, StringComparison.Ordinal);
        // From 22.6 the Showing list: the live setup first and selected, then each version named by what it changes, its
        // full name under the pointer.
        Assert.Contains($"<div class=\"rule-view\"><label for=\"rule-heavyweight\">Showing</label> <select id=\"rule-heavyweight\" class=\"rule-select\" data-rule-choice=\"heavyweight\" aria-label=\"The version this card is drawn by\"><option value=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\" title=\"{live}\" selected>The live setup</option><option value=\"{RunScreen.Slug(bothExits)}\" data-variant=\"1\" title=\"{bothExits}\">Version: sold on either exit</option>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-band", page, StringComparison.Ordinal);

        // The variant chosen in the link: its own book, H5 and not H3, under the band naming it.
        var chosen = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?heavyweight={RunScreen.Slug(bothExits)}")));

        Assert.Contains($"data-rule=\"{RunScreen.Slug(bothExits)}\" data-variant=\"1\"", chosen, StringComparison.Ordinal);
        Assert.Contains($"<option value=\"{RunScreen.Slug(bothExits)}\" data-variant=\"1\" title=\"{bothExits}\" selected>Version: sold on either exit</option>", chosen, StringComparison.Ordinal);
        Assert.Contains("<div class=\"variant-band\" role=\"status\" data-variant=\"1\"><p class=\"band-head\">A version being tested, not tonight's holdings</p><p>Version: sold on either exit</p><p class=\"band-note\">It runs every night beside the live setup with its own paper account. It replaces the live setup only if it proves better and you approve it on the Loop page.</p></div>", chosen, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"H5\"", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("<tr data-ticker=\"H3\"", chosen, StringComparison.Ordinal);

        // Its words with the clause the live rule's lack marked and its exit's chip marked, on either; the live rule's
        // card marks none. Beside the holdings the card draws by default, the page's book at the live setting, read off the
        // store, it shares none of them and adds H5.
        Assert.Contains("<mark class=\"differs\">", chosen, StringComparison.Ordinal);
        Assert.Contains("<li class=\"rule-chip differs\">sold on either exit</li>", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("<mark class=\"differs\">", page, StringComparison.Ordinal);
        Assert.DoesNotContain("rule-chip differs", page, StringComparison.Ordinal);

        var pageBook = Strings(store, $"SELECT ticker FROM heavyweight_holding WHERE entered_on <= '{TheSwitch}' AND (ended_on IS NULL OR ended_on > '{TheSwitch}') ORDER BY sector, ticker;");

        Assert.DoesNotContain("H5", pageBook);
        Assert.Contains($"<p class=\"rule-share\" data-shared=\"\" data-added=\"H5\" data-dropped=\"{string.Join(",", pageBook)}\">Beside the live setup's holdings tonight, it shares none, adds H5 and drops {(pageBook.Count == 0 ? "none" : string.Join(", ", pageBook))}.</p>", chosen, StringComparison.Ordinal);

        // A link naming no standing rule returns to the live rule.
        var unknown = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?heavyweight=no-such-rule")));

        Assert.Contains($"data-rule=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\"", unknown, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-band", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSAndP400HeavyweightsSelectorListsTheProvisionalRuleAloneWithTheLineThatNoVariantIsRegisteredBeforeItsFreeze()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")));

        Assert.Contains("<select id=\"rule-heavyweight\" class=\"rule-select\" data-rule-choice=\"heavyweight\"", card, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(card, "<option "));
        Assert.Contains($"<option value=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\" title=\"{RuleScreen.ProvisionalChoice}\" selected>The setup as it runs, not yet tested</option>", card, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"no-variant\" data-no-variant=\"true\">{ReaderWords.Plain(RuleScreen.NoVariantLine("S&P 400"))}</p>", card, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-band", card, StringComparison.Ordinal);
    }
}
