using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// figure-fits. Every row of a figure in ARCHITECTURE.html fits the column the
// document draws it in, measured from the stylesheet's own numbers.
//
// A figure is a scroll box, so a row wider than its column is clipped rather
// than shown: the boxes past the edge keep their text and no reader sees it.
public class FigureFits
{
    // One row of one figure. Arrows are what tell a sequence from a set of
    // peers, and a band is the dashed frame that takes width off the column.
    internal sealed record FigureRow(string Figure, int Index, int Boxes, int Arrows, bool InBand)
    {
        internal int Items => Boxes + Arrows;

        internal string Where => $"{Figure} row {Index}";
    }

    // The numbers the document is drawn at, read off the document. A literal
    // kept beside the check answers for the stylesheet the check was written
    // against rather than the one in the file.
    internal sealed record Measurements(
        int Column,
        int SidePadding,
        int FigureBorder,
        int FigurePadding,
        int BandBorder,
        int BandPadding,
        int Gap,
        int BoxMinimum,
        int ArrowFontSize,
        int ArrowSidePadding,
        int StackBelow)
    {
        // A glyph's advance does not exceed the size it is set at, so an arrow
        // is bounded by its own font size and padding. The bound sits above what
        // an arrow measures, which is the side that keeps a pass a pass.
        internal int ArrowWidth => ArrowFontSize + (2 * ArrowSidePadding);

        // The narrowest viewport at which rows still hold one line. Below it
        // every row stacks, so this is where a sequence has least room.
        internal int LayoutWidth => Math.Min(Column, StackBelow);
    }

    internal static string StyleBlock(string document)
    {
        var open = document.IndexOf("<style>", StringComparison.Ordinal);
        var close = document.IndexOf("</style>", StringComparison.Ordinal);

        if (open < 0 || close < open)
        {
            throw new InvalidOperationException("ARCHITECTURE.html carries no stylesheet.");
        }

        return document[(open + "<style>".Length)..close];
    }

    // The narrow-width block, taken out before the base rules are read, because
    // the selectors inside it repeat the ones outside and a reader matching
    // either would answer for whichever came first.
    internal static (string Base, string Narrow, int Breakpoint) Split(string stylesheet)
    {
        var opening = Regex.Match(stylesheet, @"@media\s*\(\s*max-width\s*:\s*(\d+)px\s*\)\s*\{");

        if (!opening.Success)
        {
            throw new InvalidOperationException(
                "The stylesheet declares no narrow-width rule, so every row holds one line at " +
                "every width and a sequence longer than the column has no width at which it fits.");
        }

        var depth = 0;
        var index = opening.Index + opening.Length - 1;

        for (; index < stylesheet.Length; index++)
        {
            if (stylesheet[index] == '{')
            {
                depth++;
            }
            else if (stylesheet[index] == '}' && --depth == 0)
            {
                break;
            }
        }

        if (depth != 0)
        {
            throw new InvalidOperationException("The narrow-width rule is never closed.");
        }

        var block = stylesheet[opening.Index..(index + 1)];

        return (stylesheet.Remove(opening.Index, block.Length), block, int.Parse(opening.Groups[1].Value));
    }

