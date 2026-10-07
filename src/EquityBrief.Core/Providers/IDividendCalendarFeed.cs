namespace EquityBrief.Core.Providers;

// One declared ex-dividend date the provider's dividend calendar files for a listing on the index's own exchange.
public sealed record ExDividend(string Ticker, DateOnly Date);

// The provider's dividend calendar, asked for one session at a time: every listing whose ex-dividend date falls on that
// session, in one request a page, whatever the index's size.
// see: The night asks the dividend calendar for each of the next 21 sessions, one request a session
public interface IDividendCalendarFeed
{
    int Requests { get; }

    Task<IReadOnlyList<ExDividend>> ExDividendsAsync(DateOnly session, CancellationToken cancellation = default);
}
