using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Report;

// A quarter's income lines as its results release's own tables state them, each none where no row is read.
public sealed record ReleaseQuarter(DateOnly Quarter, decimal? Revenue, decimal? GrossProfit, decimal? OperatingIncome, decimal? NetIncome);

// The same quarter a year earlier as its filing states it, which a release's year-earlier column is checked against.
public sealed record YearEarlierFiled(decimal? Revenue, decimal? GrossProfit, decimal? OperatingIncome, decimal? NetIncome);

// A quarter's lines read from its results release by fixed line names, for a quarter the release reports before its filing
// is stored. A row is a line's name followed at once by its figures. Its scale is the nearest unit note before it, "in
// thousands", "in millions" or "in billions", and with none the row is not read. A row of two figures or four, the quarter
// and the same quarter a year earlier then the year to date's two, has its first two ordered by the nearest pair of
// consecutive years before it, the later year's column being the quarter's; a row of five is a trend of five quarters,
// newest first; a row of any other shape is not read, and a change in per cent closing a row is no figure of it. A row is
// read only where its year-earlier column is that quarter as filed, within a per cent or half a unit of the row's scale,
// so a nine months' column, an adjusted figure or another table's row is never read as the quarter's; where no pair of
// years stands, the column of the first two holding the filed figure is the year before. A line whose year-earlier quarter
// is not filed is not read.
// see: A quarter's lines are drawn from its filing where stored and then from its results release's own tables, and are not on file until the filing otherwise
public static partial class ReleaseFigures
{
    public const decimal YearEarlierShare = 0.01m;

    // How far before a row its unit note and its column heading's years are looked for.
    public const int HeadingReach = 4000;

    // The quarters a trend table's row states, newest first.
    public const int TrendQuarters = 5;

    // Each line's names in the order they are tried, the company's whole figure before a part of it.
    public static IReadOnlyList<string> RevenueNames { get; } =
        ["Total revenues, net of interest expense", "Total revenues", "Total revenue", "Total net revenues", "Total net revenue", "Total net sales", "Net revenues", "Net revenue", "Net sales", "Revenues", "Revenue", "Total sales", "Sales"];

    public static IReadOnlyList<string> GrossProfitNames { get; } = ["Gross profit"];

    public static IReadOnlyList<string> OperatingIncomeNames { get; } = ["Operating income", "Income from operations", "Operating income (loss)", "Operating profit"];

    // Net income attributable to the company's own holders first, as its filing states it, before the whole of it.
    public static IReadOnlyList<string> NetIncomeNames { get; } = ["Net income attributable to", "Net earnings attributable to", "Net income (loss) attributable to", "Net income", "Net earnings", "Net income (loss)"];

    public static ReleaseQuarter? Read(string text, DateOnly quarter, YearEarlierFiled before)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var flat = Spaces().Replace(text, " ");
        var read = new ReleaseQuarter(
            quarter,
            Line(flat, RevenueNames, before.Revenue),
            Line(flat, GrossProfitNames, before.GrossProfit),
            Line(flat, OperatingIncomeNames, before.OperatingIncome),
            Line(flat, NetIncomeNames, before.NetIncome));

