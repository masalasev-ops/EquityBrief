using System.Text;

namespace EquityBrief.Web.Marks;

// One line of a stock's brief read: what it states, its words, the stored figure it reads where it reads one, and a link
// after its words where it points somewhere, with the words that end the line after the link.
public sealed record BriefLine(string Key, string Words, string? Figure = null, string? Href = null, string? LinkWords = null, string? After = null);

// One of the brief read's three panels: what the chart says, what the business says, and the way in.
public sealed record BriefPanel(string Key, string Heading, IReadOnlyList<BriefLine> Lines);

// A setup's standing tonight on the stock's index as its chip names it: the setup, its state and its words.
public sealed record BriefChip(string Family, string Label, string State, string Words);

// The brief read the chart page opens on: its summary line, its three panels, a chip a setup, and the line of what to
// watch for where one applies, every word written by code from stored figures.
public sealed record BriefView(string Summary, IReadOnlyList<BriefPanel> Panels, IReadOnlyList<BriefChip> Chips, string? WatchFor);

public sealed partial class MarkRenderer
{
    public const string BriefFooter = "Written by code from tonight's stored figures. No model judged it, and nothing here picks it.";

    // The brief read: its summary line, its three panels side by side and stacked at a phone's width, a chip a setup, the
    // line of what to watch for where one applies, and the footer with the stock's full page beside it.
    // see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
    public string BriefRead(string ticker, BriefView view)
    {
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"brief-read\" data-ticker=\"{Escaped(ticker)}\">"));
        html.Append(Formatted($"<p class=\"brief-summary\">{Escaped(view.Summary)}</p><div class=\"brief-panels\">"));

        foreach (var panel in view.Panels)
        {
            html.Append(Formatted($"<section class=\"brief-panel\" data-panel=\"{Escaped(panel.Key)}\"><h3>{Escaped(panel.Heading)}</h3><ul>"));

            foreach (var line in panel.Lines)
            {
                var figure = line.Figure is { } held ? Formatted($" data-figure=\"{Escaped(held)}\"") : string.Empty;
                var link = line.Href is { } href ? Formatted($" <a href=\"{Escaped(href)}\">{Escaped(line.LinkWords ?? href)}</a>{Escaped(line.After ?? string.Empty)}") : string.Empty;

                html.Append(Formatted($"<li data-line=\"{Escaped(line.Key)}\"{figure}>{Escaped(line.Words)}{link}</li>"));
            }

            html.Append("</ul></section>");
        }

        html.Append("</div><p class=\"brief-chips\">");

        foreach (var chip in view.Chips)
        {
            html.Append(Formatted($"<span class=\"fam-tag brief-chip\" data-family=\"{Escaped(chip.Family)}\" data-state=\"{Escaped(chip.State)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(chip.Family)}\"></span>{Escaped(chip.Label)}: {Escaped(chip.Words)}</span>"));
        }

        html.Append("</p>");
        html.Append(view.WatchFor is { } watch ? Formatted($"<p class=\"brief-watch\">{Escaped(watch)}</p>") : string.Empty);
        html.Append(Formatted($"<p class=\"brief-foot\"><span>{BriefFooter}</span> <a href=\"#/name/{Uri.EscapeDataString(ticker)}\">The stock's full page</a></p></div>"));

        return html.ToString();
    }
}
