namespace EquityBrief.Core.Providers;

// The daily series of a market measure that is no member's: the index itself and the VIX.
//
// Asked by the history pull on the operator's command and by no night, one request a series over the
// whole span, at the weight the historical endpoint it is asked through carries. The count is on the
// interface for the reason the bar feed's is: the run log records what was asked, measured off the
// feed rather than stated by the caller.
// see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
public interface IMarketSeriesFeed
{
    int Requests { get; }

    Task<IReadOnlyList<ProviderBar>> SeriesAsync(
        string series,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
