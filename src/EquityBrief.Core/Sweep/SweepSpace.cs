using System.Globalization;

namespace EquityBrief.Core.Sweep;

// The kinds of dial the second stage searches over: the nine dials with the stop's bounds split into two ordered
// ones, and the nine dials the seven conditions make.
public enum DialKind
{
    Strength,
    DepthLow,
    DepthHigh,
    DryUp,
    Freshness,
    RewardToRisk,
    StopLow,
    StopHigh,
    Market,
    Band,
    High,
    Sector,
    Volume,
    Rsi,
    BeatWindow,
    BeatSize,
    Tightness,
    Length,
    Gap,
}

// One dial of the search: its kind, the part of the filter it belongs to, every value it can take in order with
// the tested ones inside a range the search may widen by looking beyond an end, and which index, if any, is the
// dial off.
public sealed class SweepDial
{
    public required DialKind Kind { get; init; }

    public required string Name { get; init; }

    public required string Part { get; init; }

    public required IReadOnlyList<string> Labels { get; init; }

    public required int TestedLow { get; init; }

    public required int TestedHigh { get; init; }

    public int Off { get; init; } = -1;

    public int Count => Labels.Count;

    // Whether the grid limits depth at an end: not beyond off, and not on a dial of two tested values.
    public bool Exempt => TestedHigh - TestedLow + 1 <= 2;

    public bool IsCondition => Kind >= DialKind.High;
}

// The space the second stage searches: every dial with its current range, a point being one index a dial. The
// range starts at each dial's tested values and widens where the search looks beyond an end. Built once per
// design over the conditions that survived, and mutable only by widening.
// see: A starting point is proposed from the deepest setting of a plateau on the edge and never its best variation, and nothing is registered before the operator approves it
public sealed class SweepSpace
{
    // How far beyond a grid end a dial of three or more values is looked, in values.
    public const int Beyond = 2;

    readonly List<SweepDial> dials;
    readonly int[] low;
    readonly int[] high;

    public SweepGrid Grid { get; }

    public IReadOnlyList<int> ConditionsOn { get; }

    SweepSpace(SweepGrid grid, IReadOnlyList<int> conditionsOn, List<SweepDial> dials)
    {
        Grid = grid;
        ConditionsOn = conditionsOn;
        this.dials = dials;
        low = [.. dials.Select(dial => dial.TestedLow)];
        high = [.. dials.Select(dial => dial.TestedHigh)];
    }

    public IReadOnlyList<SweepDial> Dials => dials;

    public int Count => dials.Count;

    public int Low(int dial) => low[dial];

    public int High(int dial) => high[dial];

