using System.Globalization;

namespace EquityBrief.Core.Report;

// One confirmed swing low the trend rule compares: the session it was made on and its price.
public sealed record SwingLow(DateOnly Session, decimal Price);

// Why the trend rule read a stock as it did, in plain words from the inputs it reads: the close against its 50-day average,
// the 50-day against the 200-day, and the newest confirmed swing low against the one before it. An uptrend needs all three
// rising, a downtrend all three falling, and a range is any other mix; each input that kept the stock out of an uptrend is
// named with its figures. It decides nothing and is drawn beside the trend gate's own stored words.
// see: How the rules read it opens on the checks of the family that picked the stock, and every input that failed is named in plain words
public static class TrendWords
{
    public static string? Why(string? state, decimal? close, double? fifty, double? twoHundred, IReadOnlyList<SwingLow> lows)
    {
        if (state is null || state == Ladders.TrendState.Uptrend)
        {
            return null;
        }

        if (close is not { } price || fifty is not { } short50 || twoHundred is not { } long200 || lows.Count < 2)
        {
            return "The trend rule reads an uptrend where the close is above its 50-day average, the 50-day is above the 200-day and the newest swing low is above the one before it; the inputs it read for this stock are not all stored, so the page cannot say which of them it missed.";
        }

        var newest = lows[0];
        var before = lows[1];
        var missed = new List<string>();

        if (Prices.Statistic.FromPrice(price) <= short50)
        {
            missed.Add(Invariant($"the close of {price:0.00} is not above its 50-day average of {short50:0.00}"));
        }

        if (short50 <= long200)
        {
            missed.Add(Invariant($"the 50-day average of {short50:0.00} is not above the 200-day's {long200:0.00}"));
        }

        if (newest.Price <= before.Price)
        {
            missed.Add(Invariant($"the newest swing low, {newest.Price:0.00} on {newest.Session:yyyy-MM-dd}, is not above the one before it, {before.Price:0.00} on {before.Session:yyyy-MM-dd}"));
        }

        var named = state == Ladders.TrendState.Downtrend ? "a downtrend" : state == Ladders.TrendState.Range ? "a range" : state.Replace('_', ' ');

        return missed.Count == 0
            ? Invariant($"The trend rule reads an uptrend where the close is above its 50-day average, the 50-day is above the 200-day and the newest swing low is above the one before it; the inputs stored for the night meet all three, and the night's own reading of {named} stands as it stored it.")
            : "The trend rule reads an uptrend where the close is above its 50-day average, the 50-day is above the 200-day and the newest swing low is above the one before it. Here "
                + Joined(missed) + Invariant($", so it reads {named}.");
    }

    static string Joined(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
