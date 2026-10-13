namespace EquityBrief.Core.Providers;

// The two free sources answered from recorded responses, a ticker's Yahoo chart, Nasdaq history and Nasdaq quote each
// read by the parser the live feed reads with, and asked in the order the live feed asks them. A ticker the capture holds
// nothing for is answered as a source answers a session it holds no bar for: none.
public sealed class RecordedSubstituteBarFeed(
    IReadOnlyDictionary<string, string> charts,
    IReadOnlyDictionary<string, string> histories,
    IReadOnlyDictionary<string, string> quotes) : ISubstituteBarFeed
{
    public const string ChartPrefix = "substitute-yahoo-";

    public const string HistoryPrefix = "substitute-nasdaq-history-";

    public const string QuotePrefix = "substitute-nasdaq-info-";

    public static RecordedSubstituteBarFeed None { get; } = new(
        new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    public int Requests { get; private set; }

    public static RecordedSubstituteBarFeed FromFolder(string folder) =>
        new(Captured(folder, ChartPrefix), Captured(folder, HistoryPrefix), Captured(folder, QuotePrefix));

    static Dictionary<string, string> Captured(string folder, string prefix) =>
        Directory
            .GetFiles(folder, prefix + "*.json")
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)[prefix.Length..], File.ReadAllText, StringComparer.Ordinal);

    public Task<DayBar?> BarAsync(string ticker, DateOnly session, CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(charts.TryGetValue(ticker, out var chart)
            ? SubstituteBars.YahooChart(chart).LastOrDefault(bar => bar.Session == session)
            : null);
    }

    public Task<DayRange?> RangeAsync(string ticker, DateOnly session, CancellationToken cancellation = default)
    {
        Requests++;

        if (histories.TryGetValue(ticker, out var history)
            && SubstituteBars.NasdaqHistory(history).FirstOrDefault(range => range.Session == session) is { } dated)
        {
            return Task.FromResult<DayRange?>(dated);
        }

        Requests++;

        return Task.FromResult(quotes.TryGetValue(ticker, out var quote) && SubstituteBars.NasdaqInfo(quote) is { } closed && closed.Session == session
            ? closed
            : null);
    }
}
