using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// news-parse. The news feed's payload is read as the provider sends it.
//
// The parser was written against a captured response, which is the obligation
// 1.2 filed against this checkpoint after finding a membership parser that read
// a field the provider does not send and a fixture that agreed with it for two
// checkpoints because the same session wrote both.
//
// What this check does not do is assert the coverage measurement. That is a
// measurement of the provider on one fortnight, not a property of this code,
// and asserting it here would turn a recorded observation into a test that
// fails when the news does. It is recorded in PROGRESS with its sample named.
public class NewsCoverage
{
    const string Fixture = "membership-2026-09-05";

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static IReadOnlyList<NewsArticle> Captured() =>
        RecordedNewsFeed.Parse(File.ReadAllText(
            Directory.GetFiles(FixtureFolder(), RecordedNewsFeed.FilePrefix + "*.json").Single()));

    [Fact]
    public void TheCapturedArticlesCarryEverythingAClaimWouldRestOn()
    {
        var articles = Captured();

        Assert.True(articles.Count >= 4, $"The fixture holds {articles.Count} articles, expected at least 4.");

        // The text arrives with the row. That is what makes a source document
        // storable without a second fetch, and the measurement found it true of
        // every one of 1,253 articles.
        Assert.All(articles, article => Assert.False(string.IsNullOrWhiteSpace(article.Text)));
        Assert.All(articles, article => Assert.False(string.IsNullOrWhiteSpace(article.Title)));
        Assert.All(articles, article => Assert.NotEmpty(article.Symbols));
    }

    [Fact]
    public void AnArticleIsAttributedToEveryNameItNames()
    {
        // One request a night is only possible because the row says which names
        // it is about. An article naming five tickers is one article for each
        // of them, which is what a per-name count has to be built on.
        var articles = Captured();
        var multi = articles.Where(article => article.Symbols.Count > 1).ToArray();

        Assert.NotEmpty(multi);
        Assert.All(articles, article => Assert.All(
            article.Symbols,
            symbol => Assert.DoesNotContain('.', symbol)));
    }

    [Fact]
    public void ThePublishedInstantKeepsItsTimeAndNotOnlyItsDate()
    {
        // The provider sends an offset instant. Rounded to a date at the parse
        // it would land on the wrong session for anything published in the
        // evening, which is when a great deal of company news is published.
        var articles = Captured();

        Assert.Contains(articles, article => article.Published.TimeOfDay != TimeSpan.Zero);
        Assert.All(articles, article => Assert.Equal(TimeSpan.Zero, article.Published.Offset));
    }

    [Fact]
    public void TheChannelIsTheDomainAndIsNotClaimedToBeThePublisher()
    {
        // The 1.7 measurement's finding, kept where the code that could forget
        // it lives. 1,173 of 1,253 articles arrived under one domain, so a
        // publisher list keyed on this field would have three entries and would
        // filter almost nothing.
        Assert.Equal("finance.yahoo.com", RecordedNewsFeed.Channel("https://finance.yahoo.com/x/y.html"));
        Assert.Equal("seekingalpha.com", RecordedNewsFeed.Channel("https://www.seekingalpha.com/a"));
        Assert.Equal(string.Empty, RecordedNewsFeed.Channel("not a url"));

        Assert.All(Captured(), article => Assert.False(string.IsNullOrWhiteSpace(article.Channel)));
    }

    [Fact]
    public async Task OneRequestServesTheWindow()
    {
        var feed = RecordedNewsFeed.FromFolder(FixtureFolder());

        var inside = await feed.ArticlesAsync(new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 8));
        var outside = await feed.ArticlesAsync(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 2));

        Assert.NotEmpty(inside);
        Assert.Empty(outside);
        Assert.Equal(2, feed.Requests);
    }

    [Fact]
    public void APayloadThatCannotBeParsedFailsRatherThanReadingAsAQuietFortnight()
    {
        var refusal = Assert.Throws<FormatException>(() => RecordedNewsFeed.Parse("{}"));

        Assert.Contains("quiet fortnight", refusal.Message, StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => RecordedNewsFeed.Parse(
            """[{"title":"x","link":"https://a.b","content":"y","symbols":["AAPL.US"]}]"""));
    }
}
