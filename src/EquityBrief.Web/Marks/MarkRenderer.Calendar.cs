using System.Text;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Report;

namespace EquityBrief.Web.Marks;

// The ninety days after the night a name's calendar draws, as the core read them: each dated event in order and each
// kind of the market events table that ends inside the days.
public sealed record CalendarView(DateOnly Night, DateOnly Through, IReadOnlyList<CalendarItem> Items, IReadOnlyList<string> PastTheTable);

// The calendar and the sources, the two regions before where the research stands: the next ninety days' dated events as a
// table and a timeline beside the dated items the research read, and every document the written regions cite with the
// figures two sources state differently and the line naming what is computed.
// see: The calendar draws a name's next ninety days of results, ex-dividend dates and the Fed's and the CPI's dates beside the dated items the research read
// see: A figure the filing and the provider's fetch state differently beyond rounding is listed under the sources with the filing first, and the page draws the fetch's
public sealed partial class MarkRenderer
{
    // The lanes the timeline draws, top to bottom, each a kind of event with its name at the lane's start.
    static readonly (string Kind, string Name)[] Lanes =
    [
        (CalendarItem.Results, "Results"),
        (CalendarItem.ExDividend, "Ex-dividend"),
        ("fomc", "FOMC"),
        ("cpi", "CPI"),
    ];

    const double LaneHigh = 30;

    const double LaneLeft = 96;

