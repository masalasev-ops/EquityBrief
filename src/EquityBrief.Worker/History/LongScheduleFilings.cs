using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;

namespace EquityBrief.Worker.History;

// One filing of the trust's older schedules as its header names it: its accession, its form, the period it reports, the day it
// was filed, the series it carries and each document with its type.
public sealed record TrustFiling(string Accession, string Form, DateOnly? Period, DateOnly? Filed, IReadOnlyList<string> Series, IReadOnlyList<(string Type, string File)> Documents);

// The trust's filings of the three funds' schedules from 2004-12-31 to 2018-09-30, kept whole in a folder before anything
// reads them: the archive's file of funds' tickers naming each fund's series, each series' list of its N-Q, N-CSR and N-CSRS
// filings, the trust's own filing index for the quarters before series were filed, each candidate filing's header, and with
// documents asked for, each document of a filing carrying one of the three series whose type is the filing's form. Nothing in
// the store is read or written.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
public sealed class LongScheduleFilings(ITrustScheduleFeed feed) : IComponent
{
    public static ComponentAccess Access => new(Stores: [], Feeds: [Feed.FilingsArchive]);

    // Each index's fund by its ticker.
    public static IReadOnlyDictionary<string, string> Funds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GSPC"] = "IVV",
        ["MID"] = "IJH",
        ["SML"] = "IJR",
    };

    public static IReadOnlyList<string> Forms { get; } = ["N-Q", "N-CSR", "N-CSRS"];

    public static readonly DateOnly FirstPeriod = new(2004, 12, 31);

    public static readonly DateOnly LastPeriod = new(2018, 9, 30);

    // A filing reporting the last period is filed within this many days of it.
    public const int FiledWithin = 120;

    public const string TickersFile = "company_tickers_mf.json";

    public const string HeadersFolder = "headers";

    public const string DocumentsFolder = "documents";

    public int Requests => feed.Requests;

    // The quarter ends the long history reads, from the first period to the last.
    public static IReadOnlyList<DateOnly> QuarterEnds { get; } =
    [
        .. Enumerable.Range(0, ((LastPeriod.Year - FirstPeriod.Year) * 4) + ((LastPeriod.Month - FirstPeriod.Month) / 3) + 1)
            .Select(step => new DateOnly(FirstPeriod.Year, FirstPeriod.Month, 1).AddMonths(3 * step))
            .Select(month => new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month))),
    ];

    // Each fund's series off the archive's file of funds' tickers, none for a fund it does not carry.
    public static IReadOnlyDictionary<string, string> SeriesOf(string tickers)
    {
        using var document = JsonDocument.Parse(tickers);
        var fields = document.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()).ToList();
        var (seriesAt, symbolAt) = (fields.IndexOf("seriesId"), fields.IndexOf("symbol"));
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var symbol = row[symbolAt].GetString();

            if (symbol is not null && Funds.Values.Contains(symbol, StringComparer.Ordinal) && row[seriesAt].GetString() is { } series)
            {
                found[symbol] = series;
            }
        }

        return found;
    }

    static readonly Regex Accession = new(@"<accession-n(?:umbe|o)r?>\s*([0-9-]{20})\s*<", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // The accessions a series' list page names.
    public static IReadOnlyList<string> AccessionsOf(string atom) =>
        [.. Accession.Matches(atom).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    static readonly Regex Field = new(@"^\s*([A-Z][A-Z -]+):\s*(\S.*?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    static readonly Regex Tagged = new(@"<(SERIES-ID|TYPE|FILENAME|PERIOD|FILING-DATE)>\s*([^\s<]+)", RegexOptions.Compiled);

    // A filing as its header names it: the index's headers page, its fields written out and its documents listed, or the
    // header file, its fields as tags and no document listed.
    public static TrustFiling HeaderOf(string accession, string header)
    {
        var text = WebUtility.HtmlDecode(header);
        string? form = null;
        DateOnly? period = null;
        DateOnly? filed = null;

        static DateOnly? Day(string value) =>
            DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

        foreach (Match field in Field.Matches(text))
        {
            var (name, value) = (field.Groups[1].Value.Trim(), field.Groups[2].Value.Trim());

            if (name == "CONFORMED SUBMISSION TYPE")
            {
                form ??= value;
            }
            else if (name == "CONFORMED PERIOD OF REPORT")
            {
                period ??= Day(value);
            }
            else if (name == "FILED AS OF DATE")
            {
                filed ??= Day(value);
            }
        }

        var series = new List<string>();
        var documents = new List<(string, string)>();
        string? type = null;

        foreach (Match tag in Tagged.Matches(text))
        {
            var value = tag.Groups[2].Value;

            switch (tag.Groups[1].Value)
            {
                case "SERIES-ID":
                    series.Add(value);
                    break;
                case "PERIOD":
                    period ??= Day(value);
                    break;
                case "FILING-DATE":
                    filed ??= Day(value);
                    break;
                case "TYPE":
                    form ??= value;
                    type = value;
                    break;
                default:
                    if (type is not null)
                    {
                        documents.Add((type, value));
                    }

                    type = null;
                    break;
            }
        }

        return new TrustFiling(accession, form ?? string.Empty, period, filed, [.. series.Distinct(StringComparer.Ordinal)], documents);
    }

    // Every file the capture asks for, written under the folder as the archive sent it, and the filings its headers name.
    public async Task<IReadOnlyList<TrustFiling>> CaptureAsync(string folder, bool documents, Action<string> progress, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(folder);

        var tickers = await feed.FundTickersAsync(cancellation);

        await File.WriteAllTextAsync(Path.Combine(folder, TickersFile), tickers, cancellation);

        var series = SeriesOf(tickers);
        var accessions = new SortedSet<string>(StringComparer.Ordinal);

        progress(FormattableString.Invariant($"the funds' series: {string.Join(", ", series.Select(pair => pair.Key + " " + pair.Value))}"));

        foreach (var (fund, id) in series)
        {
            foreach (var form in Forms)
            {
                for (var start = 0; ; start += SecEdgarTrustScheduleFeed.ListPage)
                {
                    var page = await feed.SeriesFilingsAsync(id, form, start, cancellation);

                    await File.WriteAllTextAsync(Path.Combine(folder, FormattableString.Invariant($"list-{fund}-{form}-{start}.xml")), page, cancellation);

                    var named = AccessionsOf(page);

                    accessions.UnionWith(named);

                    if (named.Count < SecEdgarTrustScheduleFeed.ListPage)
                    {
                        break;
                    }
                }
            }
        }

        // The trust's own filings reporting a quarter end before any series list reaches, read off its filing index.
        var index = await feed.IndexAsync(FundSnapshots.Trust, cancellation);
        var pages = new List<string> { index };

        await File.WriteAllTextAsync(Path.Combine(folder, "submissions.json"), index, cancellation);

        foreach (var page in FilerFilings.Pages(index))
        {
            var text = await feed.PageAsync(page, cancellation);

            await File.WriteAllTextAsync(Path.Combine(folder, page), text, cancellation);
            pages.Add(text);
        }

        var primary = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var filing in FilerFilings.Read(pages))
        {
            primary.TryAdd(filing.Accession, filing.PrimaryDocument);

            if (Forms.Contains(filing.Form, StringComparer.Ordinal) && filing.Reported is { } reported && reported >= FirstPeriod && reported <= LastPeriod)
            {
                accessions.Add(filing.Accession);
            }
        }

        progress(FormattableString.Invariant($"{accessions.Count} filing(s) named by the lists and the index"));

        var headers = Path.Combine(folder, HeadersFolder);
        var filings = new List<TrustFiling>();

        Directory.CreateDirectory(headers);

        foreach (var accession in accessions)
        {
            var file = Path.Combine(headers, accession + ".html");
            var header = File.Exists(file) ? await File.ReadAllTextAsync(file, cancellation) : await feed.HeaderAsync(FundSnapshots.Trust, accession, cancellation);

            await File.WriteAllTextAsync(file, header, cancellation);

            var filing = HeaderOf(accession, header);

            if (filing.Period is { } period && period >= FirstPeriod && period <= LastPeriod)
            {
                filings.Add(filing);
            }
        }

        progress(FormattableString.Invariant($"{filings.Count} filing(s) report a period from {FirstPeriod:yyyy-MM-dd} to {LastPeriod:yyyy-MM-dd}"));

        if (documents)
        {
            var kept = Path.Combine(folder, DocumentsFolder);

            Directory.CreateDirectory(kept);

            // A filing of one of the three forms at one of the quarter ends, carrying one of the funds' series or, before series
            // were filed, none: its primary document as the trust's index names it, and each other document of its form.
            foreach (var filing in filings.Where(filing =>
                Forms.Contains(filing.Form, StringComparer.Ordinal)
                && filing.Period is { } period && QuarterEnds.Contains(period)
                && (filing.Series.Count == 0 || filing.Series.Intersect(series.Values, StringComparer.Ordinal).Any())))
            {
                string[] wanted =
                [
                    .. (primary.TryGetValue(filing.Accession, out var main) ? new[] { main } : [])
                        .Concat(filing.Documents.Where(one => one.Type == filing.Form).Select(one => one.File))
                        .Distinct(StringComparer.Ordinal),
                ];

                foreach (var document in wanted)
                {
                    var file = Path.Combine(kept, filing.Accession + "-" + document);

                    if (!File.Exists(file))
                    {
                        await File.WriteAllTextAsync(file, await feed.DocumentAsync(FundSnapshots.Trust, filing.Accession, document, cancellation), cancellation);
                        progress($"kept {filing.Accession} {document}");
                    }
                }
            }
        }

        return filings;
    }
}
