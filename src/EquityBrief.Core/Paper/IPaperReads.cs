using EquityBrief.Core.Candidates;

namespace EquityBrief.Core.Paper;

// What Paper trading's accounts read of the store, through the one reader the read surface and the worker share, so the
// page and the worker work the same figures from the same rows.
// see: The monthly run stores each case standing from Paper trading's accounts in Core, and the local run writes its words
public interface IPaperReads
{
    Task<IReadOnlyList<PaperRow>> PaperRowsAsync(string index);

    Task<IReadOnlyList<PlannedTradeRow>> PlannedTradesAsync(string index, DateOnly on);

    Task<IReadOnlyList<PickRow>> PicksAsync(DateOnly on, string? ticker = null);

    Task<IndexNightRow?> IndexNightAsync(string index, DateOnly on);

    Task<IReadOnlyList<CandidateRow>> RegisteredCandidatesAsync();

    Task<IReadOnlyList<TakenTradeRow>> TakenTradesAsync();

    Task<IReadOnlyList<FamilyReplayRow>> FamilyReplaysAsync();

    Task<IReadOnlyDictionary<(string Ticker, DateOnly Session), decimal>> ClosesAsync(IReadOnlyList<string> tickers, DateOnly from);

    Task<IReadOnlyDictionary<(string Ticker, DateOnly Session), (double? Cost, decimal? Value)>> MemberCostsAsync(string index, IReadOnlyList<(string Ticker, DateOnly Session)> bought);

    Task<IReadOnlyDictionary<DateOnly, (double? Breadth, double? VixOverMedian)>> ExposureNightsAsync(string index, IReadOnlyList<DateOnly> nights);

    Task<DateOnly?> ModelCutoffAsync();

    Task<IReadOnlyDictionary<DateOnly, decimal>> FundClosesAsync(string series, DateOnly from);

    Task<(IReadOnlyList<RuleTestRow> Rules, IReadOnlyList<RuleTestYearRow> Years)> RuleTestsAsync(string index, string history = Loop.LongTest.Standing);

    Task<IReadOnlyList<DateOnly>> SessionsFromAsync(DateOnly from);

    Task<IReadOnlyDictionary<string, PaperStockRow>> PaperStocksAsync(IReadOnlyList<string> tickers, DateOnly night);
}
