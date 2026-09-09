using EquityBrief.Core.Swings;

namespace EquityBrief.Core.Ladders;

// The four trend states, as SCHEMA's `trend_state` column carries them.
//
// The fourth is not a label. A name with fewer than two hundred bars has no long
// average and a name with fewer than two swings of a kind has nothing to compare,
// so the rule has an input it does not have and says so rather than answering.
// see: The trend state is read from the averages and the last two swings, and a name that cannot be classified says so
public static class TrendState
{
    public const string Uptrend = "uptrend";
    public const string Downtrend = "downtrend";
    public const string Range = "range";
    public const string NotClassified = "not_classified";

    public static IReadOnlyList<string> All { get; } = [Uptrend, Downtrend, Range, NotClassified];
}

// What the classifier read, and what it concluded. The reason is null when the
// state is one of the three the rule reaches, and it names the missing input
// when it is not.
public readonly record struct Trend(string State, string? Reason)
{
    public bool Classified => Reason is null;
}

// The trend classifier's rule.
//
// Section 10 said "from the averages and the last two swings", which is a
// description rather than a rule, and it selects which ladder shape applies: a
// downtrend carries no tranches at all. 4.0 made it a rule.
//
// Uptrend when the close is above the 50-day average, the 50 is above the 200,
// and the most recent swing low is above the one before it. Downtrend is the
// mirror. Range when every input is present and neither holds.
//
// The averages say where the price is against its own history and the swings say
// what the structure has been doing, and both are needed: an average pair can
// cross on a name going nowhere, and a pair of higher lows can sit under a price
// below both averages.
//
// It writes nothing and returns its label to the ladder builder.
// see: The trend classifier returns its label to the ladder builder
// see: Code owns every number
public static class TrendSeries
{
    // The two swings of one kind the rule compares. Two rather than one, because
    // the rule is about a structure moving rather than about where a swing sits.
    public const int SwingsCompared = 2;

    public static Trend For(
        decimal close,
        decimal? shortAverage,
        decimal? longAverage,
        IReadOnlyList<Swing> swings)
    {
        if (shortAverage is not { } shortMean)
        {
            return new Trend(TrendState.NotClassified, "no 50-day average");
        }

        if (longAverage is not { } longMean)
        {
            return new Trend(TrendState.NotClassified, "no 200-day average");
        }

        var lows = Recent(swings, SwingSeries.Low);
        var highs = Recent(swings, SwingSeries.High);

        // Which kind is missing decides which reading cannot be made, so the
        // reason names the kind rather than saying the swings were short. A name
        // with two lows and no highs can be read for an uptrend and not for a
        // downtrend, and answering range for it would be answering a question
        // the rule could not ask.
        if (lows.Count < SwingsCompared && highs.Count < SwingsCompared)
        {
            return new Trend(
                TrendState.NotClassified,
                $"fewer than {SwingsCompared} swing lows and fewer than {SwingsCompared} swing highs");
        }

        if (close > shortMean && shortMean > longMean)
        {
            return lows.Count < SwingsCompared
                ? new Trend(TrendState.NotClassified, $"fewer than {SwingsCompared} swing lows")
                : Rising(lows)
                    ? new Trend(TrendState.Uptrend, null)
                    : new Trend(TrendState.Range, null);
        }

        if (close < shortMean && shortMean < longMean)
        {
            return highs.Count < SwingsCompared
                ? new Trend(TrendState.NotClassified, $"fewer than {SwingsCompared} swing highs")
                : Falling(highs)
                    ? new Trend(TrendState.Downtrend, null)
                    : new Trend(TrendState.Range, null);
        }

        return new Trend(TrendState.Range, null);
    }

    // The last two swings of one kind, oldest first. Ordered by the session the
    // swing is on rather than by the session it was confirmed on, because the
    // structure is what the price did and not when it became knowable.
    static IReadOnlyList<Swing> Recent(IReadOnlyList<Swing> swings, string direction) =>
    [
        .. swings
            .Where(swing => swing.Direction == direction)
            .OrderBy(swing => swing.SessionDate)
            .TakeLast(SwingsCompared),
    ];

    static bool Rising(IReadOnlyList<Swing> lows) => lows[^1].Price > lows[^2].Price;

    static bool Falling(IReadOnlyList<Swing> highs) => highs[^1].Price < highs[^2].Price;
}
