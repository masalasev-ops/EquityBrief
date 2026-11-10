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
        // starting setting, 7 in every 9 and 126 trading days.
        string[] parts =
        [
            "Buy at about",
            "Sell when",
            "its filings weaken",
            "Aim to sell at",
            "No fixed target",
            "This setup's record on the S&P 400",
            "No long record yet: the filings this setup reads reach back only to 2013, so its record starts with its live trades.",
            "The rule's record over every stock it bought, not this stock's chance.",
            "No price is set in advance: it sells when a new filing drops its score below 7 in every 9 of the checks that apply, or after 126 trading days.",
            "How it sells",
        ];
        int[] at = [.. parts.Select(part => card.IndexOf(part, StringComparison.Ordinal))];

        Assert.All(parts.Zip(at), one => Assert.True(one.Second >= 0, one.First));
        Assert.Equal(at.Order(), at);
    }
}
