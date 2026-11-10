namespace EquityBrief.Core.Loop;

// A series' 2-session RSI as the dip reads it, NaN where it reads none, which the dip's own sale under a plan reads. It reads
// through the dip's own code, so the dip's rules pin it and no other rule does.
// see: The buy and sell points sell the dip at the next open after its 2-session RSI closes above 65
public static class PlanRsi
{
    public static double[] Of(IReadOnlyList<double> closes) =>
        [.. Families.DipRule.Rsi(closes, Families.DipRule.RsiSessions).Select(one => one ?? double.NaN)];
}
