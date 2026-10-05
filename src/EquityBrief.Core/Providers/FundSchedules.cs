using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// One filing before the funds' first public N-PORT: its accession, the document carrying the trust's schedules of
// investments, the quarter's end those schedules are as of, the day it was filed and its form.
public sealed record FundSchedule(string Accession, string Document, DateOnly Period, DateOnly Filed, string Form);

// Reads a fund's schedule of investments out of the trust's N-Q for the quarter to 2018-12-31 and its annual report for
// the year to 2019-03-31, each one document carrying the schedules of dozens of the trust's funds as tables of company
// names, share counts and values with no identifier, page after page. A fund's schedule opens on a page headed by the
// schedule's title, the date it is as of and the fund's own name, runs on pages headed the same, and closes at its total
// of common stocks. The annual report's shareholder report carries a summary of a fund's schedule, its largest holdings
// and the rest as other securities, and the complete schedule further on, which is the one read.
// see: The funds' holdings before their first public N-PORT are read from their N-Q of 2018-12-31 and their annual report of 2019-03-31
public static class FundSchedules
{
    // The two filings, each read for both funds.
    public static IReadOnlyList<FundSchedule> Filings { get; } =
    [
        new("0001193125-19-059323", "d655820dnq.htm", new(2018, 12, 31), new(2019, 3, 1), "N-Q"),
        new("0001193125-19-167652", "d738391dncsr.htm", new(2019, 3, 31), new(2019, 6, 7), "N-CSR"),
    ];

    // Each wider index's fund as its schedule's pages name it.
    public static IReadOnlyDictionary<string, string> Titles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MID"] = "Core S&P Mid-Cap ETF",
        ["SML"] = "Core S&P Small-Cap ETF",
    };

    // How far past a page's heading its date and its fund's name are read.
    const int HeadingWidth = 3000;

    static readonly Regex Heading = new(@"Schedule(?:\s|<[^>]+>|&nbsp;|&#160;)+of(?:\s|<[^>]+>|&nbsp;|&#160;)+Investments", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Row = new(@"<tr\b.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex Cell = new(@"<td\b.*?</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex Fund = new(@"iShares\W*(.+?ETF)", RegexOptions.Compiled);
    static readonly Regex Count = new(@"^\(?[\d,]+\)?$", RegexOptions.Compiled);
    static readonly Regex Notes = new(@"(\s*\([a-z]{1,2}\))+[\s.]*$", RegexOptions.Compiled);

    // A fund's holdings of common stock as one filing's complete schedule names them, each with its name alone and the
    // footnote marks after it taken off. A document carrying no complete schedule for the fund as of the filing's quarter
    // end, a summary alone among them, is refused rather than read as holding nothing.
    public static FundSnapshot Parse(string html, string series, string title, FundSchedule filing)
    {
        var period = filing.Period.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var headings = Heading.Matches(html).Select(match => match.Index).ToArray();
        var complete = new List<FiledHolding>();
        var summaries = 0;
        var through = -1;

        foreach (var page in headings.Where(at => Heads(html, at, title, period)))
        {
            if (page < through || Read(html, page, headings, title, period) is not { } schedule)
            {
                continue;
            }

            through = schedule.End;

            if (schedule.Summary)
            {
                summaries++;
            }
            else if (schedule.Holdings.Count > complete.Count)
            {
                complete = schedule.Holdings;
            }
        }

        return complete.Count > 0
            ? new FundSnapshot(series, filing.Period, filing.Accession, complete)
            : throw new FormatException(
                $"The {filing.Form} {filing.Accession} carries no complete schedule of investments for the {title} as of {period}" +
                (summaries > 0 ? ", only a summary of it." : "."));
    }

    // Whether a page's heading is the fund's schedule as of the quarter's end: the first fund it names after the heading
    // is this one, and the date stands before it.
    static bool Heads(string html, int at, string title, string period)
    {
        var text = Text(html.Substring(at, Math.Min(HeadingWidth, html.Length - at)));
        var fund = Fund.Match(text);
        var dated = text.IndexOf(period, StringComparison.Ordinal);

        return fund.Success && fund.Groups[1].Value == title && dated >= 0 && dated < fund.Index;
    }

    // One schedule from its first page: none where a page heading for another fund, or any page heading before its common
    // stocks begin, comes first, since that page is no schedule's first.
    static (List<FiledHolding> Holdings, bool Summary, int End)? Read(string html, int page, int[] headings, string title, string period)
    {
        var holdings = new List<FiledHolding>();
        var (stocks, summary) = (false, false);
        var next = Array.FindIndex(headings, at => at > page);

        for (var row = Row.Match(html, page); row.Success; row = row.NextMatch())
        {
            for (; next >= 0 && next < headings.Length && headings[next] < row.Index; next++)
            {
                if (!stocks || !Heads(html, headings[next], title, period))
                {
                    return null;
                }
            }

            var cells = Cell.Matches(row.Value).Select(cell => Text(cell.Value)).Where(cell => cell.Length > 0).ToArray();

            if (cells.Length == 0)
            {
                continue;
            }

            if (cells.Length == 1 && cells[0].StartsWith("Common Stocks", StringComparison.OrdinalIgnoreCase))
            {
                stocks = true;

                continue;
            }

            if (!stocks)
            {
                continue;
            }

            if (cells[0].StartsWith("Total Common Stocks", StringComparison.OrdinalIgnoreCase))
            {
                return (holdings, summary, row.Index + row.Length);
            }

            if (cells[0].StartsWith("Other securities", StringComparison.OrdinalIgnoreCase))
            {
                summary = true;

                continue;
            }

            var name = Notes.Replace(cells[0], string.Empty).Trim();
            var amounts = cells.Where(cell => Count.IsMatch(cell)).ToArray();

            // A row's first count is the shares held and its last their value in dollars.
            if (amounts.Length >= 2 && name.Any(char.IsLetter) && !name.EndsWith('%'))
            {
                holdings.Add(new FiledHolding(name, null, null, FundSnapshots.CommonEquity, Amount(amounts[0]), Amount(amounts[^1])));
            }
        }

        return null;
    }

    // A count as the schedule prints it, its thousands marked by commas, none where it cannot be read.
    static decimal? Amount(string printed) =>
        decimal.TryParse(printed.Trim('(', ')'), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    // A piece of the document as its reader sees it: the tags taken out, the entities read and the spaces closed up.
    static string Text(string html) =>
        Space.Replace(WebUtility.HtmlDecode(Tag.Replace(html, " ")).Replace(' ', ' '), " ").Trim();
}
