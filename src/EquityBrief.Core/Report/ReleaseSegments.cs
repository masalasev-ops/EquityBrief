using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Report;

// The segment table read from a results release's own words, for a quarter the release reports before its filing is
// stored. The release lays each table out as a heading closed by a colon or followed by its columns' years, then each
// segment's name with its figures, the quarter's first and the same quarter a year earlier's next, then a total where it
// states one: its revenue table under a heading naming revenue or sales, and its segments' operating profit under one
// naming operating profit or income. Every figure is read at the scale that makes the table's total, or the segments' own
// sum where it states none, the quarter's reported revenue. The table stands only where the segments sum to the table's
// own total within half a unit of its last decimal place a figure, that total is the quarter's revenue within a tenth of a
// per cent and no two of its segments share a name; otherwise none is read and the filing's table stands. The column after
// the quarter's is read as the same quarter a year earlier only where its total is that quarter's stored revenue within a
// per cent, and no year-earlier figure is read otherwise.
// see: A newer quarter's segments are read from its results release's own tables until its filing is stored, labelled so
public static partial class ReleaseSegments
{
    // What the table is drawn as coming from.
    public const string Report = "the results release";

    // The source a facts file names the table's figures by, which the page reads to tell a segment commentary written from
    // them from one written from the filing's.
    public const string FactSource = "results release";

    // How near the table's total must come to the quarter's reported revenue, as a share of it.
    public const decimal TotalShare = 0.001m;

    // How near the year-earlier column's total must come to that quarter's stored revenue, as a share of it, for the column
    // to be read as the same quarter a year earlier; wider than the quarter's own, since a release can restate the year
    // before slightly.
    public const decimal YearEarlierShare = 0.01m;

    // The most entries a table is read for before it is taken for prose.
    const int MostEntries = 30;

    // The most words a segment's name runs to before its figures.
    const int MostNameWords = 8;

    static readonly decimal[] Scales = [1m, 1_000m, 1_000_000m, 1_000_000_000m];

    // One line of a release's table: the name it opens on and its figures in order, percentages left out.
    sealed record Entry(string Name, IReadOnlyList<decimal> Figures, int Decimals);

