using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// One paper account before it is walked: its key, family, standing, name and words, the register's name its test row
// carries, none for the live or provisional rule, whose row is read by its standing, whether it is a book, the trades it
// counts with each one's stored result, the day it starts, the name its stored trades are kept under, none where they
// are the S&P 500's page list or the operator's, the words naming the version it counts, and the trades it leaves out
// as listed before that version, each with the words naming the version that listed it.
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
    string? Listed = null,
    string? Version = null,
    IReadOnlyList<(PaperTrade Trade, double? Result, string Version)>? Earlier = null)
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

    // The words a provisional account and the operator's own name what they count, having no registered version.
    public const string ProvisionalVersion = "provisional settings, no registered version: every trade since its first";

    public const string YourVersion = "your own trades: every one since your first";

    // The words an earlier trade listed before its account's rule first registered is named by.
    public const string BeforeRegistration = "before its first registration";

    // A registered version as the page names it: the day it registered and its code version.
    public static string VersionWords(CandidateRow row) =>
        "the version registered " + Stamp(DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime)) + (row.EvaluatorVersion.Length > 0 ? ", code " + row.EvaluatorVersion : string.Empty);

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // The words an account names the version it counts by, saying where its start was carried back before the registration
    // by a replay that reproduced every trade.
    public static string CountedWords(CandidateRow standing, DateOnly start) =>
        VersionWords(standing) + (start < FamilyRecords.FirstSession(standing.RegisteredAt) ? ", carried back by a replay that reproduced every trade" : string.Empty);

    // Where a registered rule's account starts: where the Run page's record of the rule counts from, its standing
    // registration's first session, or the session a replay under its version found it counting from where that replay
    // reproduced every trade. The S&P 400's and 600's records read no replay.
    // see: A paper account counts only the trades of its rule's current version, from where the Run page's record of that rule starts
    public static DateOnly StartOf(CandidateRow standing, IReadOnlyList<FamilyReplayRow> replays) =>
        FamilyRecords.StartOf(
            new RegisterRow(standing.Id, standing.Candidate, string.Empty, string.Empty, standing.Evaluator, standing.Parameters, standing.EvaluatorVersion, standing.Event, standing.Retires, standing.RegisteredAt, standing.Evidence),
            replays).First;

    // The version that listed a trade: its rule's newest registration whose first session is on or before the trade's buy.
    public static string VersionAt(IReadOnlyList<CandidateRow> lineage, DateOnly bought) =>
        lineage.Where(row => FamilyRecords.FirstSession(row.RegisteredAt) <= bought).MaxBy(row => (row.RegisteredAt, row.Id)) is { } listed
            ? VersionWords(listed)
            : BeforeRegistration;

    // A registered rule's account's trades split at its start: those it counts, bought on or after it, and those it leaves
    // out, each named by the version that listed it.
    public static PaperAccountSource Started(PaperAccountSource account, IReadOnlyList<(PaperTrade Trade, double? Result)> trades, CandidateRow standing, IReadOnlyList<FamilyReplayRow> replays, IReadOnlyList<CandidateRow> lineage)
    {
        var start = StartOf(standing, replays);

        return Split(account, trades, start, CountedWords(standing, start), lineage);
    }

    static PaperAccountSource Split(PaperAccountSource account, IReadOnlyList<(PaperTrade Trade, double? Result)> trades, DateOnly start, string version, IReadOnlyList<CandidateRow> lineage) =>
        account with
        {
            Trades = [.. trades.Where(one => one.Trade.ListedOn >= start)],
            Since = start,
            Version = version,
            Earlier = [.. trades.Where(one => one.Trade.ListedOn < start).Select(one => (one.Trade, one.Result, VersionAt(lineage, one.Trade.ListedOn)))],
        };

    // Every account on an index in the one family order, each family's live or provisional rule first and its variants
    // after it as its card numbers them, then the operator's own. A registered rule's account counts the trades of its
    // current version alone, from where the Run page's record of the rule starts; a provisional account and the
    // operator's own count every trade from their first.
    // see: A paper account counts only the trades of its rule's current version, from where the Run page's record of that rule starts
    public static IReadOnlyList<PaperAccountSource> Accounts(
        bool large,
        string indexName,
        DateOnly night,
        IReadOnlyList<CandidateRow> register,
        IndexRuleSettings? provisional,
        IReadOnlyList<PaperRow> rows,
        IReadOnlyList<PickCell> pageCells,
        IReadOnlyList<(PaperTrade Trade, double? Result, string Family)> yours,
        IReadOnlyList<FamilyReplayRow>? replays = null)
    {
        var accounts = new List<PaperAccountSource>();
        var replayed = large ? replays ?? [] : [];

        // A trade is bought at its fill on the session after its night, sized from the close its plan was set at; one
        // still waiting on that session, or holding none, is bought by no account.
        // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
        static IReadOnlyList<(PaperTrade, double?)> Of(IEnumerable<PaperRow> rows) =>
        [
            .. rows.Where(row => row.Fill is not null && row.FilledOn is not null).Select(row => (new PaperTrade(
                row.Ticker,
                row.FilledOn!.Value,
                row.Fill!.Value,
                row.Stop,
                row.Ended,
                PaperAccount.SaleOf(row.Fill!.Value, row.Stop, row.Ended, row.Result, row.Book, row.Entry),
                null,
                row.Place,
                row.Book ? BookHoldings : null,
                row.Entry,
                row.MarkedFrom,
                row.Bought), row.Result)),
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
            var registrations = RuleScreen.Registrations(family, large, indexName, register);

            // A provisional account starts at the first night its list kept a trade, filled at the next open or not, so
            // pricing entries at the open moves no account's start.
            var first = pageList
                ? pageCells.Where(cell => cell.Family == family && cell.RepeatOf is null && cell.Status != PickStatus.Missing && cell.Buy is not null).Select(cell => (DateOnly?)cell.Night).Min()
                : rows.Where(head.Keeps).Select(row => (DateOnly?)row.Bought).Min();

            accounts.Add(live is not null
                ? Started(head, trades, live, replayed, [.. registrations.Where(row => FamilyRecords.IsLive(row.Candidate))])
                : head with { Trades = trades, Since = first, Version = ProvisionalVersion, Earlier = [] });

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
                    null,
                    row.Candidate);

                accounts.Add(Started(variant, Of(rows.Where(variant.Keeps)), row, replayed, [.. registrations.Where(one => one.Candidate == row.Candidate)]));
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
            yours.Count > 0 ? yours.Min(one => one.Trade.Bought) : null,
            Version: YourVersion,
            Earlier: []));

        return accounts;
    }

    // The S&P 500's page list of one swing family as its trades: every trade but a repeat listing, one with no outcome
    // stored and one not filled at its next session's open, each bought at its fill and sold where its result puts it, in
    // the order its night listed them.
    static IReadOnlyList<(PaperTrade, double?)> PageTrades(IEnumerable<PickCell> cells) =>
    [
        .. cells
            .Where(cell => cell.RepeatOf is null && cell.Status != PickStatus.Missing && cell.Buy is not null && cell.FilledOn is not null)
            .GroupBy(cell => cell.Night)
            .SelectMany(night => night.Select((cell, place) => (new PaperTrade(
                cell.Ticker,
                cell.FilledOn!.Value,
                cell.Buy!.Value,
                cell.Stop,
                cell.EndedOn,
                PaperAccount.SaleOf(cell.Buy!.Value, cell.Stop, cell.EndedOn, cell.Result, false, cell.Planned)
                    ?? (cell.EndedOn is not null && cell.ReturnPct is { } made ? cell.Buy!.Value * (1m + (Statistic.ToPrice(made) / 100m)) : null),
                null,
                place + 1,
                null,
                cell.Planned,
                cell.Buy,
                cell.Night), cell.Result))),
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
                        var row = rows.FirstOrDefault(held => held.Ticker == trade.Ticker && held.Bought == trade.ListedOn && account.Keeps(held));
                        var member = members.GetValueOrDefault((trade.Ticker, trade.Bought));

                        return (trade with
                        {
                            RoundTrip = PaperAccount.RoundTripOf(trade.Buy, trade.Stop, row?.CostRisk, row?.CostFraction, member.Cost, closes.ContainsKey((trade.Ticker, trade.Bought)), member.Value, trade.Planned),
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
            var months = account.Since is { } first ? RuleVerdict.MonthsBetween(first, night) : 0;

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
                account.Standing == PaperAccountView.You ? null : RuleVerdict.TooFew(account.Book, ended, months),
                account.Since,
                test?.Adjusted,
                RuleVerdict.CountLine(account.Book, account.Trades.Count, ended, months, account.Standing == PaperAccountView.You),
                account.Version,
                account.Earlier?.Count ?? 0);
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

        return [.. account.Trades.Select(one => Logged(account.Book, one.Trade, one.Result, night, close, skipped.GetValueOrDefault((one.Trade.Ticker, one.Trade.Bought)), null))];
    }

    // The trades an account leaves out as listed before the version it counts, each with the version that listed it and an
    // open one's result so far as the log reads it; walked by nothing.
    public static IReadOnlyList<PaperTradeView> EarlierLog(PaperAccountSource account, DateOnly night, Func<string, DateOnly, decimal?> close) =>
        [.. (account.Earlier ?? []).Select(one => Logged(account.Book, one.Trade, one.Result, night, close, null, one.Version))];

    static PaperTradeView Logged(bool book, PaperTrade trade, double? stored, DateOnly night, Func<string, DateOnly, decimal?> close, string? skipped, string? version)
    {
        var open = trade.Ended is null;
        var result = stored;

        if (open && close(trade.Ticker, night) is { } now && close(trade.Ticker, trade.Bought) is { } then && then > 0m)
        {
            var ratio = now / then;

            result = book || trade.Stop is null
                ? Statistic.FromRatio(ratio - 1m)
                : trade.Buy > trade.Stop ? Statistic.FromRatio((ratio - 1m) * trade.Buy / (trade.Buy - trade.Stop.Value)) : null;
        }
        else if (open)
        {
            result = null;
        }

        return new PaperTradeView(trade.Ticker, trade.Bought, trade.Buy, trade.Stop, trade.Ended, result, open, skipped, version);
    }
}
