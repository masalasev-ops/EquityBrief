using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// A fact's label on a picker's card keeps a column of its own however long its value runs, so a long value never squeezes
// the label into a column a letter wide.
public class PickerFactLabelTests
{
    [Fact]
    public void AFactsLabelKeepsAColumnOfItsOwnBesideALongValue()
    {
        Assert.Contains(".ai-facts li{display:grid;grid-template-columns:minmax(min(12em,50%),1fr) minmax(0,auto);", Stylesheet.Css, StringComparison.Ordinal);
        Assert.DoesNotContain(".ai-facts li{display:grid;grid-template-columns:minmax(0,1fr)", Stylesheet.Css, StringComparison.Ordinal);
    }
}
