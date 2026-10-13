using System.Text;

namespace EquityBrief.Web.Marks;

// One heading of the chart of what is behind a stock: its name, the stock's percentile among its index's members tonight
// as a share, none where it holds no reading under the heading, and its push on the learned score where one is drawn.
public sealed record BehindHeadingView(string Heading, double? Percentile, double? Push);

// The chart of what is behind a stock: its index's name, each heading in the core table's order, and, where the learned
// score has passed the tester on the index, the score of a setup at the means it learned on and the stock's score, which
// the pushes take the one to the other, in the score's own unit; none where the push view is not drawn, with why where
// the score passed and its stored text could not be read.
public sealed record BehindItView(string Index, IReadOnlyList<BehindHeadingView> Headings, double? Centre = null, double? Score = null, string? Unread = null);

// The chart of what is behind a stock, on a pick's card and on a stock's page: its readings view, always drawn, a bar a
// heading of the stock's percentile among its index's members tonight, a heading with no stored reading drawn as not
// read; and its push view, drawn only where the learned score has passed the tester on the index, each heading's push on
// the score in green where it pushes up and red where it pushes down, its sign always written.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
public sealed partial class MarkRenderer
{
    // The width the bars are drawn at, in the picture's own units.
    const int BehindWidth = 100;

    public string BehindIt(BehindItView view)
    {
        var body = new StringBuilder();
        var pushed = view.Centre is not null && view.Score is not null;

        body.Append(Invariant, $"<section class=\"behind-it\" data-index=\"{Escaped(view.Index)}\" data-push=\"{(pushed ? "drawn" : "none")}\"><h5>What is behind it</h5>");
        body.Append(Invariant, $"<p class=\"behind-lede\">Where the stock stands among the {Escaped(view.Index)}'s members tonight under each heading, in hundredths of the members, a reading the same for every member left out.</p>");
        body.Append("<ul class=\"behind-readings\">");

        foreach (var heading in view.Headings)
        {
            if (heading.Percentile is { } share)
            {
                var hundredths = (int)Math.Round(share * 100);

                body.Append(Invariant, $"<li data-heading=\"{Escaped(heading.Heading)}\" data-percentile=\"{share.ToString("0.####", Invariant)}\"><span class=\"bh-name\">{Escaped(heading.Heading)}</span>");
                body.Append(Invariant, $"<svg class=\"bh-bar\" viewBox=\"0 0 {BehindWidth} 8\" width=\"{BehindWidth}\" height=\"8\" aria-hidden=\"true\"><rect class=\"bh-track\" width=\"{BehindWidth}\" height=\"8\"></rect><rect class=\"bh-fill\" width=\"{hundredths}\" height=\"8\"></rect></svg>");
                body.Append(Invariant, $"<b class=\"bh-figure\">{hundredths}</b></li>");
            }
            else
            {
                body.Append(Invariant, $"<li data-heading=\"{Escaped(heading.Heading)}\" data-percentile=\"none\"><span class=\"bh-name\">{Escaped(heading.Heading)}</span><span class=\"degraded\">not read</span></li>");
            }
        }

        body.Append("</ul>");

        if (view.Centre is { } centre && view.Score is { } score)
        {
            var largest = Math.Max(view.Headings.Max(heading => Math.Abs(heading.Push ?? 0)), 1e-9);

            body.Append(Invariant, $"<p class=\"behind-lede\">Each heading's push on the learned score, which passed the walk-forward tester on this index: its weights times the stock's readings less their means over the setups it learned on, in typical moves, the score's own unit.</p>");
            body.Append(Invariant, $"<ul class=\"behind-push\" data-centre=\"{centre.ToString("R", Invariant)}\" data-score=\"{score.ToString("R", Invariant)}\">");

            foreach (var heading in view.Headings)
            {
                var push = heading.Push ?? 0;
                var length = (int)Math.Round(Math.Abs(push) / largest * (BehindWidth / 2));
                var side = push >= 0 ? "gain" : "loss";
                var from = push >= 0 ? BehindWidth / 2 : (BehindWidth / 2) - length;

                body.Append(Invariant, $"<li data-heading=\"{Escaped(heading.Heading)}\" data-push=\"{push.ToString("R", Invariant)}\"><span class=\"bh-name\">{Escaped(heading.Heading)}</span>");
                body.Append(Invariant, $"<svg class=\"bh-bar\" viewBox=\"0 0 {BehindWidth} 8\" width=\"{BehindWidth}\" height=\"8\" aria-hidden=\"true\"><rect class=\"bh-track\" width=\"{BehindWidth}\" height=\"8\"></rect><rect class=\"bh-{side}\" x=\"{from}\" width=\"{length}\" height=\"8\"></rect><line class=\"bh-zero\" x1=\"{BehindWidth / 2}\" x2=\"{BehindWidth / 2}\" y1=\"0\" y2=\"8\"></line></svg>");
                body.Append(Invariant, $"<b class=\"{BySign("bh-figure", push)}\">{push.ToString("+0.00;-0.00;0.00", Invariant)}</b></li>");
            }

            var together = score - centre;

            body.Append(Invariant, $"</ul><p class=\"behind-total\">A setup at the means the score learned on scores {centre.ToString("0.00", Invariant)}; the pushes together, <b class=\"{BySign("bh-total", together)}\">{together.ToString("+0.00;-0.00;0.00", Invariant)}</b>, take it to this stock's score of {score.ToString("0.00", Invariant)}. It is the score's reading, not this stock's chance.</p>");
        }

        if (view.Unread is { } why)
        {
            body.Append(Invariant, $"<p class=\"degraded\" data-push=\"unread\">The learned score passed on this index, and its push is not drawn: its stored text could not be read: {Escaped(why)}</p>");
        }

        body.Append("</section>");

        return body.ToString();
    }

    // A figure's classes with the gain or the loss beside them by its sign at the hundredths it is written to, a figure
    // rounding to nothing carrying neither.
    static string BySign(string classes, double figure) => Math.Round(figure, 2) switch
    {
        > 0 => classes + " gain",
        < 0 => classes + " loss",
        _ => classes,
    };
}
