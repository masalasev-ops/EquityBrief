using System.Globalization;

namespace EquityBrief.Core.Sweep;

// The sweep's structural axes, each a choice in the filter's design rather than a dial on it.
public enum StrengthMeasure
{
    // The mean of a member's two places among the members' returns over 63 and 126 sessions, the live measure.
    ThreeAndSixMonths,

    // The place of the return over 126 sessions alone.
    SixMonths,

    // The place of the return from 252 sessions back to 21 sessions back, the latest month left out.
    TwelveLessOne,
}

public enum UptrendRule
{
    // The trend classifier at the live rule and at each of its three versions.
    Classifier,
    ClassifierBelowBoth,
    ClassifierBelowBothUnderACross,
    ClassifierHoldsTwoNights,

    // Three rules the classifier is not: the close above the 200-day average, the 50-day above the 200-day,
    // and the 200-day above itself twenty sessions before.
    CloseAboveTwoHundred,
    FiftyAboveTwoHundred,
    RisingTwoHundred,
}

public enum SupportKind
{
    // A support band holding the close with a member that is not a moving average, the live setup's band.
    AnchoredBand,

    // Any support band holding the close.
    AnyBand,

    // The 20-day or the 50-day average itself, the close within half a typical move either side of it.
    Average,
}

public enum TriggerKind
{
    // A close above the previous session's high, the live trigger.
    AbovePreviousHigh,

    // A close above the previous session's close.
    AbovePreviousClose,

    // A close in the top quarter of the session's own range.
    TopQuarterOfRange,
}

public enum PlanRule
{
    // The ladder's first tranche, entered in its zone.
    Ladder,

    // Entered at the close, stopped at the setup band's low edge and won at the nearest band above.
    NearestBands,

    // Section 10's plan: the band beneath where the setup band's edge is under a typical move, and the target
    // the lowest band two typical moves or more above. The live plan.
    Clear,
}

// One structural design: every structural choice, the exit among them.
public readonly record struct SweepDesign(
    StrengthMeasure Strength,
    UptrendRule Uptrend,
    SupportKind Support,
    int ReferenceHigh,
    TriggerKind Trigger,
    PlanRule Plan,
    int Hold,
    bool BreakEven,
    int EarningsWindow)
{
    // The live rule's own design: the live strength measure, the live classifier, anchored bands, the high of
    // the last 20 sessions, a close above the previous session's high, section 10's plan held up to 63
    // sessions with no move to break-even, and a 15-session earnings window.
    public static SweepDesign Live { get; } = new(
        StrengthMeasure.ThreeAndSixMonths,
        UptrendRule.Classifier,
        SupportKind.AnchoredBand,
        20,
        TriggerKind.AbovePreviousHigh,
        PlanRule.Clear,
        63,
        false,
        15);

    // The selection design, the exit set aside: every exit of one selection design is read off one forward walk.
    public SweepDesign Selection => this with { Hold = SweepAxes.Holds[^1], BreakEven = false };

    public int ExitIndex => SweepAxes.ExitIndex(Hold, BreakEven);

    public string Key => string.Join(
        "|",
        Strength,
        Uptrend,
        Support,
        ReferenceHigh.ToString(CultureInfo.InvariantCulture),
        Trigger,
        Plan,
        Hold.ToString(CultureInfo.InvariantCulture),
        BreakEven ? "breakeven" : "fixed",
        EarningsWindow.ToString(CultureInfo.InvariantCulture));

    // A design prints as its key. The printing a record writes for itself reads every property, the selection
    // among them, which is a design whose selection is a design again, and never ends.
    public override string ToString() => Key;
}

// The axes, the dials and the grids each stage reads them over, stated once for the runner, the tests and the
// report alike.
public static class SweepAxes
{
    public static IReadOnlyList<int> ReferenceHighs { get; } = [10, 20, 50];

    // The hold caps, each a session count, and the earnings windows, 0 meaning no earnings exclusion at all.
    public static IReadOnlyList<int> Holds { get; } = [10, 20, 40, 63];