    // The next ninety days as a timeline: a lane a kind of event, each event a dot on the day it falls on, its words beneath
    // the pointer, the days counted from the night along the axis, and an estimated date drawn hollow.
    public string CalendarTimeline(string ticker, CalendarView view)
    {
        if (view.Items.Count == 0)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"calendar-timeline\" data-items=\"0\">No dated event of {Escaped(ticker)}'s or the market's falls in the {PageCalendar.Days} days after {DayOf(view.Night)}, so no timeline is drawn.</p>");
        }

        var high = ChartTop + (Lanes.Length * LaneHigh) + ChartBottom;
        var span = view.Through.DayNumber - view.Night.DayNumber;
        double X(DateOnly day) => LaneLeft + ((day.DayNumber - view.Night.DayNumber) * (ChartWide - LaneLeft - ChartRight) / span);
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"calendar-timeline\" viewBox=\"0 0 {Number(ChartWide)} {Number(high)}\" role=\"img\" data-mark=\"calendar-timeline\" data-items=\"{view.Items.Count}\" aria-label=\"The {PageCalendar.Days} days after {DayOf(view.Night)} for {Escaped(ticker)}\">"));

        for (var lane = 0; lane < Lanes.Length; lane++)
        {
            var y = ChartTop + (lane * LaneHigh) + (LaneHigh / 2);

            svg.Append(Formatted($"<text class=\"m-cap\" x=\"{Number(LaneLeft - 8)}\" y=\"{Number(y + 4)}\" text-anchor=\"end\">{Lanes[lane].Name}</text>"));
            svg.Append(Formatted($"<line class=\"m-gridline\" x1=\"{Number(LaneLeft)}\" x2=\"{Number(ChartWide - ChartRight)}\" y1=\"{Number(y)}\" y2=\"{Number(y)}\"/>"));
        }

        foreach (var days in new[] { 0, 30, 60, 90 })
        {
            var x = X(view.Night.AddDays(days));

            // The last day's words end at its rule, since words centred on the plot's right edge run past the figure.
            var anchor = days == PageCalendar.Days ? "end" : "middle";

            svg.Append(Formatted($"<line class=\"m-axisline\" x1=\"{Number(x)}\" x2=\"{Number(x)}\" y1=\"{Number(ChartTop)}\" y2=\"{Number(high - ChartBottom)}\"/>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(x)}\" y=\"{Number(high - 10)}\" text-anchor=\"{anchor}\">{(days == 0 ? "the night" : "+" + days.ToString(Invariant) + " days")}</text>"));
        }

        foreach (var item in view.Items)
        {
            var lane = Array.FindIndex(Lanes, one => one.Kind == item.Kind);

            if (lane < 0)
            {
                continue;
            }

            var y = ChartTop + (lane * LaneHigh) + (LaneHigh / 2);

            svg.Append(Formatted($"<circle class=\"ct-dot ct-{Escaped(item.Kind)}{(item.Declared ? string.Empty : " ct-estimated")}\" cx=\"{Number(X(item.Date))}\" cy=\"{Number(y)}\" r=\"6\" data-date=\"{DayOf(item.Date)}\" data-kind=\"{Escaped(item.Kind)}\" data-declared=\"{(item.Declared ? "yes" : "no")}\"><title>{DayOf(item.Date)}: {Escaped(item.Words)}</title></circle>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // The calendar region: the timeline, the table of the next ninety days' events with each one's days after the night, a
    // line for each kind of the market events table that ends inside them, and the dated items the research read.
    public string TheCalendar(string ticker, CalendarView view, WrittenCell? items, IReadOnlyList<SourceCell> documents)
    {
        var region = new StringBuilder();

        region.Append(Formatted($"<section class=\"page-calendar\" data-ticker=\"{Escaped(ticker)}\" data-night=\"{DayOf(view.Night)}\" data-through=\"{DayOf(view.Through)}\" data-events=\"{view.Items.Count}\">"));
        region.Append("<div class=\"fig\">").Append(CalendarTimeline(ticker, view)).Append("</div>");

        if (view.Items.Count > 0)
        {
            region.Append("<div class=\"tbl-wrap\"><table class=\"calendar-dates\"><tr><th>Date</th><th>Event</th><th>Days after the night</th></tr>");

            foreach (var item in view.Items)
            {
                var words = item.Amount is { } each ? item.Words + ", " + Figures.Price(each) + " a share" : item.Words;

                region.Append(Formatted($"<tr data-date=\"{DayOf(item.Date)}\" data-kind=\"{Escaped(item.Kind)}\" data-declared=\"{(item.Declared ? "yes" : "no")}\"><td>{DayOf(item.Date)}</td><td>{Escaped(words)}</td><td class=\"num\">{(item.Date.DayNumber - view.Night.DayNumber).ToString(Invariant)}</td></tr>"));
            }

            region.Append("</table></div>");
        }

        foreach (var past in view.PastTheTable)
        {
            region.Append(Formatted($"<p class=\"calendar-past\" data-past=\"{Escaped(past)}\">The table of each {Escaped(past)} ends inside these days, so a later one is not ruled out.</p>"));
        }

        if (items is not null)
        {
            region.Append(WrittenSection(ticker, items, documents));
        }

        return region.Append("</section>").ToString();
    }

    // The sources region: every document the written regions cite with its date and link, those naming neither the company
    // nor its industry folded apart beneath and the list saying where fewer than two remain; the latest quarter's lines the
    // filing and the provider's fetch state differently beyond rounding, the filing first; and the one line naming what is
    // computed rather than sourced.
    // see: A stock's industry group is the provider's until an override is set and is checked against its own account, and the sources list and the cycle read only documents naming the company or its industry in their opening
    public string Sources(string ticker, IReadOnlyList<SourceCell> documents, IReadOnlyList<FigureConflict> conflicts, bool filedFactsHeld, Func<SourceCell, bool>? relevant = null)
    {
        var region = new StringBuilder();
        var kept = documents.Where(document => relevant?.Invoke(document) ?? true).ToArray();
        var leftOut = documents.Except(kept).ToArray();

        region.Append(Formatted($"<section class=\"sources-region\" data-ticker=\"{Escaped(ticker)}\" data-documents=\"{kept.Length}\" data-left-out=\"{leftOut.Length}\" data-relevance=\"{(relevant is null ? "untested" : "tested")}\" data-conflicts=\"{conflicts.Count}\">"));
        region.Append("<div class=\"sub\">Every document the written regions cite</div>");

        if (documents.Count == 0)
        {
            region.Append("<p class=\"degraded\" data-documents=\"none\">no written section cites a document</p>");
        }
        else
        {
            if (kept.Length < EquityBrief.Core.Report.IndustryCheck.FewestSources)
            {
                region.Append(Formatted($"<p class=\"degraded\" data-documents=\"few\">Not enough relevant sources: {kept.Length} of the {documents.Count} documents the written regions cite name the company or its industry.</p>"));
            }

            if (kept.Length > 0)
            {
                region.Append("<ol class=\"sources\">");

                foreach (var document in kept)
                {
                    region.Append(Formatted($"<li data-document=\"{Escaped(document.Id)}\" data-published-on=\"{Published(document)}\" data-url=\"{Sent(document.Url)}\">"));
                    region.Append(Link(document)).Append("</li>");
                }

                region.Append("</ol>");
            }

            if (leftOut.Length > 0)
            {
                region.Append(Formatted($"<details class=\"sources-left-out\" data-left-out=\"{leftOut.Length}\"><summary>{leftOut.Length} {(leftOut.Length == 1 ? "document names" : "documents name")} neither the company nor its industry, left out of the list</summary><ol class=\"sources\">"));

                foreach (var document in leftOut)
                {
                    region.Append(Formatted($"<li data-document=\"{Escaped(document.Id)}\" data-published-on=\"{Published(document)}\" data-url=\"{Sent(document.Url)}\" data-relevant=\"no\">"));
                    region.Append(Link(document)).Append("</li>");
                }

                region.Append("</ol></details>");
            }
        }

        region.Append("<div class=\"sub\">Figures checked and not used</div>");

        if (conflicts.Count > 0)
        {
            region.Append("<div class=\"tbl-wrap\"><table class=\"figures-checked\"><tr><th>Line</th><th>Quarter to</th><th>The filing states</th><th>The provider's fetch states</th><th>The page draws</th></tr>");

            foreach (var conflict in conflicts)
            {
                region.Append(Formatted($"<tr data-line=\"{Escaped(conflict.Line)}\" data-filed=\"{conflict.Filed.ToString(Invariant)}\" data-fetched=\"{conflict.Fetched.ToString(Invariant)}\"><td>{Escaped(conflict.Line)}</td><td>{DayOf(conflict.PeriodEnd)}</td>"));
                region.Append(Formatted($"<td class=\"num\">{Figures.Money(conflict.Filed)}, first filed {DayOf(conflict.FiledOn)} as {Escaped(conflict.Concept)}</td><td class=\"num\">{Figures.Money(conflict.Fetched)}</td><td>the fetch's</td></tr>"));
            }

            region.Append("</table></div>");
        }
        else
        {
            region.Append(filedFactsHeld
                ? "<p class=\"degraded\" data-conflicts=\"none\">The latest quarter's lines the filing states agree with the provider's fetch within rounding.</p>"
                : "<p class=\"degraded\" data-conflicts=\"unread\">No filing's facts are stored for the latest quarter, so its lines are checked against no second source.</p>");
        }

        region.Append("<p class=\"computed-line\">Computed by code from stored figures and sourced from no document: the levels and every distance in typical days, the plan and its shares, the typical moves, the margins and the growth, the payouts, the multiples, the long view, the tiles and the calendar's estimated dates.</p>");

        return region.Append("</section>").ToString();
    }
}
