using System.Globalization;
using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.App;

// One row of a setup's failure list as the page draws it: its setup, the early years or the live nights, and the row.
public sealed record ResearchFailureView(string Family, string Population, FailureRow Row);

// One idea of the week as the page draws it: its setup, its rank, its line and sentence, what became of it, and where it
// was tested the notebook's verdict and why.
public sealed record ResearchIdeaView(string Family, int Rank, string Line, string Reason, string Outcome, string? Verdict, string? Why);

// The researcher's newest week: the day it ran, what it came to and its words, and its failure lists and ideas on the index.
public sealed record ResearchWeekView(string Day, string Outcome, string Detail, IReadOnlyList<ResearchFailureView> Failures, IReadOnlyList<ResearchIdeaView> Ideas);

// This week's research on the Improvement loop page for the index it reads: when the researcher last ran and what it came
// to; each idea it gave, its sentence above its line, and whether it was tested with its verdict, held already or waiting a
// week; and each setup's failure list, where its losers gather over the early years and since the restart.
// see: The researcher reads each rule's failure list over the early years and the nights since the restart, and the later years as one word
public sealed partial class SinglePageApp
{
    public const string ResearchLede = "Each Sunday the local model reads where each setup's losers gather and the notebook, the later years only as passed or failed, and proposes up to three ideas a setup in the grammar. At most 10 are tested a week; the rest wait for the next.";

    public string ResearchRegion(UniverseChoice chosen, ResearchWeekView? week)
    {
        if (week is null)
        {
            return Cards.Computed("This week's research", "<p class=\"empty\" data-research=\"none\" data-summary>The researcher has not run yet. It runs each Sunday at 13:00 UTC.</p>", title: "What the researcher proposed", lede: Escaped(ResearchLede), region: "loop-research");
        }

        var html = new StringBuilder(week.Outcome == "failed"
            ? $"<p data-summary data-research=\"failed\">The week of {Escaped(DayOf(week.Day))} failed: the model answered nothing on either asking, so the week is counted as failed.</p>"
            : string.Create(CultureInfo.InvariantCulture, $"<p data-summary data-research=\"week\">The week of {Escaped(DayOf(week.Day))} on the {Escaped(chosen.Name)}: {week.Ideas.Count} idea(s), {week.Ideas.Count(one => one.Outcome == "tested")} tested, {week.Ideas.Count(one => one.Outcome == "waiting")} waiting, {week.Ideas.Count(one => one.Outcome == "held")} already in the notebook.</p>"));

        html.Append("<p class=\"lm-h\">This week's ideas</p>");

        if (week.Ideas.Count == 0)
        {
            html.Append("<p class=\"empty\">No idea on this index this week.</p>");
        }
        else
        {
            html.Append("<div class=\"tbl-wrap\"><table class=\"lm-table research-table\"><thead><tr><th>Setup</th><th>The idea</th><th>What became of it</th></tr></thead><tbody>");

            foreach (var idea in week.Ideas)
            {
                var became = idea.Outcome switch
                {
                    "tested" when idea.Verdict is { } verdict => $"<b>{Escaped(VerdictWords(verdict))}</b> {Escaped(idea.Why ?? string.Empty)}",
                    "tested" => "Tested; its result was not written.",
                    "held" => "Already in the notebook, so not tested again.",
                    _ => "Waits for next week's tests.",
                };

                html.Append(CultureInfo.InvariantCulture, $"<tr data-research=\"{Escaped(idea.Outcome)}\"><td data-h=\"Setup\">{Escaped(IdeaGrammar.FamilyWords(idea.Family))}</td><td data-h=\"The idea\">{Escaped(idea.Reason)}<code class=\"idea-line\">{Escaped(idea.Line)}</code></td><td data-h=\"What became of it\">{became}</td></tr>");
            }

            html.Append("</tbody></table></div>");
        }

        html.Append("<p class=\"lm-h\">Where each setup's losers gather</p>");

        foreach (var family in IdeaGrammar.Families)
        {
            html.Append($"<p class=\"lm-p\"><b>{Escaped(IdeaGrammar.FamilyWords(family))}</b></p>");

            foreach (var (population, words) in new[] { ("early", "Over 2005 to 2018"), ("live", "Since the rules restarted") })
            {
                var rows = week.Failures.Where(one => one.Family == family && one.Population == population).Select(one => one.Row).ToArray();

                if (rows.Length == 0)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<p class=\"lm-small\" data-failures=\"{population}\">{words}: no reading holds {FailureList.LeastInBand} of its closed picks in a band where its losers gather.</p>");

                    continue;
                }

                html.Append(CultureInfo.InvariantCulture, $"<p class=\"lm-small\">{words}, of {rows[0].Picks:N0} closed picks, {rows[0].Losers:N0} losers:</p>");
                html.Append("<div class=\"tbl-wrap\"><table class=\"lm-table failure-table\"><thead><tr><th>Reading</th><th>Band</th><th class=\"r\">Share of its losers</th><th class=\"r\">Share of its picks</th><th class=\"r\">Picks in the band</th></tr></thead><tbody>");

                foreach (var row in rows)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<tr data-failures=\"{population}\"><td data-h=\"Reading\">{Escaped(row.Reading)}</td><td data-h=\"Band\">{Escaped(row.Band)}</td><td data-h=\"Share of its losers\" class=\"r num\">{row.LosersShare:0%}</td><td data-h=\"Share of its picks\" class=\"r num\">{row.PicksShare:0%}</td><td data-h=\"Picks in the band\" class=\"r num\">{row.InBand:N0}</td></tr>");
                }

                html.Append("</tbody></table></div>");
            }
        }

        return Cards.Computed("This week's research", html.ToString(), title: "What the researcher proposed", lede: Escaped(ResearchLede), region: "loop-research");
    }

    static string DayOf(string day) =>
        DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on) ? on.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : day;
}
