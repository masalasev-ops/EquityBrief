namespace EquityBrief.Core.Providers;

// What the night's filings refresh asks the archive for: a day's index of every filing it disseminated, one filing's
// own index page, which is where an 8-K states its items, and a filer's facts under the concepts asked for.
//
// Free, keyless and from the SEC rather than the provider, so its documents are counted apart from the provider's
// requests and weigh nothing on the provider's allowance. A day's index the archive posted none for, a filing page it
// does not hold and a filer it holds no facts for are each answered with none, which is no failure; a refusal throws.
// see: The night refreshes the facts of the members that filed since its last read of the archive's daily index, after the close under its own limit
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
}
