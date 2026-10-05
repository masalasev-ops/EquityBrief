namespace EquityBrief.Core.Providers;

// The funds' quarter-end holdings answered from recorded documents: each fund's filing list by its series, each
// filing's holdings document by its accession and any other document of a filing by its accession and its name, read by
// the same readers as the archive's own answers. A series, a filing or a document it holds nothing for is refused, as
// the archive's would be, rather than answered with nothing.
public sealed class RecordedFundSnapshotFeed(
    IReadOnlyDictionary<string, string> lists,
    IReadOnlyDictionary<string, string> snapshots,
    IReadOnlyDictionary<string, string>? documents = null) : IFundSnapshotFeed
{
    public int Requests { get; private set; }

    public Task<IReadOnlyList<FundFiling>> FilingsAsync(string series, CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(lists.TryGetValue(series, out var atom)
            ? FundSnapshots.ParseFilings(atom, series)
            : throw new ProviderRefusal($"No recorded list of {series}'s filings.", transient: false));
    }

    public Task<FundSnapshot> SnapshotAsync(string accession, CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(snapshots.TryGetValue(accession, out var xml)
            ? FundSnapshots.ParseSnapshot(xml, accession)
            : throw new ProviderRefusal($"No recorded holdings document for the filing {accession}.", transient: false));
    }

    // A document recorded under its filing's accession and its own name, as "accession/name".
    public Task<string> DocumentAsync(string accession, string document, CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(documents is not null && documents.TryGetValue(accession + "/" + document, out var body)
            ? body
            : throw new ProviderRefusal($"No recorded document {document} for the filing {accession}.", transient: false));
    }
}
