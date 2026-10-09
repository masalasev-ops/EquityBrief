using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 17.10: each of the loop's four figures has a key naming every box it draws, in the
// architecture and in the guide, a box read off the figure's own drawing by its title and found in the key's words;
// the reader is shown to find a key missing a box. And the guide's worked examples of the loop hold placeholders and no
// figure, a digit standing in none of them but in an index's name.
// see: The monthly run puts at most one proposal a family an index to the operator, from a clean copy of main's commit on the first Saturday of the month
public partial class ArchitectureConformance
{
    // The architecture's figures by caption: the nightly half, the test years, the monthly half and one month across the
    // indices.
    static readonly string[] LoopFigures = ["Figure 13.3.", "Figure 13.4.", "Figure 13.5.", "Figure 13.7."];

    // The guide's drawings of the same four, by the id their titles carry.
    static readonly string[] GuideLoopFigures = ["f-loop-night", "f-loop-windows", "f-loop-month", "f-loop-indices"];

    [Fact]
    public void EachLoopFiguresKeyNamesEveryBoxItDrawsInTheArchitectureAndTheGuide()
    {
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        foreach (var caption in LoopFigures)
        {
            var (boxes, key) = ArchitectureFigure(architecture, caption);

            Assert.True(boxes.Count >= 5, $"{caption} draws {boxes.Count} titled boxes, expected at least 5.");
            Assert.True(Unnamed(boxes, key).Count == 0, $"{caption}'s key does not name: {string.Join("; ", Unnamed(boxes, key))}");
        }

        foreach (var id in GuideLoopFigures)
        {
            var (boxes, key) = GuideFigure(guide, id);

            Assert.True(boxes.Count >= 5, $"The guide's {id} draws {boxes.Count} titled boxes, expected at least 5.");
            Assert.True(Unnamed(boxes, key).Count == 0, $"The guide's {id} key does not name: {string.Join("; ", Unnamed(boxes, key))}");
        }

        // The reader finds a key missing a box: the operator's own box taken out of figure 13.7's key.
        var (drawn, keyed) = ArchitectureFigure(architecture, "Figure 13.7.");

        Assert.Equal(["Your word"], Unnamed(drawn, Regex.Replace(keyed, "your word", "the operator's say", RegexOptions.IgnoreCase)));

        // The worked examples: three, holding the placeholders and no figure but an index's name.
        var examples = Regex.Matches(guide, "<div class=\"why\" data-example=\"[a-z-]+\">(.*?)</div>", RegexOptions.Singleline)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .ToArray();

        Assert.Equal(3, examples.Length);
        Assert.All(examples, example => Assert.DoesNotMatch(@"\d", Regex.Replace(example, @"S&P [456]00", string.Empty)));
        Assert.Contains(examples, example => example.Contains("n.n", StringComparison.Ordinal));
        Assert.Contains(examples, example => example.Contains("nn%", StringComparison.Ordinal));
    }

    // The boxes a key does not name, each box's title looked for in the key's words with the markup taken out.
    static IReadOnlyList<string> Unnamed(IReadOnlyList<string> boxes, string key)
    {
        var words = Plain(key);

        return [.. boxes.Distinct(StringComparer.Ordinal).Where(box => !words.Contains(Plain(box), StringComparison.Ordinal))];
    }

    static string Plain(string text) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " ")), @"\s+", " ").Trim().ToLowerInvariant();

    // An architecture figure's titled boxes, the texts it draws in the ink colour, and its key, the paragraph after its
    // caption opening with the word.
    static (IReadOnlyList<string> Boxes, string Key) ArchitectureFigure(string document, string caption)
    {
        var at = document.IndexOf(caption, StringComparison.Ordinal);

        Assert.True(at >= 0, $"The architecture holds no {caption}");

        var start = document.LastIndexOf("<figure", at, StringComparison.Ordinal);
        var drawing = document[start..document.IndexOf("</svg>", start, StringComparison.Ordinal)];
        var opens = document.IndexOf("<p><b>Key.</b>", at, StringComparison.Ordinal);

        Assert.True(opens >= 0, $"No key follows {caption}");

        var boxes = Regex.Matches(drawing, "<text[^>]*fill=\"var\\(--ink\\)\"[^>]*>([^<]+)</text>")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value).Trim())
            .ToArray();

        return (boxes, document[opens..document.IndexOf("</p>", opens, StringComparison.Ordinal)]);
    }

    // A guide figure's titled boxes, the texts it sets as titles, and its key, the block after the drawing.
    static (IReadOnlyList<string> Boxes, string Key) GuideFigure(string document, string id)
    {
        var at = document.IndexOf($"aria-labelledby=\"{id}\"", StringComparison.Ordinal);

        Assert.True(at >= 0, $"The guide draws no figure {id}.");

        var drawing = document[at..document.IndexOf("</svg>", at, StringComparison.Ordinal)];
        var opens = document.IndexOf("<div class=\"key\">", at, StringComparison.Ordinal);
        var boxes = Regex.Matches(drawing, "<text class=\"t\"[^>]*>([^<]+)</text>")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value).Trim())
            .ToArray();

        return (boxes, document[opens..document.IndexOf("</div>", opens, StringComparison.Ordinal)]);
    }
}