    // The space over the extended grid and the conditions given, by number from 1, in order.
    public static SweepSpace For(IEnumerable<int> conditionsOn, SweepGrid? grid = null)
    {
        var over = grid ?? SweepGrid.Extended;
        var fine = SweepGrid.Fine;
        var on = conditionsOn.Distinct().Order().ToArray();
        var dials = new List<SweepDial>();

        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        void Ordered(DialKind kind, string name, string part, IReadOnlyList<double> all, IReadOnlyList<double> tested, Func<double, string> label, int off = -1)
        {
            dials.Add(new SweepDial
            {
                Kind = kind,
                Name = name,
                Part = part,
                Labels = [.. all.Select(label)],
                TestedLow = SweepGrid.IndexOf(all, tested.Min()),
                TestedHigh = SweepGrid.IndexOf(all, tested.Max()),
                Off = off,
            });
        }

        Ordered(DialKind.Strength, "strength", "strength", over.StrengthBars, fine.StrengthBars, Number);
        Ordered(DialKind.DepthLow, "depth low", "pullback", over.DepthLows, fine.DepthLows, Number);
        Ordered(DialKind.DepthHigh, "depth high", "pullback", over.DepthHighs, fine.DepthHighs, Number);
        Ordered(DialKind.DryUp, "dry-up", "pullback", over.DryUpCeilings, fine.DryUpCeilings, value => double.IsPositiveInfinity(value) ? "off" : Number(value), SweepGrid.IndexOf(over.DryUpCeilings, SweepGrid.Off));
        Ordered(DialKind.Freshness, "freshness", "trigger", [.. over.Freshness.Select(value => (double)value)], [.. fine.Freshness.Select(value => (double)value)], value => Number(value));
        Ordered(DialKind.RewardToRisk, "reward to risk", "trade", over.RewardToRiskFloors, fine.RewardToRiskFloors, Number);

        var lows = over.StopBounds.Select(bounds => bounds.Low).Distinct().Order().ToArray();
        var highs = over.StopBounds.Select(bounds => bounds.High).Distinct().Order().ToArray();

        Ordered(DialKind.StopLow, "nearest stop", "trade", lows, lows, Number);
        Ordered(DialKind.StopHigh, "farthest stop", "trade", highs, highs, Number);
        Ordered(DialKind.Market, "market", "market", over.MarketFloors, fine.MarketFloors, value => double.IsNegativeInfinity(value) ? "off" : Number(value * 100) + "%", SweepGrid.IndexOf(over.MarketFloors, SweepGrid.MarketOff));
        Ordered(DialKind.Band, "band strength", "pullback", [.. over.BandStrengths.Select(value => (double)value)], [.. fine.BandStrengths.Select(value => (double)value)], value => Number(value));

        void Condition(DialKind kind, string name, IReadOnlyList<string> labels, int tested)
        {
            dials.Add(new SweepDial
            {
                Kind = kind,
                Name = name,
                Part = "condition",
                Labels = ["off", .. labels],
                TestedLow = 0,
                TestedHigh = tested,
                Off = 0,
            });
        }

        foreach (var condition in on)
        {
            switch (condition)
            {
                case 1: Condition(DialKind.High, "52-week high", [.. SweepConditions.AllHighRatios.Select(Number)], SweepConditions.HighRatios.Count); break;
                case 2: Condition(DialKind.Sector, "sector rank", [.. SweepConditions.AllSectorTops.Select(value => "top " + value.ToString(CultureInfo.InvariantCulture))], SweepConditions.SectorTops.Count); break;
                case 3: Condition(DialKind.Volume, "turn-up volume", [.. SweepConditions.AllTurnVolumes.Select(Number)], SweepConditions.TurnVolumes.Count); break;
                case 4: Condition(DialKind.Rsi, "RSI reset", [.. SweepConditions.AllRsiLevels.Select(Number)], SweepConditions.RsiLevels.Count); break;
                case 5:
                    Condition(DialKind.BeatWindow, "beat window", [.. SweepConditions.AllBeatWindows.Select(value => value.ToString(CultureInfo.InvariantCulture))], SweepConditions.BeatWindows.Count);
                    dials.Add(new SweepDial
                    {
                        Kind = DialKind.BeatSize,
                        Name = "beat size",
                        Part = "condition",
                        Labels = [.. SweepConditions.BeatSizes.Select(value => value == 0 ? "any" : Number(value) + "%")],
                        TestedLow = 0,
                        TestedHigh = SweepConditions.BeatSizes.Count - 1,
                    });
                    break;
                case 6: Condition(DialKind.Tightness, "tightness", [.. SweepConditions.AllTightness.Select(Number)], SweepConditions.Tightness.Count); break;
                case 7:
                    Condition(DialKind.Length, "pullback length", [.. SweepConditions.AllPullbackLengths.Select(value => value.ToString(CultureInfo.InvariantCulture))], SweepConditions.PullbackLengths.Count);
                    Condition(DialKind.Gap, "gap inside", [.. SweepConditions.AllGapMoves.Select(Number)], SweepConditions.GapMoves.Count);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(conditionsOn), condition, "no such condition");
            }
        }