    // One declaration out of one rule, refusing rather than defaulting. A number
    // this could not find would be measured as zero, and a column of zero passes
    // nothing while a width of zero passes everything.
    internal static int Number(string stylesheet, string selector, string declaration)
    {
        var rule = Regex.Match(stylesheet, @"(?m)^\s*" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");

        if (!rule.Success)
        {
            throw new InvalidOperationException($"The stylesheet has no rule for `{selector}`.");
        }

        var value = Regex.Match(rule.Groups[1].Value, declaration);

        if (!value.Success)
        {
            throw new InvalidOperationException(
                $"`{selector}` no longer declares what `{declaration}` reads, and the width a row " +
                "is measured against cannot be assumed once the document stops stating it.");
        }

        return int.Parse(value.Groups[1].Value);
    }

    // The second length of a padding shorthand is the side, and a zero carries
    // no unit.
    const string SidePaddingOf = @"padding\s*:\s*\d+(?:px)?\s+(\d+)px";

    internal static Measurements Numbers(string document)
    {
        var (rules, _, breakpoint) = Split(StyleBlock(document));

        return new Measurements(
            Column: Number(rules, "main", @"max-width\s*:\s*(\d+)px"),
            SidePadding: Number(rules, "main", SidePaddingOf),
            FigureBorder: Number(rules, ".fig", @"border\s*:\s*(\d+)px"),
            FigurePadding: Number(rules, ".fig", @"padding\s*:\s*(\d+)px"),
            BandBorder: Number(rules, ".band", @"border\s*:\s*(\d+)px"),
            BandPadding: Number(rules, ".band", @"padding\s*:\s*(\d+)px"),
            Gap: Number(rules, ".row", @"gap\s*:\s*(\d+)px"),
            BoxMinimum: Number(rules, ".box", @"min-width\s*:\s*(\d+)px"),
            ArrowFontSize: Number(rules, ".arrow", @"font-size\s*:\s*(\d+)px"),
            ArrowSidePadding: Number(rules, ".arrow", SidePaddingOf),
            StackBelow: breakpoint);
    }

    // Every row of every box figure, with the band it sits in. The SVG figures
    // carry a drawing rather than boxes and have no row to measure.
    internal static IReadOnlyList<FigureRow> Rows(string document)
    {
        var rows = new List<FigureRow>();

        foreach (Match opening in Regex.Matches(document, "<div class=\"fig\">"))
        {
            var body = document[opening.Index..ArchitectureFigures.Close(document, opening.Index)];

            var title = Regex.Match(body, @"<div class=""title"">\s*(Figure\s+\d+\.\d+)");
            var figure = title.Success ? title.Groups[1].Value : $"the figure at character {opening.Index}";

            var bands = Regex
                .Matches(body, "<div class=\"band\">")
                .Select(band => (Open: band.Index, Close: ArchitectureFigures.Close(body, band.Index)))
                .ToArray();

            var index = 0;

            foreach (Match row in Regex.Matches(body, "<div class=\"row\">"))
            {
                var text = body[row.Index..ArchitectureFigures.Close(body, row.Index)];

                rows.Add(new FigureRow(
                    figure,
                    ++index,
                    Regex.Matches(text, "<div class=\"box[^\"]*\">").Count,
                    Regex.Matches(text, "<div class=\"arrow\">").Count,
                    bands.Any(band => row.Index > band.Open && row.Index < band.Close)));
            }
        }

        return rows;
    }

    // What the column gives this row at the width its boxes still hold one line.
    internal static int Column(FigureRow row, Measurements css)
    {
        var width = css.LayoutWidth
            - (2 * css.SidePadding)
            - (2 * css.FigureBorder)
            - (2 * css.FigurePadding);

        return row.InBand ? width - (2 * css.BandBorder) - (2 * css.BandPadding) : width;
    }

    // What this row needs with every box at the least width the stylesheet lets
    // a box hold.
    internal static int Required(FigureRow row, Measurements css) =>
        (row.Boxes * css.BoxMinimum)
        + (row.Arrows * css.ArrowWidth)
        + ((row.Items - 1) * css.Gap);

    // A row whose boxes carry no arrow is a set of peers and wraps to as many
    // lines as it needs, so its width is bounded by one box. A row with arrows
    // reads in one direction and holds one line, which is the shape that can run
    // past the column.
    internal static IReadOnlyList<string> TooWide(IEnumerable<FigureRow> rows, Measurements css) =>
        rows
            .Where(row => row.Arrows > 0 && Required(row, css) > Column(row, css))
            .Select(row =>
                $"{row.Where}: {row.Boxes} boxes and {row.Arrows} arrows need {Required(row, css)}px " +
                $"of the {Column(row, css)}px the column gives it at {css.LayoutWidth}px.")
            .ToArray();

    [Fact]
    public void EverySequenceFitsTheColumnItIsDrawnIn()
    {
        var document = Corpus.Read("docs/ARCHITECTURE.html");
        var css = Numbers(document);
        var rows = Rows(document);

        // Two scopes, and the second carries the property. How many rows the
        // document holds is a fact about how much figure there is; the sequences
        // among them are what can be too wide.
        Assert.True(rows.Count >= 15, $"Read {rows.Count} figure rows, expected at least 15.");

        var sequences = rows.Where(row => row.Arrows > 0).ToArray();

        Assert.True(sequences.Length >= 12, $"Read {sequences.Length} sequences, expected at least 12.");

        Assert.Empty(TooWide(rows, css));
    }

    [Fact]
    public void ARowWiderThanItsColumnIsReported()
    {
        // The permanent proof, over a constructed row rather than a break and a
        // revert by hand. The widest sequence the document draws is five boxes,
        // and a sixth is what this refuses.
        var css = Numbers(Corpus.Read("docs/ARCHITECTURE.html"));

        var widest = new FigureRow("a figure", 1, Boxes: 5, Arrows: 4, InBand: true);
        var wider = widest with { Boxes = 6, Arrows = 5 };

        Assert.Empty(TooWide([widest], css));
        Assert.Single(TooWide([wider], css));
        Assert.True(Required(wider, css) > Column(wider, css));
    }

    [Fact]
    public void ABoxAloneFitsTheColumn()
    {
        // What makes wrapping safe for a set of peers: a box is never narrower
        // than its minimum, so a row that wraps runs past the column only if one
        // box cannot fit, and then no number of lines would help.
        var css = Numbers(Corpus.Read("docs/ARCHITECTURE.html"));

        var alone = new FigureRow("a figure", 1, Boxes: 1, Arrows: 0, InBand: true);

        Assert.True(
            css.BoxMinimum <= Column(alone, css),
            $"A box holds {css.BoxMinimum}px and the column inside a band gives {Column(alone, css)}px.");
    }

    [Fact]
    public void TheDocumentWrapsASetOfPeersAndStacksEveryRowWhenNarrow()
    {
        // The exemption above rests on two rules the document states, so both are
        // read. Without the first, a set of peers holds one line and is clipped
        // like any sequence; without the second, a sequence has no width at which
        // it stops being one line.
        var (rules, narrow, breakpoint) = Split(StyleBlock(Corpus.Read("docs/ARCHITECTURE.html")));

        Assert.Matches(@"\.row\s*:not\(\s*:has\(\s*\.arrow\s*\)\s*\)\s*\{[^}]*flex-wrap\s*:\s*wrap", rules);
        Assert.Matches(@"(?m)^\s*\.row\s*\{[^}]*flex-wrap\s*:\s*nowrap", rules);

        Assert.Matches(@"(?m)^\s*\.row\s*\{[^}]*flex-wrap\s*:\s*wrap", narrow);
        Assert.Matches(@"(?m)^\s*\.box\s*\{[^}]*flex\s*:\s*1\s+1\s+100%", narrow);

        Assert.True(breakpoint > 0, "The narrow-width rule states no width.");
    }

    [Fact]
    public void TheDocumentsTextSpansTheColumnItsTablesAndFiguresDrawIn()
    {
        // The text is set in the column the tables and figures draw in. A rule capping paragraphs,
        // lists, decisions or notes narrower than the column leaves a strip beside each of them that
        // the tables fill, which on a wide screen reads as a page drawn in its left half.
        var document = Corpus.Read("docs/ARCHITECTURE.html");
        var (rules, _, _) = Split(StyleBlock(document));
        var capped = new Regex(@"max-width\s*:\s*(?!none\b)[^;}]+");

        // The reader, shown to find a cap in either unit and to pass one lifted.
        Assert.Matches(capped, "p{margin:0;max-width:82ch}");
        Assert.Matches(capped, "ul,ol{max-width:700px}");
        Assert.DoesNotMatch(capped, ".key p{max-width:none;margin-bottom:7px}");

        foreach (var selector in new[] { "p", "ul,ol", "li", ".decision", ".note" })
        {
            var rule = Regex.Match(rules, @"(?m)^\s*" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");

            Assert.True(rule.Success, $"The stylesheet has no rule for `{selector}`.");
            Assert.DoesNotMatch(capped, rule.Groups[1].Value);
        }

        // And no element carries a cap of its own.
        Assert.DoesNotMatch(@"style=""[^""]*max-width", document);
    }
}
