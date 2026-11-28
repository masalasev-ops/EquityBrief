using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// A monthly setup's holdings as what the system bought draws them: how its rule sells a holding, in words with its next
// reshuffle where one falls, and each open holding's state tonight by its own rule.
// see: A holding with no price stop draws how its rule sells it and whether it still qualifies tonight by its own rule
public sealed record PaperBookLine(string Sells, IReadOnlyDictionary<string, string> Tonight);

// An index's paper accounts as a page reads them: each before its walk, each drawn, the closes they were marked by, the
// sessions they were walked over, each stock's company and readings on the night, the index's fund's closes, and each
// open holding's standing tonight.
public sealed record PaperAccounts(
    IReadOnlyList<PaperAccountSource> Sources,
    IReadOnlyList<PaperAccountView> Views,
    Func<string, DateOnly, decimal?> Close,
    IReadOnlyList<DateOnly>? Sessions = null,
    IReadOnlyDictionary<string, PaperStockRow>? Stocks = null,
    Func<DateOnly, decimal?>? Fund = null,
    IReadOnlyDictionary<string, string>? Standings = null,
    PaperCombinedView? Combined = null,
    TradeGroupsView? Groups = null)
{
    // Each monthly setup's line by its family.
    public IReadOnlyDictionary<string, PaperBookLine> Books { get; init; } = new Dictionary<string, PaperBookLine>(StringComparer.Ordinal);

    // The live rules' trades under a plan through the night, and the orders from earlier nights still waiting for their
    // price, which Home, Tonight and Past picks draw as a group of their own.
    public IReadOnlyList<PlannedTradeRow> Planned { get; init; } = [];

    public IReadOnlyList<WaitingOrderView> StillWaiting { get; init; } = [];
}