        return new SweepSpace(over, on, dials);
    }

    public int IndexOf(DialKind kind) => dials.FindIndex(dial => dial.Kind == kind);

    // The settings a point stands for: the nine dials, the stop read off its two, and the conditions.
    public DialSetting Setting(ReadOnlySpan<int> point)
    {
        var lows = Grid.StopBounds.Select(bounds => bounds.Low).Distinct().Order().ToArray();
        var highs = Grid.StopBounds.Select(bounds => bounds.High).Distinct().Order().ToArray();
        var stop = Grid.StopBounds.ToList().IndexOf((lows[point[(int)DialKind.StopLow]], highs[point[(int)DialKind.StopHigh]]));

        return new DialSetting(
            point[(int)DialKind.Strength],
            point[(int)DialKind.DepthLow],
            point[(int)DialKind.DepthHigh],
            point[(int)DialKind.DryUp],
            point[(int)DialKind.Freshness],
            point[(int)DialKind.RewardToRisk],
            stop,
            point[(int)DialKind.Market],
            point[(int)DialKind.Band]);
    }

    public ConditionSetting Conditions(ReadOnlySpan<int> point)
    {
        var setting = ConditionSetting.Off;

        for (var dial = (int)DialKind.Band + 1; dial < dials.Count; dial++)
        {
            var at = point[dial];

            setting = dials[dial].Kind switch
            {
                DialKind.High => setting with { High = at - 1 },
                DialKind.Sector => setting with { Sector = at - 1 },
                DialKind.Volume => setting with { Volume = at - 1 },
                DialKind.Rsi => setting with { Rsi = at - 1 },
                DialKind.BeatWindow => setting with { BeatWindow = at - 1 },
                DialKind.BeatSize => setting with { BeatSize = at },
                DialKind.Tightness => setting with { Tightness = at - 1 },
                DialKind.Length => setting with { Length = at - 1 },
                _ => setting with { Gap = at - 1 },
            };
        }

        return setting;
    }

    // A point from a setting of the grid and a condition setting, the conditions the space does not hold left
    // off; refused where the setting switches on a condition the space does not hold.
    public int[] Point(DialSetting setting, ConditionSetting conditions)
    {
        var point = new int[dials.Count];
        var (low, high) = Grid.StopBounds[setting.Stop];
        var lows = Grid.StopBounds.Select(bounds => bounds.Low).Distinct().Order().ToArray();
        var highs = Grid.StopBounds.Select(bounds => bounds.High).Distinct().Order().ToArray();

        point[(int)DialKind.Strength] = setting.Strength;
        point[(int)DialKind.DepthLow] = setting.DepthLow;
        point[(int)DialKind.DepthHigh] = setting.DepthHigh;
        point[(int)DialKind.DryUp] = setting.DryUp;
        point[(int)DialKind.Freshness] = setting.Freshness;
        point[(int)DialKind.RewardToRisk] = setting.RewardToRisk;
        point[(int)DialKind.StopLow] = Array.IndexOf(lows, low);
        point[(int)DialKind.StopHigh] = Array.IndexOf(highs, high);
        point[(int)DialKind.Market] = setting.Market;
        point[(int)DialKind.Band] = setting.Band;

        var held = new HashSet<int>(conditions.On);

        for (var dial = (int)DialKind.Band + 1; dial < dials.Count; dial++)
        {
            point[dial] = dials[dial].Kind switch
            {
                DialKind.High => conditions.High + 1,
                DialKind.Sector => conditions.Sector + 1,
                DialKind.Volume => conditions.Volume + 1,
                DialKind.Rsi => conditions.Rsi + 1,
                DialKind.BeatWindow => conditions.BeatWindow + 1,
                DialKind.BeatSize => conditions.BeatSize,
                DialKind.Tightness => conditions.Tightness + 1,
                DialKind.Length => conditions.Length + 1,
                _ => conditions.Gap + 1,
            };

            held.Remove(ConditionOf(dials[dial].Kind));
        }

        if (held.Count > 0)
        {
            throw new InvalidOperationException($"The space does not hold condition {held.Min()}, which the setting switches on.");
        }

        return point;
    }

    public static int ConditionOf(DialKind kind) => kind switch
    {
        DialKind.High => 1,
        DialKind.Sector => 2,
        DialKind.Volume => 3,
        DialKind.Rsi => 4,
        DialKind.BeatWindow or DialKind.BeatSize => 5,
        DialKind.Tightness => 6,
        DialKind.Length or DialKind.Gap => 7,
        _ => 0,
    };

    // The live rule's point: its fine settings, every condition off.
    public int[] LivePoint() => Point(Grid.Carry(SweepGrid.Fine, DialSetting.LiveOnFine), ConditionSetting.Off);

    public bool Inside(ReadOnlySpan<int> point)
    {
        for (var dial = 0; dial < dials.Count; dial++)
        {
            if (point[dial] < low[dial] || point[dial] > high[dial])
            {
                return false;
            }
        }

        return true;
    }

    // A move that changes no reading is not a step: the beat's size while the beat's window is off.
    public bool IsNoOp(ReadOnlySpan<int> point, int dial)
    {
        if (dials[dial].Kind != DialKind.BeatSize)
        {
            return false;
        }

        var window = IndexOf(DialKind.BeatWindow);

        return window >= 0 && point[window] == dials[window].Off;
    }

    // The point one or more steps along a dial, or none where it leaves the dial's current range.
    public int[]? Step(ReadOnlySpan<int> point, int dial, int steps)
    {
        var moved = point[dial] + steps;

        if (moved < low[dial] || moved > high[dial])
        {
            return null;
        }

        var next = point.ToArray();

        next[dial] = moved;

        return next;
    }

    // Whether a dial's end in a direction is one the grid limits depth at: a dial of three or more tested values
    // whose value there is not off and beyond which the dial holds a value the search has not opened. Beyond
    // off, at either end of a two-value dial, and at a dial's own end, beyond which the rule allows no value,
    // the grid does not limit depth.
    public bool LimitsAt(int dial, int direction)
    {
        var one = dials[dial];

        if (one.Exempt)
        {
            return false;
        }

        var end = direction < 0 ? low[dial] : high[dial];

        return end != one.Off && (direction < 0 ? end > 0 : end < one.Count - 1);
    }

    // Whether the dial holds values beyond its current end in a direction, which is what limits depth there.
    public bool CanExtend(int dial, int direction) => LimitsAt(dial, direction);

    // Whether a point sits at a dial's own end in a direction: the last value the rule allows, not off, on a
    // dial of three or more values. The search names such an end as a limit where the proposal sits on it.
    public bool AtTheRulesEnd(ReadOnlySpan<int> point, int dial, int direction)
    {
        var one = dials[dial];

        if (one.Exempt || point[dial] == one.Off)
        {
            return false;
        }

        return direction < 0 ? point[dial] == 0 : point[dial] == one.Count - 1;
    }

    // Widens the dial's range by up to two values beyond its end, and returns the indexes opened.
    public IReadOnlyList<int> Extend(int dial, int direction)
    {
        var opened = new List<int>();

        for (var step = 0; step < Beyond && CanExtend(dial, direction); step++)
        {
            if (direction < 0)
            {
                low[dial]--;
                opened.Add(low[dial]);
            }
            else
            {
                high[dial]++;
                opened.Add(high[dial]);
            }
        }

        return opened;
    }

    // The settings the current ranges hold, as a count.
    public double Size()
    {
        var size = 1d;

        for (var dial = 0; dial < dials.Count; dial++)
        {
            size *= high[dial] - low[dial] + 1;
        }

        return size;
    }

    // Steps between two points, summed over the dials.
    public static int Distance(ReadOnlySpan<int> one, ReadOnlySpan<int> other)
    {
        var distance = 0;

        for (var dial = 0; dial < one.Length; dial++)
        {
            distance += Math.Abs(one[dial] - other[dial]);
        }

        return distance;
    }

    public string Label(int dial, int index) => dials[dial].Labels[index];

    public string Describe(ReadOnlySpan<int> point)
    {
        var parts = new List<string>();

        for (var dial = 0; dial < dials.Count; dial++)
        {
            parts.Add($"{dials[dial].Name} {dials[dial].Labels[point[dial]]}");
        }

        return string.Join(", ", parts);
    }

    // The one dial two points differ on, and the move in plain words; none where they differ on none or on more.
    public (int Dial, string Part, string Change)? Moved(ReadOnlySpan<int> from, ReadOnlySpan<int> to)
    {
        var moved = -1;

        for (var dial = 0; dial < dials.Count; dial++)
        {
            if (from[dial] != to[dial])
            {
                if (moved >= 0)
                {
                    return null;
                }

                moved = dial;
            }
        }

        if (moved < 0)
        {
            return null;
        }

        return (moved, dials[moved].Part, $"the {dials[moved].Name} from {dials[moved].Labels[from[moved]]} to {dials[moved].Labels[to[moved]]}");
    }

    public static string Key(ReadOnlySpan<int> point)
    {
        var text = new System.Text.StringBuilder(point.Length * 3);

        for (var dial = 0; dial < point.Length; dial++)
        {
            if (dial > 0)
            {
                text.Append(',');
            }

            text.Append(point[dial].ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    public static int[] Parse(string key) => [.. key.Split(',').Select(part => int.Parse(part, CultureInfo.InvariantCulture))];
}
