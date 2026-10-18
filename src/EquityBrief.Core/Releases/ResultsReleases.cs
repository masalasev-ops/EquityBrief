using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Releases;

// One results announcement a reader found: the filer's CIK padded to ten digits, the accession, the form, the day the
// SEC dated it, the instant it accepted it, in UTC, and the items it carries.
public sealed record ReleaseFiling(string Cik, string Accession, string Form, DateOnly Filed, DateTimeOffset Accepted, IReadOnlyList<string> Items);

// One further page of a filer's submissions, which the file names with the days its filings run between.
public sealed record SubmissionsPage(string Name, DateOnly From, DateOnly To);

// A release's text as it is stored: compressed, the SHA-256 of its UTF-8 bytes in lower-case hex and its length in
// characters.
public sealed record StoredText(byte[] Gzip, string Sha256, int Length);

// Where a member's results releases are found and how one is kept. Written against the SEC's answers captured on
// 2026-10-18 before this was written: the current filings feed, a filer's submissions and their further page, and an
// 8-K's index page at each of New York's offsets. The current feed states each filing's form, its filer, its accession,
// the day filed, its items and the instant it was accepted with its offset; the submissions state the same instant in
// UTC with the items; an 8-K's own index page prints the instant in New York's time, read through the session zone; and
// the release is the exhibit the archive's reader finds on that page, kept as the reader makes it text, compressed.
// see: A member's results release is the EX-99 exhibit of its 8-K carrying item 2.02, stored compressed with the instant the SEC accepted it
public static class ResultsReleases
{
    // The forms a results announcement is filed on.
    public static IReadOnlyList<string> Forms { get; } = SecEdgarDailyIndex.Announcements;

    public static bool IsAnnouncement(string form) => Forms.Contains(form, StringComparer.Ordinal);

    // ---- the current filings feed ----------------------------------------------

    // The filings a page of the current feed lists, newest first.
    public const int CurrentPageSize = 100;

    public static string CurrentPath(int start) =>
        FormattableString.Invariant($"cgi-bin/browse-edgar?action=getcurrent&type=8-K&company=&dateb=&owner=include&start={start}&count={CurrentPageSize}&output=atom");

