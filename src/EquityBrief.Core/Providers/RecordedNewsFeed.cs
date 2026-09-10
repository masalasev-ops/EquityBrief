using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One article, as the feed delivers it.
//
// Symbols is the attribution and is why a night needs no per-name request: the
// feed is queryable by date with no ticker, and every row names the tickers it
// is about, so one request is fanned out in code.
// see: News is one dated query, paged to cover the day, and attributed to names locally
//
// Channel is the domain the link points at, and it is named Channel rather than
// Publisher because the 1.7 measurement showed it is the aggregator that
// delivered the article and not the outlet that wrote it: 1,173 of 1,253
// articles arrived under one domain, and the writing outlet appears only inside
// the text where it appears at all.
public sealed record NewsArticle(
    DateTimeOffset Published,
    string Title,
    string Channel,
    IReadOnlyList<string> Symbols,
    string Text);

// The news feed of section 5, behind an interface so the nightly path and the
// suite meet the same shape.
//
// News was the only feed without one until 2.5, which meant it was the only feed
// whose request count no contract forced: the 1.7 measurement ran through a
// class the nightly path does not reach, and a live implementation could have
// made one request per name with nothing to say so. `Requests` is on the
// interface for the same reason it is on the other four.
//
// One request for the window, whatever the universe is. The feed is queryable by
// date with no ticker, which was probed on the operator's key at 1.5 rather than
// assumed, and every row names the tickers it is about, so the fan-out is
// arithmetic on a payload rather than a second request
// (see: News is one dated query, paged to cover the day, and attributed to names locally).
public interface INewsFeed
{
    int Requests { get; }

    Task<IReadOnlyList<NewsArticle>> ArticlesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default);
}

// One request's worth of articles, indexed by the names they are about.
//
// The fan-out, done in code and not by the provider. This is what the zero
// per-name rule buys: one dated request covers the whole market and every name
// in it is reached by reading the attribution the payload already carries.
public static class NewsAttribution
{
    public static IReadOnlyDictionary<string, IReadOnlyList<NewsArticle>> ByName(
        IEnumerable<NewsArticle> articles)
    {
        var byName = new Dictionary<string, List<NewsArticle>>(StringComparer.OrdinalIgnoreCase);

        foreach (var article in articles)
        {
            foreach (var symbol in article.Symbols)
            {
                if (!byName.TryGetValue(symbol, out var forName))
                {
                    byName[symbol] = forName = [];
                }

                forName.Add(article);
            }
        }

        return byName.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<NewsArticle>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}

// The news feed, answered from a recorded response.
//
// The parser was written against a captured response, which is the obligation
// 1.2 filed against this checkpoint. The measurement this checkpoint runs needs
// live responses in any case; what it must not do is write the parser against a
// payload composed to suit it.
public sealed class RecordedNewsFeed(string response) : INewsFeed
{
    public const string FilePrefix = "news-";

    public int Requests { get; private set; }

    public static RecordedNewsFeed FromFolder(string folder)
    {
        var files = Directory.GetFiles(folder, FilePrefix + "*.json");

        return files.Length == 1
            ? new RecordedNewsFeed(File.ReadAllText(files[0]))
            : throw new InvalidOperationException(
                $"{folder} holds {files.Length} news responses and a night fetches one.");
    }

    // One request for the window, whatever the universe is.
    public Task<IReadOnlyList<NewsArticle>> ArticlesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult<IReadOnlyList<NewsArticle>>(
        [
            .. Parse(response)
                .Where(article => DateOnly.FromDateTime(article.Published.UtcDateTime) >= from
                    && DateOnly.FromDateTime(article.Published.UtcDateTime) <= to),
        ]);
    }

    public static IReadOnlyList<NewsArticle> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                "The captured news response is not an array of articles. A payload that cannot be " +
                "parsed must fail rather than answer with no articles, which would read as a " +
                "quiet fortnight rather than as a broken feed.");
        }

        return [.. document.RootElement.EnumerateArray().Select(Read)];
    }

    static NewsArticle Read(JsonElement entry)
    {
        // The instant carries an offset, "2026-09-08T20:56:32+00:00", so it is
        // parsed as one rather than as a date. Rounded to a date here it would
        // land on the wrong session for anything published in the evening,
        // which is when a great deal of company news is published.
        if (!entry.TryGetProperty("date", out var date) || date.ValueKind is not JsonValueKind.String
            || !DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var published))
        {
            throw new FormatException("An article carries no date this feed will accept.");
        }

        var symbols = entry.TryGetProperty("symbols", out var tickers) && tickers.ValueKind is JsonValueKind.Array
            ? tickers.EnumerateArray()
                .Where(one => one.ValueKind is JsonValueKind.String)
                .Select(one => one.GetString()!)
                // "AAPL.US" as delivered. The exchange suffix is dropped here
                // because every store in this system keys on the ticker alone,
                // and carrying both forms would leave two spellings of one name.
                .Select(one => one.Split('.')[0])
                .Where(one => one.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return new NewsArticle(
            published,
            Text(entry, "title"),
            Channel(Text(entry, "link")),
            symbols,
            Text(entry, "content"));
    }

    // The domain, lowercased and without a leading www. What this is not is the
    // publisher, which the 1.7 measurement is the record of.
    public static string Channel(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri)
            ? uri.Host.ToLowerInvariant().StartsWith("www.", StringComparison.Ordinal)
                ? uri.Host.ToLowerInvariant()[4..]
                : uri.Host.ToLowerInvariant()
            : string.Empty;

    static string Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"An article carries no {name}, or one that is not a string.");
}
