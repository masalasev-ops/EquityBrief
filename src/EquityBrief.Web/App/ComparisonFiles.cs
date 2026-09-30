using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Components;
using EquityBrief.Core.Research;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// The comparisons as files read beside the application rather than on any of its pages: for each report a trial or a
// review asked beside, its sections with every model's drafts side by side; each section's rates over the reports
// before the research prompt's addendum merged and the reports after; and the two cases' figures on both sides over the
// reports since their ask changed. Each is a document of its own with the app's stylesheet inline, no script and no
// link into the application, composed by the marks the page drew the drafts with, and reading no store.
// see: The drafts compared beside a report and the research template's before and after counts are written to files by a command, and drawn on no page
public sealed partial class ComparisonFiles(MarkRenderer marks) : IComponent
{
    // It is handed what the read API read and touches no store; what the command writes with it are files in a folder
    // the repository ignores.
    public static ComponentAccess Access => ComponentAccess.Nothing;

    // The read surface's first argument that writes the files and starts nothing.
    public const string Verb = "comparisons";

    // The folder in the checkout the files are written to.
    public const string Folder = "sampleReports";

    public const string RatesFile = "section_rates_before_and_after_the_addendum.html";

    public const string BothSidesFile = "two_cases_both_sides.html";

    // A report's file: its stock and the day it wrote for, and its place among that stock's reports of that day after
    // the first, since a stock can be written more than once in a day.
    public static string DraftsFile(string ticker, DateOnly day, int ofTheDay) =>
        Safe(ticker) + "_comparison_" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        + (ofTheDay > 1 ? "_" + ofTheDay.ToString(CultureInfo.InvariantCulture) : string.Empty) + ".html";

    // Every file the view warrants: one for each report a trial or a review asked beside, in the order the reports
    // started, and the rates' and the both-sides' once their windows are full.
    public IReadOnlyList<(string Name, string Document)> Files(ComparisonView view)
    {
        var files = new List<(string, string)>();

        var reports = view.Trials
            .GroupBy(row => row.RunId, StringComparer.Ordinal)
            .OrderBy(report => report.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var day in reports.GroupBy(report => (report.First().Ticker, report.First().Day)))
        {
            foreach (var (report, place) in day.Select((report, at) => (report, at + 1)))
            {
                files.Add((DraftsFile(day.Key.Ticker, day.Key.Day, place), Drafts([.. report])));
            }
        }

        if (view.RatesReady)
        {
            files.Add((RatesFile, Rates(view)));
        }

        if (view.BothSidesReady)
        {
            files.Add((BothSidesFile, BothSides(view)));
        }

        return files;
    }

    // One report's sections a trial or a review asked for, each with the pass's side, the trial's and the review's.
    public string Drafts(IReadOnlyList<TrialRow> rows)
    {
        var first = rows[0];

        return Document(
            "EquityBrief: " + first.Ticker + ", " + Day(first.Day) + ", drafts compared",
            "Each section a second model or a review asked for beside the report, never written into it: each side's result, rounds and cost, with every draft side by side.",
            "drafts",
            marks.TrialsRegion(rows, SinglePageApp.NameRoute));
    }

