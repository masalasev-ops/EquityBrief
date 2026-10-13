using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// One paper account before it is walked: its key, family, standing, name and words, the register's name its test row
// carries, none for the live or provisional rule, whose row is read by its standing, whether it is a book, its trades
// with each one's stored result, the day its record starts, and the name its stored trades are kept under, none where
// they are the S&P 500's page list or the operator's.
public sealed record PaperAccountSource(
    string Key,
    string Family,
    string Standing,
    string Name,
    string Words,
    string? Tested,
    bool Book,
    IReadOnlyList<(PaperTrade Trade, double? Result)> Trades,
    DateOnly? Since,
    string? Listed = null)
{
    // Whether a stored trade is one of this account's: kept under its name, or under its name and the filter version it
    // was listed from.
    public bool Keeps(PaperRow row) =>
        Listed is { } name && (row.Account == name || row.Account.StartsWith(name + ", from version ", StringComparison.Ordinal));
}

// An index's paper accounts as a page reads them: each before its walk, each drawn, and the closes they were marked by.
public sealed record PaperAccounts(IReadOnlyList<PaperAccountSource> Sources, IReadOnlyList<PaperAccountView> Views, Func<string, DateOnly, decimal?> Close);

// The projection from stored trades and test rows to paper trading's accounts and their trade logs. The accounts are an
// index's: each family's live or provisional rule's list, the page's own, each registered variant's own list or book,
// numbered as its card numbers it, and the operator's own trades. Each is walked by the core's one function from the
// account settings, and its verdict and its record's label are read off the newest per-rule test and its own trades,
// never merged. Not in the read API, whose claim is that it computes nothing, and not in the page, whose claim is the same.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A paper account draws its test years verdict and its record since registration apart, and never merges them
public static class PaperScreen
{
    // The keys the page's link carries: the account a trade log draws and the family whose variants the chart adds.
    public const string AccountQuery = "account";

    public const string FamilyQuery = "family";

    // The operator's own account's key.
    public const string YouKey = "you";

    // The most holdings a book can hold, each sector's leaders across the eleven sectors, which a holding's equal share
    // is sized across.
    public static int BookHoldings => GicsSectors.Funds.Count * HeavyweightRule.Leaders;

    // The fund an index's accounts are compared with.
    public static string Fund(string index) => index switch
    {
        "MID" => "IJH",
        "SML" => "IJR",
        _ => "SPY",
    };

    static string ListKey(string family) => ReadApi.PageList + "-" + family;

    // Every account on an index in the one family order, each family's live or provisional rule first and its variants
    // after it as its card numbers them, then the operator's own.
    public static IReadOnlyList<PaperAccountSource> Accounts(
        bool large,
        string indexName,
        DateOnly night,
        IReadOnlyList<CandidateRow> register,
        IndexRuleSettings? provisional,
        IReadOnlyList<PaperRow> rows,
        IReadOnlyList<PickCell> pageCells,
        IReadOnlyList<(PaperTrade Trade, double? Result, string Family)> yours)
    {
        var accounts = new List<PaperAccountSource>();

        static IReadOnlyList<(PaperTrade, double?)> Of(IEnumerable<PaperRow> rows) =>
        [
            .. rows.Select(row => (new PaperTrade(
                row.Ticker,
                row.Bought,
                row.Entry,
                row.Stop,
                row.Ended,
                PaperAccount.SaleOf(row.Entry, row.Stop, row.Ended, row.Result, row.Book),
                null,
                row.Place,
                row.Book ? BookHoldings : null), row.Result)),
        ];

        foreach (var family in SetupFamilies.Order)
        {
            var (live, variants) = RuleScreen.Accounts(family, large, indexName, register, night, provisional);
            var book = family == HeavyweightRule.Name;
            var pageList = large && SetupFamilies.InPageOrder.Any(swing => swing.Name == family);
            var listed = pageList ? null : !large && live is not null ? live.Candidate : ReadApi.PageList + " " + family;

            // The S&P 500's page lists every family but the fundamentals-first family by its live rule, registered or not.
            var isLive = live is not null || (large && family != FundamentalsRule.Name);
            var head = new PaperAccountSource(
                ListKey(family),
                family,
                isLive ? PaperAccountView.Live : PaperAccountView.Provisional,
                isLive ? "Live" : "Provisional",
                live?.Candidate ?? FamilyWords(family) + (isLive ? ", the page's own list" : ", the page's list on provisional settings"),
                null,
                book,
                [],
                null,
                listed);
            IReadOnlyList<(PaperTrade, double?)> trades = pageList ? PageTrades(pageCells.Where(cell => cell.Family == family)) : Of(rows.Where(head.Keeps));

            accounts.Add(head with { Trades = trades, Since = trades.Count > 0 ? trades.Min(trade => trade.Item1.Bought) : null });

            foreach (var (row, tab) in variants)
            {
                var variant = new PaperAccountSource(
                    RunScreen.Slug(row.Candidate),
                    family,
                    PaperAccountView.Variant,
                    tab,
                    row.Candidate,
                    row.Candidate,
                    book,
                    [],
                    DateOnly.FromDateTime(register.Where(one => one.Candidate == row.Candidate).Min(one => one.RegisteredAt).UtcDateTime),
                    row.Candidate);

                accounts.Add(variant with { Trades = Of(rows.Where(variant.Keeps)) });
            }
        }

        accounts.Add(new PaperAccountSource(
            YouKey,
            YouKey,
            PaperAccountView.You,
            "You",
            "your own trades taken from the cards on this index",
            null,
            false,
            [.. yours.Select(one => (one.Trade, one.Result))],
            yours.Count > 0 ? yours.Min(one => one.Trade.Bought) : null));

        return accounts;
    }

