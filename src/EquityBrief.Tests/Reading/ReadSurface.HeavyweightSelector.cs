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
// see: A variant's picks are shown on its card when chosen and its results only under its tests
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
        Assert.Contains($"<select data-rule-choice=\"heavyweight\" aria-label=\"The rule this card is drawn by\"><option value=\"{RuleScreenWords.LiveSlug}\" selected data-variant=\"none\">{live}</option><option value=\"{RunScreen.Slug(bothExits)}\" data-variant=\"1\">Variant 1: {bothExits}</option>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-band", page, StringComparison.Ordinal);

        // The variant chosen in the link: its own book, H5 and not H3, under the band naming it.
        var chosen = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?heavyweight={RunScreen.Slug(bothExits)}")));

        Assert.Contains($"data-rule=\"{RunScreen.Slug(bothExits)}\" data-variant=\"1\"", chosen, StringComparison.Ordinal);
        Assert.Contains($"<option value=\"{RunScreen.Slug(bothExits)}\" selected data-variant=\"1\">Variant 1: {bothExits}</option>", chosen, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"variant-band\" role=\"status\" data-variant=\"1\">{RuleScreenWords.Band(1)}</p>", chosen, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"H5\"", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("<tr data-ticker=\"H3\"", chosen, StringComparison.Ordinal);

        // Its words with the clause the live rule's lack marked, its exit on either; the live rule's card marks none.
        Assert.Contains("<mark class=\"differs\">", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("<mark class=\"differs\">", page, StringComparison.Ordinal);

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

        Assert.Contains("<select data-rule-choice=\"heavyweight\"", card, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(card, "<option "));
        Assert.Contains($"<option value=\"{RuleScreenWords.LiveSlug}\" selected data-variant=\"none\">{RuleScreen.ProvisionalChoice}</option>", card, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"no-variant\" data-no-variant=\"true\">{RuleScreen.NoVariantLine("S&P 400")}</span>", card, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-band", card, StringComparison.Ordinal);
    }
}
