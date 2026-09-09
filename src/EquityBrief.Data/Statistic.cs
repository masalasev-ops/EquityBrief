namespace EquityBrief.Data;

// The one way a price becomes a statistic.
//
// CLAUDE.md's rule is that prices are decimal in code and TEXT in storage, that
// statistics are double, and that there is no implicit conversion between the
// two worlds: a helper that crosses the boundary does so explicitly and is named
// for it. Money is the storage crossing. This is the other one, and it had no
// helper until an indicator needed it.
//
// It is deliberately one-way. A statistic never becomes a price again: an
// average of closes is not a price something can be bought at, and a helper that
// converted back would be an invitation to quote one as money. Where a figure
// derived from prices does have to be reported as money, it is computed in
// decimal from the start rather than converted back from a double.
//
// The name says what is being given up. A decimal carries more significant
// digits than a double, so the crossing loses precision, and it is acceptable
// here for the reason the rule permits statistics to be double at all: an
// average, a ratio and a standard deviation are approximations of a population
// and their last digits are noise. A price's last digits are not.
public static class Statistic
{
    public static double FromPrice(decimal price) => (double)price;

    public static double FromVolume(long shares) => shares;
}
