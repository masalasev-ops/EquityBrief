using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 23.10: on the operator's ruling of 2026-11-16 a book's own exit read back off the rendered page beside its
// live book: Quality at a fair price frozen on the S&P 400 of the universes' store draws the fold walked from the first night
// after its registration, a record that draws no pick, its holdings and its positions' change beside the live book's; and
// the provisional card, with no live rule, draws none.
// see: Each book's own exit is walked beside its live book as a record on its card, and registers no version and draws no pick
public partial class ReadSurface
{
    // The row 23.10's replay fix's carried pull request adds that this check reaches: a book's own exit on Tonight.
    internal static string[] OwnExitRows =>
    [
        CheckReach.Key("15.7 Tonight", "A book's own exit"),
    ];

    [Fact]
    public async Task AFrozenBooksCardDrawsItsOwnExitBesideItsLiveBookAsARecordThatDrawsNoPick()
    {
        using var store = await FrozenQualityStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-quality\".*?</section></div></section>"));
        var fold = Regex.Match(section, "<details class=\"fold own-exit\".*?</details>", RegexOptions.Singleline).Value;

        Assert.NotEqual(string.Empty, fold);

        // On the operator's answers of 2026-11-16 the line above the fold, read without opening it, the two equal on the
        // walk's first nights.
        Assert.Matches("<p class=\"record-said own-exit-line\" data-through=\"[0-9-]+\">Sold the old way, with no stop and no take-profit, these holdings would have made (-?[0-9.]+) points a month against \\1 as they trade now, since 1 Oct 2026\\.</p>", section);
        Assert.Contains($"<summary>{MarkRenderer.OwnExitHeading}</summary>", fold, StringComparison.Ordinal);
        Assert.Contains("draws no pick and cannot become a revision as it stands; if it leads after its first 12 months, it is brought to you.", fold, StringComparison.Ordinal);
        Assert.Contains("with no stop and no take-profit", fold, StringComparison.Ordinal);

        // Walked from 1 Oct, the night after the freeze: the reshuffle chose M1 and M2 as the live book did, and with no stop
        // in this store the own exit's two positions are the live book's, their mean change the same.
        Assert.Contains("It holds 2 tonight: M1, M2.", fold, StringComparison.Ordinal);
        Assert.Contains("Since 1 Oct 2026 its 2 position(s) moved", fold, StringComparison.Ordinal);
        Assert.Contains("over the running revision's 2.", fold, StringComparison.Ordinal);

        var own = Regex.Match(fold, "data-own-mean=\"([^\"]+)\"").Groups[1].Value;
        var live = Regex.Match(fold, "data-live-mean=\"([^\"]+)\"").Groups[1].Value;

        Assert.NotEqual("none", own);
        Assert.Equal(own, live);

        // It draws no pick: the card's holdings table and its buys are the live book's alone.
        Assert.Equal(["M1", "M2"], Regex.Matches(section, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
    }

    [Fact]
    public async Task AProvisionalBooksCardDrawsNoOwnExit()
    {
        using var store = UniversesStore();

        QualityNightsOn(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-quality\".*?</section></div></section>"));

        Assert.DoesNotContain("own-exit", section, StringComparison.Ordinal);
    }
}
