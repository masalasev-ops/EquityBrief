using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// Paper trading and Home's account over the faults the operator's order of 2026-11-07 names, each held to the page as it
// should read against the page as it read on 2026-11-06.
public partial class ReadSurface
{
    static readonly DateOnly OrderNight = new(2026, 11, 6);

    // An open trade of XYZ, whose company the filer names Block, Inc., as What the system bought drew it on 2026-11-06.
    static PaperBoughtView BlockTrade() =>
        new("list-drift", "drift", "XYZ", "Block, Inc.", new(2026, 11, 4), new(2026, 11, 5), 88.29m, 83.26m, 98.56m, false, 60, null, new PaperPosition(85.48m, 85.48m, 83.26m, -0.64, -0.032, 2, PaperAccount.NearStop), null, [], null);

    static string PaperPageOf(PaperPageView page, MarkRenderer? marks = null) =>
        WebUtility.HtmlDecode(new SinglePageApp().PaperRegion(marks ?? new MarkRenderer(), Universes.Large, OrderNight, page, new PaperLink(Universes.Large.Word), "pullback", "pullback", (100_000m, 1m, 0.2m), 0.2m));

    // The page drew "XYZ Run of 63 trading days, Inc" in What the system bought: the reader's words read the filer's "Block"
    // as the tester's word for a run of 63 trading days.
    // see: Paper trading, the Loop page and the Ledger draw every stored word through one table of phrases into the reader's words
    [Fact]
    public void ACompanysNameIsDrawnAsItsFilerWroteItAndNeverInTheReadersWords()
    {
        var page = PaperPageOf(new PaperPageView([], [BlockTrade()], [], [], new PaperNotBought([]), [], null));

        Assert.Contains("<span class=\"pb-co\" data-as-filed>Block, Inc.</span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Run of 63", page, StringComparison.Ordinal);

        // The page's own words are still read: a block outside a filed element is a run, and the words after a filed element
        // closes, its own elements nested inside it however deep, are read again.
        Assert.Equal("a run of 63 trading days", ReaderText.Words("a block"));
        Assert.Equal(
            "<span data-as-filed>Block <b>Edge <b>Gates</b></b> Co</span> its gain over the easy alternative",
            ReaderText.Page("<span data-as-filed>Block <b>Edge <b>Gates</b></b> Co</span> its edge"));
        Assert.Equal("<i data-as-filed/> a run of 63 trading days", ReaderText.Page("<i data-as-filed/> a block"));
    }
}
