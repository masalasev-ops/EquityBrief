namespace EquityBrief.Core.Returns;

// The standard normal's distribution function and its inverse.
//
// The spending function is written in both, so both are here rather than in the
// file that spends: a figure a page draws is computed by code this repository
// carries, and a distribution function is the kind of arithmetic that is
// otherwise reached for from a library on the day it is needed.
// see: Code owns every number
public static class Normal
{
    // The share of the distribution at or below a point.
    public static double Cdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2));

    // The point with this share of the distribution at or below it.
    //
    // Two rational approximations, one for each tail and one for the middle,
    // which is Acklam's arrangement and is accurate to about a part in a
    // billion. The spending function reads this at levels of the order of a
    // hundredth, where that is far finer than anything a verdict turns on.
    public static double Quantile(double p)
    {
        if (p is <= 0 or >= 1 || double.IsNaN(p))
        {
            throw new ArgumentOutOfRangeException(
                nameof(p),
                p,
                "The normal's inverse is asked for a share that is not strictly between nought and one. " +
                "A level of nought or one is a test that never rejects or always does, which is a fault " +
                "in what asked rather than a point on the distribution.");
        }

        const double low = 0.02425;

        if (p < low)
        {
            return Tail(Math.Sqrt(-2 * Math.Log(p)));
        }

        if (p > 1 - low)
        {
            return -Tail(Math.Sqrt(-2 * Math.Log(1 - p)));
        }

        var q = p - 0.5;
        var r = q * q;

        return (((((-39.69683028665376 * r + 220.9460984245205) * r + -275.9285104469687) * r
            + 138.3577518672690) * r + -30.66479806614716) * r + 2.506628277459239) * q
            / (((((-54.47609879822406 * r + 161.5858368580409) * r + -155.6989798598866) * r
            + 66.80131188771972) * r + -13.28068155288572) * r + 1);
    }

    static double Tail(double q) =>
        (((((-0.007784894002430293 * q + -0.3223964580411365) * q + -2.400758277161838) * q
            + -2.549732539343734) * q + 4.374664141464968) * q + 2.938163982698783)
        / ((((0.007784695709041462 * q + 0.3224671290700398) * q + 2.445134137142996) * q
            + 3.754408661907416) * q + 1);

    // The complementary error function, by the Chebyshev fit, whose relative
    // error is under about a part in ten million everywhere.
    //
    // The complement rather than the error function itself, because what is read
    // off it here is a tail of the order of a thousandth, and taking one less a
    // near-equal number is where the precision of a small answer goes.
    static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + (0.5 * z));

        var answer = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418
            + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587
            + t * (-0.82215223 + t * 0.17087277)))))))));

        return x >= 0 ? answer : 2 - answer;
    }
}
