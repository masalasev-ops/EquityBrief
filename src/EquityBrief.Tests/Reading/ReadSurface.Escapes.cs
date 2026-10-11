using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 6.8 correction of 2026-10-11: a document's title and link, and an article's link, sent with characters
// written as entities are drawn as those characters escaped once, where they were escaped again: BDX's page drew
// "Becton Dickinson &amp; Co's Dividend Analysis" in its sources, and 626 links across the pages carried
// "&amp;amp;utm_medium", an address the provider never wrote.
public partial class ReadSurface
{
    [Fact]
    public void ADocumentsTitleAndLinkAndAnArticlesLinkSentWithEntitiesAreDrawnEscapedOnce()
    {
        var marks = new MarkRenderer();
        SourceCell document = new(
            "d1",
            "Becton Dickinson &amp; Co's Dividend Analysis",
            "https://www.example.invalid/news/1?utm_source=feed&amp;utm_medium=referral",
            new DateOnly(2026, 9, 25));

        // The Sources region and each written section's list draw a document by one link.
        var sources = marks.Sources("BDX", [document], [], filedFactsHeld: true);

        Assert.Contains("data-url=\"https://www.example.invalid/news/1?utm_source=feed&amp;utm_medium=referral\"", sources, StringComparison.Ordinal);
        Assert.Contains(
            "<a href=\"https://www.example.invalid/news/1?utm_source=feed&amp;utm_medium=referral\">Becton Dickinson &amp; Co's Dividend Analysis</a>",
            sources,
            StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;amp;", sources, StringComparison.Ordinal);

        // An article's link in the News region, beside its title drawn so since the 12.6 correction of the same day.
        var region = marks.NewsRegion(new NewsView(
            "CVX",
            new DateOnly(2026, 10, 9),
            [new NewsRow("a1", "A title", "wire.example", "2026-10-09", "https://example.invalid/a?x=1&amp;y=2", NewsRow.Unlabelled, null, null, null, null, null, "all unlabelled")],
            1, 0, 0, 0, 0, 1, null, null, null, null));

        Assert.Contains("href=\"https://example.invalid/a?x=1&amp;y=2\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;amp;", region, StringComparison.Ordinal);
    }
}