        return read.Revenue is null && read.GrossProfit is null && read.OperatingIncome is null && read.NetIncome is null ? null : read;
    }

    // The first row under one of the names whose year-earlier column is the filed year-earlier figure.
    static decimal? Line(string flat, IReadOnlyList<string> names, decimal? filedBefore)
    {
        if (filedBefore is not { } prior)
        {
            return null;
        }

        foreach (var name in names)
        {
            foreach (Match row in Row(name).Matches(flat))
            {
                if (Figures(flat, row.Index + row.Length) is not { Count: >= 2 } figures || Scale(flat, row.Index) is not { } scale)
                {
                    continue;
                }

                bool Filed(decimal figure) => Math.Abs((figure * scale) - prior) <= Math.Max(Math.Abs(prior) * YearEarlierShare, scale / 2);

                // Two figures or four, the quarter's pair and then the year to date's: the heading's years order the pair
                // where they stand, and with none the column holding the filed year-earlier figure is the year before. Five,
                // a trend of five quarters newest first, its fifth the year-earlier quarter. No other shape is read.
                int? current = figures.Count switch
                {
                    2 or 4 => Years(flat, row.Index) switch
                    {
                        true => Filed(figures[0]) ? 1 : null,
                        false => Filed(figures[1]) ? 0 : null,
                        null => Filed(figures[1]) ? 0 : Filed(figures[0]) ? 1 : null,
                    },
                    TrendQuarters => Filed(figures[TrendQuarters - 1]) ? 0 : null,
                    _ => null,
                };

                if (current is { } at)
                {
                    return figures[at] * scale;
                }
            }
        }

        return null;
    }

    // A row opening on the name and followed by its figures. The name starts the row: before it stands the row before's last
    // figure, a closing bracket, a per cent sign, a colon, an empty cell's mark or the text's start, never a word, so a
    // segment's or an adjusted line naming the same words is not read. A name ending in "attributable to" runs on to the
    // holders it names, never the noncontrolling interests.
    const string RowStart = @"(?<=(?:^|[0-9)%:�–—])\s*)";

    static Regex Row(string name) => name.EndsWith("attributable to", StringComparison.Ordinal)
        ? new Regex(RowStart + Regex.Escape(name) + @" (?!noncontrolling|non-controlling)[A-Za-z0-9.,&'’\- ]{1,60}?(?=\s*(?:\(\d\)\s*)?(?:\$\s*)?\(?-?[0-9][0-9,]*(?:\.[0-9]+)?\)?(?:\s|$))", RegexOptions.IgnoreCase)
        : new Regex(RowStart + Regex.Escape(name) + @"(?=\s*(?:\(\d\)|\*+)?\s*(?:\$\s*)?\(?-?[0-9][0-9,]*(?:\.[0-9]+)?\)?(?:\s|$))", RegexOptions.IgnoreCase);

    // The figures following a row's name, a dollar sign read past, a footnote mark read past only where it follows the name
    // itself, and a percentage or a word ending them.
    static IReadOnlyList<decimal>? Figures(string flat, int from)
    {
        var figures = new List<decimal>();
        var opened = false;

        foreach (Match token in Token().Matches(flat[from..Math.Min(flat.Length, from + 200)]))
        {
            var said = token.Value;

            if (said is "$" || (!opened && figures.Count == 0 && Footnote().IsMatch(said)))
            {
                opened |= said is "$";
                continue;
            }

            // A per cent sign standing apart closes a change column, whose figure is no amount.
            if (said == "%")
            {
                if (figures.Count > 0)
                {
                    figures.RemoveAt(figures.Count - 1);
                }

                break;
            }

            if (Number().Match(said) is not { Success: true } number || said.EndsWith('%'))
            {
                break;
            }

            var value = decimal.Parse(number.Groups["digits"].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);

            figures.Add(number.Groups["open"].Success || said.StartsWith('-') ? -value : value);
        }

        return figures;
    }

    // The scale of the nearest unit note before a place, none where none stands within reach.
    static decimal? Scale(string flat, int at)
    {
        var from = Math.Max(0, at - HeadingReach);

        return UnitNote().Matches(flat[from..at]).LastOrDefault() is { } note
            ? note.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "thousands" => 1_000m,
                "millions" => 1_000_000m,
                _ => 1_000_000_000m,
            }
            : null;
    }

    // Whether the nearest pair of consecutive years before a place puts the later year second, and none where no pair of
    // years one apart stands within reach.
    static bool? Years(string flat, int at)
    {
        var from = Math.Max(0, at - HeadingReach);

        if (YearPair().Matches(flat[from..at]).LastOrDefault() is not { } pair)
        {
            return null;
        }

        var first = int.Parse(pair.Groups["first"].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(pair.Groups["second"].Value, CultureInfo.InvariantCulture);

        return second == first + 1 ? true : first == second + 1 ? false : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\$|\(?-?[0-9][0-9,]*(?:\.[0-9]+)?\)?%?|[^\s$]+")]
    private static partial Regex Token();

    [GeneratedRegex(@"^(?<open>\()?-?(?<digits>[0-9][0-9,]*(?:\.[0-9]+)?)\)?%?$")]
    private static partial Regex Number();

    [GeneratedRegex(@"^(?:\(\d\)|\*+)$")]
    private static partial Regex Footnote();

    [GeneratedRegex(@"\bin\s+(?<unit>thousands|millions|billions)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnitNote();

    [GeneratedRegex(@"\b(?<first>(?:19|20)\d{2})\s+(?<second>(?:19|20)\d{2})\b")]
    private static partial Regex YearPair();
}
