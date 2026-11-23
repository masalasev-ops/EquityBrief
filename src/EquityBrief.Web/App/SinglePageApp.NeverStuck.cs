using System.Globalization;
using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.App;

// Never stuck on the Improvement loop page: since the monthly run before the newest, how many ideas passed, or that none did
// with the three nearest misses, each with what the researcher tried next on its setup; and the researcher's weeks since, those
// that failed counted, beside the day the next runs regardless.
// see: A month with no idea passed names its nearest misses and what the researcher tried next, and a failed week is counted and never stops the next
public sealed partial class SinglePageApp
{
    public const string StuckLede = "Whether the loop is finding anything. Where no idea has passed since the monthly run before the newest, the ideas that came closest to the bar are named, with what the researcher tried next from each. A week the model answered nothing is asked once more, counted here, and the next week runs regardless.";

    public string NeverStuckRegion(StuckView view)
    {
        var next = view.NextWeek.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        if (view.Since is not { } since)
        {
            return Cards.Computed("Never stuck", Invariant($"<p class=\"empty\" data-stuck=\"none\" data-summary>No monthly run has been stored yet, so there is no month to read. The researcher's next week runs {Escaped(next)}.</p>"), title: "Is the loop finding anything", lede: Escaped(StuckLede), region: "loop-stuck");
        }

        var day = since.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        var html = new StringBuilder(view.Passed > 0
            ? Invariant($"<p data-summary data-stuck=\"passed\">{view.Passed} idea(s) passed since the monthly run of {Escaped(day)}, each running as its setup's variant or queued for a slot.</p>")
            : Invariant($"<p data-summary data-stuck=\"stuck\">No idea has passed since the monthly run of {Escaped(day)}.</p>"));

        if (view.Passed == 0)
        {
            if (view.Nearest.Count == 0)
            {
                html.Append("<p class=\"lm-small\" data-stuck=\"no-miss\">No idea came near the bar since then either.</p>");
            }
            else
            {
                html.Append("<p class=\"lm-h\">The nearest misses</p><ol class=\"stuck-misses\">");

                foreach (var (miss, tried) in view.Nearest)
                {
                    var reached = Invariant($"reached 1 in {Math.Round(1 / miss.Adjusted!.Value):0}, the bar 1 in {Math.Round(1 / miss.Bar):0}");

                    html.Append(Invariant($"<li data-miss=\"{miss.Id}\"><p><b>{Escaped(IdeaGrammar.FamilyWords(miss.Family))}, {Escaped(IdeaGrammar.IndexWords(miss.Index))}</b>: {Escaped(miss.Words)}</p><p class=\"lm-small\">It {Escaped(reached)}.</p>"));
                    html.Append(tried is { } idea
                        ? Invariant($"<p class=\"lm-small\" data-next=\"tried\">What the researcher tried next: {Escaped(idea.Reason)}</p><code class=\"idea-line\">{Escaped(idea.Line)}</code>")
                        : Invariant($"<p class=\"lm-small\" data-next=\"waiting\">The researcher reads it among the setup's tested ideas on {Escaped(next)}.</p>"));
                    html.Append("</li>");
                }

                html.Append("</ol>");
            }
        }

        html.Append(view.FailedWeeks > 0
            ? Invariant($"<p class=\"lm-small\" data-stuck=\"failed\">{view.FailedWeeks} of the researcher's {view.Weeks} week(s) since failed: each was asked once more before it was counted. The next week runs {Escaped(next)} regardless.</p>")
            : Invariant($"<p class=\"lm-small\" data-stuck=\"weeks\">The researcher has run {view.Weeks} week(s) since, none failed. The next week runs {Escaped(next)}.</p>"));

        return Cards.Computed("Never stuck", html.ToString(), title: "Is the loop finding anything", lede: Escaped(StuckLede), region: "loop-stuck");
    }
}
