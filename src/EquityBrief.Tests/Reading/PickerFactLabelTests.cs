using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// A fact's label on a picker's card keeps a column of its own however long its value runs, so a long value never squeezes
// the label into a column a letter wide: the facts' table lays its columns out at fixed widths, the label's the widest, on
// a wide window and on a phone.
public class PickerFactLabelTests
{
    [Fact]
    public void AFactsLabelKeepsAColumnOfItsOwnBesideALongValue()
    {
        Assert.Contains(".judge-facts{width:100%;border-collapse:collapse;table-layout:fixed;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".judge-facts col.l{width:42%} .judge-facts col.v{width:20%}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".judge-facts col.l{width:44%} .judge-facts col.v{width:34%}", Stylesheet.Css, StringComparison.Ordinal);
    }
}
