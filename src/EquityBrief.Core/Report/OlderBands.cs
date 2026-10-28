namespace EquityBrief.Core.Report;

// A band read from the history pulled before the store's year, where the stored year holds no band on a side: its role,
// its edges at the store's scale and the session the pulled prices turned at. It is drawn on the name page alone, marked
// older, and no rule, level, plan, listing or gate reads it.
public sealed record OlderBand(string Role, decimal Low, decimal High, DateOnly MadeOn);

// The older bands a page draws: the swing highs above the price and the swing lows below it that the pulled sessions turned
// at, nearest the price first and at most two a side, each the turning session's high less half a typical day's move, or
// its low plus half of one, at the store's scale; read only for a side the stored year's bands leave empty.
// see: Where the stored year holds no band on a side the name page draws bands read from the pulled history, marked older and read by nothing else
public static class OlderBands
{
    // A turn stands where its high is the highest, or its low the lowest, of the sessions this many either side of it.
    public const int Reach = 10;

    // The most older bands a side draws.
    public const int MostASide = 2;

    public static IReadOnlyList<OlderBand> Of(
        IReadOnlyList<(DateOnly Session, decimal High, decimal Low)> pulled,
        decimal scale,
        decimal price,
        decimal typicalMove,
        bool above,
        bool below)
    {
        if (pulled.Count < (2 * Reach) + 1 || scale <= 0 || typicalMove <= 0)
        {
            return [];
        }

        var half = typicalMove / 2;
        var bands = new List<OlderBand>();

        if (above)
        {
            bands.AddRange(Turns(pulled, high: true)
                .Select(turn => (turn.Session, Price: turn.Price * scale))
                .Where(turn => turn.Price - half > price)
                .OrderBy(turn => turn.Price)
                .Select(turn => new OlderBand("resistance", turn.Price - half, turn.Price, turn.Session))
                .Aggregate(new List<OlderBand>(), Apart)
                .Take(MostASide));
        }

        if (below)
        {
            bands.AddRange(Turns(pulled, high: false)
                .Select(turn => (turn.Session, Price: turn.Price * scale))
                .Where(turn => turn.Price + half < price)
                .OrderByDescending(turn => turn.Price)
                .Select(turn => new OlderBand("support", turn.Price, turn.Price + half, turn.Session))
                .Aggregate(new List<OlderBand>(), Apart)
                .Take(MostASide));
        }

        return bands;
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
