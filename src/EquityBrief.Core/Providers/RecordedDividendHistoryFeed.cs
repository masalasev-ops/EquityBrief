namespace EquityBrief.Core.Providers;

// One name's dividends, answered from a recorded response, read by the parser the live feed reads with.
//
// A name the capture does not hold is refused as the provider refuses a name it does not serve, so a replayed history
// run names it and goes on rather than keeping it as a company that never paid.
public sealed class RecordedDividendHistoryFeed(IReadOnlyDictionary<string, string> responses) : IDividendHistoryFeed
{
    public const string Prefix = "dividends-";

    public int Requests { get; private set; }

    public static RecordedDividendHistoryFeed FromFolder(string folder) =>
        new(Directory
            .GetFiles(folder, Prefix + "*.json")
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path)[Prefix.Length..],
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyList<DividendPaid>> DividendsAsync(string ticker, DateOnly from, CancellationToken cancellation = default)
    {
        Requests++;

        if (!responses.TryGetValue(ticker, out var captured))
        {
            throw new ProviderRefusal($"No captured dividends for {ticker}.", transient: false);
        }

        return Task.FromResult<IReadOnlyList<DividendPaid>>([.. DividendAnswers.Parse(captured, ticker).Where(paid => paid.ExDate >= from)]);
    }
}
