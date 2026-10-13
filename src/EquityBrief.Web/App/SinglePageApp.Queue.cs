using System.Text;

namespace EquityBrief.Web.App;

// One stock's settled requests on one New York day: the ticker, the day, the newest request's instant as the store spells
// it, what it came to, when it settled as the queue page marks an instant, why where it says, and how many were asked.
public sealed record SettledCell(string Ticker, string Day, string AskedAt, string State, string? Settled, string? Reason, int Asked);

// The research passes no request names on one New York day: how many wrote a report, and what their paid calls cost as
// the store spells it and as the page states it.
public sealed record ByHandCell(string Day, int Reports, string Cost, string Stated);

// What the queue page draws beside its requests: the settled days, the passes no request names, how many rows of either
// the window leaves out, whether every day is drawn, and the days the window holds.
public sealed record QueueDays(IReadOnlyList<SettledCell> Settled, IReadOnlyList<ByHandCell> ByHand, int Older, bool All, int Days);

// The queue page's settled days and its passes written by hand.
public sealed partial class SinglePageApp
{
    // The key the link drawing every day carries.
    public const string QueueAllKey = "all";

    // The settled requests a row a stock and New York day, newest first, with the newest's outcome and how many times the
    // stock was asked that day.
    // see: The queue page draws settled requests a row a stock and New York day over fourteen days, and the passes no request names a row a day
    static string SettledDays(QueueDays days)
    {
        var body = new StringBuilder();

        body.Append(Invariant($"<div class=\"queue-part\" data-region=\"settled\" data-rows=\"{days.Settled.Count}\">"));
        body.Append("<h3>Settled</h3>");

        if (days.Settled.Count == 0)
        {
            body.Append(days.All
                ? "<p class=\"degraded\" data-settled=\"none\">No request has been settled yet.</p>"
                : Invariant($"<p class=\"degraded\" data-settled=\"none\">No request asked over the last {days.Days} New York days has settled.</p>"));
        }
        else
        {
            body.Append(days.All
                ? "<p class=\"lede\">A row a stock and New York day it was asked on, newest first, over every day, with what came of its newest request and how many times it was asked. Nothing is removed.</p>"
                : Invariant($"<p class=\"lede\">A row a stock and New York day it was asked on, newest first, over the last {days.Days} days, with what came of its newest request and how many times it was asked. Nothing is removed.</p>"));
            body.Append("<div class=\"tbl-wrap\"><table class=\"queue-table settled-days\"><thead><tr><th>Name</th><th>Day</th><th>Asked</th><th>Came to</th><th>Settled</th><th>Why</th></tr></thead><tbody>");

            foreach (var day in days.Settled)
            {
                body.Append(Invariant($"<tr data-ticker=\"{Escaped(day.Ticker)}\" data-asked-at=\"{Escaped(day.AskedAt)}\" data-state=\"{Escaped(day.State)}\" data-day=\"{Escaped(day.Day)}\" data-asked=\"{day.Asked}\">"));
                body.Append(Invariant($"<td class=\"c-nm\"><a class=\"tk\" href=\"{NameRoute}{Uri.EscapeDataString(day.Ticker)}\">{Escaped(day.Ticker)}</a></td>"));
                body.Append(Invariant($"<td class=\"q-day\">{Escaped(day.Day)}</td><td class=\"q-asked\">{(day.Asked == 1 ? "once" : Invariant($"{day.Asked} times"))}</td>"));
                body.Append(Invariant($"<td class=\"q-state\">{Escaped(day.State)}</td>"));
                body.Append(Invariant($"<td class=\"q-when\">{day.Settled ?? "<span class=\"degraded\">no time stored</span>"}</td>"));
                body.Append(Invariant($"<td>{Escaped(day.Reason ?? string.Empty)}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        body.Append("</div>");

        return body.ToString();
    }

    // The research passes no request names, a row a New York day with how many reports they wrote and their cost.
    // see: The queue page draws settled requests a row a stock and New York day over fourteen days, and the passes no request names a row a day
    static string ByHandDays(QueueDays days)
    {
        var body = new StringBuilder();

        body.Append(Invariant($"<div class=\"queue-part\" data-region=\"by-hand\" data-rows=\"{days.ByHand.Count}\">"));
        body.Append("<h3>Written by hand</h3>");

        if (days.ByHand.Count == 0)
        {
            body.Append(Invariant($"<p class=\"degraded\" data-by-hand=\"none\">No report was written without a request{(days.All ? string.Empty : Invariant($" over the last {days.Days} New York days"))}.</p>"));
        }
        else
        {
            body.Append("<p class=\"lede\">Reports no request names, a regenerate pressed on a stock's page or a pass run from the worker's command, a row a New York day with what their paid calls cost.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"queue-table by-hand\"><thead><tr><th>Day</th><th class=\"num\">Reports</th><th class=\"num\">Cost</th></tr></thead><tbody>");

            foreach (var day in days.ByHand)
            {
                body.Append(Invariant($"<tr data-day=\"{Escaped(day.Day)}\" data-reports=\"{day.Reports}\" data-cost=\"{Escaped(day.Cost)}\"><td class=\"q-day\">{Escaped(day.Day)}</td><td class=\"num\">{day.Reports}</td><td class=\"num\">{Escaped(day.Stated)}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        body.Append("</div>");

        // The link between the window and every day, beneath both tables it governs.
        body.Append(days.All
            ? Invariant($"<p class=\"queue-older\" data-all=\"true\"><a href=\"{QueueRoute}\">Only the last {days.Days} New York days</a></p>")
            : days.Older > 0
                ? Invariant($"<p class=\"queue-older\" data-all=\"false\" data-older=\"{days.Older}\"><a href=\"{QueueRoute}?{QueueAllKey}=1\">{days.Older} older {(days.Older == 1 ? "row" : "rows")}: every day</a></p>")
                : string.Empty);

        return body.ToString();
    }
}
