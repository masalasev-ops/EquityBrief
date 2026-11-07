using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.History;

// One holding of an older schedule as the trust filed it: the company's name with its footnote marks taken off, the industry
// heading it was filed under, the shares held and their value in dollars.
public sealed record LongHolding(string Name, string? Industry, decimal? Shares, decimal? Value)
{
    // The price a share was valued at, none where the schedule states no shares or no value.
    public decimal? ValuePerShare => Shares is > 0m && Value is { } value ? value / Shares.Value : null;
}

// One fund's complete schedule at one quarter end, read off one filing.
public sealed record LongSchedule(string Index, DateOnly Period, string Accession, IReadOnlyList<LongHolding> Holdings);

// Reads a fund's complete schedule of investments out of one of the trust's N-Q, N-CSR or N-CSRS documents from 2004-12-31
// to 2018-09-30. Each document carries the schedules of dozens of the trust's funds as tables, each schedule opening on a page
// headed Schedule of Investments with the fund's name and the date it is as of, its common stocks under industry headings
// each carrying its share of the fund, and closing at its total of common stocks; the older N-Q names the fund on its first
// page alone. A shareholder report carries a summary of each schedule, its largest holdings and the rest as other securities,
// and the complete schedule further on, which is the one read; a document carrying no complete schedule for the fund at the
// quarter end is refused rather than read as holding nothing.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
public static class LongSchedules
{
    // Each index's fund as its schedules name it, before and after the trust renamed its core funds in 2012.
    public static IReadOnlyDictionary<string, Regex> Titles { get; } = new Dictionary<string, Regex>(StringComparer.Ordinal)
    {
        ["GSPC"] = Title(@"S&P\s+500\s+(?:Index\s+Fund|ETF)"),
        ["MID"] = Title(@"S&P\s+(?:MidCap\s+400\s+Index\s+Fund|Mid-Cap\s+ETF)"),
        ["SML"] = Title(@"S&P\s+(?:SmallCap\s+600\s+Index\s+Fund|Small-Cap\s+ETF)"),
    };

