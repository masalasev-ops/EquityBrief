namespace EquityBrief.Core.History;

// One setup as the sector splits read it: its index and family, its era, whether it settled and reached its target, its
// edge after costs, and its sector fund's 5-day change, the fund against its 20-day average and its sector's third among
// the sectors by its members' 63-day mean return on its session, each none where its sector or the series reads none.
public sealed record SplitSetup(string Index, string Family, string Era, bool Settled, bool Target, double? EdgeAfterCost, string? Change, string? Average, string? Rank);

// One side of one split: its settled trades, those at target and their mean edge after costs.
public sealed record SplitSide(int Trades, int Targets, double? Edge);

// The sector splits: each family's setups split three ways by their sector on their session as it stood, read-only, and the
// one variant the plan names, the pullback with its sector below its 20-day average, which goes to the operator only where
// it holds in every era over the three indices together with enough trades on each side.
// see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
public static class SectorSplits
{
    public const int ChangeDays = 5;

    public const int AverageDays = 20;

    public const int ReturnDays = 63;

    // The trades each side of the variant's split holds at least in every era, the three indices together.
    public const int SideTrades = 50;

    public const string Up = "up";
    public const string Down = "down";
    public const string Above = "above";
    public const string Below = "below";
    public const string Top = "top third";
    public const string Middle = "middle third";
    public const string Bottom = "bottom third";

    // The fund's change over the 5 sessions to the session: up where its close stands above the close 5 sessions before;
    // none where the series holds no close there.
    public static string? Change(IReadOnlyList<double?> closes, int at) =>
        at >= ChangeDays && closes[at] is { } now && closes[at - ChangeDays] is { } then && then > 0
            ? now > then ? Up : Down
            : null;

    // The fund against its 20-day average: above where its close stands over the mean of the 20 closes to the session; none
    // where any of them is missing.
    public static string? Average(IReadOnlyList<double?> closes, int at)
    {
        if (at < AverageDays - 1 || closes[at] is not { } now)
        {
            return null;
        }

        var sum = 0.0;

        for (var back = 0; back < AverageDays; back++)
        {
            if (closes[at - back] is not { } close)
            {
                return null;
            }

            sum += close;
        }

        return now > sum / AverageDays ? Above : Below;
    }

    // A sector's third among those ranked on a session, its place counted from the best mean return, 0 the best.
    public static string Third(int place, int ranked) =>
        place * 3 < ranked ? Top : place * 3 >= 2 * ranked ? Bottom : Middle;

    // A member's return over the 63 sessions to the session; none where either close is missing.
    public static double? Return(IReadOnlyList<double?> closes, int at) =>
        at >= ReturnDays && closes[at] is { } now && closes[at - ReturnDays] is { } then && then > 0 ? (now / then) - 1 : null;

    // One side of a split over the setups given: the settled ones, those at target and their mean edge after costs.
    public static SplitSide Side(IEnumerable<SplitSetup> setups)
    {
        var settled = setups.Where(setup => setup.Settled).ToArray();
        var edges = settled.Where(setup => setup.EdgeAfterCost is not null).Select(setup => setup.EdgeAfterCost!.Value).ToArray();

        return new SplitSide(settled.Length, settled.Count(setup => setup.Target), edges.Length > 0 ? edges.Average() : null);
    }

    // Whether the pullback with its sector below its 20-day average holds as a variant: in every era over the three
    // indices together, both sides hold at least the trades a side and the side below stands above the side above.
    public static bool VariantHolds(IReadOnlyList<(string Era, SplitSide Below, SplitSide Above)> eras) =>
        eras.Count > 0 && eras.All(era =>
            era.Below.Trades >= SideTrades && era.Above.Trades >= SideTrades
            && era.Below.Edge is { } below && era.Above.Edge is { } above && below > above);
}
