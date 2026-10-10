using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Report;

// One member as the valuation's peer table reads it on the night: its ticker, its multiple on its last four quarters'
// earnings, its dividend's yield at its close in per cent and its newest quarter's sales growth on a year in per cent,
// each none where the night read none, and whether it is the stock the page is about.
public sealed record PeerValue(string Ticker, double? Multiple, double? Yield, double? SalesGrowth, bool Own);

// The peer table: the industry it was read over, the stock's own row among its industry's S&P 500 members in the multiple's
// order, and the median of each column over every row holding it, with how many held it.
public sealed record PeerTable(string Industry, IReadOnlyList<PeerValue> Rows, double? MedianMultiple, int Multiples, double? MedianYield, int Yields, double? MedianGrowth, int Growths);

// The valuation against the stock's industry's S&P 500 members, from the night's own readings and no request, worked by one
// function so the page and its file draw the same rows and medians. The order is the multiple's, lowest first, with a
// row reading none last, which ranks no company: it lays the multiples out so the stock's place among them reads.
// see: The valuation reads a stock against its own quarters and its industry's S&P 500 members from the night's readings alone
public static class PeerValuation
{
    public static PeerTable? Of(string industry, string ticker, IReadOnlyList<PeerValue> members)
    {
        if (members.Count == 0)
        {
            return null;
        }

        var rows = members
            .GroupBy(member => member.Ticker, StringComparer.Ordinal)
            .Select(group => group.First() with { Own = string.Equals(group.Key, ticker, StringComparison.Ordinal) })
            .OrderBy(member => member.Multiple is null ? 1 : 0)
            .ThenBy(member => member.Multiple)
            .ThenBy(member => member.Ticker, StringComparer.Ordinal)
            .ToArray();

        var multiples = rows.Where(row => row.Multiple is not null).Select(row => row.Multiple!.Value).ToArray();
        var yields = rows.Where(row => row.Yield is not null).Select(row => row.Yield!.Value).ToArray();
        var growths = rows.Where(row => row.SalesGrowth is not null).Select(row => row.SalesGrowth!.Value).ToArray();

        return new PeerTable(industry, rows, Median(multiples), multiples.Length, Median(yields), yields.Length, Median(growths), growths.Length);
    }

    // The middle value, the mean of the two middle ones over an even count, none over none.
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.Order().ToArray();
        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}
