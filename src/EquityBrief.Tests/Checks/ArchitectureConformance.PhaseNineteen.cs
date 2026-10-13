using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 19.2: Tonight's top's figure's key names every part the figure draws, in the architecture and
// in the guide, the guide's picture numbering the same parts, neither figure's text holding any night's figure.
public partial class ArchitectureConformance
{
    [Fact]
    public void TheTonightTopFiguresKeyNamesEveryPartTheFigureDraws()
    {
        // The architecture's figure 15.3 and the key beneath it, and the guide's same picture and the list beneath it.
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        var (drawn, keyed) = Parts(architecture, "Figure 15.3.", "<div class=\"key\">", "</div>");

        Assert.Equal(Enumerable.Range(1, 6).Select(part => part.ToString(CultureInfo.InvariantCulture)), drawn.Keys);
        Assert.Equal(drawn.Keys, keyed.Keys);
        Assert.All(keyed.Values, words => Assert.True(words.Length > 40, $"A key line says too little to explain a part: '{words}'."));

        var (pictured, listed) = Parts(guide, "Tonight's top, its figures blanked out", "<ol class=\"parts\">", "</ol>");

        Assert.Equal(drawn.Keys, pictured.Keys);
        Assert.Equal(pictured.Keys, listed.Keys);

        // Every figure drawn as a placeholder and never a night's: no digit in either figure's text but a part's own
        // number and the three indices' names.
        foreach (var figure in new[] { Figure(architecture, "Figure 15.3."), Figure(guide, "Tonight's top, its figures blanked out") })
        {
            var words = Regex.Matches(figure, "<text[^>]*>(.*?)</text>").Select(text => Regex.Replace(text.Groups[1].Value, "<[^>]+>", string.Empty))
                .Where(text => !Regex.IsMatch(text, @"^\d$"))
                .Select(text => Regex.Replace(text, @"S&amp;P (500|400|600)", string.Empty));

            Assert.All(words, text => Assert.DoesNotMatch(@"\d", text));
        }
    }
}
