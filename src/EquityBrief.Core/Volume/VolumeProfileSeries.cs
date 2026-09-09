using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Volume;

// One session's range and the shares that traded in it.
//
// Prices decimal, volume long. Nothing here is a statistic about prices, so
// nothing here crosses the money boundary: the band edges are prices, the shares
// are a count, and the only double in the output is a ratio of two counts.
public readonly record struct ProfileBar(DateOnly SessionDate, decimal High, decimal Low, long Volume);

// One price band of the profile.
//
// ShareOfPeriod is double and is a fraction of the window's total volume, which
// is a ratio of two share counts rather than an arithmetic on prices. It is
// stored REAL for that reason, and it is carried beside the count rather than
// left to a reader to divide, because the count alone says nothing without the
// period it is a share of.
public readonly record struct ProfileBand(decimal Low, decimal High, long Shares, double ShareOfPeriod);

// The volume profile arithmetic, as a pure function of a session-ordered series.
//
// Each day's volume is spread across that day's own range in proportion to how
// much of that range each band covers, which is what section 7's catalogue row
// says the builder does. The alternative, putting a day's whole volume at its
// close, is the version most charting packages ship and it is wrong for the use
// this profile is put to: a day that opened at 300 and closed at 320 did not
// trade every share at 320, and the level builder is looking for the prices
// holders actually paid.
// see: Code owns every number
public static class VolumeProfileSeries
{
    // Section 17's level window. The profile accumulates over the same sixty
    // sessions the level builder reads, and not a span of its own.
    // see: The volume profile accumulates over the same sixty sessions as the level window
    public const int Window = 60;

    // Twenty bands across the window's own high and low.
    // see: The volume profile is twenty bands across the window's own range
    public const int Bands = 20;

    // Band edges are put in the form every price in this store carries, which is
    // four places with trailing zeros removed, so the edges a reader sees are
    // prices of the same shape as the ones beside them.
    //
    // Through PriceForm rather than through decimal.Round, and the difference is
    // not cosmetic. decimal.Round trims a scale longer than four places and
    // leaves a shorter one alone, so an edge that lands on three places is
    // written "66.006" and one that needs four is written "382.3910", from the
    // same expression. `band_low` is a primary key column. Two renderings of one
    // price are two rows, and 3.6 found this by adding a fourth name whose
    // narrower range put edges on both sides of that line.
    // The twenty-one edges are computed once and each band takes two adjacent
    // members of that array, which is what makes the bands tile: band k's high
    // is band k+1's low by construction rather than by two roundings agreeing.
    public const int Places = 4;

    // The window, or nothing.
    //
    // A name with fewer than sixty stored sessions gets no profile at all rather
    // than a profile over what it has.
    // see: A name with fewer than sixty sessions gets no volume profile
    public static IReadOnlyList<ProfileBand> For(IReadOnlyList<ProfileBar> bars)
    {
        if (bars.Count < Window)
        {
            return [];
        }

        var window = bars.Skip(bars.Count - Window).ToArray();

        var high = window.Max(bar => bar.High);
        var low = window.Min(bar => bar.Low);
        var total = window.Sum(bar => bar.Volume);

        var edges = EdgesOf(low, high);

        // Decimal all the way through the spreading, so the only rounding is the
        // one at the end that turns a share of a day into a whole number of
        // shares. A double here would put a rounding error into every one of
        // twenty bands over sixty sessions and leave the sum near the total
        // rather than at it.
        var spread = new decimal[edges.Length - 1];

        foreach (var bar in window)
        {
            Spread(bar, edges, spread);
        }

        return Apportioned(edges, spread, total);
    }

    // The twenty-one prices that bound the twenty bands, low first.
    //
    // The multiplication happens before the division so the arithmetic is on the
    // whole range rather than on a width already rounded, which is the same
    // ordering the bar adjustment uses and for the same reason.
    //
    // Consecutive edges that round to one price collapse, so a window whose
    // range is narrower than twenty times the storage precision produces fewer
    // than twenty bands rather than a band with no width. It cannot happen to a
    // name whose range over sixty sessions is more than a fifth of a cent, and
    // it is handled here rather than refused because a refusal in the nightly
    // path for a case nothing can reach is a worse failure than a smaller
    // profile.
    static decimal[] EdgesOf(decimal low, decimal high)
    {
        if (high == low)
        {
            return [low, high];
        }

        var range = high - low;
        var edges = new List<decimal> { low };

        for (var band = 1; band <= Bands; band++)
        {
            var edge = band == Bands
                ? high
                : PriceForm.Round(low + (band * range / Bands), Places);

            if (edge > edges[^1])
            {
                edges.Add(edge);
            }
        }

        return [.. edges];
    }

    // One session's volume across the bands its range covers.
    static void Spread(ProfileBar bar, decimal[] edges, decimal[] spread)
    {
        var range = bar.High - bar.Low;

        if (range == 0)
        {
            // A session that traded at one price all day. Its volume belongs to
            // the band holding that price, and the band is found by walking the
            // edges rather than by dividing, because the last band has to take
            // the window's high and a division would put it in a twenty-first.
            spread[BandHolding(bar.Low, edges)] += bar.Volume;

            return;
        }

        for (var band = 0; band < spread.Length; band++)
        {
            var overlap = Math.Min(edges[band + 1], bar.High) - Math.Max(edges[band], bar.Low);

            if (overlap > 0)
            {
                spread[band] += bar.Volume * overlap / range;
            }
        }
    }

    // The band a single price sits in. Low edge inclusive, high edge exclusive,
    // except at the top where the window's own high has to land somewhere.
    static int BandHolding(decimal price, decimal[] edges)
    {
        for (var band = edges.Length - 2; band > 0; band--)
        {
            if (price >= edges[band])
            {
                return band;
            }
        }

        return 0;
    }

    // Whole shares, summing to the window's total.
    //
    // The done condition is that the shares in the bands sum to the window's
    // total volume, and flooring twenty fractions loses up to nineteen shares.
    // So the floors are taken first and the shares they leave over are handed to
    // the bands with the largest fractions, which is the largest remainder rule
    // and is the ordinary way a whole is divided into whole parts. Ties go to
    // the lower band, so the answer does not depend on the order a sort happens
    // to be stable in.
    static IReadOnlyList<ProfileBand> Apportioned(decimal[] edges, decimal[] spread, long total)
    {
        var shares = new long[spread.Length];
        var assigned = 0L;

        for (var band = 0; band < spread.Length; band++)
        {
            shares[band] = (long)decimal.Floor(spread[band]);
            assigned += shares[band];
        }

        var owed = total - assigned;

        foreach (var band in Enumerable.Range(0, spread.Length)
            .OrderByDescending(band => spread[band] - decimal.Floor(spread[band]))
            .ThenBy(band => band)
            .Take((int)Math.Max(0, owed)))
        {
            shares[band]++;
        }

        // The share of the period is computed from the apportioned count rather
        // than from the fraction it came from, so the number stored beside a
        // count is the share of that count and not of something near it.
        return [.. Enumerable.Range(0, spread.Length).Select(band => new ProfileBand(
            edges[band],
            edges[band + 1],
            shares[band],
            total == 0 ? 0 : (double)shares[band] / total))];
    }
}
