using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Cards;

// An open trade on the night: tonight's close as the store holds it, the trade's price at its own scale, its stop tonight,
// its result so far in multiples of the risk its plan's close put up and as a fraction of its buy, the sessions since the
// night that listed it, and its standing in words.
public sealed record PaperPosition(decimal? Close, decimal? Now, decimal? Stop, double? Risks, double? Fraction, int? Held, string Standing);

// A trade that ended: the price it was sold at, its result in risks and as a fraction of its buy, how it ended in words,
// the sessions from its listing night to its end, and its edge as its fill stored it.
public sealed record PaperEnded(decimal? Sale, double? Risks, double? Fraction, string Ending, int? Held, double? Edge);

// One reason listed stocks were not bought, with each stock and the night that listed it, newest first.
public sealed record PaperUnbought(string Why, IReadOnlyList<(string Ticker, DateOnly Listed)> Stocks);

// The listed stocks an account did not buy, a reason at a time, the most held first.
public sealed record PaperNotBought(IReadOnlyList<PaperUnbought> Reasons)
{
    public int Count => Reasons.Sum(reason => reason.Stocks.Count);
}

// A rule's trades against others': how many it took, those another took in the same stock off the same night, and those
// it alone took.
public sealed record PaperOverlap(int Trades, int Shared, int Only);

// A test year as the scorecard draws it: better or worse than what it was read against, and the edge each of its units
// made over that in the rule's unit, none where it holds no unit.
public sealed record PaperYear(int Year, bool Complete, string Word, double? Edge, int Units);

// Where a rule's adjusted p stands: its zone, how often a rule picking at random would do as well, and its place on the
// scale from nought at the left to one at the right.
public sealed record PaperLuck(string Zone, string Words, double Place);

// A record's progress toward the count a verdict reads: its decided trades or a book's months, the count needed, the share
// of it held, and the mean edge of its decided trades with how many held one.
public sealed record PaperProgress(int Counted, int Needed, double Share, double? MeanEdge, int Edged);