    public static SegmentReading? Read(string text, DateOnly quarter, decimal? reportedRevenue, decimal? yearEarlierRevenue = null)
    {
        if (reportedRevenue is not { } revenue || revenue <= 0m || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var flat = Spaces().Replace(text, " ");

        foreach (Match heading in RevenueHeading().Matches(flat))
        {
            var (entries, total) = Table(flat, heading.Index + heading.Length);

            if (entries.Count < 2 || entries.Any(entry => entry.Figures.Count == 0)
                || entries.Select(entry => Footnote().Replace(entry.Name, string.Empty).Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() < entries.Count)
            {
                continue;
            }

            // A table stating no total is read against its segments' own sum, column by column.
            var whole = total is { Figures.Count: > 0 } stated
                ? stated
                : new Entry("Total", [.. Enumerable.Range(0, entries.Min(entry => entry.Figures.Count)).Select(at => entries.Sum(entry => entry.Figures[at]))], entries.Max(entry => entry.Decimals));
            var scales = Scales.Where(times => Math.Abs((whole.Figures[0] * times) - revenue) <= revenue * TotalShare).ToArray();
            var sum = entries.Sum(entry => entry.Figures[0]);
            var tolerance = (entries.Count + 1) * Half(entries.Max(entry => entry.Decimals));

            if (scales.Length == 0 || Math.Abs(sum - whole.Figures[0]) > tolerance)
            {
                continue;
            }

            var times = scales[0];

            // The column after the quarter's is the same quarter a year earlier only where its total is that quarter's stored
            // revenue, which a column of adjusted figures or of the year to date is not.
            var earlier = whole.Figures.Count > 1
                && entries.All(entry => entry.Figures.Count > 1)
                && yearEarlierRevenue is { } before
                && before > 0m
                && Math.Abs((whole.Figures[1] * times) - before) <= before * YearEarlierShare;

            // The segments' operating profit, read from the first table under its heading naming every segment.
            var earned = EarningsHeading().Matches(flat)
                .Select(found => Table(flat, found.Index + found.Length).Entries)
                .FirstOrDefault(rows => entries.All(entry => rows.Any(row => Same(row.Name, entry.Name) && row.Figures.Count > 0)))
                ?? [];

            decimal? Figure(Entry? entry, int at) => entry is not null && entry.Figures.Count > at && (at == 0 || earlier) ? entry.Figures[at] * times : null;

            var company = whole.Figures[0] * times;

            return new SegmentReading(
                Report,
                quarter,
                3,
                company,
                sum * times,
                [
                    .. entries.Select(entry =>
                    {
                        var profit = earned.FirstOrDefault(row => Same(row.Name, entry.Name));

                        return new SegmentRow(
                            entry.Name,
                            Figure(entry, 0)!.Value,
                            Figure(entry, 1),
                            Percents.FromFraction(QuarterFetch.Grown(Figure(entry, 0), Figure(entry, 1))),
                            Figure(profit, 0),
                            Figure(profit, 1),
                            Percents.FromFraction(QuarterFetch.Grown(Figure(profit, 0), Figure(profit, 1))),
                            Percents.FromFraction(QuarterFetch.Margined(Figure(entry, 0), company))!.Value);
                    }),
                ],
                null);
        }

        return null;
    }

    // The entries of the table opening at a place and its total: each name with the figures after it, until the total's
    // line, another heading closed by a colon, or words running on as prose.
    static (IReadOnlyList<Entry> Entries, Entry? Total) Table(string flat, int from)
    {
        var entries = new List<Entry>();
        var name = new List<string>();
        var figures = new List<decimal>();
        var decimals = 0;

        foreach (Match token in Token().Matches(flat[from..]))
        {
            var said = token.Value;

            if (said == "$" || Footnote().IsMatch(said) && Footnote().Replace(said, string.Empty).Length == 0)
            {
                continue;
            }

            if (Number().Match(said) is { Success: true } number)
            {
                if (name.Count == 0)
                {
                    // Figures with no name before them are a column heading's years, read past.
                    continue;
                }

                if (!said.EndsWith('%'))
                {
                    var digits = number.Groups["digits"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
                    var value = decimal.Parse(digits, NumberStyles.Number, CultureInfo.InvariantCulture);

                    figures.Add(number.Groups["open"].Success ? -value : value);
                    decimals = Math.Max(decimals, digits.Contains('.') ? digits.Length - digits.IndexOf('.') - 1 : 0);
                }

                continue;
            }

            if (figures.Count > 0)
            {
                var line = new Entry(string.Join(" ", name), [.. figures], decimals);

                if (line.Name.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
                {
                    return (entries, line);
                }

                entries.Add(line);
                (name, figures, decimals) = ([], [], 0);

                if (entries.Count > MostEntries)
                {
                    return (entries, null);
                }
            }

            if (said.EndsWith(':') && entries.Count > 0)
            {
                return (entries, null);
            }

            name.Add(said);

            if (name.Count > MostNameWords)
            {
                return (entries, null);
            }
        }

        return (entries, null);
    }

    // Two names of one segment, read without case or the marks a footnote leaves on them.
    static bool Same(string one, string other) =>
        string.Equals(Footnote().Replace(one, string.Empty).Trim(), Footnote().Replace(other, string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    // Half a unit of a figure's last decimal place.
    static decimal Half(int decimals) => Enumerable.Repeat(0.1m, decimals).Aggregate(0.5m, (half, tenth) => half * tenth);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // A heading naming revenue or sales, closed by a colon or followed by its columns' years.
    [GeneratedRegex(@"\b(?:Net revenues|Net revenue|Revenues|Revenue|Net sales|Sales)\s*\*?\s*(?::|(?=(?:\s+(?:19|20)\d{2}){2}\b))", RegexOptions.IgnoreCase)]
    private static partial Regex RevenueHeading();

    // A heading naming the segments' operating profit or income, closed the same way.
    [GeneratedRegex(@"\b(?:Segment operating profit|Segment operating income|Operating profit|Operating income|Segment profit)\s*\*?\s*(?::|(?=(?:\s+(?:19|20)\d{2}){2}\b))", RegexOptions.IgnoreCase)]
    private static partial Regex EarningsHeading();

    [GeneratedRegex(@"\$|\(?-?[0-9][0-9,]*(?:\.[0-9]+)?\)?%?|[^\s$]+")]
    private static partial Regex Token();

    [GeneratedRegex(@"^(?<open>\()?-?(?<digits>[0-9][0-9,]*(?:\.[0-9]+)?)\)?%?$")]
    private static partial Regex Number();

    [GeneratedRegex(@"\(\d\)|\*+")]
    private static partial Regex Footnote();
}
