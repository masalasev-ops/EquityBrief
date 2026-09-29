using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Swings;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Worker.Sweep;

// One plan's answers under the eight exits: its reward to risk and stop distance, and under each exit whether it
// won, lost, ran out of sessions or was never entered, the bar its own paths set, the break-even it planned, and
// what the trade came to in multiples of the risk it planned.
public sealed class SweepPlanOutcomes
{
    public const byte Immature = 0;
    public const byte Win = 1;
    public const byte Loss = 2;
    public const byte Unresolved = 3;
    public const byte NeverEntered = 4;

    public double RewardToRisk;
    public double StopMoves;
    public readonly byte[] Code = new byte[8];
    public readonly float[] Null = new float[8];
    public readonly float[] BreakEven = new float[8];
    public readonly float[] Multiple = new float[8];
}

// One member-session some setting of the grid could list: every reading each axis chooses between, and each plan
// with its outcomes. A member-session no setting of either grid could list is not kept, since no variation's
// count reads it.
public sealed class SweepCandidate
{
    public int Name;
    public int Session;
    public int Year;
    public int Block;
    public double Breadth = double.NaN;
    public byte Uptrend;
    public readonly double[] Strength = [double.NaN, double.NaN, double.NaN];
    public readonly double[] Depth = [double.NaN, double.NaN, double.NaN];
    public readonly double[] DryUp = [double.NaN, double.NaN, double.NaN];

    // The setup band's strength under each kind of support, and -1 where the close sits in none.
    public readonly sbyte[] Band = [-1, -1, -1];

    // The trigger's arrival under each trigger and each support, the sessions back it first fired, and -1 where it
    // did not arrive inside the longest window.
    public readonly sbyte[] Age = [-1, -1, -1, -1, -1, -1, -1, -1, -1];

    // The sessions to the next earnings date on file, and -1 where none is.
    public short Earnings = -1;

    public readonly SweepPlanOutcomes?[] Plans = new SweepPlanOutcomes?[7];

    public static int AgeAt(TriggerKind trigger, SupportKind support) => ((int)trigger * 3) + (int)support;

    public static int PlanAt(PlanRule rule, SupportKind support) => rule switch
    {
        PlanRule.Ladder => 0,
        PlanRule.NearestBands => 1 + (int)support,
        _ => 4 + (int)support,
    };

    public void Write(BinaryWriter writer)
    {
        writer.Write(Name);
        writer.Write(Session);
        writer.Write(Year);
        writer.Write(Block);
        writer.Write(Breadth);
        writer.Write(Uptrend);

        foreach (var values in new[] { Strength, Depth, DryUp })
        {
            foreach (var value in values)
            {
                writer.Write(value);
            }
        }

        foreach (var value in Band)
        {
            writer.Write(value);
        }

        foreach (var value in Age)
        {
            writer.Write(value);
        }

        writer.Write(Earnings);

        foreach (var plan in Plans)
        {
            writer.Write(plan is not null);

            if (plan is null)
            {
                continue;
            }

            writer.Write(plan.RewardToRisk);
            writer.Write(plan.StopMoves);

            for (var exit = 0; exit < 8; exit++)
            {
                writer.Write(plan.Code[exit]);
                writer.Write(plan.Null[exit]);
                writer.Write(plan.BreakEven[exit]);
                writer.Write(plan.Multiple[exit]);
            }
        }
    }