// Paper trading's page over the accounts the core works: the keys its link carries, the variants registered after the
// newest night, the page's lists of what the rules bought, the averaged account and each account's trade log. It draws
// what the core works and works no account itself.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public static class PaperScreen
{
    // The keys the page's link carries: the account a trade log draws and the family whose variants the chart adds.
    public const string AccountQuery = "account";

    public const string FamilyQuery = PaperLink.FamilyKey;

    // The operator's own account's key.
    public const string YouKey = PaperBooks.YouKey;

    // Each variant registered after the register was read for the night, which no night has read yet, in the families' page
    // order: numbered and named by what it changes as its card will number it once the register is read past its
    // registration, with the day it was registered and its first night. A night reads the rows registered before it starts,
    // so a variant registered on a trading day before that day's night starts is first read that evening, and one registered
    // later by the next trading day's night; with no usual start to read it is placed on the next trading day.
    // see: A variant registered after the newest night is shown on Paper trading at once with the night that first reads it
    public static IReadOnlyList<PaperComingView> Coming(
        bool large,
        string indexName,
        DateOnly night,
        IReadOnlyList<CandidateRow> register,
        IndexRuleSettings? provisional,
        TimeOnly? nightStarts,
        DateTimeOffset? asOf = null)
    {
        var read = asOf ?? RuleRegister.PastTheNight(night);
        var families = SetupFamilies.PageOrder.Where(family => family != FundamentalsRule.Name).Append(QualityRule.Name).Append(ImprovingRule.Name);
        var coming = new List<PaperComingView>();

        foreach (var family in families)
        {
            var settings = large || family == DipRule.Name ? null : provisional;
            var standing = RuleRegister.Accounts(family, large, indexName, register, night, settings, read).Variants.Select(one => one.Row.Candidate).ToHashSet(StringComparer.Ordinal);
            var (_, variants) = RuleRegister.Accounts(family, large, indexName, register, night, settings, DateTimeOffset.MaxValue);

            foreach (var (row, tab) in variants.Where(one => !standing.Contains(one.Row.Candidate) && one.Row.RegisteredAt >= read))
            {
                coming.Add(new PaperComingView(family, PaperBooks.TabShort(tab), PaperBooks.TabChanges(tab), row.Candidate, DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime), PaperBooks.FirstNight(row.RegisteredAt, nightStarts)));
            }
        }

        return coming;
    }

    // Paper trading's page: the trades the live and provisional rules bought, those of the family the chips show or every
    // family, open and closed and newest first, each open one's figures tonight with the variants of its family holding it
    // too and each closed one's ending; the trades of earlier versions, folded; the stocks they did not buy; the trades only
    // the chosen family's variants took; and the index's fund over the days the tiles count, from the earliest start among
    // them. A holding's standing tonight is handed in, worked by the heavyweights rule's own functions.
    public static PaperPageView Page(
        IReadOnlyList<PaperAccountSource> sources,
        IReadOnlyList<PaperAccountView> views,
        DateOnly night,
        IReadOnlyList<DateOnly> sessions,
        Func<string, DateOnly, decimal?> close,
        IReadOnlyDictionary<string, PaperStockRow> stocks,
        Func<string, string?> standing,
        string? show,
        string variants,
        string fundName,
        Func<DateOnly, decimal?> fund,
        PaperCombinedView? combined = null,
        IReadOnlyDictionary<string, PaperBookLine>? books = null,
        IReadOnlyList<TrailStopRow>? raised = null)
    {
        var heads = views.Where(view => view.Head && (show is null || view.Family == show)).ToArray();
        var source = sources.ToDictionary(account => account.Key, StringComparer.Ordinal);

        double? Move(string ticker) => stocks.TryGetValue(ticker, out var held) ? held.TypicalMove : null;

        string? Company(string ticker) => stocks.TryGetValue(ticker, out var held) ? held.Company : null;

        // A holding's state tonight by its own setup's rule: the heavyweights' standing handed in, and Quality at a fair
        // price's and the Fundamentals family's off their book's line.
        // see: A holding with no price stop draws how its rule sells it and whether it still qualifies tonight by its own rule
        string? Tonight(string family, string ticker) =>
            books?.GetValueOrDefault(family)?.Tonight.GetValueOrDefault(ticker) ?? (family == HeavyweightRule.Name ? standing(ticker) : null);

        PaperPosition? Opened(PaperTrade trade, string family = HeavyweightRule.Name) =>
            trade.Ended is null || trade.Ended > night ? PaperAccount.Position(trade, night, sessions, close, Move(trade.Ticker), trade.Stop is null ? Tonight(family, trade.Ticker) : null) : null;

        // From the operator's answers of 2026-11-28, item 4, a trailing trade's sale read against the stop the night before it
        // fired stored for it.
        PaperEnded? Shut(PaperTrade trade, double? result, string family) =>
            trade.Ended is { } end && end <= night
                ? PaperAccount.Ended(trade, result, sessions, close, TrailStops.Before(raised, family, trade.Ticker, trade.ListedOn, trade.FiredOn ?? end))
                : null;

        IReadOnlyList<(string Name, IReadOnlyList<PaperTrade> Trades)> VariantsOf(string family) =>
        [
            .. views
                .Where(view => view.Family == family && view.Standing == PaperAccountView.Variant)
                .Select(view => (view.Short ?? view.Name, (IReadOnlyList<PaperTrade>)[.. source[view.Key].Trades.Select(one => one.Trade)])),
        ];

        PaperBoughtView Drawn(PaperAccountView head, PaperTrade trade, double? result, string? version) =>
            new(
                head.Key,
                head.Family,
                trade.Ticker,
                Company(trade.Ticker),
                trade.ListedOn,
                trade.Bought,
                trade.Buy,
                trade.Stop,
                trade.Target,
                trade.Trail is not null,
                trade.Cap,
                trade.Ended,
                Opened(trade, head.Family),
                Shut(trade, result, head.Family),
                version is null && (trade.Ended is null || trade.Ended > night) ? PaperAccount.HeldBy(trade.Ticker, night, VariantsOf(head.Family)) : [],
                trade.Past,
                version,
                PaperAccount.Selling(trade, night, close))
            {
                Sells = trade.Stop is null ? books?.GetValueOrDefault(head.Family)?.Sells : null,
            };

        var bought = heads
            .SelectMany(head => (head.Walk?.Trades ?? [])
                .Where(one => one.Skipped is null)
                .Select(one => Drawn(head, one.Trade, source[head.Key].Trades.FirstOrDefault(own => own.Trade == one.Trade).Result, null)))
            .OrderByDescending(trade => trade.Bought)
            .ThenBy(trade => SetupFamilies.OrderOf(trade.Family))
            .ThenBy(trade => trade.Ticker, StringComparer.Ordinal)
            .ToArray();
        PaperBoughtView[] earlier =
        [
            .. heads
                .SelectMany(head => (source[head.Key].Earlier ?? []).Select(one => Drawn(head, one.Trade, one.Result, one.Version)))
                .OrderByDescending(trade => trade.Bought)
                .ThenBy(trade => trade.Ticker, StringComparer.Ordinal),
        ];
        // Each account's same trades before its version, drawn apart under the account and in none of its figures.
        // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
        PaperSameView[] same =
        [
            .. heads
                .Where(head => source[head.Key] is { SameSince: not null, Same.Count: > 0 })
                .Select(head => new PaperSameView(
                    head.Family,
                    source[head.Key].SameSince!.Value,
                    [.. source[head.Key].Same.Select(one => Drawn(head, one.Trade, one.Result, null)).OrderByDescending(trade => trade.Bought).ThenBy(trade => trade.Ticker, StringComparer.Ordinal)],
                    source[head.Key].CarriedBySwap)),
        ];
        var chosen = views.FirstOrDefault(view => view.Head && view.Family == variants);
        var headTrades = chosen is null ? [] : source[chosen.Key].Trades.Select(one => one.Trade).Concat((source[chosen.Key].Earlier ?? []).Select(one => one.Trade)).Concat(source[chosen.Key].Same.Select(one => one.Trade)).ToArray();
        PaperOnlyView[] only =
        [
            .. views
                .Where(view => view.Family == variants && view.Standing == PaperAccountView.Variant)
                .SelectMany(view => source[view.Key].Trades
                    .Where(one => !headTrades.Any(trade => trade.Ticker == one.Trade.Ticker && trade.ListedOn == one.Trade.ListedOn))
                    .Select(one => new PaperOnlyView(one.Trade.Ticker, Company(one.Trade.Ticker), [view.Short ?? view.Name], one.Trade.ListedOn, one.Trade.Bought, one.Trade.Buy, one.Trade.Stop is null, Opened(one.Trade), Shut(one.Trade, one.Result, view.Family))))
                .OrderByDescending(trade => trade.Bought)
                .ThenBy(trade => trade.Ticker, StringComparer.Ordinal),
        ];
        var from = views.Where(view => view.Head && view.Since is not null).Select(view => view.Since).Min();
        var counted = from is { } start ? PaperAccount.FundOver([.. sessions.Where(session => session >= start && session <= night)], fund) : null;

        // Every rule's and every variant's own trades as its tabs draw them, those its walk bought, and each running rule's
        // earlier revisions' trades, each named by the revision that listed it.
        // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
        var traded = views
            .Where(view => view.Standing != PaperAccountView.You)
            .ToDictionary(
                view => view.Key,
                view => (IReadOnlyList<PaperBoughtView>)
                [
                    .. (view.Walk is { } walk
                            ? walk.Trades.Where(one => one.Skipped is null).Select(one => (one.Trade, Result: source[view.Key].Trades.FirstOrDefault(own => own.Trade == one.Trade).Result))
                            : source[view.Key].Trades.Select(one => (one.Trade, one.Result)))
                        .Select(one => Drawn(view, one.Trade, one.Result, null)),
                ],
                StringComparer.Ordinal);
        var earlierTraded = heads.ToDictionary(
            head => head.Key,
            head => (IReadOnlyList<PaperBoughtView>)
            [
                .. (source[head.Key].Earlier ?? []).Select(one => Drawn(head, one.Trade, one.Result, one.Version)),
                .. source[head.Key].Same.Select(one => Drawn(head, one.Trade, one.Result, PaperBooks.VersionAt(source[head.Key].Revisions, one.Trade.ListedOn))),
            ],
            StringComparer.Ordinal);

        return new PaperPageView(
            views,
            [.. bought.Where(trade => trade.Open is not null)],
            [.. bought.Where(trade => trade.Closed is not null)],
            earlier,
            PaperAccount.NotBought([.. heads.Select(head => (head.Walk, source[head.Key].Unfilled ?? []))]),
            only,
            new PaperFundView(fundName, counted?.Return, from, counted?.From),
            combined)
        {
            Same = same,
            Traded = traded,
            EarlierTraded = earlierTraded,
            Skipped =
            [
                .. heads.SelectMany(head => (head.Walk?.Trades ?? [])
                    .Where(one => one.Skipped is not null && one.Trade.Bought <= night)
                    .Select(one => (one.Skipped!, one.Trade.Ended is null || one.Trade.Ended > night))),
            ],
            FundCloses =[.. sessions.Where(session => session <= night).Select(session => (Session: session, Close: fund(session))).Where(one => one.Close is not null).Select(one => (one.Session, one.Close!.Value))],
            Unbought =
            [
                .. heads.OrderBy(head => SetupFamilies.OrderOf(head.Family)).Select(head => PaperAccount.UnboughtBy(
                    head.Family,
                    head.Walk,
                    source[head.Key].Unfilled ?? [],
                    night,
                    heads
                        .Where(other => other.Key != head.Key)
                        .SelectMany(other => PaperAccount.HeldOpen(other.Walk, night).Select(ticker => (Ticker: ticker, other.Family)))
                        .GroupBy(one => one.Ticker, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)[.. group.Select(one => one.Family).Distinct(StringComparer.Ordinal)], StringComparer.Ordinal))),
            ],
        };
    }

    // The live setups' accounts, averaged, on an index: each family's live or provisional account walked, given an equal
    // share of one account of the settings' size, over the sessions from the day every one of them counts, the latest of
    // their starts; none before the settings are set or where no such account is walked. Each trade as its own setup's
    // account bought it, with its return on its position, its sale over its buy, or tonight's close over its buy session's
    // while it is open.
    // see: The averaged account gives each live rule's own account an equal share, from the day every one of them counts
    public static PaperCombinedView? Averaged(
        IReadOnlyList<PaperAccountView> views,
        AccountSettings? settings,
        IReadOnlyList<DateOnly> sessions,
        DateOnly night,
        Func<string, DateOnly, decimal?> close,
        Func<DateOnly, decimal?> fund)
    {
        var heads = views.Where(view => view.Head && view.Since is not null && view.Walk is not null).OrderBy(view => SetupFamilies.OrderOf(view.Family)).ToArray();

        if (settings is not { } held || heads.Length == 0)
        {
            return null;
        }

        var since = heads.Max(view => view.Since!.Value);
        var walked = PaperAccount.Averaged(held.Size, [.. heads.Select(view => (view.Family, view.Walk!))], [.. sessions.Where(session => session >= since && session <= night)], fund);
        var (open, closed) = PaperAccount.Held(walked.Walk, night);

        double? ReturnOf(PaperTrade trade) =>
            trade.Ended is { } ended && ended <= night
                ? trade.Sale is { } sale && trade.Buy > 0m ? Statistic.FromRatio((sale / trade.Buy) - 1m) : null
                : close(trade.Ticker, night) is { } now && (trade.MarkedFrom ?? close(trade.Ticker, trade.Bought)) is { } then && then > 0m ? Statistic.FromRatio((now / then) - 1m) : null;

        return new PaperCombinedView(
            walked.Walk,
            since,
            open,
            closed,
            walked.Accounts,
            [
                .. walked.Trades.Select(one => new PaperCombinedTradeView(
                    one.Family,
                    one.Taken.Trade.Ticker,
                    one.Taken.Trade.Bought,
                    one.Taken.Trade.Buy,
                    one.Taken.Shares,
                    one.Taken.Skipped,
                    one.Taken.Trade.Ended,
                    one.Taken.Skipped is null ? ReturnOf(one.Taken.Trade) : null,
                    one.Taken.Trade.Ended is null || one.Taken.Trade.Ended > night,
                    one.Taken.Trade.Stop is null ? one.Taken.Trade.BookHoldings : null)),
            ])
        {
            Size = held.Size,
        };
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
    [
        .. (account.Earlier ?? []).Select(one => Logged(account.Book, one.Trade, one.Result, night, close, null, one.Version)),
        .. account.SameSince is { } since ? account.Same.Select(one => Logged(account.Book, one.Trade, one.Result, night, close, null, MarkRenderer.SameWords(since, account.CarriedBySwap))) : [],
    ];

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

        return new PaperTradeView(trade.Ticker, trade.Bought, trade.Buy, trade.Stop, trade.Ended, result, open, skipped, version, trade.Halved);
    }
}
