using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.1: on the S&P 400 a swing card's selector lists, after the provisional rule, the variant reading no
// market check by its registration's words, and draws no line saying no variant is registered; chosen in the link, the
// card marks the clause naming the check off among those the provisional rule's words lack.
// see: An S&P 400 or 600 swing family may carry a variant with no market check, the check standing on its provisional rule
public partial class ReadSurface
{
    // The card's rule row's part 19.1 adds, which 17.2's own parts leave out.
    internal const string MarketOffPart = "on the S&P 400 or 600 a variant reading no market check listed after the provisional rule by its registration's words, which end on the market check off";

    internal static readonly string[] MarketOffRows =
    [
        CheckReach.Key("15.7 Tonight", "A family's card's rule, " + MarketOffPart),
    ];

    [Fact]
    public async Task TheSAndP400SelectorListsTheVariantReadingNoMarketCheckBesideTheProvisionalRule()
    {
        using var store = UniversesStore();

        var (registrations, refusal) = IndexRules.Variants(
            "breakout",
            "MID",
            [new Dictionary<string, double> { [IndexRuleCandidate.LargeBreadthParameter] = IndexRuleCandidate.NoMarketCheck }],
            [],
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(refusal);

        var variant = Assert.Single(registrations!);

        RegisteredRule(store, 500, variant, "2026-10-01T12:00:00Z");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")), "breakout");

        // The provisional rule first and selected, then the variant by its number and its registration's words, which end
        // on the market check off; no line that no variant is registered.
        Assert.EndsWith(", the " + IndexRuleCandidate.MarketCheckOff, variant.Candidate, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(card, "<option ").Count);
        Assert.Contains($"<option value=\"{RuleScreenWords.LiveSlug}\" selected data-variant=\"none\">{RuleScreen.ProvisionalChoice}</option>", card, StringComparison.Ordinal);
        Assert.Contains($"<option value=\"{RunScreen.Slug(variant.Candidate)}\" data-variant=\"1\">Variant 1: {variant.Candidate}</option>", card, StringComparison.Ordinal);
        Assert.DoesNotContain("data-no-variant", card, StringComparison.Ordinal);

        // Chosen in the link, the card is drawn under the band with the clause naming the check off marked.
        var chosen = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400&breakout={RunScreen.Slug(variant.Candidate)}")), "breakout");

        Assert.Contains($"<option value=\"{RunScreen.Slug(variant.Candidate)}\" selected data-variant=\"1\">", chosen, StringComparison.Ordinal);
        Assert.Contains("the " + IndexRuleCandidate.MarketCheckOff, Marked(chosen));
    }
}
