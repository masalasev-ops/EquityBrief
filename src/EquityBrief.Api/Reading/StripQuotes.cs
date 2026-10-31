using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quotes;

namespace EquityBrief.Api.Reading;

// Yahoo's quotes of the market strip's symbols, asked in one request at most once every five minutes while the regular
// session is open and kept in memory for the read surface's life, never stored. Outside the session nothing is asked;
// an answer that failed is kept as none for the same five minutes, so a page opened again does not ask again at once.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public sealed class StripQuotes(IStripQuoteFeed feed)
{
    readonly SemaphoreSlim gate = new(1, 1);

    (DateTimeOffset At, IReadOnlyDictionary<string, SparkQuote>? Quotes)? kept;

    public int Requests => feed.Requests;

    // The quotes and the instant they were asked at, or none outside the session.
    public async Task<(IReadOnlyDictionary<string, SparkQuote>? Quotes, DateTimeOffset? Asked)> AskAsync(DateTimeOffset now, TimeZoneInfo zone, CancellationToken cancellation = default)
    {
        if (!RegularSession.IsOpen(now, zone))
        {
            return (null, null);
        }

        await gate.WaitAsync(cancellation);

        try
        {
            if (kept is { } held && now - held.At < TimeSpan.FromMinutes(MarketStrip.CacheMinutes) && now >= held.At)
            {
                return (held.Quotes, held.At);
            }

            IReadOnlyDictionary<string, SparkQuote>? quotes;

            try
            {
                quotes = await feed.QuotesAsync(MarketStrip.YahooSymbols, cancellation);
            }
            catch (Exception failure) when (failure is ProviderRefusal or FormatException or JsonException or TaskCanceledException)
            {
                quotes = null;
            }

            kept = (now, quotes);

            return (quotes, now);
        }
        finally
        {
            gate.Release();
        }
    }
}
