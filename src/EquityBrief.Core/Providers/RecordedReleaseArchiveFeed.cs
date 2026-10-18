using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Providers;

// The local run's release fetch, answered from captured archive responses, from 20.2. A day's index and a filing's page
// are the captures the filings refresh reads, named for the day and for the accession, and a day no capture is named
// for is a day the archive posted no index for; the current filings feed is its one capture, answering the first page
// and no further one; a filer's submissions are the capture whose own CIK is the one asked for and a further page the
// capture named for the filer's ticker and the page's number; and a release exhibit is the capture named for its
// filing's accession. A page, an exhibit or a filer the fetch asks for that no capture holds refuses by name, since a
// replay runs over a set that should be complete and an absence there is a fault in the fixture.
// see: A member's results release is the EX-99 exhibit of its 8-K carrying item 2.02, stored compressed with the instant the SEC accepted it
public sealed class RecordedReleaseArchiveFeed : IReleaseArchiveFeed
{
    public const string CurrentPrefix = "current-8k-";

    public const string CurrentExtension = ".atom";

    public const string ExhibitPrefix = "release-exhibit-";

    public const string SubmissionsPrefix = "filings-";

    public const string FurtherPrefix = "submissions-";

    readonly IReadOnlyDictionary<DateOnly, string> days;

    readonly IReadOnlyDictionary<string, string> pages;

    readonly IReadOnlyDictionary<string, string> exhibits;

    readonly IReadOnlyDictionary<string, string> submissions;

    readonly IReadOnlyDictionary<string, string> further;

    readonly IReadOnlyList<string> current;

    readonly List<string> asked = [];

    RecordedReleaseArchiveFeed(
        IReadOnlyDictionary<DateOnly, string> days,
        IReadOnlyDictionary<string, string> pages,
        IReadOnlyDictionary<string, string> exhibits,
        IReadOnlyDictionary<string, string> submissions,
        IReadOnlyDictionary<string, string> further,
        IReadOnlyList<string> current)
    {
        this.days = days;
        this.pages = pages;
        this.exhibits = exhibits;
        this.submissions = submissions;
        this.further = further;
        this.current = current;
    }

    public int Documents => asked.Count;

    // Every document asked for, in order: the day, the page's or the exhibit's accession, the filer's CIK, the further
    // page's name or the current feed's start.
    public IReadOnlyList<string> Asked => asked;

    public static RecordedReleaseArchiveFeed FromFolder(string folder)
    {
        var refresh = RecordedFilingsRefreshFeed.DayPrefix;
        var days = Directory.GetFiles(folder, refresh + "*" + RecordedFilingsRefreshFeed.DayExtension).ToDictionary(
            path => DateOnly.ParseExact(Path.GetFileNameWithoutExtension(path)[refresh.Length..], "yyyyMMdd", CultureInfo.InvariantCulture),
            File.ReadAllText);

        var pages = Directory.GetFiles(folder, RecordedFilingsRefreshFeed.PagePrefix + "*" + RecordedFilingsRefreshFeed.PageExtension).ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)[RecordedFilingsRefreshFeed.PagePrefix.Length..],
            File.ReadAllText,
            StringComparer.Ordinal);

        var exhibits = Directory.GetFiles(folder, ExhibitPrefix + "*.htm").ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)[ExhibitPrefix.Length..],
            File.ReadAllText,
            StringComparer.Ordinal);

        var submissions = new Dictionary<string, string>(StringComparer.Ordinal);
        var tickerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(folder, SubmissionsPrefix + "*.json"))
        {
            var text = File.ReadAllText(path);
            var cik = SecEdgarArchive.CikOf(text);

            submissions[cik] = text;
            tickerOf[cik] = Path.GetFileNameWithoutExtension(path)[SubmissionsPrefix.Length..];
        }

        // A further page is named for the filer's ticker and its number, and answers the name the first page gives it.
        var further = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (cik, ticker) in tickerOf)
        {
            foreach (var path in Directory.GetFiles(folder, FurtherPrefix + ticker + "-*.json"))
            {
                var number = Path.GetFileNameWithoutExtension(path)[(FurtherPrefix + ticker + "-").Length..];

                further["CIK" + cik + "-submissions-" + number + ".json"] = File.ReadAllText(path);
            }
        }

        return new(days, pages, exhibits, submissions, further, [.. Directory.GetFiles(folder, CurrentPrefix + "*" + CurrentExtension).Order(StringComparer.Ordinal).Select(File.ReadAllText)]);
    }

    // A fetch over constructed answers: the days' indexes, the pages and the exhibits by accession, the submissions by
    // CIK, the further pages by name and the current feed's pages in order.
    public static RecordedReleaseArchiveFeed Of(
        IReadOnlyDictionary<DateOnly, string>? days = null,
        IReadOnlyDictionary<string, string>? pages = null,
        IReadOnlyDictionary<string, string>? exhibits = null,
        IReadOnlyDictionary<string, string>? submissions = null,
        IReadOnlyDictionary<string, string>? further = null,
        IReadOnlyList<string>? current = null) =>
        new(
            days ?? new Dictionary<DateOnly, string>(),
            pages ?? new Dictionary<string, string>(StringComparer.Ordinal),
            exhibits ?? new Dictionary<string, string>(StringComparer.Ordinal),
            (submissions ?? new Dictionary<string, string>(StringComparer.Ordinal)).ToDictionary(pair => SecEdgarArchive.Padded(pair.Key), pair => pair.Value, StringComparer.Ordinal),
            further ?? new Dictionary<string, string>(StringComparer.Ordinal),
            current ?? []);

    public Task<string?> CurrentAsync(int start, CancellationToken cancellation = default)
    {
        asked.Add(start.ToString(CultureInfo.InvariantCulture));

        var page = start / ResultsReleases.CurrentPageSize;

        return Task.FromResult(start % ResultsReleases.CurrentPageSize == 0 && page < current.Count ? current[page] : null);
    }

    public Task<string?> DailyIndexAsync(DateOnly day, CancellationToken cancellation = default)
    {
        asked.Add(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return Task.FromResult(days.TryGetValue(day, out var text) ? text : null);
    }

    public Task<string?> FilingPageAsync(string cik, string accession, CancellationToken cancellation = default)
    {
        asked.Add(accession);

        return pages.TryGetValue(accession, out var page)
            ? Task.FromResult<string?>(page)
            : throw new InvalidOperationException($"The release fetch asked for the page of filing {accession} and the fixture holds no capture of it.");
    }

    public Task<string?> ExhibitAsync(string cik, string accession, string file, CancellationToken cancellation = default)
    {
        asked.Add(accession + "/" + file);

        return exhibits.TryGetValue(accession, out var exhibit)
            ? Task.FromResult<string?>(exhibit)
            : throw new InvalidOperationException($"The release fetch asked for the exhibit {file} of filing {accession} and the fixture holds no capture of it.");
    }

    public Task<string?> SubmissionsAsync(string cik, CancellationToken cancellation = default)
    {
        var padded = SecEdgarArchive.Padded(cik);

        asked.Add(padded);

        return submissions.TryGetValue(padded, out var text)
            ? Task.FromResult<string?>(text)
            : throw new InvalidOperationException($"The release fetch asked for the submissions of CIK {padded} and the fixture holds no capture of them.");
    }

    public Task<string?> SubmissionsPageAsync(string name, CancellationToken cancellation = default)
    {
        asked.Add(name);

        return further.TryGetValue(name, out var text)
            ? Task.FromResult<string?>(text)
            : throw new InvalidOperationException($"The release fetch asked for the further submissions page {name} and the fixture holds no capture of it.");
    }
}
