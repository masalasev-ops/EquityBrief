namespace EquityBrief.Core.Filter;

// The market in one word, read from the night's breadth by a stated rule: weak below the open filter
// version's breadth floor, where the market gate lists nobody; healthy at the healthy point or above;
// mixed between. A night whose breadth could not be read carries no word.
//
// The weak point is the market gate's own floor, read from the settings the night ran under rather than
// stated here, so the word and the gate cannot disagree. The healthy point is proposed in section 17 and
// moves no gate: it names a market, and nothing is listed or left off by it.
// see: The market on the Run page is named in one word by a stated rule that moves no gate
public static class MarketLabel
{
    public const double HealthyFrom = 0.60;

    public const string Weak = "weak";
    public const string Mixed = "mixed";
    public const string Healthy = "healthy";
    public const string NotRead = "not read";

    public static string For(double? breadth, double floor) =>
        breadth switch
        {
            null => NotRead,
            var share when share < floor => Weak,
            var share when share >= HealthyFrom => Healthy,
            _ => Mixed,
        };
}