    public static SweepCandidate Read(BinaryReader reader)
    {
        var candidate = new SweepCandidate
        {
            Name = reader.ReadInt32(),
            Session = reader.ReadInt32(),
            Year = reader.ReadInt32(),
            Block = reader.ReadInt32(),
            Breadth = reader.ReadDouble(),
            Uptrend = reader.ReadByte(),
        };

        foreach (var values in new[] { candidate.Strength, candidate.Depth, candidate.DryUp })
        {
            for (var at = 0; at < values.Length; at++)
            {
                values[at] = reader.ReadDouble();
            }
        }

        for (var at = 0; at < candidate.Band.Length; at++)
        {
            candidate.Band[at] = reader.ReadSByte();
        }

        for (var at = 0; at < candidate.Age.Length; at++)
        {
            candidate.Age[at] = reader.ReadSByte();
        }

        candidate.Earnings = reader.ReadInt16();

        for (var at = 0; at < candidate.Plans.Length; at++)
        {
            if (!reader.ReadBoolean())
            {
                continue;
            }

            var plan = new SweepPlanOutcomes { RewardToRisk = reader.ReadDouble(), StopMoves = reader.ReadDouble() };

            for (var exit = 0; exit < 8; exit++)
            {
                plan.Code[exit] = reader.ReadByte();
                plan.Null[exit] = reader.ReadSingle();
                plan.BreakEven[exit] = reader.ReadSingle();
                plan.Multiple[exit] = reader.ReadSingle();
            }

            candidate.Plans[at] = plan;
        }

        return candidate;
    }
}

// Pass three: for each member-session some setting could list, the bands the level builder would have drawn that
// night, each support's setup band, each trigger's arrival over the sessions before, and each plan's outcomes.
public static class SweepCandidates
{
    // One name's bands on one of its bars, as the level builder draws them and as the gates read them.
    sealed record Bands(IReadOnlyList<Level> Levels, IReadOnlyList<FilterBand> Filter);

