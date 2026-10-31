using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Cards;

// One trade a paper account may take: its stock, the session it was bought on and its price, its stop or none for a book's
// holding, the session it ended on and its price or none while it is open, its round trip in dollars a share or none where
// no cost could be read, its place among the session's buys, for a holding with no stop the most holdings its book can
// hold, and from 19.10, for a trade a list or a book kept, the close its plan was set at, which it is sized from the night
// before, and the price the bar store holds now for its buy, which an open position is marked from. Paper trading's page
// reads besides its target, the distance its stop trails where it trails, the sessions it is given, which end of its plan
// its fill opened past, its edge over the same plan on every member or a holding's over its size cut as its fill stored
// it, how a page list's trade ended or why a holding was sold, as stored, and the exit of the menu it is walked under by
// its number, none for its rule's own, with its stop's distance in the night's typical moves, which a trail is counted in.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public sealed record PaperTrade(
    string Ticker,
    DateOnly Bought,
    decimal Buy,
    decimal? Stop,
    DateOnly? Ended,
    decimal? Sale,
    decimal? RoundTrip,
    int Place,
    int? BookHoldings = null,
    decimal? Planned = null,
    decimal? MarkedFrom = null,
    DateOnly? Listed = null,
    decimal? Target = null,
    decimal? Trail = null,
    int? Cap = null,
    string? Past = null,
    double? Edge = null,
    string? Outcome = null,
    int? Exit = null,
    double? RiskMoves = null,
    bool Halved = false)
{
    // The price the trade is sized from: the close its plan was set at where it was set the night before.
    public decimal SizedAt => Planned ?? Buy;

    // From 21.9 the share of the account's risk setting the trade is sized at: half on a night its rule's graded exposure
    // held one of its two switches, and the whole otherwise.
    // see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
    public decimal RiskShare => Halved ? 0.5m : 1m;

    // The night that listed it, which an account's start and its versions are read by.
    public DateOnly ListedOn => Listed ?? Bought;
}

// What became of one trade in an account: the shares it bought, none where it was skipped, and why it was skipped.
public sealed record PaperTaken(PaperTrade Trade, long Shares, string? Skipped);

// An account's walk: its value at the close of each session, each trade as the account took or skipped it, how many were
// taken, skipped and uncosted, its return and its worst drop from a high, the fund's return over the same sessions or the
// first session the fund's series holds where it starts later, the first session its open positions are marked from,
// the value before it realized alone, and each session's return on the account's size, which its chart draws.
public sealed record PaperWalk(
    IReadOnlyList<(DateOnly Session, decimal Value)> Values,
    IReadOnlyList<PaperTaken> Trades,
    int Taken,
    int Skipped,
    int Uncosted,
    double? Return,
    double? WorstDrop,
    double? FundReturn,
    DateOnly? FundFrom,
    DateOnly? MarkedFrom,
    IReadOnlyList<(DateOnly Session, double Return)> Returns)
{
    // The account's return less the fund's over the same sessions, none where either is not read.
    public double? Difference => Return is { } own && FundReturn is { } fund ? own - fund : null;
}

