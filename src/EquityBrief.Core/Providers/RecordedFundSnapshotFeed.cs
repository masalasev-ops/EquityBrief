namespace EquityBrief.Core.Providers;

// The funds' quarter-end holdings answered from recorded documents: each fund's filing list by its series and each
// filing's holdings document by its accession, read by the same readers as the archive's own answers. A series or a
// filing it holds nothing for is refused, as the archive's would be, rather than answered with nothing.
public sealed class RecordedFundSnapshotFeed(
    IReadOnlyDictionary<string, string> lists,
    IReadOnlyDictionary<string, string> snapshots) : IFundSnapshotFeed
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
}
