using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The chart of what is behind a stock, worked out when a page is drawn from the readings the night stored for every member
// of its index and the family's learned score there, by the core's one table of headings and its arithmetic: the stock's
// percentile under each heading among the members, and each heading's push only where the score passed on the index.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
public static class BehindScreen
{
    public static BehindItView Of(string ticker, string indexName, IReadOnlyDictionary<string, IReadOnlyList<double?>> readings, RidgeModel? model, bool passed, string? unread = null)
    {
        IReadOnlyList<double?> own = readings.TryGetValue(ticker, out var held) ? held : new double?[LedgerReadings.Count];
        var percentiles = LedgerHeadings.Percentiles(own, [.. readings.Values]);
        var pushes = passed && model is not null && readings.ContainsKey(ticker) ? LedgerHeadings.Pushes(model, own) : null;

        return new BehindItView(
            indexName,
            [.. LedgerHeadings.All.Select((heading, at) => new BehindHeadingView(heading.Name, percentiles[at], pushes?[at]))],
            pushes is null ? null : LedgerHeadings.Centre(model!),
            pushes is null ? null : model!.Score(own),
            passed ? unread : null);
    }
}
