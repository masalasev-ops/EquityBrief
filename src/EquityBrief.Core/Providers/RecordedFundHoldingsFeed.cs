namespace EquityBrief.Core.Providers;

// The funds' holdings files answered from recorded files rather than from the network: one captured file an index,
// named for it. A folder holding none reads no wider index, which is every fixture night before the S&P 400 and 600
// joined. It holds no client, so it cannot fall back to the network.
public sealed class RecordedFundHoldingsFeed(IReadOnlyDictionary<string, string> responses) : IFundHoldingsFeed
{
    public const string FilePrefix = "fund-holdings-";

    public static RecordedFundHoldingsFeed None { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public int Requests { get; private set; }

    public IReadOnlyList<string> Indices => [.. FundHoldings.Indices.Where(responses.ContainsKey)];

    public static RecordedFundHoldingsFeed FromFolder(string folder) =>
        new(Directory
            .GetFiles(folder, FilePrefix + "*.csv")
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..],
                File.ReadAllText,
                StringComparer.Ordinal));

    public Task<FundHoldingsFile> HoldingsAsync(string indexCode, CancellationToken cancellation = default)
    {
        Requests++;

        return !responses.TryGetValue(indexCode, out var captured) || !FundHoldings.Funds.TryGetValue(indexCode, out var fund)
            ? throw new ProviderRefusal($"No captured holdings file for the {indexCode} index.", transient: false)
            : Task.FromResult(FundHoldings.Parse(captured, fund.Fund));
    }
}