    public static IReadOnlyList<int> EarningsWindows { get; } = [0, 5, 10, 15];

    // The rising 200-day's span, a month of sessions: the average above its own value that many sessions before.
    public const int RisingSpan = 20;

    // The sessions the 12-less-1 measure reads back to, and the month it leaves out.
    public const int TwelveMonthSessions = 252;

    public const int LatestMonthSessions = 21;

    // An exit's index among the eight: the hold's place, then the break-even move off and on.
    public static int ExitIndex(int hold, bool breakEven)
    {
        var at = Holds.ToList().IndexOf(hold);

        return at < 0 ? throw new ArgumentOutOfRangeException(nameof(hold), hold, "no such hold") : (at * 2) + (breakEven ? 1 : 0);
    }

    public static int Exits => Holds.Count * 2;

    public static (int Hold, bool BreakEven) ExitOf(int index) => (Holds[index / 2], index % 2 == 1);

    // Whether the classifier-free uptrend rules can carry the ladder, which is built from the classifier's label.
    public static bool Expressible(SweepDesign design) =>
        design.Plan != PlanRule.Ladder || design.Uptrend is UptrendRule.Classifier or UptrendRule.ClassifierBelowBoth or UptrendRule.ClassifierBelowBothUnderACross or UptrendRule.ClassifierHoldsTwoNights;

    // Every design the grid holds, in a fixed order, the inexpressible ones left out.
    public static IReadOnlyList<SweepDesign> Designs(bool selectionOnly)
    {
        var designs = new List<SweepDesign>();

        foreach (var strength in Enum.GetValues<StrengthMeasure>())
        foreach (var uptrend in Enum.GetValues<UptrendRule>())
        foreach (var support in Enum.GetValues<SupportKind>())
        foreach (var high in ReferenceHighs)
        foreach (var trigger in Enum.GetValues<TriggerKind>())
        foreach (var plan in Enum.GetValues<PlanRule>())
        foreach (var earnings in EarningsWindows)
        {
            var selection = new SweepDesign(strength, uptrend, support, high, trigger, plan, Holds[^1], false, earnings);

            if (!Expressible(selection))
            {
                continue;
            }

            if (selectionOnly)
            {
                designs.Add(selection);
                continue;
            }

            for (var exit = 0; exit < Exits; exit++)
            {
                var (hold, breakEven) = ExitOf(exit);

                designs.Add(selection with { Hold = hold, BreakEven = breakEven });
            }
        }

        return designs;
    }

    // The count of every structural combination, the expressible ones among them.
    public static int AllCombinations =>
        Enum.GetValues<StrengthMeasure>().Length
        * Enum.GetValues<UptrendRule>().Length
        * Enum.GetValues<SupportKind>().Length
        * ReferenceHighs.Count
        * Enum.GetValues<TriggerKind>().Length
        * Enum.GetValues<PlanRule>().Length
        * Exits
        * EarningsWindows.Count;
}