    // The S&P 500's page list of one swing family as its trades: every trade but a repeat listing and one with no outcome
    // stored, each sold where its result puts it, in the order its night listed them.
    static IReadOnlyList<(PaperTrade, double?)> PageTrades(IEnumerable<PickCell> cells) =>
    [
        .. cells
            .Where(cell => cell.RepeatOf is null && cell.Status != PickStatus.Missing && cell.Buy is not null)
            .GroupBy(cell => cell.Night)
            .SelectMany(night => night.Select((cell, place) => (new PaperTrade(
                cell.Ticker,
                cell.Night,
                cell.Buy!.Value,
                cell.Stop,
                cell.EndedOn,
                PaperAccount.SaleOf(cell.Buy!.Value, cell.Stop, cell.EndedOn, cell.Result, false)
                    ?? (cell.EndedOn is not null && cell.ReturnPct is { } made ? cell.Buy!.Value * (1m + (Statistic.ToPrice(made) / 100m)) : null),
                null,
                place + 1), cell.Result))),
    ];

    // The operator's taken trades on an index as their account's: each bought at its fill on its fill's session, sold at
    // its end or its recorded exit, a holding's result read as a fraction of its fill.
    public static IReadOnlyList<(PaperTrade Trade, double? Result, string Family)> Yours(IReadOnlyList<TakenTradeRow> taken, string index) =>
    [
        .. taken.Where(trade => trade.Index == index).Select(trade =>
        {
            var book = trade.Family == HeavyweightRule.Name;

            return (new PaperTrade(
                trade.Ticker,
                trade.FillDate,
                trade.Fill,
                book ? null : trade.Stop,
                trade.EndedOn ?? trade.ExitDate,
                trade.EndPrice ?? trade.ExitPrice,
                null,
                0,
                book ? BookHoldings : null), book && trade.Result is { } percent ? percent / 100 : trade.Result, trade.Family);
        }),
    ];

    // The family's words a provisional account is named by.
    static string FamilyWords(string family) =>
        family == HeavyweightRule.Name ? SetupFamilies.SectorHeavyweights.Heading : SetupFamilies.Named(family)?.Label ?? family;

    // Every account's trades priced with their round trips: the stored round trip, else the member readings' cost on the
    // buy session, else the published table at the company value read that session; none where its bar is not held.
    public static IReadOnlyList<PaperAccountSource> Costed(
        IReadOnlyList<PaperAccountSource> accounts,
        IReadOnlyList<PaperRow> rows,
        IReadOnlyDictionary<(string Ticker, DateOnly Session), (double? Cost, decimal? Value)> members,
        IReadOnlyDictionary<(string Ticker, DateOnly Session), decimal> closes)
    {
        return
        [
            .. accounts.Select(account => account with
            {
                Trades =
                [
                    .. account.Trades.Select(one =>
                    {
                        var trade = one.Trade;
                        var row = rows.FirstOrDefault(held => held.Ticker == trade.Ticker && held.Bought == trade.Bought && account.Keeps(held));
                        var member = members.GetValueOrDefault((trade.Ticker, trade.Bought));

                        return (trade with
                        {
                            RoundTrip = PaperAccount.RoundTripOf(trade.Buy, trade.Stop, row?.CostRisk, row?.CostFraction, member.Cost, closes.ContainsKey((trade.Ticker, trade.Bought)), member.Value),
                        }, one.Result);
                    }),
                ],
            }),
        ];
    }

