namespace EquityBrief.Core.Providers;

// The market series feed, answered from recorded responses rather than from the network: one captured
// file a series, named for it. It ships so a pull pointed at a capture is one the shipped code can make,
// and it holds no client, so it cannot fall back to the provider.
public sealed class RecordedMarketSeriesFeed(IReadOnlyDictionary<string, string> responses) : IMarketSeriesFeed
{
    public const string FilePrefix = "market-";

    public int Requests { get; private set; }

    public static RecordedMarketSeriesFeed FromFolder(string folder) =>
        new(Directory
            .GetFiles(folder, FilePrefix + "*.json")
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..],
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyList<ProviderBar>> SeriesAsync(
        string series,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        Requests++;

        // A series with no capture is refused as the provider refuses one it does not list, so the pull
        // names it and stores nothing for it rather than reading it as a series with no sessions.
        if (!responses.TryGetValue(series, out var captured))
        {
            throw new ProviderRefusal($"No captured response for the {series} series.", transient: false);
        }

        return Task.FromResult<IReadOnlyList<ProviderBar>>(
        [
            .. RecordedHistoricalBarFeed.Parse(captured, series)
                .Where(bar => bar.SessionDate >= from && bar.SessionDate <= to)
                .OrderBy(bar => bar.SessionDate),
        ]);
    }
}
