namespace EquityBrief.Core.Providers;

// What the night's filings refresh asks the archive for: a day's index of every filing it disseminated, one filing's
// own index page, which is where an 8-K states its items, and a filer's facts under the concepts asked for.
//
// Free, keyless and from the SEC rather than the provider, so its documents are counted apart from the provider's
// requests and weigh nothing on the provider's allowance. A day's index the archive posted none for, a filing page it
// does not hold and a filer it holds no facts for are each answered with none, which is no failure; a refusal throws.
// see: The night refreshes the facts and the insiders' trades of the members that filed since its last read of the archive's daily index, after the close under its own limit
public interface IFilingsRefreshFeed
{
    // The documents asked for, each one request.
    int Documents { get; }

    Task<string?> DailyIndexAsync(DateOnly day, CancellationToken cancellation = default);

    Task<string?> FilingPageAsync(string cik, string accession, CancellationToken cancellation = default);

    Task<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>?> FactsAsync(
        string cik,
        IReadOnlyList<string> concepts,
        CancellationToken cancellation = default);

    // From 19.13 one insider's Form 4 or 4/A whole, as the archive serves its submission under the filer its index line
    // names, none where it holds no such filing.
    // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
    Task<string?> OwnershipAsync(string cik, string accession, CancellationToken cancellation = default);

    // From the 17.3 correction of 2026-10-27 a report's listing of its documents and the document named in it, each one
    // request, none where the archive holds no such document.
    // see: A report the archive's facts answer carries no figure of is read from the report's own XBRL instance
    Task<string?> FilingListingAsync(string cik, string accession, CancellationToken cancellation = default);

    Task<string?> FilingDocumentAsync(string cik, string accession, string file, CancellationToken cancellation = default);
}

// The SEC's quarterly insider transactions data sets: the page listing every quarter's file and one quarter's file
// whole, each one request, free and keyless; none where the archive holds no such document, and a refusal throws.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public interface IInsiderDataSetFeed
{
    // The documents asked for, each one request.
    int Documents { get; }

    Task<string?> ListingAsync(CancellationToken cancellation = default);

    Task<byte[]?> QuarterAsync(string path, CancellationToken cancellation = default);
}

// What the local run's release fetch asks the archive for, from 20.2: a page of the current filings feed of 8-Ks, a
// day's index, one filing's own index page, the exhibit it names, and a filer's submissions with a further page of
// them. Free, keyless and from the SEC, at its pace and under its contact as every archive request is; a document the
// archive does not hold is answered with none, and a refusal throws.
// see: A member's results release is the EX-99 exhibit of its 8-K carrying item 2.02, stored compressed with the instant the SEC accepted it
public interface IReleaseArchiveFeed
{
    // The documents asked for, each one request.
    int Documents { get; }

    Task<string?> CurrentAsync(int start, CancellationToken cancellation = default);

    Task<string?> DailyIndexAsync(DateOnly day, CancellationToken cancellation = default);

    Task<string?> FilingPageAsync(string cik, string accession, CancellationToken cancellation = default);

    Task<string?> ExhibitAsync(string cik, string accession, string file, CancellationToken cancellation = default);

    Task<string?> SubmissionsAsync(string cik, CancellationToken cancellation = default);

    Task<string?> SubmissionsPageAsync(string name, CancellationToken cancellation = default);
}
