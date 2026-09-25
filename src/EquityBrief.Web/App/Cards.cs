using System.Globalization;
using System.Text;

namespace EquityBrief.Web.App;

// The pieces every screen is laid out in: a card per region, the stamp saying where the
// region's figures came from and as of when, and the key that says how to read a figure
// and what to take from it. A card holds a region's markup as the marks and the regions
// wrote it and adds only words and layout, so a card can be restyled without a figure
// inside it moving.
// see: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
// see: Every figure carries a plain-language key
public static class Cards
{
    // A region computed from the nightly store: a slate rule across its top and a stamp
    // naming the night its figures are from.
    public static string Computed(
        string label,
        string body,
        string? title = null,
        string? lede = null,
        string? stamp = null,
        string? id = null,
        string? region = null)
    {
        var card = new StringBuilder();

        card.Append("<section class=\"card\"");
        card.Append(id is null ? string.Empty : $" id=\"{Escaped(id)}\"");
        card.Append(region is null ? string.Empty : $" data-card=\"{Escaped(region)}\"");
        card.Append("><div class=\"card-h\"><div>");
        card.Append($"<div class=\"lbl\">{Escaped(label)}</div>");
        card.Append(title is null ? string.Empty : $"<h2>{title}</h2>");
        card.Append(lede is null ? string.Empty : $"<p class=\"lede\">{lede}</p>");
        card.Append("</div>");
        card.Append(stamp is null ? string.Empty : $"<div>{stamp}</div>");
        card.Append("</div>");
        card.Append($"<div class=\"card-b\">{body}</div></section>");

        return card.ToString();
    }

    // A section written by research or taken from a filing, which may be weeks old: a
    // plum-grey rule and a column on the left stating when it was written or filed.
    public static string Dated(
        string label,
        string dateKey,
        DateOnly? on,
        string body,
        string? title = null,
        string? section = null,
        bool filed = false,
        string? note = null,
        string? id = null)
    {
        var card = new StringBuilder();

        card.Append($"<section class=\"card spined{(filed ? " fund" : string.Empty)}\"");
        card.Append(id is null ? string.Empty : $" id=\"{Escaped(id)}\"");
        card.Append(section is null ? string.Empty : $" data-section=\"{Escaped(section)}\"");
        card.Append("><div class=\"spine\">");
        card.Append($"<div class=\"lbl\">{Escaped(label)}</div>");
        card.Append("<div class=\"dl\">");
        card.Append($"<span class=\"dl-k\">{Escaped(dateKey)}</span>");
        card.Append(on is { } date ? $"<b>{Day(date)}</b>" : "<b>not on file</b>");
        card.Append(note is null ? string.Empty : $"<span>{Escaped(note)}</span>");
        card.Append("</div></div><div class=\"main\">");
        card.Append(title is null ? string.Empty : $"<h2>{title}</h2>");
        card.Append($"<div class=\"card-b\">{body}</div></div></section>");

        return card.ToString();
    }

    // The stamp on a computed region: the night its figures are from.
    public static string Night(DateOnly? night) =>
        night is { } on
            ? $"<span class=\"stamp computed\">Computed for {Day(on)}</span>"
            : "<span class=\"stamp computed\">Computed from the store</span>";

    // How to read a figure, and what to take from it.
    public static string Key(string lead, string text, string? take = null) =>
        $"<div class=\"key\"><p><b>{lead}</b> {text}</p>" +
        (take is null ? string.Empty : $"<p class=\"take\"><b>What to take from it.</b> {take}</p>") +
        "</div>";

    // Words and their meanings, as a key's body or a glossary's.
    public static string Words(params (string Word, string Meaning)[] pairs) =>
        "<dl class=\"dg\">" + string.Concat(pairs.Select(pair => $"<dt>{pair.Word}</dt><dd>{pair.Meaning}</dd>")) + "</dl>";

    // The line naming what a screen is and as of when, which the shell moves into its
    // masthead and an exported report draws at its head.
    public static string Masthead(string title, string identity, string asOf) =>
        $"<div class=\"screen-mast\" data-title=\"{Escaped(title)}\">{identity}<span class=\"m-asof\">{asOf}</span></div>";

    public static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A dated screen's calendar: the night drawn, the stored nights either side of it, and a month
    // grid over the nights the screen draws, newest month first, in which a day holding a night is
    // underlined and opens it, the night drawn is marked, and every other day is no link at all.
    public static string NightPicker(DateOnly night, IReadOnlyList<DateOnly> held, string route, string newest)
    {
        if (held.Count == 0)
        {
            return string.Empty;
        }

        var before = held.Where(one => one < night).Select(one => (DateOnly?)one).Max();
        var after = held.Where(one => one > night).Select(one => (DateOnly?)one).Min();

        string Move(string move, string said, string mark, DateOnly? to) => to is { } day
            ? $"<a class=\"np-move\" data-move=\"{move}\" href=\"{route}{Day(day)}\" title=\"{said}, {Day(day)}\" aria-label=\"{said}, {Day(day)}\">{mark}</a>"
            : $"<span class=\"np-move\" data-move=\"{move}\" aria-disabled=\"true\">{mark}</span>";

        return $"<span class=\"night-picker\" data-route=\"{route}\" data-night=\"{Day(night)}\">"
            + Move("earlier", "The night before", "&#8249;", before)
            + $"<details class=\"np-cal\"><summary class=\"np-date\" aria-label=\"Choose a night to view\">{Day(night)}</summary>"
            + $"<div class=\"np-months\">{Months(night, held, route)}</div></details>"
            + Move("later", "The night after", "&#8250;", after)
            + (after is null ? string.Empty : $"<a class=\"np-newest\" href=\"{newest}\">newest</a>")
            + "</span>";
    }

    // One grid a month, from the month of the newest night held back to the oldest's, seven columns from
    // Monday.
    static string Months(DateOnly night, IReadOnlyList<DateOnly> held, string route)
    {
        var nights = held.ToHashSet();
        var drawn = new StringBuilder();
        var month = new DateOnly(held.Max().Year, held.Max().Month, 1);
        var oldest = new DateOnly(held.Min().Year, held.Min().Month, 1);

        for (; month >= oldest; month = month.AddMonths(-1))
        {
            drawn.Append("<div class=\"np-month\" data-month=\"").Append(month.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Append("\">");
            drawn.Append("<div class=\"np-caption\">").Append(month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)).Append("</div>");
            drawn.Append("<div class=\"np-grid\"><span class=\"np-wd\">Mo</span><span class=\"np-wd\">Tu</span><span class=\"np-wd\">We</span>");
            drawn.Append("<span class=\"np-wd\">Th</span><span class=\"np-wd\">Fr</span><span class=\"np-wd\">Sa</span><span class=\"np-wd\">Su</span>");
            drawn.Append(string.Concat(Enumerable.Repeat("<span class=\"np-pad\"></span>", ((int)month.DayOfWeek + 6) % 7)));

            for (var day = month; day.Month == month.Month; day = day.AddDays(1))
            {
                drawn.Append(nights.Contains(day)
                    ? $"<a class=\"np-day\" href=\"{route}{Day(day)}\" data-night=\"{Day(day)}\"{(day == night ? " aria-current=\"date\"" : string.Empty)}>{day.Day}</a>"
                    : $"<span class=\"np-off\">{day.Day}</span>");
            }

            drawn.Append("</div></div>");
        }

        return drawn.ToString();
    }

    public static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
