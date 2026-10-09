using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// One filing a day's index lists: the filer's CIK padded to ten digits, its name as the index writes it, the form, the
// day it was filed and its accession number.
public sealed record DailyFiling(string Cik, string Company, string Form, DateOnly Filed, string Accession);

// The archive's index of one day's filings, and the items one 8-K's own index page states.
//
// Written against the archive's daily indexes of 2026-09-01 to 2026-09-04 and two 8-K index pages, captured before
// this was written. A day's index is a text file whose lines read `CIK|Company Name|Form Type|Date Filed|File Name`
// under that column line and a line of dashes, the file name being `edgar/data/<cik>/<accession>.txt`, so the
// accession is read off it. The archive posts a day's index at about 22:00 New York time on the day it covers. A day
// it posted none for, a weekend, a holiday or a day not yet posted, answers 403 with its storage's own refusal in XML,
// where a request the archive itself refuses answers 403 with a page of its own, so the two are told apart by the
// body and never by the status.
// see: The night refreshes the facts of the members that filed since its last read of the archive's daily index, after the close under its own limit
public static class SecEdgarDailyIndex
{
    public const string Columns = "CIK|Company Name|Form Type|Date Filed|File Name";

    // The forms a refresh asks a filer's facts for: the quarterly and annual reports and their amendments, and the
    // results announcement and its amendment, an announcement read as one only where its items carry results.
    public static IReadOnlyList<string> Reports { get; } = ["10-Q", "10-K", "10-Q/A", "10-K/A"];

    public static IReadOnlyList<string> Announcements { get; } = ["8-K", "8-K/A"];

    public static string PathOf(DateOnly day) =>
        "Archives/edgar/daily-index/" + day.Year.ToString(CultureInfo.InvariantCulture)
        + "/QTR" + ((day.Month + 2) / 3).ToString(CultureInfo.InvariantCulture)
        + "/master." + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".idx";

    // The storage's own answer for a file it does not hold, which is a day the archive posted no index for.
    public static bool NonePosted(string body) =>
        body.Contains("<Code>AccessDenied</Code>", StringComparison.Ordinal)
        || body.Contains("<Code>NoSuchKey</Code>", StringComparison.Ordinal);

    public static IReadOnlyList<DailyFiling> Parse(string text, DateOnly day)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var columns = Array.FindIndex(lines, line => string.Equals(line.Trim(), Columns, StringComparison.Ordinal));

        if (columns < 0 || columns + 1 >= lines.Length || !lines[columns + 1].StartsWith("---", StringComparison.Ordinal))
        {
            throw new FormatException(
                FormattableString.Invariant($"The archive's index for {day:yyyy-MM-dd} carries no column line over a line of dashes, so its filings cannot be read."));
        }

        var filings = new List<DailyFiling>();

        foreach (var line in lines.Skip(columns + 2))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var parts = line.Split('|');

            if (parts.Length < 5
                || parts[0].Length == 0
                || !parts[0].All(char.IsAsciiDigit)
                || !DateOnly.TryParseExact(parts[^2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filed)
                || Accession.Match(parts[^1]) is not { Success: true } accession)
            {
                throw new FormatException(
                    FormattableString.Invariant($"A line of the archive's index for {day:yyyy-MM-dd} carries no CIK, filing day or accession it can be read by: {line.Trim()}"));
            }

            filings.Add(new DailyFiling(
                parts[0].PadLeft(10, '0'),
                string.Join('|', parts[1..^3]).Trim(),
                parts[^3].Trim(),
                filed,
                accession.Groups["accession"].Value));
        }

        return filings;
    }

    // The item numbers an 8-K's index page states under its Items heading, in its order, none where it states none.
    public static IReadOnlyList<string> Items(string indexPage)
    {
        var block = ItemsBlock.Match(indexPage);

        return block.Success
            ? [.. Item.Matches(block.Groups["items"].Value).Select(match => match.Groups["number"].Value)]
            : [];
    }

    // An announcement carrying results: item 2.02 read as a whole item, so 2.021 would not answer for it.
    public static bool CarriesResults(IReadOnlyList<string> items) =>
        items.Contains(SecEdgarArchive.ResultsItem, StringComparer.Ordinal);

    static readonly Regex Accession = new(@"/(?<accession>\d{10}-\d{2}-\d{6})\.txt\s*$", RegexOptions.CultureInvariant);

    static readonly Regex ItemsBlock = new(
        @"<div class=""infoHead"">Items</div>\s*<div class=""info"">(?<items>.*?)</div>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    static readonly Regex Item = new(@"Item\s+(?<number>\d+\.\d{2})(?!\d)", RegexOptions.CultureInvariant);
}
