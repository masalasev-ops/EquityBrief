using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EquityBrief.Core.Research;

// Which fetching tool handed a document over.
//
// On the document rather than derived from its address, because the address does
// not say how it arrived and the two answers differ where it matters most: the
// 1.7 measurement showed the licensed news feed delivers 1,173 of 1,253 articles
// under one domain, and that the domain is the aggregator that carried the
// article rather than the outlet that wrote it. A channel read off a host would
// therefore call every article in the feed the same thing.
// see: News is one dated query, paged to cover the day, and attributed to names locally
public enum DocumentChannel
{
    FilingsArchive,
    NewsFeed,
    SearchTool,
}

// One document as a fetching tool hands it over, before anything has judged it.
//
// `Text` is null where the fetch produced none and `RetrievalFailure` says what
// happened. Both are carried rather than one, because a page that answered with
// an empty body and a page that refused automated access are the same absence to
// a reader and different things to a person: the measurement behind 6.3 asked
// eight pages for their text and one of them answered 403, which is the case this
// pair exists to keep legible.
public sealed record FetchedDocument(
    DocumentChannel Channel,
    string Url,
    string Title,
    DateOnly? PublishedOn,
    string? Text,
    string? RetrievalFailure = null);

// One row of the source documents store.
//
// `Body` is null on every refusal, which is what keeping a refusal without
// storing the document means, and `PublishedOn` is null on the refusal that is
// about the date being absent. Neither is an absence of data
// (see: A stored source is not automatically an admissible one).
public sealed record StoredDocument(
    string Id,
    string Url,
    string Title,
    DateOnly? PublishedOn,
    DateTimeOffset FetchedAt,
    string? Body,
    string Admissibility)
{
    public bool Admitted =>
        string.Equals(Admissibility, Research.Admissibility.Accepted, StringComparison.Ordinal);
}

// What one pass fetched, what it may use, and what it has to report.
//
// Held together rather than returned as three lists, because the three numbers
// are only meaningful beside each other: a pass that admitted nothing and a pass
// that fetched nothing produce the same empty set of usable documents and are
// different events, and the second is not a refusal of anything.
public sealed record DocumentIntake(
    IReadOnlyList<StoredDocument> Rows,
    IReadOnlyList<StoredDocument> Admitted,
    IReadOnlyList<StoredDocument> Refused,
    string Detail)
{
    // Whether a section resting on this intake can be written at all. A pass that
    // found no admissible source omits the section with one line saying so rather
    // than writing it from a document the test refused.
    public bool NothingAdmissible => Admitted.Count == 0;
}

// The intake: every document a pass fetched, tested and turned into rows.
//
// This is the shape of storing rather than the storing itself. The insert belongs
// to the runner that fetched the document, because SCHEMA gives Insert on
// `source_document` to the two research runners and to nobody else, and a write
// is attributed to the file it appears in, so a statement in a shared helper
// would be a write no component declared. 6.8 and 6.9 are the checkpoints that
// carry it. What lives here is everything that decides what the row says, so the
// runner has no judgement of its own to make.
//
// The test is applied by whichever runner fetched the document and never by the
// checker, ruled at 6.0: the test runs on a document as it is fetched, which is
// where the runners are, and the checker reads the verdict this leaves.
public static class SourceDocuments
{
    // The id is the document's own address, hashed. So a second pass that fetches
    // the same url writes a row that conflicts with the one already stored rather
    // than a second row saying the same thing, and the verdict kept is the one
    // taken the first time, which is what makes it a record of what was decided
    // then rather than of what the rules say today.
    //
    // Sixteen bytes of SHA-256 rather than the whole digest, which is 32
    // characters: this is an identity for a row in one store and not a guard
    // against anyone constructing a collision.
    public const int IdCharacters = 32;

    public static string Id(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..IdCharacters];

    // A url parameter that looks like a credential. A document's url is public
    // and a provider's request url carries this provider's key in its query
    // string, so a url of this shape can only have come from a fetcher handing
    // over its own request rather than the document's address.
    //
    // Refused rather than stripped, and refused loudly: the hard rule is that no
    // request url reaches a log line or a store row, and a fetcher that hands one
    // over is a defect in the fetcher. Stripping would leave the defect in place
    // and the row half wrong.
    public static readonly string[] CredentialMarkers =
        ["api_token", "api_key", "apikey", "access_token", "token=", "secret", "password"];