    // Every 8-K and 8-K/A a page of the current feed lists, one a filer it names, newest first; a page that is no Atom
    // feed or an entry that names no filer, accession, day filed or accepted instant refused.
    public static IReadOnlyList<ReleaseFiling> FromCurrent(string atom)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(atom);
        }
        catch (XmlException unreadable)
        {
            throw new FormatException("The current filings feed answered with no Atom document, so no filing in it can be read.", unreadable);
        }

        XNamespace atomSpace = "http://www.w3.org/2005/Atom";

        if (document.Root is not { } feed || feed.Name != atomSpace + "feed")
        {
            throw new FormatException("The current filings feed answered with a document that is no Atom feed.");
        }

        var filings = new List<ReleaseFiling>();

        foreach (var entry in feed.Elements(atomSpace + "entry"))
        {
            var title = (string?)entry.Element(atomSpace + "title") ?? string.Empty;
            var summary = (string?)entry.Element(atomSpace + "summary") ?? string.Empty;
            var form = (string?)entry.Element(atomSpace + "category")?.Attribute("term") ?? string.Empty;

            if (!IsAnnouncement(form))
            {
                continue;
            }

            var cik = TitleCik.Match(title);
            var accession = AccNo.Match(summary);
            var filed = FiledOn.Match(summary);

            if (!cik.Success
                || !accession.Success
                || !filed.Success
                || !DateOnly.TryParseExact(filed.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || !DateTimeOffset.TryParseExact((string?)entry.Element(atomSpace + "updated"), "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var accepted))
            {
                throw new FormatException($"An entry of the current filings feed names no filer, accession, day filed or accepted instant it can be read by: {title}");
            }

            filings.Add(new ReleaseFiling(
                SecEdgarArchive.Padded(cik.Groups["cik"].Value),
                accession.Groups["accession"].Value,
                form,
                day,
                accepted.ToUniversalTime(),
                [.. Item.Matches(summary).Select(match => match.Groups["number"].Value)]));
        }

        return [.. filings.DistinctBy(filing => (filing.Cik, filing.Accession))];
    }

    // ---- a filer's submissions -------------------------------------------------

    // The results announcements a filer's submissions list in their recent window, and the further pages they name.
    public static (string Cik, IReadOnlyList<ReleaseFiling> Filings, IReadOnlyList<SubmissionsPage> Pages) FromSubmissions(string json)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        var cik = SecEdgarArchive.CikOf(json);

        if (!root.TryGetProperty("filings", out var filings) || !filings.TryGetProperty("recent", out var recent))
        {
            throw new FormatException($"The submissions of {cik} carry no recent filings, so none can be read.");
        }

        var pages = new List<SubmissionsPage>();

        if (filings.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray())
            {
                if (Text(file, "name") is { } name
                    && Day(Text(file, "filingFrom")) is { } from
                    && Day(Text(file, "filingTo")) is { } to)
                {
                    pages.Add(new SubmissionsPage(name, from, to));
                }
            }
        }

        return (cik, Rows(recent, cik), pages);
    }

    // A filer's names and tickers as its submissions state them, from 20.3: its name now and each it filed under before,
    // newest first, and its tickers, which the anonymiser reads.
    public static (string Cik, IReadOnlyList<string> Names, IReadOnlyList<string> Tickers) FilerOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        var names = new List<string>();

        if (Text(root, "name") is { Length: > 0 } name)
        {
            names.Add(name);
        }

        if (root.TryGetProperty("formerNames", out var former) && former.ValueKind == JsonValueKind.Array)
        {
            names.AddRange(former.EnumerateArray().Select(entry => Text(entry, "name")).OfType<string>().Where(entry => entry.Length > 0));
        }

        var tickers = root.TryGetProperty("tickers", out var listed) && listed.ValueKind == JsonValueKind.Array
            ? listed.EnumerateArray().Where(ticker => ticker.ValueKind == JsonValueKind.String).Select(ticker => ticker.GetString()!).Where(ticker => ticker.Length > 0).ToList()
            : [];

        return (SecEdgarArchive.CikOf(json), [.. names.Distinct(StringComparer.OrdinalIgnoreCase)], [.. tickers.Distinct(StringComparer.Ordinal)]);
    }

    // The results announcements one further page of a filer's submissions lists, its arrays at its top.
    public static IReadOnlyList<ReleaseFiling> FromSubmissionsPage(string json, string cik)
    {
        using var document = JsonDocument.Parse(json);

        return Rows(document.RootElement, SecEdgarArchive.Padded(cik));
    }

    // The path of a further page of a filer's submissions, beside its first.
    public static string SubmissionsPagePath(string name) => "submissions/" + name;

    static IReadOnlyList<ReleaseFiling> Rows(JsonElement block, string cik)
    {
        var rows = new List<ReleaseFiling>();
        var count = block.TryGetProperty("form", out var forms) && forms.ValueKind == JsonValueKind.Array ? forms.GetArrayLength() : 0;

        for (var at = 0; at < count; at++)
        {
            var form = Text(block, "form", at) ?? string.Empty;
            var items = (Text(block, "items", at) ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (!IsAnnouncement(form) || !SecEdgarDailyIndex.CarriesResults(items))
            {
                continue;
            }

            var accession = Text(block, "accessionNumber", at);

            if (accession is null
                || Day(Text(block, "filingDate", at)) is not { } filed
                || !DateTimeOffset.TryParseExact(Text(block, "acceptanceDateTime", at), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var accepted))
            {
                throw new FormatException($"A results announcement in the submissions of {cik} names no accession, day filed or accepted instant it can be read by.");
            }

            rows.Add(new ReleaseFiling(cik, accession, form, filed, accepted, items));
        }

        return rows;
    }

    // ---- an 8-K's own index page -----------------------------------------------

    // The instant the SEC accepted a filing, which its index page prints in New York's time, read through the session
    // zone; none where the page prints none or a time that zone never shows.
    public static DateTimeOffset? AcceptedOn(string indexPage, TimeZoneInfo zone)
    {
        var printed = AcceptedLine.Match(indexPage);

        if (!printed.Success
            || !DateTime.TryParseExact(printed.Groups["at"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            || zone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    // ---- the release's text, kept ----------------------------------------------

    // The text compressed at the smallest size, with the hash of its UTF-8 bytes and its length, which a reading reads
    // back and checks.
    public static StoredText Compressed(string text)
    {
        var bytes = Utf8.GetBytes(text);

        using var compressed = new MemoryStream();

        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return new StoredText(compressed.ToArray(), Convert.ToHexStringLower(SHA256.HashData(bytes)), text.Length);
    }

    public static string Decompressed(byte[] gzip)
    {
        using var input = new MemoryStream(gzip);
        using var unzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(unzip, Utf8);

        return reader.ReadToEnd();
    }

    // The hash a stored text is checked against, of its UTF-8 bytes.
    public static string HashOf(string text) => Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(text)));

    // ---- a history run's stop -----------------------------------------------

    // The instant a history run stops at, given as the operator's time in the session zone: the first instant after now
    // the zone's clock shows that time, today where it is still ahead and tomorrow where it has passed.
    public static DateTimeOffset UntilNext(TimeOnly local, DateTimeOffset now, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        for (var tried = 0; tried < 3; tried++, day = day.AddDays(1))
        {
            var shown = day.ToDateTime(local);

            if (zone.IsInvalidTime(shown))
            {
                continue;
            }

            var at = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(shown, zone), TimeSpan.Zero);

            if (at > now)
            {
                return at;
            }
        }

        throw new ArgumentException(FormattableString.Invariant($"{local:HH:mm} is a time the session zone does not show on the days after {now:yyyy-MM-dd}."), nameof(local));
    }

    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    static string? Text(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static string? Text(JsonElement block, string field, int at) =>
        block.TryGetProperty(field, out var array) && array.ValueKind == JsonValueKind.Array && at < array.GetArrayLength() && array[at].ValueKind == JsonValueKind.String
            ? array[at].GetString()
            : null;

    static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    static readonly Regex TitleCik = new(@"\((?<cik>\d{1,10})\)\s*\((?:Filer|Subject|Reporting)\)\s*$", RegexOptions.CultureInvariant);

    static readonly Regex AccNo = new(@"AccNo:</b>\s*(?<accession>\d{10}-\d{2}-\d{6})", RegexOptions.CultureInvariant);

    static readonly Regex FiledOn = new(@"Filed:</b>\s*(?<day>\d{4}-\d{2}-\d{2})", RegexOptions.CultureInvariant);

    static readonly Regex Item = new(@"Item\s+(?<number>\d+\.\d{2})(?!\d)", RegexOptions.CultureInvariant);

    static readonly Regex AcceptedLine = new(
        @"<div class=""infoHead"">Accepted</div>\s*<div class=""info"">(?<at>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})</div>",
        RegexOptions.CultureInvariant);
}
