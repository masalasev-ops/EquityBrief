using System.Globalization;
using System.Net;
using System.Text;

namespace EquityBrief.Worker.Sweep;

// One of the strongest settings a sweep read where none met its floors: its key, its trades and the years its edge
// stood above nothing, its edge, and how far it falls short of each floor, nought where it meets one.
public sealed record Strongest(string Key, int Trades, int YearsBeating, double? Edge)
{
    public int TradesShort => Math.Max(0, FamilySweep.TradeFloor - Trades);

    public int YearsShort => Math.Max(0, FamilySweep.YearsBeating - YearsBeating);
}

// What a sweep's report states where no setting meets its floors: the strongest settings it read, each with the floors
// it falls short of and by how much, and what could be tried next, every sentence written by code from a fixed pattern.
// Nothing is proposed or registered, and the family keeps its provisional settings and keeps listing until the
// operator rules on it.
// see: No family on any index is set aside or hidden by a test result without the operator's word
public static class SweepNonePassed
{
    // How many of the strongest settings the report states.
    public const int Shown = 5;

    // The highest edges among the settings read, ties to the key, at most five.
    public static IReadOnlyList<Strongest> StrongestOf(IEnumerable<Strongest> settings) =>
    [
        .. settings
            .Where(one => one.Edge is not null)
            .OrderByDescending(one => one.Edge)
            .ThenBy(one => one.Key, StringComparer.Ordinal)
            .Take(Shown),
    ];

    // Each floor the strongest settings fall short of, with how many do and the least and the most they are short by.
    public static IReadOnlyList<string> FloorsMissed(IReadOnlyList<Strongest> strongest)
    {
        var lines = new List<string>();
        var trades = strongest.Where(one => one.TradesShort > 0).Select(one => one.TradesShort).ToArray();
        var years = strongest.Where(one => one.YearsShort > 0).Select(one => one.YearsShort).ToArray();

        if (trades.Length > 0)
        {
            lines.Add(Invariant($"{trades.Length} of the {strongest.Count} fall short of {FamilySweep.TradeFloor} trades, by {Span(trades)}: a level of a dial that lists more, or a history over more names, would hold more trades."));
        }

        if (years.Length > 0)
        {
            lines.Add(Invariant($"{years.Length} of the {strongest.Count} fall short of an edge above nothing in {FamilySweep.YearsBeating} of the 8 years, by {Span(years)} year(s): the years each fell short in are its yearly edges in the table of every setting."));
        }

        return lines;
    }

    // The dials whose level in a setting sits at an end of its grid, each with the level a further reading could take
    // past it; a dial read at one level alone has no end to read past.
    public static IReadOnlyList<string> GridEnds(IReadOnlyList<(string Dial, IReadOnlyList<string> Levels)> dials, IReadOnlyList<int> setting)
    {
        var lines = new List<string>();

        for (var dial = 0; dial < dials.Count; dial++)
        {
            var (name, levels) = dials[dial];

            if (levels.Count < 2)
            {
                continue;
            }

            if (setting[dial] == 0)
            {
                lines.Add(Invariant($"The strongest setting reads {name} at {levels[0]}, the lowest level the grid holds: a level below it could be read."));
            }
            else if (setting[dial] == levels.Count - 1)
            {
                lines.Add(Invariant($"The strongest setting reads {name} at {levels[^1]}, the highest level the grid holds: a level above it could be read."));
            }
        }

        return lines;
    }

    // The ideas' run's tests that fit a family, named so the operator can have any of them run on it; a sweep reads no
    // ideas' run, so the page cannot say which of them have run.
    public static string Ideas(IReadOnlyList<string> rules) =>
        rules.Count == 0
            ? "The ideas' run reads the swing families alone, so none of its tests fits this family."
            : "The ideas' run's tests that fit this family, each run by hand on it and judged against its rule: " + string.Join("; ", rules) + ". A sweep reads no ideas' run, so this page cannot say which of them have run on it.";

    // The section the report draws in the proposal's place: what was found, the strongest settings with their
    // shortfalls, each edge in the report's own form, and what could be tried next.
    public static string Section(IReadOnlyList<Strongest> strongest, IReadOnlyList<string> next, string provisionalTable, Func<double?, string> edge)
    {
        var html = new StringBuilder();

        html.Append(Invariant($"<p class=\"none-passed\" data-shown=\"{strongest.Count}\">No setting has at least {FamilySweep.TradeFloor} trades and an edge above nothing in at least {FamilySweep.YearsBeating} of the 8 years, so nothing is proposed. The family keeps its provisional settings and keeps listing until the operator rules on it, and this page brings the operator the {strongest.Count} strongest settings the sweep read and what could be tried next.</p>"));
        html.Append(provisionalTable);
        html.Append("<h3>The strongest settings</h3><p>The highest edges among every setting read, each with the floors it falls short of and by how much.</p>");
        html.Append(Invariant($"<div class=\"table\"><table class=\"strongest\"><thead><tr><th>Setting</th><th>Trades</th><th>Short of {FamilySweep.TradeFloor} trades</th><th>Years above nothing</th><th>Short of {FamilySweep.YearsBeating} years</th><th>Edge</th></tr></thead><tbody>"));

        foreach (var one in strongest)
        {
            html.Append(Invariant($"<tr data-key=\"{Esc(one.Key)}\" data-trades-short=\"{one.TradesShort}\" data-years-short=\"{one.YearsShort}\"><td>{Esc(one.Key)}</td><td class=\"num\">{one.Trades:N0}</td><td class=\"num\">{one.TradesShort:N0}</td><td class=\"num\">{one.YearsBeating} of 8</td><td class=\"num\">{one.YearsShort}</td><td class=\"num\">{edge(one.Edge)}</td></tr>"));
        }

        html.Append("</tbody></table></div><h3>What could be tried next</h3><ul class=\"next\">");

        foreach (var line in next)
        {
            html.Append(Invariant($"<li>{Esc(line)}</li>"));
        }

        return html.Append("</ul>").ToString();
    }

    static string Span(int[] values) =>
        values.Min() == values.Max()
            ? values.Min().ToString("N0", CultureInfo.InvariantCulture)
            : Invariant($"{values.Min():N0} to {values.Max():N0}");

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