    public static List<SweepCandidate> For(
        SweepSeries series,
        int name,
        IReadOnlyList<SweepColumns.Session> sessions,
        IReadOnlyList<DateOnly> calendar,
        int firstScored,
        int fromSession,
        int toSession)
    {
        var found = new List<SweepCandidate>();
        var bands = new Dictionary<int, Bands>();
        var grid = SweepGrid.Fine;
        var coarse = SweepGrid.Coarse;
        var loosestStrength = Math.Min(grid.LoosestStrength, coarse.LoosestStrength);
        var highs = SweepAxes.ReferenceHighs.Count;

        Bands BandsAt(int bar)
        {
            if (bands.TryGetValue(bar, out var held))
            {
                return held;
            }

            var levels = LevelsOn(series, bar);

            held = new Bands(levels, [.. levels.Select(level => new FilterBand(level.LowEdge, level.HighEdge, level.Role, level.Strength, level.HasNonAverageAnchor))]);
            bands[bar] = held;

            return held;
        }

        FilterBand? SupportAt(int bar, SupportKind support)
        {
            var close = series.Bars[bar].Close;
            var held = BandsAt(bar).Filter;

            return support switch
            {
                SupportKind.AnchoredBand => SweepReadings.BandHolding(held, close, anchoredOnly: true),
                SupportKind.AnyBand => SweepReadings.BandHolding(held, close, anchoredOnly: false),
                _ => double.IsNaN(series.Atr[bar])
                    ? null
                    : SweepReadings.AverageHolding(held, close, series.Sma20[bar], series.Sma50[bar], Statistic.ToPrice(series.Atr[bar]))?.Zone,
            };
        }

        for (var bar = 0; bar < series.Bars.Length; bar++)
        {
            var session = series.SessionAt[bar];

            if (session < firstScored || session < fromSession || session >= toSession || !series.Member[bar] || series.Gap[bar] || series.Uptrend[bar] == 0)
            {
                continue;
            }

            var cross = sessions[session];
            var strength = new double[3];

            for (var measure = 0; measure < 3; measure++)
            {
                strength[measure] = cross.Strength[measure].TryGetValue(name, out var place) ? place : double.NaN;
            }

            if (!strength.Any(value => value >= loosestStrength))
            {
                continue;
            }

            var deepEnough = Enumerable.Range(0, highs).Any(high =>
                series.Depth[(bar * highs) + high] is var depth && depth >= grid.LoosestDepthLow && depth <= grid.LoosestDepthHigh);

            if (!deepEnough)
            {
                continue;
            }

            var candidate = new SweepCandidate
            {
                Name = name,
                Session = session,
                Year = calendar[session].Year - SweepColumns.FirstScored.Year,
                Block = (session - firstScored) / Blocks.Sessions,
                Breadth = cross.Breadth ?? double.NaN,
                Uptrend = series.Uptrend[bar],
            };

            strength.CopyTo(candidate.Strength, 0);

            for (var high = 0; high < highs; high++)
            {
                candidate.Depth[high] = series.Depth[(bar * highs) + high];
                candidate.DryUp[high] = series.DryUp[(bar * highs) + high];
            }

            var setup = new FilterBand?[3];

            foreach (var support in Enum.GetValues<SupportKind>())
            {
                setup[(int)support] = SupportAt(bar, support);
                candidate.Band[(int)support] = setup[(int)support] is { } band ? (sbyte)Math.Min(band.Strength, sbyte.MaxValue) : (sbyte)-1;
            }

            if (setup.All(band => band is null))
            {
                continue;
            }

            var arrived = false;

            foreach (var trigger in Enum.GetValues<TriggerKind>())
            {
                foreach (var support in Enum.GetValues<SupportKind>())
                {
                    if (setup[(int)support] is null)
                    {
                        continue;
                    }

                    bool? EventAt(int back)
                    {
                        var at = bar - back;

                        return at < 1 ? null : SweepReadings.Event(trigger, series.Bars[at], series.Bars[at - 1], SupportAt(at, support));
                    }

                    var tonight = EventAt(0);
                    var before = Enumerable.Range(1, SweepColumns.LongestWindow).Select(EventAt).ToArray();
                    var (age, _) = SwingGates.Arrival(tonight, before, SweepColumns.LongestWindow);

                    if (age is { } back)
                    {
                        candidate.Age[SweepCandidate.AgeAt(trigger, support)] = (sbyte)back;
                        arrived = true;
                    }
                }
            }

            if (!arrived)
            {
                continue;
            }

            var close = series.Bars[bar].Close;
            var atr = series.Atr[bar];
            var after = series.Bars.Skip(bar + 1).Take(ForwardReturnSeries.SetupSessionCap).Select(one => new ReturnBar(one.Session, one.Close)).ToArray();
            var volatility = NullWin.Volatility(Changes(series.Bars, bar));
            var seed = NullWin.SeedFor(series.Name.Ticker, calendar[session].DayNumber);
            var plans = new SweepPlan?[7];

            if (series.Label[bar] == TrendState.Uptrend && !double.IsNaN(atr))
            {
                plans[0] = LadderPlan(series, bar, BandsAt(bar).Levels);
            }

            foreach (var support in Enum.GetValues<SupportKind>())
            {
                if (setup[(int)support] is not { } band)
                {
                    continue;
                }

                var held = BandsAt(bar).Filter;
                double? move = double.IsNaN(atr) ? null : atr;

                if (support == SupportKind.Average)
                {
                    var average = SweepReadings.AverageHolding(held, close, series.Sma20[bar], series.Sma50[bar], Statistic.ToPrice(atr))!.Value.Average;
                    var stop = PriceForm.Round(average - Statistic.ToPrice(atr), Statistic.Places);

                    plans[SweepCandidate.PlanAt(PlanRule.NearestBands, support)] = SweepReadings.Nearest(held, close, stop, move);
                    plans[SweepCandidate.PlanAt(PlanRule.Clear, support)] = SweepReadings.Clear(held, close, null, stop, move);
                }
                else
                {
                    plans[SweepCandidate.PlanAt(PlanRule.NearestBands, support)] = SweepReadings.Nearest(held, close, band.LowEdge, move);
                    plans[SweepCandidate.PlanAt(PlanRule.Clear, support)] = SweepReadings.Clear(held, close, band, null, move);
                }
            }

            var kept = false;

            for (var at = 0; at < plans.Length; at++)
            {
                if (plans[at] is not { } plan
                    || Statistic.FromRatio(plan.RewardToRisk) < grid.LoosestRewardToRisk
                    || plan.StopMoves < grid.LoosestStopLow
                    || plan.StopMoves > grid.LoosestStopHigh)
                {
                    continue;
                }

                candidate.Plans[at] = Outcomes(plan, after, close, volatility, seed, series.Bars, bar);
                kept = true;
            }

            if (!kept)
            {
                continue;
            }

            candidate.Earnings = SessionsToEarnings(series.Name.Prints, calendar, session);
            found.Add(candidate);
        }

        return found;
    }

