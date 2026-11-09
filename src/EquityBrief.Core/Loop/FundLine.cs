namespace EquityBrief.Core.Loop;

// The second line beside a result read over the long history: its index's own fund held over the same sessions, the
// fund's return over them stated in the same risk as the result, so a result reads against the fund as it reads against
// its benchmark. The benchmark itself stays the ruled one, the same plan entered on every member and sold as the trade is.
// see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
public static class FundLine
{
    // Each index's own fund.
    public static IReadOnlyDictionary<string, string> Funds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GSPC"] = "SPY",
        ["MID"] = "IJH",
        ["SML"] = "IJR",
    };

    public static string? FundOf(string index) => Funds.GetValueOrDefault(index);

    // A result's edge over the fund held from one close to another: the result less the fund's return over them, scaled
    // from a share of the price into the result's risk; none where either close or the risk is not held.
    public static double? Edge(double result, double price, double risk, double bought, double sold) =>
        double.IsFinite(result) && price > 0 && risk > 0 && bought > 0 && sold > 0 && double.IsFinite(bought) && double.IsFinite(sold)
            ? result - (((sold / bought) - 1) * price / risk)
            : null;

    // A fund's closes laid against a calendar, NaN on a session it holds no close for.
    public static double[] Aligned(IReadOnlyList<DateOnly> calendar, IEnumerable<(DateOnly Session, double Close)> closes)
    {
        var byDay = new Dictionary<DateOnly, double>();

        foreach (var (session, close) in closes)
        {
            byDay[session] = close;
        }

        var aligned = new double[calendar.Count];

        for (var at = 0; at < calendar.Count; at++)
        {
            aligned[at] = byDay.TryGetValue(calendar[at], out var close) ? close : double.NaN;
        }

        return aligned;
    }

    // An edge over the fund from a trade's listing session to the session it ended on, as calendar places.
    public static double? Edge(double[]? fund, double result, double price, double risk, int entry, int exit) =>
        fund is not null && entry >= 0 && exit >= entry && exit < fund.Length
            ? Edge(result, price, risk, fund[entry], fund[exit])
            : null;

    // The mean of the edges held, with how many it is over.
    public static (int Units, double? Mean) Mean(IEnumerable<double?> edges)
    {
        var (sum, count) = (0.0, 0);

        foreach (var edge in edges)
        {
            if (edge is { } held)
            {
                sum += held;
                count++;
            }
        }

        return (count, count > 0 ? sum / count : null);
    }
}
