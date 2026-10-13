using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Cards;

// One trade a paper account may take: its stock, the session it was bought on at that close and its price, its stop or
// none for a book's holding, the session it ended on and its price or none while it is open, its round trip in dollars a
// share or none where no cost could be read, its place among the session's buys, and for a holding with no stop the most
// holdings its book can hold.
public sealed record PaperTrade(
    string Ticker,
    DateOnly Bought,
    decimal Buy,
    decimal? Stop,
    DateOnly? Ended,
    decimal? Sale,
    decimal? RoundTrip,
    int Place,
    int? BookHoldings = null);

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
// see: A paper account draws its test years verdict and its record since registration apart, and never merges them
public static class PaperAccount
{
    public const string UnderTheCash = "the free cash was under its cost";

    public const string NotSized = "the plan sizes no shares";

    // The price a stored trade ended at: a swing trade's buy and its result in multiples of its risk over the distance
    // to its stop, a book's holding its buy grown by its result, so a split since moves neither; none while it is open
    // or where its result was not read.
    public static decimal? SaleOf(decimal buy, decimal? stop, DateOnly? ended, double? result, bool book) =>
        ended is null || result is not { } made ? null
        : book ? buy * (1m + (Statistic.ToPrice(made * 100) / 100m))
        : stop is { } floor && buy > floor ? buy + (Statistic.ToPrice(made) * (buy - floor))
        : null;

    // A trade's round trip in dollars a share: the one its own table stores, in multiples of its risk or as a fraction of
    // its buy; else the member readings' cost on its buy session, in per cent of the buy; else the published table at the
    // company value read on its buy session, a company valued at none read in the table's $1 to 2 billion band; none
    // where the store holds no bar for its buy session.
    public static decimal? RoundTripOf(decimal buy, decimal? stop, double? storedRisks, double? storedFraction, double? readPercent, bool barHeld, decimal? value) =>
        storedRisks is { } risks && stop is { } floor && buy > floor ? Statistic.ToPrice(risks) * (buy - floor)
        : storedFraction is { } fraction ? Statistic.ToPrice(fraction * 100) / 100m * buy
        : readPercent is { } percent ? Statistic.ToPrice(percent) / 100m * buy
        : barHeld ? EquityBrief.Core.Readings.TradeCost.RoundTrip(value, buy, buy)
        : null;

    public static PaperWalk Walk(
        AccountSettings account,
        IReadOnlyList<PaperTrade> trades,
        IReadOnlyList<DateOnly> sessions,
        Func<string, DateOnly, decimal?> close,
        Func<DateOnly, decimal?>? fund = null)
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
                var worth = cash + open.Sum(position => position.Shares * position.Trade.Buy);
                var plan = PositionSize.Of(account with { Size = worth }, trade.Buy, trade.Stop, trade.BookHoldings, null);

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
                if (close(trade.Ticker, session) is { } now && close(trade.Ticker, trade.Bought) is { } then && then > 0m)
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

        double? fundReturn = null;
        DateOnly? fundFrom = null;

        if (fund is not null && sessions.Count > 0)
        {
            if (fund(sessions[0]) is { } first && first > 0m && fund(sessions[^1]) is { } last)
            {
                fundReturn = Statistic.FromRatio((last / first) - 1m);
            }
            else
            {
                fundFrom = sessions.FirstOrDefault(session => fund(session) is not null) is var held && held != default ? held : null;
            }
        }

        return new PaperWalk(
            values,
            taken,
            taken.Count(one => one.Skipped is null),
            taken.Count(one => one.Skipped is not null),
            taken.Count(one => one.Skipped is null && one.Trade.RoundTrip is null),
            values.Count > 0 ? Statistic.FromRatio((values[^1].Item2 / account.Size) - 1m) : null,
            values.Count > 0 ? Statistic.FromRatio(worst) : null,
            fundReturn,
            fundFrom,
            markedFrom,
            [.. values.Select(one => (one.Item1, Statistic.FromRatio((one.Item2 / account.Size) - 1m)))]);
    }
}
