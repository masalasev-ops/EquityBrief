using System.Text.RegularExpressions;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 22.16 correction of 2026-11-09's next: the Fundamentals family's card on Home draws the mockup's parts in its order, its
// buy price, how it sells and that it aims at no fixed target, the record box with the year its filings reach back to, the
// line of its filings' rule at its index's setting and the link to how it sells.
// see: Home draws every pick as a card in Tonight's order, the ideas to buy first and the watched after them with each card saying why
public partial class ReadSurface
{
    [Fact]
    public async Task HomesFundamentalsCardDrawsTheMockupsPartsInItsOrderWithItsRecordBox()
    {
        using var store = UniversesStore();

        ImprovingNightOn(store);

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();
        var page = await client.GetStringAsync("/screens/home?universe=400");
        var card = Read(Regex.Match(page, "<article class=\"home-idea fam-edge\" data-family=\"improving\" data-ticker=\"M1\">.*?</article>", RegexOptions.Singleline).Value);

        // The S&P 400's Fundamentals family's first test year 23.5 measured, 2016, less its three learning years; its rule at the
        // starting setting, 7 in every 9 and 126 trading days. From the operator's order of 2026-11-24 the record box stands once
        // in its setup's header, the card pointing up to it.
        string[] parts =
        [
            "Buy at the next open, at whatever price it opens; last close",
            "Sell when",
            "its filings weaken",
            "Its setup's record is above. See it",
            "No price is set in advance: it sells when a new filing drops its score below 7 in every 9 of the checks that apply, or after 126 trading days.",
            "How it sells",
        ];
        int[] at = [.. parts.Select(part => card.IndexOf(part, StringComparison.Ordinal))];

        Assert.All(parts.Zip(at), one => Assert.True(one.Second >= 0, one.First));
        Assert.Equal(at.Order(), at);

        var header = Read(Regex.Match(page, "<div class=\"idea-setup-h fam-edge\" data-family=\"improving\">.*?class=\"rec-sub\">[^<]*</span></div>", RegexOptions.Singleline).Value);

        Assert.Contains("This setup's record on the S&P 400", header, StringComparison.Ordinal);
        Assert.Contains("No long record yet: the walk that gives the other setups their record from 2019 replays prices alone, and this setup buys on filings, which reach back only to 2013. Its own test over the long history judges it from 2016, on the Improvement loop.", header, StringComparison.Ordinal);
    }
}
