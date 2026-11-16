using System.Text.RegularExpressions;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The read-back of 2026-11-16 over Friday's night run again: a book's stop and take-profit cell ran its prices into their notes
// on the heavyweights' card, "157.554 typical daily moves", and every book's card described its exit in the default's distances
// whatever its index's test chose, 3 moves and 2 times the risk on the S&P 400's book whose rows read 4 and 3.
// see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
public class BookExitCellTests
{
    [Fact]
    public void EachPriceInABooksExitCellAndItsNoteSitOnLinesOfTheirOwn()
    {
        var cell = MarkRenderer.BookExitCell(157.55m, null);

        Assert.Contains("<span class=\"exit-line\">", cell, StringComparison.Ordinal);
        Assert.Contains(".book-exit .exit-line{display:block}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".book-exit .exit-note{display:block;", Stylesheet.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSentenceEveryBooksCardSharesNamesNoDistanceOfItsOwn()
    {
        Assert.DoesNotMatch(new Regex(@"\d"), MarkRenderer.BookExitSaid);
        Assert.Contains(MarkRenderer.BookExitSaid, MarkRenderer.SetupWords[EquityBrief.Core.Families.HeavyweightRule.Name].About, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.BookExitSaid, MarkRenderer.SetupWords[EquityBrief.Core.Families.QualityRule.Name].About, StringComparison.Ordinal);
    }
}