// A paper account walked when a page is read, from the stored trades and the account file: each trade sized off the
// account's value at its buy, its cash and its open positions at cost, by the card's own sizing; a book's holding an
// equal share across the most holdings its book can hold. On each session the trades ending are sold first, then the
// trades bought in their stored place order, a trade the free cash cannot buy skipped and counted. Each trade pays its
// round trip, half at its buy and half at its sale, one with none read uncosted and counted. Each session's value marks
// an open position by the ratio of its stock's closes, so a split moves nothing; before its stock's close is read a
// position stands at its cost and the walk says from when it marks. No store, log or export holds a figure of it.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public static partial class PaperAccount
{
    public const string UnderTheCash = "the free cash was under its cost";

    public const string NotSized = "the plan sizes no shares";

    // The price a stored trade ended at: a swing trade's buy and its result in multiples of its risk over the distance
    // to its stop, a book's holding its buy grown by its result, so a split since moves neither; none while it is open
    // or where its result was not read. From 19.10 a trade bought at its next session's open states its result in the
    // risk its plan's close put up, or as a fraction of that close for a holding, so the distance is read from that
    // close, `planned`, and the sale is the buy moved by it.
    public static decimal? SaleOf(decimal buy, decimal? stop, DateOnly? ended, double? result, bool book, decimal? planned = null) =>
        ended is null || result is not { } made ? null
        : book ? buy + ((planned ?? buy) * (Statistic.ToPrice(made * 100) / 100m))
        : stop is { } floor && (planned ?? buy) > floor ? buy + (Statistic.ToPrice(made) * ((planned ?? buy) - floor))
        : null;

    // A trade's round trip in dollars a share: the one its own table stores, in multiples of its risk or as a fraction of
    // its buy; else the member readings' cost on its buy session, in per cent of the buy; else the published table at the
    // company value read on its buy session, a company valued at none read in the table's $1 to 2 billion band; none
    // where the store holds no bar for its buy session.
    public static decimal? RoundTripOf(decimal buy, decimal? stop, double? storedRisks, double? storedFraction, double? readPercent, bool barHeld, decimal? value, decimal? planned = null) =>
        storedRisks is { } risks && stop is { } floor && (planned ?? buy) > floor ? Statistic.ToPrice(risks) * ((planned ?? buy) - floor)
        : storedFraction is { } fraction ? Statistic.ToPrice(fraction * 100) / 100m * buy
        : readPercent is { } percent ? Statistic.ToPrice(percent) / 100m * buy
        : barHeld ? EquityBrief.Core.Readings.TradeCost.RoundTrip(value, buy, buy)
        : null;

    // From 21.11 a walk may refuse a trade for a reason of its own, read off the trade and the positions open as it is
    // reached, and may size a trade off settings of its own at the account's value; the combined account does both.
    public static PaperWalk Walk(
        AccountSettings account,
        IReadOnlyList<PaperTrade> trades,
        IReadOnlyList<DateOnly> sessions,
        Func<string, DateOnly, decimal?> close,
        Func<DateOnly, decimal?>? fund = null,
        Func<PaperTrade, IReadOnlyList<PaperTrade>, string?>? refuse = null,
        Func<PaperTrade, decimal, AccountSettings>? sizing = null)
    {
        var cash = account.Size;
        var open = new List<(PaperTrade Trade, long Shares)>();
        var taken = new List<PaperTaken>();
        var values = new List<(DateOnly, decimal)>();
        DateOnly? markedFrom = null;

        static decimal Half(PaperTrade trade, long shares) => trade.RoundTrip is { } trip ? shares * trip / 2m : 0m;

        foreach (var session in sessions)
        {
            // The trades ending on the session are sold at their price before any is bought.
            foreach (var position in open.Where(position => position.Trade.Ended == session && position.Trade.Sale is not null).ToArray())
            {
                cash += (position.Shares * position.Trade.Sale!.Value) - Half(position.Trade, position.Shares);
                open.Remove(position);
            }

            foreach (var trade in trades.Where(trade => trade.Bought == session).OrderBy(trade => trade.Place).ThenBy(trade => trade.Ticker, StringComparer.Ordinal))
            {
                if (refuse?.Invoke(trade, [.. open.Select(position => position.Trade)]) is { } refused)
                {
                    taken.Add(new PaperTaken(trade, 0, refused));

                    continue;
                }

                var worth = cash + open.Sum(position => position.Shares * position.Trade.Buy);
                var sized = sizing?.Invoke(trade, worth) ?? account with { Size = worth };
                var plan = PositionSize.Of(sized with { RiskPercent = sized.RiskPercent * trade.RiskShare }, trade.SizedAt, trade.Stop, trade.BookHoldings, null);

                if (plan is not { Shares: > 0 })
                {
                    taken.Add(new PaperTaken(trade, 0, NotSized));

                    continue;
                }

                var cost = (plan.Shares * trade.Buy) + Half(trade, plan.Shares);

                if (cost > cash)
                {
                    taken.Add(new PaperTaken(trade, 0, UnderTheCash));

                    continue;
                }

                cash -= cost;
                taken.Add(new PaperTaken(trade, plan.Shares, null));

                // A trade bought and ended on one session is sold at once.
                if (trade.Ended == session && trade.Sale is { } sale)
                {
                    cash += (plan.Shares * sale) - Half(trade, plan.Shares);
                }
                else
                {
                    open.Add((trade, plan.Shares));
                }
            }

            var marked = true;
            var value = cash;

            foreach (var (trade, shares) in open)
            {
                if (close(trade.Ticker, session) is { } now && (trade.MarkedFrom ?? close(trade.Ticker, trade.Bought)) is { } then && then > 0m)
                {
                    value += shares * trade.Buy * (now / then);
                }
                else
                {
                    value += shares * trade.Buy;
                    marked = false;
                }
            }

            if (!marked)
            {
                markedFrom = null;
            }
            else
            {
                markedFrom ??= session;
            }

            values.Add((session, value));
        }

        var peak = account.Size;
        var worst = 0m;

        foreach (var (_, value) in values)
        {
            peak = Math.Max(peak, value);
            worst = Math.Max(worst, peak > 0m ? (peak - value) / peak : 0m);
        }

        var held = fund is null ? new PaperFund(null, null) : FundOver(sessions, fund);

        return new PaperWalk(
            values,
            taken,
            taken.Count(one => one.Skipped is null),
            taken.Count(one => one.Skipped is not null),
            taken.Count(one => one.Skipped is null && one.Trade.RoundTrip is null),
            values.Count > 0 ? Statistic.FromRatio((values[^1].Item2 / account.Size) - 1m) : null,
            values.Count > 0 ? Statistic.FromRatio(worst) : null,
            held.Return,
            held.From,
            markedFrom,
            [.. values.Select(one => (one.Item1, Statistic.FromRatio((one.Item2 / account.Size) - 1m)))]);
    }

    // From 21.11 the combined account: every live rule's trades on an index in one pot at the account's size, each family's
    // buys of a session in the families' page order and each its own list's places, sized by its own rule's sizing off the
    // account's value, a swing trade by the risk setting and a book's holding at an equal share of a fifth of the account
    // across the most holdings its book can hold, under the account's one most-in-one-stock limit. One position a stock: a
    // second family's pick of a stock held is skipped and counted as already held; at most three open positions in one GICS
    // sector, a fourth skipped and counted; and a buy the free cash cannot cover skipped and counted, as every account's is.
    // Walked by the one function every account is walked by, when the page is read.
    // see: The combined account holds every live rule's trades in one pot with one position a stock and three a sector, each book family a fifth of it
    public const int SectorCap = 3;

    public const decimal BookShare = 0.2m;

    public const string AlreadyHeld = "another setup already held the stock";

    public const string SectorFull = "three positions in its sector were open";

    public static CombinedWalk Combined(
        AccountSettings account,
        IReadOnlyList<CombinedTrade> trades,
        IReadOnlyList<DateOnly> sessions,
        Func<string, DateOnly, decimal?> close,
        Func<DateOnly, decimal?>? fund = null)
    {
        // Each trade placed among its session's buys by its family's place in the page order and then its own.
        var ordered = trades
            .OrderBy(one => one.Trade.Bought)
            .ThenBy(one => one.FamilyPlace)
            .ThenBy(one => one.Trade.Place)
            .ThenBy(one => one.Trade.Ticker, StringComparer.Ordinal)
            .Select((one, at) => (Combined: one, Placed: one.Trade with { Place = at }))
            .ToArray();
        var of = ordered.ToDictionary(one => one.Placed, one => one.Combined);

        string? Refuse(PaperTrade trade, IReadOnlyList<PaperTrade> open) =>
            open.Any(held => held.Ticker == trade.Ticker) ? AlreadyHeld
            : of[trade].Sector is { } sector && open.Count(held => of[held].Sector == sector) >= SectorCap ? SectorFull
            : null;

        AccountSettings Sizing(PaperTrade trade, decimal worth) =>
            of[trade].Book ? account with { Size = worth * BookShare, PositionCap = account.PositionCap / BookShare } : account with { Size = worth };

        var walk = Walk(account, [.. ordered.Select(one => one.Placed)], sessions, close, fund, Refuse, Sizing);

        return new CombinedWalk(
            walk,
            [.. walk.Trades.Select(one => (one, of[one.Trade]))],
            walk.Trades.Count(one => one.Skipped == AlreadyHeld),
            walk.Trades.Count(one => one.Skipped == SectorFull),
            walk.Trades.Count(one => one.Skipped == UnderTheCash));
    }

    // A fund's return over sessions, its close on the last over its close on the first; where its series holds no close on
    // the first, no return and the first session it holds one.
    public static PaperFund FundOver(IReadOnlyList<DateOnly> sessions, Func<DateOnly, decimal?> fund)
    {
        if (sessions.Count == 0)
        {
            return new PaperFund(null, null);
        }

        if (fund(sessions[0]) is { } first && first > 0m && fund(sessions[^1]) is { } last)
        {
            return new PaperFund(Statistic.FromRatio((last / first) - 1m), null);
        }

        return new PaperFund(null, sessions.FirstOrDefault(session => fund(session) is not null) is var held && held != default ? held : null);
    }
}

// A fund's return over an account's sessions, or where its series starts later the first session it holds a close.
public sealed record PaperFund(double? Return, DateOnly? From);

// From 21.11 one trade the combined account may take: the trade, the family whose live rule's list kept it and that
// family's place in the page order, its stock's GICS sector, none where none is read, and whether it is a book's holding.
public sealed record CombinedTrade(PaperTrade Trade, string Family, int FamilyPlace, string? Sector, bool Book);

// The combined account's walk: the walk itself, each trade as it took or skipped it with the trade it came from, and how
// many it skipped as already held, for a sector at its cap and for the free cash.
public sealed record CombinedWalk(PaperWalk Walk, IReadOnlyList<(PaperTaken Taken, CombinedTrade From)> Trades, int AlreadyHeld, int SectorFull, int UnderCash);
