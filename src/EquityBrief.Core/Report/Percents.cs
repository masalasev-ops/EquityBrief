using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Report;

// The one crossing the name page's report arithmetic makes from the decimal world to the statistics one: a fraction
// worked as a decimal from money, a change, a margin or a share, out as a percentage, through `Statistic.FromRatio`.
public static class Percents
{
    public static double? FromFraction(decimal? fraction) => fraction is { } part ? Statistic.FromRatio(part) * 100 : null;
}
