using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.History;

namespace EquityBrief.Core.Loop;

// One trade of a record as the case for promotion reads it: the night that listed it, the session it ended on, none while it
// is open, and its stored result, a swing trade's in risks and a holding's as its fraction.
public sealed record PromotionTrade(DateOnly Listed, DateOnly? Ended, double? Result);

// A record over the same nights: the nights it is read from and to, its trades listed and closed over them, and its figure,
// per $1 risked for a swing rule and points a month for a book, none before a trade with a result has closed.
public sealed record PromotionRecord(DateOnly From, DateOnly Through, int Listed, int Closed, double? Figure, int Months);

// A rule's test over the long history as the case reads it: its figure from the years it was not chosen on, per unit, and the
// units it counts; none where the test holds no unit from those years.
public sealed record PromotionLong(double? Figure, int Units);

// The rule's three conditions for one variant against its running revision, each met or not.
public sealed record PromotionConditions(bool SameNights, bool LongHistory, bool Enough)
{
    public bool Meets => SameNights && LongHistory && Enough;

    public int Met => (SameNights ? 1 : 0) + (LongHistory ? 1 : 0) + (Enough ? 1 : 0);
}

// The rule a variant replaces its running revision by, at a monthly run from the one that ends the rule freeze: over the same
// nights it beat the running revision, both read from the later of the two's first sessions and never before the freeze's
// first day; over the long history's years from 2019 its test beat the running revision's over the same years; and it closed
// at least 50 trades, a book 12 months. A replacement is the operator's click and nothing else.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
public static class PromotionRule
{
    public const int Trades = 50;

    public const int Months = 12;

    // The first monthly run a case is put to the operator at, the run that ends the rule freeze.
    public static DateOnly FirstRun => RuleFreezeDates.Until;

    // Whether a case can be put to the operator on a day: from the run that ends the freeze on.
    public static bool PutOn(DateOnly day) => day >= FirstRun;

    // The night both records are read from: the later of the variant's and the running revision's first sessions, never
    // before the freeze's first day, so a variant registered later is judged on its own nights alone.
    public static DateOnly SameFrom(DateOnly variantFrom, DateOnly runningFrom)
    {
        var from = variantFrom > runningFrom ? variantFrom : runningFrom;

        return from < RuleFreezeDates.From ? RuleFreezeDates.From : from;
    }

    // A record over the nights from a day to the night: the trades listed in them, those closed by the night with a result, and
    // its figure. A swing rule's is the mean result of its closed trades, per $1 risked. A book's is the mean change of its
    // closed holdings in percentage points over the trading months the nights span, at least one, as the books' own exit
    // reads it; inside its first month, before a whole month has passed, the change so far, spread over no month.
    public static PromotionRecord Record(bool book, IReadOnlyList<PromotionTrade> trades, DateOnly from, DateOnly night)
    {
        var listed = trades.Where(trade => trade.Listed >= from && trade.Listed <= night).ToArray();
        double[] results = [.. listed.Where(trade => trade.Ended is { } end && end <= night && trade.Result is not null).Select(trade => trade.Result!.Value)];
        var months = RuleVerdict.MonthsBetween(from, night);
        double? figure = results.Length == 0 ? null : book ? results.Average() * 100 / (months == 0 ? 1.0 : TradingMonths(from, night)) : results.Average();

        return new PromotionRecord(from, night, listed.Length, results.Length, figure, months);
    }

    // The trading months between two days, at least one: the sessions between them over 21 a month.
    public static double TradingMonths(DateOnly from, DateOnly night)
    {
        var sessions = 0;

        for (var day = from; day <= night; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions++;
            }
        }

        return Math.Max(1.0, sessions / 21.0);
    }

    // A rule's test over the long history from the year the history counts from, its years before never read: the variant's
    // total over its units, and from the same rows the running revision's total over its own.
    public static (PromotionLong Variant, PromotionLong Running) Long(IReadOnlyList<(int Year, int Units, double Total, int ComparedUnits, double ComparedTotal)> years)
    {
        var judged = years.Where(year => LongLedger.ChosenOn(year.Year)).ToArray();
        var units = judged.Sum(year => year.Units);
        var compared = judged.Sum(year => year.ComparedUnits);

        return (
            new PromotionLong(units == 0 ? null : judged.Sum(year => year.Total) / units, units),
            new PromotionLong(compared == 0 ? null : judged.Sum(year => year.ComparedTotal) / compared, compared));
    }

    // The three conditions. A figure not yet read meets none it is needed for.
    public static PromotionConditions Judge(bool book, PromotionRecord variant, PromotionRecord running, PromotionLong? variantLong, PromotionLong? runningLong) =>
        new(
            variant.Figure is { } made && running.Figure is { } ran && made > ran,
            variantLong?.Figure is { } longMade && runningLong?.Figure is { } longRan && longMade > longRan,
            book ? variant.Months >= Months : variant.Closed >= Trades);

    // How near a variant stands to meeting the rule, for the nearest misses: more conditions met first, then the share of its
    // count held, then its lead over the same nights.
    public static (int Met, double Share, double Lead) Nearness(bool book, PromotionConditions conditions, PromotionRecord variant, PromotionRecord running) =>
        (conditions.Met,
         Math.Min(1.0, (book ? variant.Months : variant.Closed) * 1.0 / (book ? Months : Trades)),
         variant.Figure is { } made && running.Figure is { } ran ? made - ran : double.NegativeInfinity);

    // The chance an idea with no edge passes the bar each is held to, and how many of the ideas tested luck alone would pass.
    public static double LuckyPasses(int tested) => tested * LoopGate.Bar;
}
