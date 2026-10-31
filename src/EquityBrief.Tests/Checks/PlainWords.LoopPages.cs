using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// plain-words, 21.13: Paper trading, the Loop page and the Ledger join the pages the check covers. Every card of each,
// drawn over constructed stores, speaks in the reader's words and opens on its summary sentence; one listed term planted
// in a card of each page is found; and every term of the word list is one the pages' table of phrases puts into the
// reader's words, so no stored word or rule's own words can carry one to the screen.
// see: Paper trading, the Loop page and the Ledger draw every stored word through one table of phrases into the reader's words
public partial class PlainWords
{
    static readonly string[] LoopPages = [PaperPage, LoopPage, LedgerPage];

    [Fact]
    public async Task PaperTradingTheLoopPageAndTheLedgerSpeakInTheReadersWordsOnEveryCard()
    {
        var pages = await Reading.ReadSurface.LoopPagesForPlainWords();
        var terms = Terms();

        foreach (var name in LoopPages)
        {
            var own = pages.Where(page => page.Page == name).ToArray();

            Assert.NotEmpty(own);

            foreach (var (_, drawn, html) in own)
            {
                var faults = FaultsOn(name, html, terms);

                Assert.True(faults.Count == 0, drawn + ":\n" + string.Join("\n", faults));
            }

            // Every card region the page's row names is drawn on one of its pages, so none goes unread.
            Assert.All(RegionsOf(name), region => Assert.Contains(own, page => page.Html.Contains($"data-card=\"{region}\"", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task ATermPlantedInACardOfPaperTradingTheLoopPageOrTheLedgerIsFound()
    {
        var pages = await Reading.ReadSurface.LoopPagesForPlainWords();
        var terms = Terms();
        const string Summary = "class=\"card-summary\" data-summary>";

        foreach (var name in LoopPages)
        {
            var html = pages.First(page => page.Page == name && page.Html.Contains(Summary, StringComparison.Ordinal)).Html;
            var at = html.IndexOf(Summary, StringComparison.Ordinal) + Summary.Length;
            var planted = html[..at] + "The proposal " + html[at..];

            Assert.Empty(FaultsOn(name, html, terms));
            Assert.Contains(FaultsOn(name, planted, terms), fault => fault.StartsWith("'proposal' in the card", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryTermOfTheWordListIsPutIntoTheReadersWordsByThePagesPhrases()
    {
        foreach (var term in Terms())
        {
            foreach (var said in new[] { term, "Read " + term + " here." })
            {
                Assert.DoesNotMatch(Pattern(term), ReaderText.Words(said));
            }
        }

        // A ticker written in capitals stands as it is, and a sentence holding no term is left alone.
        Assert.Equal("HELD rose and BOOK fell.", ReaderText.Words("HELD rose and BOOK fell."));
        Assert.Equal("Bought at the open after the night.", ReaderText.Words("Bought at the open after the night."));

        // The phrases keep a term's meaning where it has two, and read figures in the reader's words.
        Assert.Equal("Above the top of its tenth 3.", ReaderText.Words("Above the upper edge of its tenth 3."));
        Assert.Equal("The rule today -$3.59 per $1 risked over 106 trade(s), the change tried +$28.00 per $1 risked over 217 trade(s)", ReaderText.Words("The rule today -3.59 risks over 106 trade(s), the proposal +28.00 risks over 217 trade(s)"));
        Assert.Equal("Could this be luck: a chance of 0.1752 over 19 runs of 63 trading days, against the bar of 0.0042.", ReaderText.Words("The gate: an adjusted p-value of 0.1752 over 19 blocks of 63 sessions, against the bar of 0.0042."));
        Assert.Equal("The median trade's best close was 1.27 times its risk above its buy.", ReaderText.Words("The median trade's best close was 1.27 risks above its buy."));

        // Only the text between tags is read: an attribute keeps its stored words.
        Assert.Equal("<p data-words=\"the proposal\">The change tried</p>", ReaderText.Page("<p data-words=\"the proposal\">The proposal</p>"));
    }
}
