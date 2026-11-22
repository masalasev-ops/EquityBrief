using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;

namespace EquityBrief.Core.Paper;

// An index's accounts before they are costed: each account with its trades as its plan made them, the night's trades under
// a plan, the register as read, the stored trades and the provisional rule's settings off the index's night.
public sealed record PaperSources(
    IReadOnlyList<PaperAccountSource> Accounts,
    IReadOnlyList<PlannedTradeRow> Planned,
    IReadOnlyList<CandidateRow> Register,
    IReadOnlyList<PaperRow> Rows,
    IndexRuleSettings? Provisional);

// An index's accounts as of a night, worked: each costed, halved and toned before its walk, each walked and judged, the
// closes they were marked by, the index's fund's closes, the sessions they were walked over and each stock's company and
// readings on the night.
public sealed record PaperWorked(
    PaperSources Read,
    IReadOnlyList<PaperAccountSource> Sources,
    IReadOnlyList<PaperAccountView> Views,
    IReadOnlyDictionary<(string Ticker, DateOnly Session), decimal> Closes,
    IReadOnlyDictionary<DateOnly, decimal> FundCloses,
    IReadOnlyList<DateOnly> Sessions,
    IReadOnlyDictionary<string, PaperStockRow> Stocks)
{
    public decimal? Close(string ticker, DateOnly session) => Closes.TryGetValue((ticker, session), out var close) ? close : null;

    public decimal? Fund(DateOnly session) => FundCloses.TryGetValue(session, out var close) ? close : null;
}

// One account's figures as the page draws them: its trades, its walk's return and its open and closed trades, its verdict,
// its record in its own measure and its progress toward the count a verdict needs.
public sealed record PaperFigures(
    string Key,
    string Family,
    string Standing,
    int Trades,
    int Ended,
    double? Return,
    int Open,
    int Closed,
    DateOnly? Since,
    string Verdict,
    string Unit,
    string? TooFew,
    int? Counted,
    PaperProgress? Progress);

// Paper trading's accounts worked from the store's rows through the one reader, the one function the page and the worker
// call, so the two draw the same figures.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public static class PaperWork
{
    // An index's accounts as their trades stand before costing, the register read as the night's page reads it, as of the
    // instant a night run again read it at. The operator's own trades are left out where the reader is a copy's.
    // see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
    public static async Task<PaperSources> SourcesAsync(IPaperReads reads, string code, string name, bool large, DateOnly night, bool yours = true)
    {
        var rows = await reads.PaperRowsAsync(code);

        // A live rule's trade under a plan buying at a price drawn as its plan made it.
        // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
        var planned = await reads.PlannedTradesAsync(code, night);
        var cells = large ? PlannedTrades.Cells(PickCells.Of(await reads.PicksAsync(night), night), planned, night) : [];
        var indexNight = await reads.IndexNightAsync(code, night);
        var provisional = indexNight is null ? null : IndexRuleSettings.Read(indexNight.Settings);
        var register = await reads.RegisteredCandidatesAsync();
        var accounts = PlannedTrades.Accounts(
            PaperBooks.Accounts(
                large,
                name,
                night,
                register,
                provisional,
                rows,
                cells,
                PaperBooks.Yours(yours ? await reads.TakenTradesAsync() : [], code),
                large ? await reads.FamilyReplaysAsync() : [],
                RuleRegister.ReadAsOf(indexNight, night)),
            planned);

        return new PaperSources(accounts, planned, register, rows, provisional);
    }

    // An index's accounts worked as of a night: costed, halved where a graded exposure held one switch, toned before the
    // local model's cutoff, then each walked and judged over the sessions from its first trade.
    public static async Task<PaperWorked> WorkAsync(IPaperReads reads, string code, string name, bool large, DateOnly night, AccountSettings? settings, bool yours = true)
    {
        var read = await SourcesAsync(reads, code, name, large, night, yours);
        var sources = read.Accounts;
        (string Ticker, DateOnly Session)[] bought = [.. sources.SelectMany(account => account.Trades.Select(one => (one.Trade.Ticker, one.Trade.Bought))).Distinct()];

        // The trades an account leaves out are read for their trade log's result so far, and walked by nothing.
        (string Ticker, DateOnly Session)[] earlier = [.. sources.SelectMany(account => (account.Earlier ?? []).Select(one => (one.Trade.Ticker, one.Trade.Bought)).Concat(account.Same.Select(one => (one.Trade.Ticker, one.Trade.Bought)))).Distinct()];
        var from = sources.Select(account => account.Since).OfType<DateOnly>().Concat(bought.Select(one => one.Session)).DefaultIfEmpty(night).Min();
        var earliest = earlier.Select(one => one.Session).Append(from).Min();

        // The stocks bought, and those waiting to be bought, whose rows draw their company and the night's close too.
        var tickers = bought.Concat(earlier).Select(one => one.Ticker)
            .Concat(read.Planned.Where(row => row.Listed <= night && PlannedTrades.IsWaiting(row)).Select(row => row.Ticker))
            .Concat(sources.SelectMany(account => (account.Unfilled ?? []).Select(one => one.Ticker)))
            .Distinct(StringComparer.Ordinal).ToArray();
        var closes = await reads.ClosesAsync(tickers, earliest);

        sources = PaperBooks.Costed(sources, read.Rows, await reads.MemberCostsAsync(code, bought), closes);

        // A rule standing at the graded exposure sizes a night's trades at half where its exposure held one switch.
        // see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
        sources = PaperBooks.Halved(sources, read.Register, await reads.ExposureNightsAsync(code, PaperBooks.ExposedNights(sources, read.Register)));

        // The drift's tone variant and a Fundamentals rule asking a flag first carry the line saying their history before the
        // local model's cutoff does not count, each with the cutoff's year its verdict is judged from.
        sources = PaperBooks.Toned(sources, read.Register, await reads.ModelCutoffAsync());

        var fund = await reads.FundClosesAsync(PaperBooks.Fund(code), from);
        var (tests, years) = await reads.RuleTestsAsync(code);
        var sessions = await reads.SessionsFromAsync(earliest);
        var stocks = await reads.PaperStocksAsync(tickers, night);

        decimal? Close(string ticker, DateOnly session) => closes.TryGetValue((ticker, session), out var close) ? close : null;

        decimal? Fund(DateOnly session) => fund.TryGetValue(session, out var close) ? close : null;

        var views = PaperBooks.Views(sources, tests, years, settings, sessions, night, Close, Fund);

        return new PaperWorked(read, sources, views, closes, fund, sessions, stocks);
    }

    // Each account's figures as the page's tiles and record draw them.
    public static IReadOnlyList<PaperFigures> Figures(PaperWorked worked) =>
    [
        .. worked.Views.Select(view => new PaperFigures(
            view.Key,
            view.Family,
            view.Standing,
            view.Trades,
            view.Ended,
            view.Walk?.Return,
            view.Open,
            view.Closed,
            view.Since,
            view.Verdict,
            view.Unit,
            view.TooFew,
            view.Counted,
            view.Progress)),
    ];
}
