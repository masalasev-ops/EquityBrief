using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Harness;

// One box in a figure: what it is called, what it says, and which kind of thing
// the diagram draws it as.
//
// Kind is the modifier after `box` in the class, being src, compute, store,
// serve, research or check, and it is what tells the outside sources in the
// system diagram from the components that read them.
internal sealed record FigureBox(string Name, string Rule, string Kind);

internal sealed record ArchitectureFigure(string Id, string Title, IReadOnlyList<FigureBox> Boxes);

// Reads ARCHITECTURE.html's figures and the boxes each one draws.
//
// This exists because architecture-conformance could not have found what it was
// missing. `ArchitectureTables.In` matches table elements, every figure in the
// document is a div, and `EveryTableInTheDocumentIsPlaced` asserted that every
// table the reader returned was placed. Its completeness was defined by the
// thing it was checking, so four figures and fifty-nine boxes were unread and
// nothing could say so.
//
// That is the shrinking-population defect in its purest form, and this is the
// third place it has appeared: once in a floor set to what a run produced, once
// in a reader whose population was its own output, and here in a placement check
// over the same. The rule the corpus takes from it is that a completeness check
// states the population it is complete over, in a form the population cannot
// change.
//
// So the population here is the document's own count of `class="fig"` openings,
// asserted against the number of figures parsed, rather than the parse being
// asked how many figures there were.
internal static class ArchitectureFigures
{
    internal static IReadOnlyList<ArchitectureFigure> In(string document)
    {
        var figures = new List<ArchitectureFigure>();

        foreach (Match opening in Regex.Matches(document, "<div class=\"fig\">"))
        {
            var body = document[opening.Index..Close(document, opening.Index)];
            var title = Text(Regex.Match(body, "<div class=\"title\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value);

            var id = Regex.Match(title, @"^Figure\s+(\d+\.\d+)");

            if (!id.Success)
            {
                throw new InvalidOperationException(
                    $"A figure's title does not open by naming it: '{title}'. The id is what a " +
                    "placement is keyed on, so a figure without one cannot be placed.");
            }

            figures.Add(new ArchitectureFigure(
                $"Figure {id.Groups[1].Value}",
                title,
                [.. Regex
                    .Matches(body, "<div class=\"box([^\"]*)\"><b>(.*?)</b>(.*?)</div>", RegexOptions.Singleline)
                    .Select(box => new FigureBox(
                        Text(box.Groups[2].Value),
                        Text(box.Groups[3].Value),
                        Text(box.Groups[1].Value)))]));
        }

        // The population, stated against the document rather than against this
        // reader's own output. A parse that silently stopped early would
        // otherwise report a smaller document, which is the defect the class
        // comment describes.
        var openings = Regex.Matches(document, "<div class=\"fig\">").Count;

        if (figures.Count != openings)
        {
            throw new InvalidOperationException(
                $"The document opens {openings} figures and {figures.Count} were read. A figure " +
                "the reader lost is a figure nothing places.");
        }

        if (figures.Count == 0)
        {
            throw new InvalidOperationException(
                "No figures were found in the architecture. Reporting zero claims from a document " +
                "that could not be parsed is the failure this guard exists to prevent.");
        }

        return figures;
    }

    // The end of the div that starts at `opening`, found by counting rather than
    // by matching the first close: a figure holds rows, bands and boxes, all of
    // them divs.
    static int Close(string document, int opening)
    {
        var depth = 0;

        foreach (Match tag in Regex.Matches(document[opening..], @"<div\b|</div>"))
        {
            depth += tag.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;

            if (depth == 0)
            {
                return opening + tag.Index + tag.Length;
            }
        }

        throw new InvalidOperationException(
            $"A figure opening at character {opening} is never closed, so its boxes cannot be read.");
    }

    static string Text(string markup) =>
        WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(markup, "<br[^>]*>", " "), "<[^>]+>", string.Empty))
            .Replace('\n', ' ')
            .Trim();
}