// The nine dials, each with the levels a stage reads it at. A level of `double.PositiveInfinity` on the dry-up
// ceiling, and of `double.NegativeInfinity` on the market floor, is the dial off.
public sealed record SweepGrid(
    IReadOnlyList<double> StrengthBars,
    IReadOnlyList<double> DepthLows,
    IReadOnlyList<double> DepthHighs,
    IReadOnlyList<double> DryUpCeilings,
    IReadOnlyList<int> Freshness,
    IReadOnlyList<double> RewardToRiskFloors,
    IReadOnlyList<(double Low, double High)> StopBounds,
    IReadOnlyList<double> MarketFloors,
    IReadOnlyList<int> BandStrengths)
{
    public const double Off = double.PositiveInfinity;

    public const double MarketOff = double.NegativeInfinity;

    // Stage 1's three coarse values of each dial.
    public static SweepGrid Coarse { get; } = new(
        [0.40, 0.50, 0.67],
        [0.5, 1, 2],
        [3, 5, 8],
        [1.0, 1.5, Off],
        [1, 3, 8],
        [1.0, 1.5, 2.5],
        [(0.5, 4), (1, 2.5), (0.5, 2.5)],
        [MarketOff, 0.45, 0.55],
        [0, 2, 4]);

    // Stage 2's fine grid.
    public static SweepGrid Fine { get; } = new(
        [0.30, 0.40, 0.50, 0.60, 0.67, 0.75, 0.85],
        [0.5, 1, 1.5, 2],
        [3, 4, 5, 6, 8],
        [0.8, 1.0, 1.25, 1.5, 2.0, Off],
        [1, 2, 3, 5, 8],
        [1.0, 1.25, 1.5, 2.0, 2.5, 3.0],
        [(0.5, 4), (1, 2.5), (0.5, 2.5), (1, 4)],
        [MarketOff, 0.40, 0.45, 0.50, 0.55],
        [0, 2, 4, 6]);

    // The fine grid with up to two values beyond each end the rule allows, at the spacing of the last two tested,
    // for the search to look beyond a grid end the proposal's depth runs into: a strength place is at most 1, no
    // depth is shallower than half a move, a trigger cannot be fresher than tonight, a band's least strength has
    // no value under nought, and the dry-up's and the market's other ends are off. The depth's high end and the
    // freshness hold two values more, to 16 moves and 21 sessions, which an index's search looks at a second time
    // beyond. The stop's two bounds hold two values each and are not extended. The tested range of each dial is the
    // fine grid's own.
    public static SweepGrid Extended { get; } = new(
        [0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.67, 0.75, 0.85, 0.95, 1.0],
        [0.5, 1, 1.5, 2, 2.5, 3],
        [2, 3, 4, 5, 6, 8, 10, 12, 14, 16],
        [0.4, 0.6, 0.8, 1.0, 1.25, 1.5, 2.0, Off],
        [1, 2, 3, 5, 8, 11, 14, 17, 21],
        [0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0],
        [(0.5, 4), (1, 2.5), (0.5, 2.5), (1, 4)],
        [MarketOff, 0.40, 0.45, 0.50, 0.55, 0.60, 0.65],
        [0, 2, 4, 6, 8, 10]);

    // The dials a cumulative table runs over besides the stop bounds, whose options it is read at one by one.
    public const int OrderedDials = 8;

    // The index of a value on one of this grid's dials, and -1 where the dial does not hold it.
    public static int IndexOf(IReadOnlyList<double> levels, double value)
    {
        for (var at = 0; at < levels.Count; at++)
        {
            if (levels[at] == value || (double.IsInfinity(levels[at]) && double.IsInfinity(value) && Math.Sign(levels[at]) == Math.Sign(value)))
            {
                return at;
            }
        }

        return -1;
    }

    public static int IndexOf(IReadOnlyList<int> levels, int value) => levels.ToList().IndexOf(value);

    // A setting of another grid carried onto this one, every value looked up by its own; refused where a value
    // is not held.
    public DialSetting Carry(SweepGrid from, DialSetting setting)
    {
        int Held(int at) => at < 0 ? throw new InvalidOperationException("The grid does not hold a value of the setting carried onto it.") : at;

        var (low, high) = from.StopBounds[setting.Stop];

        return new DialSetting(
            Held(IndexOf(StrengthBars, from.StrengthBars[setting.Strength])),
            Held(IndexOf(DepthLows, from.DepthLows[setting.DepthLow])),
            Held(IndexOf(DepthHighs, from.DepthHighs[setting.DepthHigh])),
            Held(IndexOf(DryUpCeilings, from.DryUpCeilings[setting.DryUp])),
            Held(IndexOf(Freshness, from.Freshness[setting.Freshness])),
            Held(IndexOf(RewardToRiskFloors, from.RewardToRiskFloors[setting.RewardToRisk])),
            Held(StopBounds.ToList().IndexOf((low, high))),
            Held(IndexOf(MarketFloors, from.MarketFloors[setting.Market])),
            Held(IndexOf(BandStrengths, from.BandStrengths[setting.Band])));
    }

    public IReadOnlyList<int> Sizes =>
    [
        StrengthBars.Count,
        DepthLows.Count,
        DepthHighs.Count,
        DryUpCeilings.Count,
        Freshness.Count,
        RewardToRiskFloors.Count,
        MarketFloors.Count,
        BandStrengths.Count,
    ];

    public int CellsPerStop => Sizes.Aggregate(1, (product, size) => product * size);

    public int Variations => CellsPerStop * StopBounds.Count;

    // The loosest setting of every dial, which no candidate outside can pass under any setting of the grid.
    public double LoosestStrength => StrengthBars.Min();

    public double LoosestDepthLow => DepthLows.Min();

    public double LoosestDepthHigh => DepthHighs.Max();

    public int LoosestFreshness => Freshness.Max();

    public double LoosestRewardToRisk => RewardToRiskFloors.Min();

    public double LoosestStopLow => StopBounds.Min(bounds => bounds.Low);

    public double LoosestStopHigh => StopBounds.Max(bounds => bounds.High);
}