    // The level builder's bands on one of a name's bars, over the window ending on it, the averages it holds
    // and the swings confirmed by it, and none where the window is short or the bar holds no typical move.
    public static IReadOnlyList<Level> LevelsOn(SweepSeries series, int bar)
    {
        if (bar + 1 < Core.Volume.VolumeProfileSeries.Window || double.IsNaN(series.Atr[bar]))
        {
            return [];
        }

        var window = series.Bars
            .Skip(bar + 1 - Core.Volume.VolumeProfileSeries.Window)
            .Take(Core.Volume.VolumeProfileSeries.Window)
            .Select(one => new ReplayBar(one.Session, one.High, one.Low, one.Close, one.Volume))
            .ToArray();
        var indicators = new Dictionary<string, double>(StringComparer.Ordinal) { [IndicatorSeries.Atr14] = series.Atr[bar] };

        if (!double.IsNaN(series.Sma20[bar]))
        {
            indicators[IndicatorSeries.Sma20] = series.Sma20[bar];
        }

        if (!double.IsNaN(series.Sma50[bar]))
        {
            indicators[IndicatorSeries.Sma50] = series.Sma50[bar];
        }

        if (!double.IsNaN(series.Sma200[bar]))
        {
            indicators[IndicatorSeries.Sma200] = series.Sma200[bar];
        }

        return SessionReplay.BandsOver(window, indicators, series.Swings.AsSpan(0, series.Confirmed[bar]).ToArray());
    }

    // The ladder's first tranche, as the night's listing keeps it: the zone's middle as the entry, its stop, the
    // first traded target, their reward to risk, and the zone's top edge the fill is admitted under.
    public static SweepPlan? LadderPlan(SweepSeries series, int bar, IReadOnlyList<Level> levels)
    {
        if (levels.Count == 0)
        {
            return null;
        }

        var session = series.Bars[bar].Session;
        var recent = series.Bars
            .Skip(Math.Max(0, bar + 1 - LadderSeries.ConditionLookback))
            .Take(Math.Min(bar + 1, LadderSeries.ConditionLookback))
            .Select(one => new LadderBar(one.Session, one.High, one.Low, one.Close))
            .ToArray();
        var lows = series.Swings
            .AsSpan(0, series.Confirmed[bar])
            .ToArray()
            .Where(swing => swing.Direction == SwingSeries.Low)
            .OrderBy(swing => swing.SessionDate)
            .Select(swing => swing.Price)
            .ToArray();
        var next = series.Name.Prints.FirstOrDefault(date => date >= session);
        var plan = LadderSeries.For(levels, series.Bars[bar].Close, Statistic.ToPrice(series.Atr[bar]), recent, TrendState.Uptrend, lows, next == default ? null : next);
        var first = SessionReplay.FirstTrancheOf(plan);

        if (first.Entry is not { } entry || first.Stop is not { } stop || first.Target is not { } target || first.RewardToRisk is not { } ratio || entry <= stop)
        {
            return null;
        }

        return new SweepPlan(entry, stop, target, ratio, Statistic.FromPrice(entry - stop) / series.Atr[bar], plan.Tranches[0].HighEdge);
    }

