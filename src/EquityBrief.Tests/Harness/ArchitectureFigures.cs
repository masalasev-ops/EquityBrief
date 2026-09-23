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
// A completeness check states the population it is complete over in a form the
// population cannot change. A reader whose population is its own output cannot
// report what it failed to read, so the opening pattern below names the
// population and the loop reads it, and the two cannot drift apart.
//
// The document draws a figure in one of two forms and both are figures: a box
// figure, whose rows of boxes state rules a placement can send to a check, and a
// drawn figure, which carries one picture and names itself in its caption. A
// drawn figure has no box to read, and reading it for none is what lets a
// placement say so; a reader that matched the box form alone would leave the
// drawn ones out of the population that reports them missing.
internal static class ArchitectureFigures
{
    // The two forms a figure opens in. One pattern, because it is both what the
    // loop reads and what the population is counted from.
    const string Openings = @"<div class=""fig"">|<figure class=""fig svgfig"">";

    internal static IReadOnlyList<ArchitectureFigure> In(string document)
    {
        var figures = new List<ArchitectureFigure>();

        foreach (Match opening in Regex.Matches(document, Openings))
        {
            var drawn = opening.Value.StartsWith("<figure", StringComparison.Ordinal);

            var body = drawn
                ? document[opening.Index..CloseDrawn(document, opening.Index)]
                : document[opening.Index..Close(document, opening.Index)];

            var title = Text(drawn
                ? Regex.Match(body, "<figcaption>(.*?)</figcaption>", RegexOptions.Singleline).Groups[1].Value
                : Regex.Match(body, "<div class=\"title\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value);

            figures.Add(new ArchitectureFigure(
                Id(title),
                title,
                drawn
                    ? []
                    : [.. Regex
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
        var openings = Regex.Matches(document, Openings).Count;

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

    // A figure's id, taken from the words its title opens with. The id is what a
    // placement is keyed on, so a figure that does not name itself cannot be
    // placed and is refused rather than read under a name of the reader's own.
    static string Id(string title)
    {
        var id = Regex.Match(title, @"^Figure\s+(\d+\.\d+)");

        if (!id.Success)
        {
            throw new InvalidOperationException(
                $"A figure's title does not open by naming it: '{title}'. The id is what a " +
                "placement is keyed on, so a figure without one cannot be placed.");
        }

        return $"Figure {id.Groups[1].Value}";
    }

    // The end of the drawn figure that starts at `opening`. A figure element
    // holds no figure of its own, so the first close is its own.
    static int CloseDrawn(string document, int opening)
    {
        var close = document.IndexOf("</figure>", opening, StringComparison.Ordinal);

        if (close < 0)
        {
            throw new InvalidOperationException(
                $"A figure opening at character {opening} is never closed, so its caption cannot be read.");
        }

        return close + "</figure>".Length;
    }

    // The end of the div that starts at `opening`, found by counting rather than
    // by matching the first close: a figure holds rows, bands and boxes, all of
    // them divs.
    internal static int Close(string document, int opening)
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