// One setting of the nine dials, as indexes into a grid's levels.
public readonly record struct DialSetting(
    int Strength,
    int DepthLow,
    int DepthHigh,
    int DryUp,
    int Freshness,
    int RewardToRisk,
    int Stop,
    int Market,
    int Band)
{
    // The live filter's settings on the coarse grid: strength 0.50, depth 1 to 5, dry-up under 1.5, freshness 3,
    // reward to risk 1.5, the stop 0.5 to 4 typical moves, the market at 45% and band strength 0.
    public static DialSetting LiveOnCoarse { get; } = new(1, 1, 1, 1, 1, 1, 0, 1, 0);

    // The same settings on the fine grid, which holds every one of them.
    public static DialSetting LiveOnFine { get; } = new(2, 1, 2, 3, 2, 2, 0, 2, 0);

    // The cell this setting reads in a table of the grid's ordered dials.
    public int Cell(SweepGrid grid)
    {
        var sizes = grid.Sizes;
        int[] at = [Strength, DepthLow, DepthHigh, DryUp, Freshness, RewardToRisk, Market, Band];
        var cell = 0;

        for (var dial = 0; dial < sizes.Count; dial++)
        {
            cell = (cell * sizes[dial]) + at[dial];
        }

        return cell;
    }

    public static DialSetting Of(SweepGrid grid, int stop, int cell)
    {
        var sizes = grid.Sizes;
        var at = new int[sizes.Count];

        for (var dial = sizes.Count - 1; dial >= 0; dial--)
        {
            at[dial] = cell % sizes[dial];
            cell /= sizes[dial];
        }

        return new DialSetting(at[0], at[1], at[2], at[3], at[4], at[5], stop, at[6], at[7]);
    }

    public string Describe(SweepGrid grid)
    {
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        var dryUp = grid.DryUpCeilings[DryUp];
        var market = grid.MarketFloors[Market];

        return string.Join(
            ", ",
            $"strength {Number(grid.StrengthBars[Strength])}",
            $"depth {Number(grid.DepthLows[DepthLow])} to {Number(grid.DepthHighs[DepthHigh])}",
            double.IsPositiveInfinity(dryUp) ? "no dry-up ceiling" : $"dry-up under {Number(dryUp)}",
            $"fresh within {grid.Freshness[Freshness]}",
            $"reward to risk {Number(grid.RewardToRiskFloors[RewardToRisk])}",
            $"stop {Number(grid.StopBounds[Stop].Low)} to {Number(grid.StopBounds[Stop].High)}",
            double.IsNegativeInfinity(market) ? "market off" : $"market {Number(market * 100)}%",
            $"band strength {grid.BandStrengths[Band]}");
    }
}