    // Each section's share passed first time and share left out over the reports before the addendum merged and the
    // reports after, with the reports read on each side.
    public static string Rates(ComparisonView view)
    {
        var body = new StringBuilder();

        body.Append(CultureInfo.InvariantCulture, $"<p class=\"list-count\" data-before=\"{view.Before.Count}\" data-after=\"{view.After.Count}\">The {view.Before.Count} reports before the addendum merged and the {view.After.Count} after it</p>");
        body.Append("<div class=\"tbl-wrap\"><table class=\"report-table comparison-rates\"><thead><tr><th>#</th><th>Section</th><th class=\"r\">Passed first time, before</th><th class=\"r\">Passed first time, after</th><th class=\"r\">Left out, before</th><th class=\"r\">Left out, after</th></tr></thead><tbody>");

        foreach (var (before, place) in view.RatesBefore.Select((rate, at) => (rate, at + 1)))
        {
            var after = view.RatesAfter.Single(rate => rate.Section == before.Section);

            body.Append(CultureInfo.InvariantCulture, $"<tr data-section=\"{Escaped(before.Section)}\" data-first-before=\"{before.FirstTime}\" data-first-after=\"{after.FirstTime}\" data-left-out-before=\"{before.LeftOut}\" data-left-out-after=\"{after.LeftOut}\" data-reports-before=\"{before.Reports}\" data-reports-after=\"{after.Reports}\">");
            body.Append(CultureInfo.InvariantCulture, $"<td class=\"place\">{place}</td><td>{Escaped(before.Section)}</td><td class=\"r\">{before.FirstTime} of {before.Reports}</td><td class=\"r\">{after.FirstTime} of {after.Reports}</td><td class=\"r\">{before.LeftOut} of {before.Reports}</td><td class=\"r\">{after.LeftOut} of {after.Reports}</td></tr>");
        }

        body.Append("</tbody></table></div><p class=\"report-key\">Each rate is read over the reports on its side that warranted the section.</p>");
        body.Append(ReportList("Before", view.Before)).Append(ReportList("After", view.After));

        return Document(
            "EquityBrief: section rates before and after the addendum",
            "Each section's share passed first time and share left out, over the reports whose passes started before the research prompt's addendum merged and the reports that started after it.",
            "rates",
            body.ToString());
    }

    // How many of the reports since the two cases' ask changed carried a figure on both sides, beside the drafts
    // measured before it.
    public static string BothSides(ComparisonView view)
    {
        var body = new StringBuilder();

        body.Append(CultureInfo.InvariantCulture, $"<p class=\"both-sides\" data-both-sides=\"{view.BothSides}\" data-drafted=\"{view.SinceTheAsk.Count}\" data-baseline=\"{ComparisonView.BaselineBothSides} of {ComparisonView.BaselineDrafted}\">{view.BothSides} of the {view.SinceTheAsk.Count} reports since the ask changed carried a figure on both sides of the two cases, first draft or retry, against {ComparisonView.BaselineBothSides} of {ComparisonView.BaselineDrafted} accepted drafts measured before it.</p>");
        body.Append("<ol class=\"trial-list\">");

        foreach (var report in view.SinceTheAsk)
        {
            var cell = report.Cells.Single(one => one.Section == ClaimRules.TwoCasesSection);

            body.Append(CultureInfo.InvariantCulture, $"<li data-run=\"{Escaped(report.RunId)}\" data-both-sides=\"{(cell.BothSides ? "yes" : "no")}\">{Escaped(report.Ticker)}, {Day(report.Day)}: {(cell.BothSides ? "a figure on both sides" : "no figure on both sides")}, {cell.Drafts} draft{(cell.Drafts == 1 ? string.Empty : "s")}</li>");
        }

        body.Append("</ol>");

        return Document(
            "EquityBrief: the two cases' figures on both sides",
            "Of the reports whose passes started after the two cases were asked to argue each fact on one side, the first that drafted the two cases, and how many of them carried one figure in both cases.",
            "both-sides",
            body.ToString());
    }

    static string ReportList(string side, IReadOnlyList<ReportRow> reports) =>
        $"<p class=\"lbl\">{side}</p><ol class=\"trial-list\" data-side=\"{side.ToLowerInvariant()}\">"
        + string.Concat(reports.Select(report => $"<li data-run=\"{Escaped(report.RunId)}\">{Escaped(report.Ticker)}, {Day(report.Day)}</li>"))
        + "</ol>";

    static string Document(string title, string lede, string kind, string body) =>
        $$$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{{Escaped(title)}}}</title>
            <style>
            {{{Stylesheet.Css}}}
            </style>
            </head>
            <body data-comparison="{{{kind}}}">
            <div class="wrap exported">
            <h1>{{{Escaped(title)}}}</h1>
            <p class="exported">{{{Escaped(lede)}}} Written from the store by the comparison command and drawn on no page.</p>
            {{{ReportExporter.Unrouted(ReportExporter.Opened(body))}}}
            </div>
            </body>
            </html>
            """;

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Safe(string ticker) => UnsafeInName().Replace(ticker, "_");

    [GeneratedRegex("[^A-Za-z0-9.-]")]
    private static partial Regex UnsafeInName();

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