    // Each account walked and drawn: its verdict off the newest per-rule test, its test years, its walk where the account
    // settings are set, and its record's label, in the table's order, each family's accounts by their verdict and then by
    // their return.
    public static IReadOnlyList<PaperAccountView> Views(
        IReadOnlyList<PaperAccountSource> accounts,
        IReadOnlyList<RuleTestRow> tests,
        IReadOnlyList<RuleTestYearRow> years,
        AccountSettings? settings,
        IReadOnlyList<DateOnly> sessions,
        DateOnly night,
        Func<string, DateOnly, decimal?> close,
        Func<DateOnly, decimal?> fund)
    {
        var views = accounts.Select(account =>
        {
            var test = account.Standing == PaperAccountView.You
                ? null
                : account.Tested is null
                    ? tests.FirstOrDefault(row => row.Family == account.Family && row.Standing != PaperAccountView.Variant)
                    : tests.FirstOrDefault(row => row.Family == account.Family && row.Rule == account.Tested);
            var verdict = account.Standing == PaperAccountView.You
                ? RuleVerdict.Yours
                : test is null ? RuleVerdict.NotTested : RuleVerdict.Of(test.Passed, test.Why, test.Adjusted, test.Gate, test.Stable, test.Trimmed, test.Counts);
            var walked = settings is { } held && account.Since is { } from
                ? PaperAccount.Walk(held, [.. account.Trades.Select(one => one.Trade)], [.. sessions.Where(session => session >= from && session <= night)], close, fund)
                : null;
            var ended = account.Trades.Count(one => one.Trade.Ended is not null);

            return new PaperAccountView(
                account.Key,
                account.Family,
                account.Standing,
                account.Name,
                account.Words,
                verdict,
                test?.Against,
                account.Book ? MarkRenderer.LoopPoints : "risks",
                test is null
                    ? []
                    : [.. years.Where(year => year.Family == test.Family && year.Rule == test.Rule).Select(year => new PaperYearView(year.Year, year.Complete, year.Units, year.Total, year.ComparedUnits, year.ComparedTotal))],
                walked,
                account.Trades.Count,
                ended,
                account.Standing == PaperAccountView.You ? null : RuleVerdict.TooFew(account.Book, ended, account.Since is { } since ? RuleVerdict.MonthsBetween(since, night) : 0),
                account.Since);
        });

        return
        [
            .. views
                .GroupBy(view => view.Family)
                .OrderBy(group => SetupFamilies.OrderOf(group.Key))
                .SelectMany(group => group
                    .OrderBy(view => RuleVerdict.Rank(view.Verdict))
                    .ThenByDescending(view => view.Walk?.Return ?? double.MinValue)),
        ];
    }

    // An account's trade log: each trade with its result in the account's unit, an open one's so far at tonight's close
    // by the ratio of its closes, and why the walk skipped it where it did.
    public static IReadOnlyList<PaperTradeView> Log(PaperAccountSource account, PaperWalk? walk, DateOnly night, Func<string, DateOnly, decimal?> close)
    {
        var skipped = walk?.Trades.Where(one => one.Skipped is not null).ToDictionary(one => (one.Trade.Ticker, one.Trade.Bought), one => one.Skipped) ?? [];

        return
        [
            .. account.Trades.Select(one =>
            {
                var trade = one.Trade;
                var open = trade.Ended is null;
                double? result = one.Result;

                if (open && close(trade.Ticker, night) is { } now && close(trade.Ticker, trade.Bought) is { } then && then > 0m)
                {
                    var ratio = now / then;

                    result = account.Book || trade.Stop is null
                        ? Statistic.FromRatio(ratio - 1m)
                        : trade.Buy > trade.Stop ? Statistic.FromRatio((ratio - 1m) * trade.Buy / (trade.Buy - trade.Stop.Value)) : null;
                }
                else if (open)
                {
                    result = null;
                }

                return new PaperTradeView(trade.Ticker, trade.Bought, trade.Buy, trade.Stop, trade.Ended, result, open, skipped.GetValueOrDefault((trade.Ticker, trade.Bought)));
            }),
        ];
    }
}