    // Each exit's outcome, scored as the live scorer scores a setup, and the calibrated bar its paths set.
    static SweepPlanOutcomes Outcomes(SweepPlan plan, IReadOnlyList<ReturnBar> after, decimal close, double? volatility, int seed, SweepBar[] bars, int bar)
    {
        var outcomes = new SweepPlanOutcomes { RewardToRisk = Statistic.FromRatio(plan.RewardToRisk), StopMoves = plan.StopMoves };

        for (var exit = 0; exit < SweepAxes.Exits; exit++)
        {
            var (hold, breakEven) = SweepAxes.ExitOf(exit);
            var result = breakEven
                ? SweepWalk.OverSetupMovingTheStop(after, plan.Stop, plan.Target, plan.EntryHigh, close, hold)
                : ForwardReturnSeries.OverSetup(after, plan.Stop, plan.Target, plan.EntryHigh, close, null, ForwardReturnSeries.Setup, hold);

            outcomes.Code[exit] = result.Outcome switch
            {
                ForwardReturnSeries.Win => SweepPlanOutcomes.Win,
                ForwardReturnSeries.Loss => SweepPlanOutcomes.Loss,
                ForwardReturnSeries.Unresolved => SweepPlanOutcomes.Unresolved,
                ForwardReturnSeries.NeverEntered => SweepPlanOutcomes.NeverEntered,
                _ => SweepPlanOutcomes.Immature,
            };
            outcomes.BreakEven[exit] = result.BreakEven is { } planned ? (float)planned : float.NaN;
            outcomes.Null[exit] = float.NaN;
            outcomes.Multiple[exit] = float.NaN;

            if (result.EnteredAt is { } fill && fill > plan.Stop)
            {
                var risk = Statistic.FromRatio((fill - plan.Stop) / fill) * 100;

                if (result.ReturnPct is { } made)
                {
                    outcomes.Multiple[exit] = (float)(made / risk);
                }
                else if (result.ResolvedOn is { } on && outcomes.Code[exit] == SweepPlanOutcomes.Loss)
                {
                    // Entered and stopped on one session: the fill the scorer carries, the worst the zone offered,
                    // against the close that stopped it.
                    var stopped = bars.First(one => one.Session == on).Close;

                    outcomes.Multiple[exit] = (float)(Statistic.FromRatio((stopped - fill) / fill) * 100 / risk);
                }

                if (outcomes.Code[exit] is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss
                    && result.SessionsLeft is { } left && left > 0
                    && volatility is { } scatter
                    && plan.Stop < fill && plan.Target > fill)
                {
                    var below = Statistic.FromRatio(plan.Stop / fill);
                    var above = Statistic.FromRatio(plan.Target / fill);
                    var bar1 = breakEven
                        ? NullWin.ForMovingTheStop(below, above, scatter, left, NullWin.CostBasisPoints, seed)
                        : NullWin.For(below, above, scatter, left, NullWin.CostBasisPoints, seed);

                    outcomes.Null[exit] = bar1 is { } calibrated ? (float)calibrated : float.NaN;
                }
            }
        }

        return outcomes;
    }

    // The session changes the volatility is read over: the closes of the window up to and including the listing.
    static IReadOnlyList<double> Changes(SweepBar[] bars, int bar)
    {
        var from = Math.Max(0, bar - NullWin.VolatilityWindow);
        var changes = new List<double>(NullWin.VolatilityWindow);

        for (var at = from + 1; at <= bar; at++)
        {
            changes.Add(bars[at - 1].Close <= 0 ? 0 : Statistic.FromRatio(bars[at].Close / bars[at - 1].Close));
        }

        return changes;
    }

    // The sessions from the night to the next earnings date on file, the night's own day counting nought, counted
    // on the history's calendar, and on weekdays past its end.
    static short SessionsToEarnings(DateOnly[] prints, IReadOnlyList<DateOnly> calendar, int session)
    {
        var night = calendar[session];
        var next = prints.FirstOrDefault(date => date >= night);

        if (next == default)
        {
            return -1;
        }

        if (next == night)
        {
            return 0;
        }

        var count = 0;

        for (var at = session + 1; at < calendar.Count && calendar[at] <= next; at++)
        {
            count++;
        }

        if (next > calendar[^1])
        {
            for (var day = calendar[^1].AddDays(1); day <= next; day = day.AddDays(1))
            {
                count += day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0 : 1;
            }
        }

        return (short)Math.Min(count, short.MaxValue);
    }
}