// The figures paper trading draws beside each account's walk, worked here as the walk is, so the read surface and the page
// work none of them out: an open trade's standing tonight, a closed trade's ending, the stocks not bought, a rule's trades
// against its variants', each test year, the chance a rule picking at random would do as well, and a record's progress.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public static partial class PaperAccount
{
    public const string NearTarget = "near its target";

    public const string NearStop = "near its stop";

    public const string OnTrack = "on track";

    public const string OpenedUnderStop = "opened under its stop";

    public const string OpenedOverTarget = "opened over its target";

    public const string ReachedTarget = "reached its target";

    public const string UnderStop = "closed under its stop";

    public const string UnderTrail = "closed under its trailing stop";

    public const string TimeLimit = "reached its time limit";

    public const string NoResult = "ended with no result read";

    public const string Waiting = "waits on the next session's open";

    public const string Better = "Better";

    public const string Worse = "Worse";

    public const string Even = "Even";

    public const string NoTrades = "No trades";

    public const string EasilyLuck = "Easily luck";

    public const string MaybeLuck = "Maybe luck";

    public const string Real = "Real";

    // The scale an adjusted p is placed on: from one down to the level a run is held to, from there to the bar, and from
    // the bar to a ten-thousandth, each zone read on a log scale and drawn at these shares of the whole.
    public const double EasilyShare = 0.6;

    public const double MaybeShare = 0.3;

    const double Floor = 0.0001;

    // The one in so many a rule's result must be rarer than to count as real: the bar's reciprocal.
    public static int RealOneIn => (int)Math.Round(1 / LoopGate.Bar);

    // An open trade on the night, marked by the ratio of its stock's closes as the walk marks it, so a split moves nothing:
    // its stop tonight the plan's, a trailing plan's raised to the highest close since its listing night less the distance
    // it trails and never lowered; its result so far in the risk its plan's close put up and as a fraction of its buy; the
    // sessions since its listing night; and its standing. A holding's is the words handed in. A trade whose fill opened past
    // its stop or its target says so; otherwise it is near its target or near its stop within one typical move of either at
    // the trade's own scale, the nearer where both are and the stop on a tie, and on track where neither is; and not read
    // tonight where its close or its typical move is not.
    public static PaperPosition Position(PaperTrade trade, DateOnly night, IReadOnlyList<DateOnly> sessions, Func<string, DateOnly, decimal?> close, double? typicalMove, string? holding = null)
    {
        var held = ExchangeClosures.SessionsUntil(trade.ListedOn, night);
        var book = trade.Stop is null;
        var shut = close(trade.Ticker, night);

        if (shut is not { } now || (trade.MarkedFrom ?? close(trade.Ticker, trade.Bought)) is not { } then || then <= 0m)
        {
            return new PaperPosition(shut, null, trade.Stop, null, null, held, book ? holding ?? HeavyweightStandings.NotRead : HeavyweightStandings.NotRead);
        }

        var scale = trade.Buy / then;
        var price = now * scale;
        var stop = trade.Trail is { } trail && trade.Stop is { } first
            ? sessions
                .Where(session => session > trade.ListedOn && session <= night)
                .Select(session => close(trade.Ticker, session))
                .OfType<decimal>()
                .Aggregate(first, (floor, at) => Math.Max(floor, (at * scale) - trail))
            : trade.Stop;
        var planned = trade.Planned ?? trade.Buy;
        double? risks = trade.Stop is { } level && planned > level ? Statistic.FromRatio((price - trade.Buy) / (planned - level)) : null;
        var standing = book
            ? holding ?? HeavyweightStandings.NotRead
            : trade.Past == FillPast.Stop ? OpenedUnderStop
            : trade.Past == FillPast.Target ? OpenedOverTarget
            : Standing(price, stop, trade.Target, typicalMove is { } move ? move * Statistic.FromRatio(scale) : null);

        return new PaperPosition(now, price, stop, risks, Statistic.FromRatio((now / then) - 1m), held, standing);
    }

    // Near its stop or its target within one typical move, the nearer where both are and the stop on a tie; on track where
    // neither is; not read where no typical move is.
    static string Standing(decimal price, decimal? stop, decimal? target, double? move)
    {
        if (move is not > 0)
        {
            return HeavyweightStandings.NotRead;
        }

        var toStop = Distances.InTypicalDays(price, stop, move);
        var toTarget = Distances.InTypicalDays(price, target, move);

        return toStop is <= 1 && (toTarget is not <= 1 || toStop <= toTarget) ? NearStop
            : toTarget is <= 1 ? NearTarget
            : OnTrack;
    }

    // A trade that ended: its sale and its result in risks, a holding's in its fraction alone; how it ended, a holding's
    // the reason its book sold it, a page list's trade the words its stored outcome reads as, handed in, and otherwise read
    // off where it was sold, at or over its target reached it, a trailing plan's before its cap under its trailing stop, a
    // fixed plan's at or under its stop under its stop, and at its cap its time limit; the sessions from its listing night
    // to its end; and its edge.
    public static PaperEnded Ended(PaperTrade trade, double? result)
    {
        var held = trade.Ended is { } end ? ExchangeClosures.SessionsUntil(trade.ListedOn, end) : null;
        var book = trade.Stop is null;
        double? fraction = trade.Sale is { } sale && trade.Buy > 0m ? Statistic.FromRatio((sale / trade.Buy) - 1m) : book ? result : null;

        return new PaperEnded(trade.Sale, book ? null : result, fraction, Ending(trade, held), held, trade.Edge);
    }

    static string Ending(PaperTrade trade, int? held) =>
        trade.Stop is null ? trade.Outcome is { } reason ? "sold: " + reason : "sold"
            : trade.Outcome is { } said ? said
            : trade.Sale is not { } sale ? NoResult
            : trade.Target is { } target && sale >= target ? ReachedTarget
            : trade.Trail is not null ? held is { } trailed && trade.Cap is { } trailCap && trailed >= trailCap ? TimeLimit : UnderTrail
            : trade.Stop is { } stop && sale <= stop ? UnderStop
            : held is { } kept && trade.Cap is { } cap && kept >= cap ? TimeLimit
            : UnderStop;

    // The listed stocks the accounts given did not buy: those each walk skipped, each for the reason the walk gave, and
    // those no fill bought, waiting on their next session's open or holding none for the reason stored; a reason at a time,
    // the most held first and then by its words, each reason's stocks newest listing first.
    public static PaperNotBought NotBought(IReadOnlyList<(PaperWalk? Walk, IReadOnlyList<(string Ticker, DateOnly Listed, string Why)> Unfilled)> accounts) =>
        new(
        [
            .. accounts
                .SelectMany(account => (account.Walk?.Trades ?? [])
                    .Where(one => one.Skipped is not null)
                    .Select(one => (one.Trade.Ticker, Listed: one.Trade.ListedOn, Why: one.Skipped!))
                    .Concat(account.Unfilled))
                .GroupBy(one => one.Why, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new PaperUnbought(group.Key, [.. group.OrderByDescending(one => one.Listed).ThenBy(one => one.Ticker, StringComparer.Ordinal).Select(one => (one.Ticker, one.Listed))])),
        ]);

    // A rule's trades against others': those another took in the same stock off the same night are shared, and the rest
    // are its alone.
    public static PaperOverlap Overlap(IReadOnlyList<PaperTrade> own, IReadOnlyList<PaperTrade> others)
    {
        var theirs = others.Select(trade => (trade.Ticker, trade.ListedOn)).ToHashSet();
        var shared = own.Count(trade => theirs.Contains((trade.Ticker, trade.ListedOn)));

        return new PaperOverlap(own.Count, shared, own.Count - shared);
    }

    // The accounts holding the stock open on the night, of those named, in their order.
    public static IReadOnlyList<string> HeldBy(string ticker, DateOnly night, IReadOnlyList<(string Name, IReadOnlyList<PaperTrade> Trades)> accounts) =>
    [
        .. accounts
            .Where(account => account.Trades.Any(trade => trade.Ticker == ticker && trade.Bought <= night && (trade.Ended is null || trade.Ended > night)))
            .Select(account => account.Name),
    ];

    // A test year: better where its total stood above what it was read against's, worse where under, even where level and
    // no trades where it holds none; its edge the difference over its own units.
    public static PaperYear Year(int year, bool complete, int units, double total, double compared) =>
        new(
            year,
            complete,
            units == 0 ? NoTrades : total > compared ? Better : total < compared ? Worse : Even,
            units > 0 ? (total - compared) / units : null,
            units);

    // An adjusted p as the scorecard reads it: easily luck at the level a run is held to or over, maybe luck between the bar
    // and that level, real at or under the bar; in words about so many times in a hundred, and under one in a hundred about
    // one in so many; and its place on the scale, each zone read on a log scale over its own span.
    public static PaperLuck Luck(double adjusted)
    {
        var p = Math.Max(adjusted, double.Epsilon);
        var zone = p >= LoopGate.Level ? EasilyLuck : p > LoopGate.Bar ? MaybeLuck : Real;
        var hundred = (int)Math.Round(p * 100, MidpointRounding.AwayFromZero);
        var words = p >= 0.01
            ? FormattableString.Invariant($"about {hundred} {(hundred == 1 ? "time" : "times")} in 100")
            : FormattableString.Invariant($"about 1 in {Math.Round(1 / p, MidpointRounding.AwayFromZero):#,0}");

        static double Along(double p, double from, double to) =>
            Math.Clamp((Math.Log10(from) - Math.Log10(p)) / (Math.Log10(from) - Math.Log10(to)), 0, 1);

        var place = zone == EasilyLuck ? EasilyShare * Along(p, 1, LoopGate.Level)
            : zone == MaybeLuck ? EasilyShare + (MaybeShare * Along(p, LoopGate.Level, LoopGate.Bar))
            : EasilyShare + MaybeShare + ((1 - EasilyShare - MaybeShare) * Along(Math.Max(p, Floor), LoopGate.Bar, Floor));

        return new PaperLuck(zone, words, place);
    }

    // A record's progress: a swing rule's decided trades against the count the test judges a family on, a book's months
    // since its start against its count of months; the share of the count held, never past the whole; and the mean edge of
    // its decided trades holding one.
    public static PaperProgress Progress(bool book, IReadOnlyList<PaperTrade> trades, DateOnly? since, DateOnly night)
    {
        var counted = book ? since is { } from ? RuleVerdict.MonthsBetween(from, night) : 0 : trades.Count(trade => trade.Ended is not null);
        var needed = book ? LoopScreens.BookMonths : LoopScreens.SwingTrades;
        double[] edges = [.. trades.Where(trade => trade.Ended is not null && trade.Edge is not null).Select(trade => trade.Edge!.Value)];

        return new PaperProgress(counted, needed, Math.Min(1.0, counted * 1.0 / needed), edges.Length > 0 ? edges.Average() : null, edges.Length);
    }

    // An account's trades its walk bought, those open on the night and those ended.
    public static (int Open, int Closed) Held(PaperWalk walk, DateOnly night) =>
        (walk.Trades.Count(one => one.Skipped is null && (one.Trade.Ended is null || one.Trade.Ended > night)),
         walk.Trades.Count(one => one.Skipped is null && one.Trade.Ended is { } end && end <= night));
}
