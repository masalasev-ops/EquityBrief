using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The night's filings refresh, answered from captured archive responses.
//
// A day's index is the capture named for its day, and a day no capture is named for is a day the archive posted no
// index for, which is the archive's own answer for a weekend, a holiday or a day not yet posted. A filing's page is
// the capture named for its accession, and a filer's facts the capture whose own CIK is the one asked for, read off
// the capture rather than off its name; from 19.13 an insider's Form 4 or 4/A is the capture named for its accession.
// A page, a filer or a Form 4 the refresh asks for that no capture holds refuses by name, since a replay runs over a set
// that should be complete and an absence there is a fault in the fixture.
public sealed class RecordedFilingsRefreshFeed : IFilingsRefreshFeed
{
    public const string DayPrefix = "daily-index-";

    public const string DayExtension = ".idx";

    public const string PagePrefix = "filing-page-";

    public const string PageExtension = ".htm";

    public const string FactsPrefix = "company-facts-";

    public const string OwnershipPrefix = "ownership-";

    public const string OwnershipExtension = ".txt";

    readonly IReadOnlyDictionary<DateOnly, string> days;

    readonly IReadOnlyDictionary<string, string> pages;

    readonly IReadOnlyDictionary<string, string> facts;

    readonly IReadOnlyDictionary<string, string> ownerships;

    readonly List<string> asked = [];

    RecordedFilingsRefreshFeed(
        IReadOnlyDictionary<DateOnly, string> days,
        IReadOnlyDictionary<string, string> pages,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyDictionary<string, string> ownerships)
    {
        this.days = days;
        this.pages = pages;
        this.facts = facts;
        this.ownerships = ownerships;
    }

    public int Documents => asked.Count;

    // Every document asked for, in order, as the day, the page's accession or the facts' CIK.
    public IReadOnlyList<string> Asked => asked;

    // Whether the folder holds a day's index at all, which is what lets a fixture without one be given no feed.
    public static bool Holds(string folder) =>
        Directory.Exists(folder) && Directory.GetFiles(folder, DayPrefix + "*" + DayExtension).Length > 0;

    public static RecordedFilingsRefreshFeed FromFolder(string folder)
    {
        var days = Directory.GetFiles(folder, DayPrefix + "*" + DayExtension).ToDictionary(
            path => DateOnly.ParseExact(Path.GetFileNameWithoutExtension(path)[DayPrefix.Length..], "yyyyMMdd", CultureInfo.InvariantCulture),
            File.ReadAllText);

        var pages = Directory.GetFiles(folder, PagePrefix + "*" + PageExtension).ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)[PagePrefix.Length..],
            File.ReadAllText,
            StringComparer.Ordinal);

        var facts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(folder, FactsPrefix + "*.json"))
        {
            var text = File.ReadAllText(path);

            using var document = JsonDocument.Parse(text);

            if (document.RootElement.TryGetProperty("cik", out var cik) && cik.ValueKind is JsonValueKind.Number)
            {
                facts[SecEdgarArchive.Padded(cik.GetInt64().ToString(CultureInfo.InvariantCulture))] = text;
            }
        }

        var ownerships = Directory.GetFiles(folder, OwnershipPrefix + "*" + OwnershipExtension).ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)[OwnershipPrefix.Length..],
            File.ReadAllText,
            StringComparer.Ordinal);

        return new(days, pages, facts, ownerships);
    }

    // A refresh over constructed answers: the days' indexes, the pages by accession, the facts by CIK and the insiders'
    // filings by accession.
    public static RecordedFilingsRefreshFeed Of(
        IReadOnlyDictionary<DateOnly, string> days,
        IReadOnlyDictionary<string, string>? pages = null,
        IReadOnlyDictionary<string, string>? facts = null,
        IReadOnlyDictionary<string, string>? ownerships = null) =>
        new(
            days,
            pages ?? new Dictionary<string, string>(StringComparer.Ordinal),
            (facts ?? new Dictionary<string, string>(StringComparer.Ordinal)).ToDictionary(
                pair => SecEdgarArchive.Padded(pair.Key),
                pair => pair.Value,
                StringComparer.Ordinal),
            ownerships ?? new Dictionary<string, string>(StringComparer.Ordinal));

    public Task<string?> OwnershipAsync(string cik, string accession, CancellationToken cancellation = default)
    {
        asked.Add(accession);

        return ownerships.TryGetValue(accession, out var submission)
            ? Task.FromResult<string?>(submission)
            : throw new InvalidOperationException(
                $"The refresh asked for the insider's filing {accession} and the fixture holds no capture of it.");
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
            : throw new InvalidOperationException(
                $"The refresh asked for the page of filing {accession} and the fixture holds no capture of it.");
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>?> FactsAsync(
        string cik,
        IReadOnlyList<string> concepts,
        CancellationToken cancellation = default)
    {
        var padded = SecEdgarArchive.Padded(cik);

        asked.Add(padded);

        return facts.TryGetValue(padded, out var text)
            ? Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>?>(ConceptAnswers.FromFacts(text, padded, concepts))
            : throw new InvalidOperationException(
                $"The refresh asked for the facts of CIK {padded} and the fixture holds no capture of them.");
    }
}
