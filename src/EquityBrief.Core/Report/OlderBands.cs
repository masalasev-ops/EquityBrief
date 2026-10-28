namespace EquityBrief.Core.Report;

// A band read from the history pulled before the store's year, where the stored year holds no band on a side: its role,
// its edges at the store's scale and the session the pulled prices turned at. It is drawn on the name page alone, marked
// older, and no rule, level, plan, listing or gate reads it.
public sealed record OlderBand(string Role, decimal Low, decimal High, DateOnly MadeOn);

// The older bands a page draws: the swing highs above the price and the swing lows below it that the pulled sessions turned
// at, nearest the price first and at most two a side, each the turning session's high less half a typical day's move, or
// its low plus half of one, at the store's scale; read for a side the stored year's bands leave empty, and for a side a
// pick's target or stop lies beyond every band on, as far out as the band that reaches it, up to the most a side reaching.
// see: Older bands are read out as far as a pick's stop and target, marked older and read by nothing else
public static class OlderBands
{
    // A turn stands where its high is the highest, or its low the lowest, of the sessions this many either side of it.
    public const int Reach = 10;

    // The most older bands a side draws.
    public const int MostASide = 2;

    // The most older bands a side draws reading out to a pick's target or stop.
    public const int MostReaching = 8;

    public static IReadOnlyList<OlderBand> Of(
        IReadOnlyList<(DateOnly Session, decimal High, decimal Low)> pulled,
        decimal scale,
        decimal price,
        decimal typicalMove,
        bool above,
        bool below,
        decimal? reachUp = null,
        decimal? reachDown = null,
        decimal? aboveFrom = null,
        decimal? belowFrom = null)
    {
        if (pulled.Count < (2 * Reach) + 1 || scale <= 0 || typicalMove <= 0)
        {
            return [];
        }

        var half = typicalMove / 2;
        var bands = new List<OlderBand>();

        if (above)
        {
            bands.AddRange(Reaching(
                Turns(pulled, high: true)
                    .Select(turn => (turn.Session, Price: turn.Price * scale))
                    .Where(turn => turn.Price - half > (aboveFrom ?? price))
                    .OrderBy(turn => turn.Price)
                    .Select(turn => new OlderBand("resistance", turn.Price - half, turn.Price, turn.Session))
                    .Aggregate(new List<OlderBand>(), Apart),
                band => reachUp is { } target && band.High < target));
        }

        if (below)
        {
            bands.AddRange(Reaching(
                Turns(pulled, high: false)
                    .Select(turn => (turn.Session, Price: turn.Price * scale))
                    .Where(turn => turn.Price + half < (belowFrom ?? price))
                    .OrderByDescending(turn => turn.Price)
                    .Select(turn => new OlderBand("support", turn.Price, turn.Price + half, turn.Session))
                    .Aggregate(new List<OlderBand>(), Apart),
                band => reachDown is { } stop && band.Low > stop));
        }

        return bands;
    }

    // The bands of a side nearest the price first: the two nearest, and past them each further one while the bands taken
    // fall short of the pick's level on that side, up to the most a side reaching.
    static IEnumerable<OlderBand> Reaching(IReadOnlyList<OlderBand> nearestFirst, Func<OlderBand, bool> shortOfTheLevel)
    {
        for (var at = 0; at < nearestFirst.Count && at < MostReaching; at++)
        {
            if (at >= MostASide && !shortOfTheLevel(nearestFirst[at - 1]))
            {
                yield break;
            }

            yield return nearestFirst[at];
        }
    }

    // Each session whose high is the highest, or whose low the lowest, of the sessions within the reach either side of it:
    // beyond every session before it and not passed by any after, so a run of equal prices turns once, at its first.
    static IEnumerable<(DateOnly Session, decimal Price)> Turns(IReadOnlyList<(DateOnly Session, decimal High, decimal Low)> pulled, bool high)
    {
        for (var at = Reach; at < pulled.Count - Reach; at++)
        {
            var price = high ? pulled[at].High : pulled[at].Low;
            var turned = true;

            for (var near = at - Reach; near <= at + Reach && turned; near++)
            {
                var other = high ? pulled[near].High : pulled[near].Low;

                if (near < at ? (high ? other >= price : other <= price) : near > at && (high ? other > price : other < price))
                {
                    turned = false;
                }
            }

            if (turned)
            {
                yield return (pulled[at].Session, price);
            }
        }
    }

    // A band overlapping one already kept is the same turn seen twice and is left out.
    static List<OlderBand> Apart(List<OlderBand> kept, OlderBand band)
    {
        if (!kept.Any(other => band.Low <= other.High && other.Low <= band.High))
        {
            kept.Add(band);
        }

        return kept;
    }
}
