using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Bars;

// The provider's series of a stock whose ticker changed and whose history it keeps under the old ticker, joined where the
// operator has ruled the two one company: the sessions before the new ticker's first, from the old ticker, brought to the
// new one's scale by the two closes of a session both hold, the raw close and the volume as sent. A stock with no join
// reads as the provider answers it, at no request more. One request more where the join is read.
// see: A ticker change the provider keeps under two tickers is joined once on the operator's word, and every read of the provider's series reads the join
public sealed class JoinedHistoricalBarFeed(IHistoricalBarFeed inner, Func<IReadOnlyDictionary<string, string>> joins) : IHistoricalBarFeed
{
    readonly Lazy<IReadOnlyDictionary<string, string>> held = new(joins);

    public int Requests => inner.Requests;

    public async Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var later = await inner.BarsAsync(ticker, from, to, cancellationToken).ConfigureAwait(false);

        if (!held.Value.TryGetValue(ticker, out var earlier))
        {
            return later;
        }

        var first = later.Count > 0 ? later.Min(bar => bar.SessionDate) : to;

        if (first <= from)
        {
            return later;
        }

        var before = await inner.BarsAsync(earlier, from, first, cancellationToken).ConfigureAwait(false);

        return Joined(later, before);
    }

    // The older sessions at the newer's scale, then every newer session. The scale is 1 where the two hold no session in
    // common, since nothing then says otherwise.
    public static IReadOnlyList<ProviderBar> Joined(IReadOnlyList<ProviderBar> later, IReadOnlyList<ProviderBar> earlier)
    {
        if (later.Count == 0)
        {
            return [.. earlier.OrderBy(bar => bar.SessionDate)];
        }

        var first = later.MinBy(bar => bar.SessionDate)!;
        var shared = earlier.FirstOrDefault(bar => bar.SessionDate == first.SessionDate);
        var scale = shared is { Close: > 0 } ? first.Close / shared.Close : 1m;

        return
        [
            .. earlier
                .Where(bar => bar.SessionDate < first.SessionDate)
                .OrderBy(bar => bar.SessionDate)
                .Select(bar => bar with
                {
                    Open = decimal.Round(bar.Open * scale, Statistic.Places),
                    High = decimal.Round(bar.High * scale, Statistic.Places),
                    Low = decimal.Round(bar.Low * scale, Statistic.Places),
                    Close = decimal.Round(bar.Close * scale, Statistic.Places),
                }),
            .. later.OrderBy(bar => bar.SessionDate),
        ];
    }
}
