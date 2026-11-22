namespace EquityBrief.Api.Reading;

// What paper trading reads, through the one reader the worker reads the same rows through.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public sealed partial class ReadApi
{
    // The page's own list on an index is named by this word before its family.
    public const string PageList = PaperBooks.PageList;

    // The reader of the accounts' rows over this store.
    public EquityBrief.Data.PaperReads Paper => new(databaseFile);

    // The fill each trade is read with, and the session a swing trade was sold on, as the one reader writes them.
    static string Filled(string source, string alias, string index, string owner, string session) => EquityBrief.Data.PaperReads.Filled(source, alias, index, owner, session);

    const string FillColumns = EquityBrief.Data.PaperReads.FillColumns;

    const int FillWidth = EquityBrief.Data.PaperReads.FillWidth;

    const string SoldOn = EquityBrief.Data.PaperReads.SoldOn;

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public Task<IReadOnlyList<PaperRow>> PaperRowsAsync(string index) => Paper.PaperRowsAsync(index);

    public Task<IReadOnlyList<PlannedTradeRow>> PlannedTradesAsync(string index, DateOnly on) => Paper.PlannedTradesAsync(index, on);

    public Task<IReadOnlyDictionary<string, PaperStockRow>> PaperStocksAsync(IReadOnlyList<string> tickers, DateOnly night) => Paper.PaperStocksAsync(tickers, night);

    public Task<(IReadOnlyList<RuleTestRow> Rules, IReadOnlyList<RuleTestYearRow> Years)> RuleTestsAsync(string index, string history = Core.Loop.LongTest.Standing) =>
        Paper.RuleTestsAsync(index, history);

    public Task<IReadOnlyDictionary<(string Ticker, DateOnly Session), (double? Cost, decimal? Value)>> MemberCostsAsync(string index, IReadOnlyList<(string Ticker, DateOnly Session)> bought) =>
        Paper.MemberCostsAsync(index, bought);

    public Task<IReadOnlyDictionary<(string Ticker, DateOnly Session), decimal>> ClosesAsync(IReadOnlyList<string> tickers, DateOnly from) => Paper.ClosesAsync(tickers, from);

    public Task<IReadOnlyList<DateOnly>> SessionsFromAsync(DateOnly from) => Paper.SessionsFromAsync(from);

    public Task<IReadOnlyDictionary<DateOnly, decimal>> FundClosesAsync(string series, DateOnly from) => Paper.FundClosesAsync(series, from);

    public Task<IReadOnlyDictionary<DateOnly, (double? Breadth, double? VixOverMedian)>> ExposureNightsAsync(string index, IReadOnlyList<DateOnly> nights) =>
        Paper.ExposureNightsAsync(index, nights);
}
