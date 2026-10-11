using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// The windows of one name's news a research pass reads: the span of each stored move, which is
// where the cause of that move may rest, and the days from the company's own filing to the night,
// which is what the sections built across the evidence are handed beside it. Spans that overlap
// or touch are read as one, and nothing older than three months before the night is read.
//
// Never the stored year. The rule hands a section nothing outside these windows, and one name's
// year can pass the most pages a query reads: 6.11's production run asked for MSFT's and the
// provider still had more after twenty pages of a thousand articles.
// see: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
public static class NewsWindows
{
    // Where the company has no filing a pass can read, the sections built across the evidence are
    // handed the newest documents of the quarter back from the night, which is the stretch a
    // results release would have covered.
    public const int WithoutAFilingMonths = 3;

    // How far back from the night a pass reads news at all. A move older than this is read
    // with no document, and its cause is not written.
    public const int NewsMonths = 3;

    public static IReadOnlyList<(DateOnly From, DateOnly To)> For(IReadOnlyList<MoveWindow> moves, DateOnly? filedOn, DateOnly asOf)
    {
        var earliest = asOf.AddMonths(-NewsMonths);
        var since = filedOn is { } filed && filed <= asOf ? filed : asOf.AddMonths(-WithoutAFilingMonths);

        var spans = moves
            .Where(move => move.From is { } from && from <= move.To)
            .Select(move => (From: move.From!.Value, move.To))
            .Append((From: since, To: asOf))
            .Where(span => span.To >= earliest)
            .Select(span => (From: span.From < earliest ? earliest : span.From, span.To))
            .OrderBy(span => span.From)
            .ThenBy(span => span.To)
            .ToArray();

        var windows = new List<(DateOnly From, DateOnly To)>();

        foreach (var span in spans)
        {
            if (windows.Count > 0 && span.From <= windows[^1].To.AddDays(1))
            {
                windows[^1] = (windows[^1].From, span.To > windows[^1].To ? span.To : windows[^1].To);
            }
            else
            {
                windows.Add(span);
            }
        }

        return windows;
    }

    // A window cut into pieces of at most a month, each from its first day to the day before the same date a month on:
    // the provider's time to answer grows with the articles a window holds, and a month of a heavily covered name's news
    // answers well inside a request's limit where its quarter did not.
    // see: A name's news is asked of the provider a month at a time, and a month it does not answer in time is read as unread
    public static IReadOnlyList<(DateOnly From, DateOnly To)> Months(DateOnly from, DateOnly to)
    {
        var pieces = new List<(DateOnly From, DateOnly To)>();

        for (var start = from; start <= to; start = start.AddMonths(1))
        {
            var end = start.AddMonths(1).AddDays(-1);

            pieces.Add((start, end < to ? end : to));
        }

        return pieces;
    }

    // The articles of the windows, asked a month at a time, each month the provider refuses or does not answer in time
    // named as unread and the months it answered kept, so one slow month costs that month and not the window around it.
    public static async Task<(IReadOnlyList<NewsArticle> Articles, IReadOnlyList<string> Unread)> ReadAsync(
        INameNewsFeed news,
        string ticker,
        IReadOnlyList<(DateOnly From, DateOnly To)> windows,
        CancellationToken cancellation = default)
    {
        var articles = new List<NewsArticle>();
        var unread = new List<string>();

        foreach (var (windowFrom, windowTo) in windows)
        {
            foreach (var (from, to) in Months(windowFrom, windowTo))
            {
                try
                {
                    articles.AddRange(await news.ArticlesAsync(ticker, from, to, cancellation).ConfigureAwait(false));
                }
                catch (ProviderRefusal refused)
                {
                    unread.Add("news: " + refused.Message);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    unread.Add(FormattableString.Invariant($"news: the provider did not answer for {from:yyyy-MM-dd} to {to:yyyy-MM-dd} in time on any try"));
                }
            }
        }

        return (articles, unread);
    }
}