    static Regex Title(string fund) => new(@"\biShares\W{0,12}(?:Core\s+)?" + fund, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Any of the trust's funds as a page names it.
    static readonly Regex AnyFund = new(@"\biShares\W{0,12}[A-Z][^%$]{2,80}?(?:Index\s+Fund|ETF|Fund)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // How far past a page's heading its fund's name and its date are read, in the document's own characters.
    const int HeadingWidth = 3000;

    static readonly Regex Heading = new(@"Schedule(?:\s|<[^>]+>|&nbsp;|&#160;)+of(?:\s|<[^>]+>|&nbsp;|&#160;)+Investments", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Row = new(@"<tr\b.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex Cell = new(@"<td\b.*?</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex Count = new(@"^\$?\s*\(?[\d,]+\)?$", RegexOptions.Compiled);
    // A name's footnote marks: a number or a letter in brackets, or a bare number or small letter, one or several, set after the
    // name's last word.
    static readonly Regex Notes = new(@"((?<=[A-Za-z.)])\s+(?:\d{1,2}|[a-z])(?:\s*,\s*(?:\d{1,2}|[a-z]))*|\s*\(\d{1,2}\)|\s*\([a-z]{1,2}\))+[\s.]*$", RegexOptions.Compiled);
    static readonly Regex Industry = new(@"^(.*?[A-Za-z)])[\W_]*[\d.]+\s*%$", RegexOptions.Compiled);

    // The fund's complete schedule at the quarter end the filing reports.
    public static LongSchedule Parse(string html, string index, DateOnly period, string accession)
    {
        var title = Titles[index];
        var date = period.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var headings = Heading.Matches(html).Select(match => (At: match.Index, Fund: FundAt(html, match.Index))).ToArray();
        var complete = new List<LongHolding>();
        var summaries = 0;
        var through = -1;

        foreach (var (at, fund) in headings)
        {
            if (at < through || fund is null || !title.IsMatch(fund) || !Dated(html, at, date) || Read(html, at, headings, title) is not { } schedule)
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
            ? new LongSchedule(index, period, accession, complete)
            : throw new FormatException(
                FormattableString.Invariant($"The filing {accession} carries no complete schedule of investments for the {index}'s fund as of {date}") +
                (summaries > 0 ? ", only a summary of it." : "."));
    }

    // The fund a page's heading names: the first named after it, or where none is, the last named in the page's header just
    // before it, as the later N-Q sets the fund's name above the heading; none where neither names one.
    static string? FundAt(string html, int at)
    {
        var after = Text(html.Substring(at, Math.Min(HeadingWidth, html.Length - at)));

        if (AnyFund.Match(after) is { Success: true } named)
        {
            return named.Value;
        }

        var before = Text(html.Substring(Math.Max(0, at - HeaderWidth), Math.Min(HeaderWidth, at)));

        return AnyFund.Matches(before) is { Count: > 0 } above ? above[^1].Value : null;
    }

    // How far before a heading the page's header is read for the fund's name, in the document's own characters.
    const int HeaderWidth = 1500;

    static bool Dated(string html, int at, string date) =>
        Text(html.Substring(at, Math.Min(HeadingWidth, html.Length - at))).Contains(date, StringComparison.OrdinalIgnoreCase);

    // One schedule from its first page to its total of common stocks: none where a page heading naming another fund comes
    // first, since the schedule then has no total of its own.
    static (List<LongHolding> Holdings, bool Summary, int End)? Read(string html, int page, (int At, string? Fund)[] headings, Regex title)
    {
        var holdings = new List<LongHolding>();
        var (stocks, summary) = (false, false);
        string? industry = null;
        var next = Array.FindIndex(headings, heading => heading.At > page);

        for (var row = Row.Match(html, page); row.Success; row = row.NextMatch())
        {
            for (; next >= 0 && next < headings.Length && headings[next].At < row.Index; next++)
            {
                if (headings[next].Fund is { } other && !title.IsMatch(other))
                {
                    return null;
                }
            }

            var cells = Cell.Matches(row.Value).Select(cell => Text(cell.Value)).Where(cell => cell.Length > 0).ToArray();

            if (cells.Length == 0)
            {
                continue;
            }

            if (cells[0].StartsWith("Total Common Stocks", StringComparison.OrdinalIgnoreCase))
            {
                return stocks ? (holdings, summary, row.Index + row.Length) : null;
            }

            if (cells[0].StartsWith("Common Stocks", StringComparison.OrdinalIgnoreCase))
            {
                stocks = true;

                continue;
            }

            if (!stocks)
            {
                continue;
            }

            if (cells[0].StartsWith("Other securities", StringComparison.OrdinalIgnoreCase))
            {
                summary = true;

                continue;
            }

            if (cells.Length == 1 && Industry.Match(cells[0]) is { Success: true } heading)
            {
                industry = heading.Groups[1].Value.Trim();

                continue;
            }

            var name = Notes.Replace(cells[0], string.Empty).Trim();
            var amounts = cells.Skip(1).Where(cell => Count.IsMatch(cell)).ToArray();

            // A row's last two counts are the shares held and their value in dollars, any footnote mark standing before them.
            if (amounts.Length >= 2 && name.Any(char.IsLetter) && !name.EndsWith('%'))
            {
                holdings.Add(new LongHolding(name, industry, Amount(amounts[^2]), Amount(amounts[^1])));
            }
        }

        return null;
    }

    static decimal? Amount(string printed) =>
        decimal.TryParse(printed.Trim('$', ' ', '(', ')'), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    // A piece of the document as its reader sees it: the tags taken out, the entities read and the spaces closed up.
    static string Text(string html) =>
        Space.Replace(WebUtility.HtmlDecode(Tag.Replace(html, " ")).Replace(' ', ' '), " ").Trim();
}
