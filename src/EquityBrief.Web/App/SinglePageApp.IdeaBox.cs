using System.Globalization;
using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.App;

// One press of the idea box as the page draws it: its words and day, the newest answer's outcome with its cause, line and
// sentence, the day it was confirmed, and once tested the notebook's verdict and why.
public sealed record IdeaBoxView(long Id, string Words, string Day, string? Outcome, string? Cause, string? Line, string? Reason, string? Confirmed, string? Verdict, string? Why);

// The idea box on the Improvement loop page: a box for an idea in plain words and its press, which writes the words and starts
// the local model reading them; then each idea typed, newest first, with what the model understood above the line it was read
// into and a Confirm press, or why it could not be read, or that it waits for the model; a confirmed idea saying it is tested
// first in the next Sunday run, and once tested its verdict and why.
// see: An idea typed in the box is read into one grammar line by the local model and joins the week's tests only on the operator's confirm
public sealed partial class SinglePageApp
{
    public const string IdeaBoxRoute = "/loop/idea-box";

    public const string IdeaConfirmRoute = "/loop/idea-box/confirm/";

    public const string IdeaBoxLede = "Type an idea in plain words. The local model reads it into one line of the grammar once it is free, and shows what it understood. Nothing is tested until you confirm it; a confirmed idea is tested first in the next Sunday run.";

    public string IdeaBoxRegion(IReadOnlyList<IdeaBoxView> ideas)
    {
        var html = new StringBuilder(ideas.Count == 0
            ? "<p class=\"empty\" data-ideas=\"none\" data-summary>No idea has been typed yet.</p>"
            : string.Create(CultureInfo.InvariantCulture, $"<p data-summary data-ideas=\"some\">Your newest {ideas.Count} idea(s), newest first.</p>"));

        html.Append(CultureInfo.InvariantCulture, $"<div class=\"idea-held idea-box\" data-idea-key=\"idea-box\"><form class=\"idea-press\" method=\"post\" action=\"{IdeaBoxRoute}\" data-idea-key=\"idea-box\"><label for=\"idea-words\">Your idea</label><textarea id=\"idea-words\" name=\"words\" rows=\"3\" maxlength=\"{IdeaBoxPrompt.MostCharacters}\" required></textarea><button type=\"submit\">Ask the model</button></form><p class=\"loop-said\" aria-live=\"polite\"></p></div>");

        if (ideas.Count == 0)
        {
            return Cards.Computed("The idea box", html.ToString(), title: "Your ideas", lede: Escaped(IdeaBoxLede), region: "loop-idea-box");
        }

        html.Append("<ol class=\"idea-box-ideas\">");

        foreach (var idea in ideas)
        {
            var key = "idea|" + idea.Id.ToString(CultureInfo.InvariantCulture);

            html.Append(CultureInfo.InvariantCulture, $"<li class=\"idea-held\" data-idea-key=\"{key}\" data-idea=\"{StateOf(idea)}\"><p class=\"lm-small\">{Escaped(DayOf(idea.Day))}</p><p class=\"idea-words\">{Escaped(idea.Words)}</p>");
            html.Append(StateOf(idea) switch
            {
                "waiting" => "<p class=\"lm-small\">Waiting for the local model, which reads it once the night, a local run, the labeller or the picker lets it go.</p>",
                "none" => "<p class=\"lm-small\">The model read no idea the grammar can say in these words.</p>",
                "refused" => $"<p class=\"lm-small\">Not read: {Escaped(idea.Cause ?? "the answer could not be read")}.</p>",
                _ => $"<p>Understood as: {Escaped(idea.Reason ?? string.Empty)}</p><code class=\"idea-line\">{Escaped(idea.Line ?? string.Empty)}</code>",
            });

            if (StateOf(idea) == "understood")
            {
                html.Append(CultureInfo.InvariantCulture, $"<form class=\"idea-press\" method=\"post\" action=\"{IdeaConfirmRoute}{idea.Id.ToString(CultureInfo.InvariantCulture)}\" data-idea-key=\"{key}\"><button type=\"submit\">Confirm</button></form>");
            }
            else if (StateOf(idea) == "confirmed")
            {
                html.Append(CultureInfo.InvariantCulture, $"<p class=\"lm-small\">Confirmed {Escaped(DayOf(idea.Confirmed!))}: tested first in the next Sunday run.</p>");
            }
            else if (StateOf(idea) == "tested")
            {
                html.Append(CultureInfo.InvariantCulture, $"<p class=\"lm-small\"><b>{Escaped(VerdictWords(idea.Verdict!))}</b> {Escaped(idea.Why ?? string.Empty)}</p>");
            }

            html.Append("<p class=\"loop-said\" aria-live=\"polite\"></p></li>");
        }

        html.Append("</ol>");

        return Cards.Computed("The idea box", html.ToString(), title: "Your ideas", lede: Escaped(IdeaBoxLede), region: "loop-idea-box");
    }

    // Where an idea stands: waiting for the model, read into nothing, refused, understood and waiting for the confirm,
    // confirmed, or tested.
    public static string StateOf(IdeaBoxView idea) =>
        idea.Verdict is not null ? "tested"
        : idea.Confirmed is not null ? "confirmed"
        : idea.Outcome switch
        {
            null => "waiting",
            "understood" => "understood",
            "none" => "none",
            _ => "refused",
        };
}
