namespace EquityBrief.Core.Providers;

// One name's own news, on demand.
//
// The night reads news as one dated query for the whole market and attributes it to
// names in code, and that is the only news query the night may make. This is the other
// query the same endpoint answers: the articles the provider attributes to one name over
// a window, which is what a research pass reads when that name is opened. A separate
// interface rather than a ticker on the night's, because the night's feed record is the
// one whose count the limits table bounds, and a per-name query reachable from it would
// be reachable by nothing worse than a typo.
// see: The nightly run is arithmetic only
// see: Everything expensive happens when a name is opened
public interface INameNewsFeed
{
    int Requests { get; }

    Task<IReadOnlyList<NewsArticle>> ArticlesAsync(
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default);
}

// One name's news, answered from a recorded response, one file per name.
//
// Read by the parser the night's feed uses, because the provider sends one shape
// whether or not a ticker was asked for, which 1.5 probed. A name nobody captured is
// refused by name rather than answered with no articles, which would read as a year in
// which nobody wrote about the company.
public sealed class RecordedNameNewsFeed(string folder) : INameNewsFeed
{
    public const string FilePrefix = "name-news-";

    public int Requests { get; private set; }

    public static string FileFor(string ticker) => FilePrefix + ticker.ToUpperInvariant() + ".json";

    public Task<IReadOnlyList<NewsArticle>> ArticlesAsync(
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default)
    {
        Requests++;

        var file = Path.Combine(folder, FileFor(ticker));

        if (!File.Exists(file))
        {
            throw new InvalidOperationException(
                $"No capture of {ticker}'s own news is held in '{folder}'. A name with no capture is refused rather " +
                "than answered with no articles, because an empty answer reads as a company nobody wrote about.");
        }

        return Task.FromResult<IReadOnlyList<NewsArticle>>(
        [
            .. RecordedNewsFeed.Parse(File.ReadAllText(file))
                .Where(article => DateOnly.FromDateTime(article.Published.UtcDateTime) >= from
                    && DateOnly.FromDateTime(article.Published.UtcDateTime) <= to),
        ]);
    }
}
