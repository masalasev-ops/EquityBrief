using System.Text;

namespace EquityBrief.Web.Marks;

// One check of the fundamentals scorecard as its card draws it: its number, its answer and its sentence.
public sealed record FundamentalsCheckLine(int Number, string Answer, string Words);

// A stock's fundamentals card: the sentence it opens on, the word where one is read, each check as a sentence, the line
// naming a newer quarter reported and not yet filed, and the lines beneath that are not scored.
public sealed record FundamentalsView(string Summary, string? Word, IReadOnlyList<FundamentalsCheckLine> Checks, string? Awaited, IReadOnlyList<BriefLine> Context);

public sealed partial class MarkRenderer
{
    public const string FundamentalsFooter = "Worked by code from the company's filings as first filed with the SEC. No model judged it.";

    // The fundamentals card: the sentence with the word and the count, each check ticked or crossed as a sentence with
    // its figures, a newer quarter reported and not yet filed where one is, and the four lines not scored.
    // see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
    public string Fundamentals(string ticker, FundamentalsView view)
    {
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"fundamentals\" data-ticker=\"{Escaped(ticker)}\" data-word=\"{Escaped(view.Word ?? "none")}\">"));
        html.Append(Formatted($"<p class=\"fundamentals-summary\" data-summary>{Escaped(view.Summary)}</p>"));

        if (view.Checks.Count > 0)
        {
            html.Append("<ul class=\"fundamentals-checks\">");

            foreach (var check in view.Checks)
            {
                var mark = check.Answer switch
                {
                    "passed" => "<span class=\"check-mark\" aria-label=\"passed\">&#10003;</span>",
                    "failed" => "<span class=\"check-mark\" aria-label=\"failed\">&#10007;</span>",
                    _ => "<span class=\"check-mark\" aria-label=\"not scored\">&#8211;</span>",
                };

                html.Append(Formatted($"<li data-check=\"{check.Number}\" data-answer=\"{Escaped(check.Answer)}\">{mark}<span>{Escaped(check.Words)}</span></li>"));
            }

            html.Append("</ul>");
        }

        html.Append(view.Awaited is { } awaited ? Formatted($"<p class=\"fundamentals-awaited\">{Escaped(awaited)}</p>") : string.Empty);
        html.Append("<h4 class=\"fundamentals-context-head\">Not scored, for context</h4><ul class=\"fundamentals-context\">");

        foreach (var line in view.Context)
        {
            var figure = line.Figure is { } held ? Formatted($" data-figure=\"{Escaped(held)}\"") : string.Empty;

            html.Append(Formatted($"<li data-line=\"{Escaped(line.Key)}\"{figure}>{Escaped(line.Words)}</li>"));
        }

        html.Append(Formatted($"</ul><p class=\"fundamentals-foot\">{FundamentalsFooter}</p></div>"));

        return html.ToString();
    }
}