    // Read off the query string and never off the whole url, which is the
    // difference between a guard and a word filter. A published article is
    // addressed by a slug, and a slug is words: "the secret of Apple's margin" is
    // a headline, and a marker matched anywhere in the address would refuse the
    // article for carrying one of them. A credential travels as a parameter, so
    // the query is where it can be, and a document url usually has none at all.
    public static bool CarriesACredential(string url)
    {
        var at = url.IndexOf('?', StringComparison.Ordinal);

        if (at < 0)
        {
            return false;
        }

        var query = url[(at + 1)..];

        return CredentialMarkers.Any(marker => query.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    // One document's row, with the verdict the test reached on it.
    public static StoredDocument Stored(FetchedDocument document, string verdict, DateTimeOffset fetchedAt)
    {
        if (CarriesACredential(document.Url))
        {
            throw new InvalidOperationException(
                "A document was handed over with a url carrying what looks like a credential " +
                $"parameter, which no published document has: '{Withheld(document.Url)}'. A url of " +
                "that shape is a fetcher's own request rather than the document's address, and no " +
                "request url may reach a store row or a log line. Refused here rather than stripped, " +
                "because stripping would store a row whose url is not where the document lives and " +
                "would leave the fetcher's fault in place.");
        }

        var admitted = string.Equals(verdict, Admissibility.Accepted, StringComparison.Ordinal);

        return new StoredDocument(
            Id(document.Url),
            document.Url,
            document.Title,
            document.PublishedOn,
            fetchedAt,

            // The body on an acceptance and nothing on a refusal. Both halves
            // matter: a refusal that kept the body would store a document the
            // test said may not be used, and an acceptance that dropped it would
            // leave a claim resting on a row a reader cannot check.
            admitted ? document.Text : null,
            verdict);
    }

    // Everything a pass fetched, judged and turned into rows, with the line the
    // run log carries about it.
    public static DocumentIntake Of(
        IReadOnlyList<FetchedDocument> fetched,
        DateOnly from,
        DateOnly to,
        DateTimeOffset fetchedAt)
    {
        var rows = fetched
            .Select(document => Stored(document, Admissibility.Judge(document, from, to), fetchedAt))
            .ToArray();

        var admitted = rows.Where(row => row.Admitted).ToArray();
        var refused = rows.Where(row => !row.Admitted).ToArray();

        return new DocumentIntake(rows, admitted, refused, Detail(fetched, rows));
    }

    // The run log's line for one intake.
    //
    // A count per category rather than one number of refusals, because the
    // categories are what a person can act on: four of one kind is a search
    // returning marketing and one of each is an ordinary evening. The document
    // whose text could not be retrieved is named with its url and its reason,
    // which is what section 18 promises for that case and the one place a url
    // appears here. The urls are the documents' own, never a request address.
    public static string Detail(IReadOnlyList<FetchedDocument> fetched, IReadOnlyList<StoredDocument> rows)
    {
        var line = new StringBuilder();

        line.Append(fetched.Count.ToString(CultureInfo.InvariantCulture));
        line.Append(" document(s) fetched, ");
        line.Append(rows.Count(row => row.Admitted).ToString(CultureInfo.InvariantCulture));
        line.Append(" admitted");

        var byCategory = rows
            .Where(row => !row.Admitted)
            .GroupBy(row => row.Admissibility, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (byCategory.Length > 0)
        {
            line.Append(", refused: ");
            line.Append(string.Join(", ", byCategory.Select(group =>
                group.Key + " " + group.Count().ToString(CultureInfo.InvariantCulture))));
        }

        // Paired on the row's own verdict rather than judged again here, so the
        // line and the rows cannot disagree about which document this was.
        foreach (var row in rows.Where(row =>
            string.Equals(row.Admissibility, Admissibility.NoText, StringComparison.Ordinal)))
        {
            var document = fetched.First(one => string.Equals(one.Url, row.Url, StringComparison.Ordinal));

            line.Append(". No text from ");
            line.Append(row.Url);
            line.Append(": ");
            line.Append(document.RetrievalFailure ?? "the fetch returned no text and said nothing about why");
        }

        return line.ToString();
    }

    // Enough of the offending url to find the fetcher that produced it, without
    // putting the rest of it in the message a store row or a log line would hold.
    static string Withheld(string url)
    {
        var at = url.IndexOf('?', StringComparison.Ordinal);

        return at < 0 ? url : url[..at] + "?" + ProviderCredentialsMarker;
    }

    public const string ProviderCredentialsMarker = "<query withheld>";
}
