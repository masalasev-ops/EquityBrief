using System.Globalization;
using System.Text;

namespace EquityBrief.Web.App;

// One reading recipe as the page draws it: its number, its line, its words, its source, the day it was entered, and its run
// on the index the page reads where one has worked it, the figures written, the members, the first and last sessions and the
// sessions its check worked again.
public sealed record RecipeRow(long Id, string Line, string Words, string Source, string Entered, long? Figures, int? Members, string? First, string? Last, int? Checked);

// One piece of data a recipe asked for that the store lacks: what it needs, the line that asked, its source and its day.
public sealed record DataRequestRow(string Need, string Line, string Source, string Day);

public sealed record RecipesView(IReadOnlyList<RecipeRow> Recipes, IReadOnlyList<DataRequestRow> Requests);

// The reading recipes on the Improvement loop page: every recipe entered, each with its run on the index or why it waits,
// newest first, and beneath them every piece of data a recipe asked for that the store lacks, which blocks nothing.
// see: A reading recipe is a closed set of operations over stored fields and is kept apart from the pinned catalogue
public sealed partial class SinglePageApp
{
    public const string RecipesLede = "New readings made from data the store already holds, each a short formula over the readings every stock has each night. A recipe is a reading on test: no rule reads it.";

    public const string RecipesNotRun = "not worked yet: a run over the store starts on your word";

    public const string RequestsLede = "Data a recipe asked for that the store does not hold. It is listed here and blocks nothing.";

    public string RecipesRegion(UniverseChoice chosen, RecipesView view)
    {
        if (view.Recipes.Count == 0 && view.Requests.Count == 0)
        {
            return Cards.Computed("Reading recipes", "<p class=\"empty\" data-recipes=\"empty\" data-summary>No recipe has been entered yet.</p>", title: "New readings on test", lede: Escaped(RecipesLede), region: "loop-recipes");
        }

        var html = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"<p data-summary data-recipes=\"count\">{view.Recipes.Count:N0} recipe{(view.Recipes.Count == 1 ? string.Empty : "s")}, newest first; {view.Recipes.Count(row => row.Figures is not null):N0} worked on the {Escaped(chosen.Name)}.</p>"));

        if (view.Recipes.Count > 0)
        {
            html.Append("<div class=\"tbl-wrap\"><table class=\"lm-table recipe-table\"><thead><tr><th>Entered</th><th>From</th><th>The recipe</th><th>On this index</th></tr></thead><tbody>");

            foreach (var row in view.Recipes)
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr data-recipe=\"{row.Id}\"><td data-h=\"Entered\" class=\"num\">{Escaped(DayWords(row.Entered))}</td><td data-h=\"From\">{Escaped(SourceWords(row.Source))}</td>");
                html.Append($"<td data-h=\"The recipe\">{Escaped(row.Words)}<code class=\"idea-line\">{Escaped(row.Line)}</code></td>");
                html.Append($"<td data-h=\"On this index\">{Escaped(RunWords(row))}</td></tr>");
            }

            html.Append("</tbody></table></div>");
        }

        if (view.Requests.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<h4>Data the store lacks</h4><p class=\"lm-small\">{Escaped(RequestsLede)}</p><ol class=\"data-requests\" data-requests=\"{view.Requests.Count}\">");

            foreach (var request in view.Requests)
            {
                html.Append($"<li><b>{Escaped(char.ToUpperInvariant(request.Need[0]) + request.Need[1..])}</b>, asked by {Escaped(request.Source == "the operator" ? "you" : request.Source)} on {Escaped(DayWords(request.Day))} for <code class=\"idea-line\">{Escaped(request.Line)}</code></li>");
            }

            html.Append("</ol>");
        }

        return Cards.Computed("Reading recipes", html.ToString(), title: "New readings on test", lede: Escaped(RecipesLede), region: "loop-recipes");
    }

    // A recipe's run in words: its figures, members and span with the sessions its check worked again, why it waits where
    // the store lacks what it needs, or that no run has worked it.
    public static string RunWords(RecipeRow row)
    {
        if (row.Figures is { } figures)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{figures:N0} figures of {row.Members:N0} stocks, {DayWords(row.First ?? string.Empty)} to {DayWords(row.Last ?? string.Empty)}; checked on {row.Checked:N0} sessions to read nothing later");
        }

        return Core.Loop.Recipes.Parse(row.Line).Term is { } term && Core.Loop.Recipes.Needs(term, holdsIndustries: false) is { Count: > 0 } needs
            ? "waits for " + string.Join(", ", needs)
            : RecipesNotRun;
    }
}
