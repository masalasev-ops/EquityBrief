namespace EquityBrief.Core.Bars;

// Whether a stock's closes carry a dividend: the step it should leave between the adjusted close and the raw one across its
// ex-date, the amount as paid over the raw close before, against the step they show. A gap of half of 1% of the close or more is
// a dividend not carried, so one paying less can never be read as missing.
// see: A dividend the provider's answer does not yet carry is asked for again on the five nights after and weekly, and the stock's pages and the run page say so while it waits
public static class DividendCarry
{
    public const decimal GapLimit = 0.005m;

    // None where the amount or the closes leave nothing to read.
    public static decimal? Gap(decimal amount, decimal closeBefore, decimal rawBefore, decimal closeAfter, decimal rawAfter)
    {
        if (amount <= 0 || closeBefore <= 0 || rawBefore <= 0 || closeAfter <= 0 || rawAfter <= 0)
        {
            return null;
        }

        return (amount / rawBefore) - Math.Abs(((closeBefore / rawBefore) / (closeAfter / rawAfter)) - 1);
    }

    public static bool NotCarried(decimal? gap) => gap >= GapLimit;

    // A year's closes across an ex-date, none where they do not hold a session either side of it.
    public static decimal? GapAcross(IReadOnlyList<(DateOnly Session, decimal Close, decimal RawClose)> sessions, DateOnly exDate, decimal amount)
    {
        var ordered = sessions.OrderBy(session => session.Session).ToArray();
        var at = Array.FindIndex(ordered, session => session.Session >= exDate);

        return at > 0 ? Gap(amount, ordered[at - 1].Close, ordered[at - 1].RawClose, ordered[at].Close, ordered[at].RawClose) : null;
    }
}
