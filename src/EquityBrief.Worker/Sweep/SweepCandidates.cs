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
// won, lost, ran out of sessions or was never entered, the bar its own paths set, the break-even it planned,
// what the trade came to in multiples of the risk it planned, the sessions until it ended and so freed the
// stock, and what the same plan entered at the same close on every member that night came to, the benchmark
// the edge is read against.
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
    public readonly float[] Null = [float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN];
    public readonly float[] BreakEven = [float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN];
    public readonly float[] Multiple = [float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN];

    // The sessions from the listing to the session the trade ended on, which it blocks the stock through; a
    // trade the history ran out on blocks it for the cap.
    public readonly short[] Ends = new short[8];

    public readonly float[] Benchmark = [float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN];

    // The trade's edge under an exit: its result less the benchmark, and none where either is missing.
    public float EdgeAt(int exit) => float.IsNaN(Multiple[exit]) || float.IsNaN(Benchmark[exit]) ? float.NaN : Multiple[exit] - Benchmark[exit];
}

// One member-session some setting of the grid could list: every reading each axis chooses between, each plan
// with its outcomes, and the readings the seven conditions take. A member-session no setting of either grid
// could list is not kept, since no variation's count reads it.
public sealed class SweepCandidate
{
    // The sessions back the trigger's arrival and the turn-up day's volume are read over, the longest window.
    public const int Backs = SweepColumns.LongestWindow + 1;

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

    // The conditions' readings: the close over the 52-week high; the sector's rank, -1 where the name carries no
    // sector; the volume ratio on each session back the trigger may have fired on; the lowest RSI since each
    // reference high and whether the RSI rose on each session back; the sessions since the newest surprise's
    // reaction session and its size, -1 and none where there is none; the tightness; and the sessions since
    // each reference high with the largest gap down inside that pullback.
    public float HighRatio = float.NaN;
    public sbyte SectorRank = -1;
    public readonly float[] TurnVolume = new float[Backs];
    public readonly float[] RsiLow = [float.NaN, float.NaN, float.NaN];
    public short RsiUpMask;
    public short SurpriseSessions = -1;
    public float SurprisePercent = float.NaN;
    public float Tightness = float.NaN;
    public readonly short[] PullbackSessions = [-1, -1, -1];
    public readonly float[] GapMoves = [float.NaN, float.NaN, float.NaN];

    public SweepCandidate()
    {
        Array.Fill(TurnVolume, float.NaN);
    }

    public static int AgeAt(TriggerKind trigger, SupportKind support) => ((int)trigger * 3) + (int)support;

    public static int PlanAt(PlanRule rule, SupportKind support) => rule switch
    {
        PlanRule.Ladder => 0,
        PlanRule.NearestBands => 1 + (int)support,
        _ => 4 + (int)support,
    };

    // The readings the conditions take under one design: the reference high's place and the trigger's age.
    public ConditionSetting.Readings Readings(int high, int age) => new(
        HighRatio,
        SectorRank,
        age >= 0 && age < Backs ? TurnVolume[age] : float.NaN,
        RsiLow[high],
        age >= 0 && age < Backs && ((RsiUpMask >> age) & 1) != 0,
        SurpriseSessions,
        SurprisePercent,
        Tightness,
        PullbackSessions[high],
        GapMoves[high]);

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
                writer.Write(plan.Ends[exit]);
                writer.Write(plan.Benchmark[exit]);
            }
        }

        writer.Write(HighRatio);
        writer.Write(SectorRank);

        foreach (var value in TurnVolume)
        {
            writer.Write(value);
        }

        foreach (var value in RsiLow)
        {
            writer.Write(value);
        }

        writer.Write(RsiUpMask);
        writer.Write(SurpriseSessions);
        writer.Write(SurprisePercent);
        writer.Write(Tightness);

        foreach (var value in PullbackSessions)
        {
            writer.Write(value);
        }

        foreach (var value in GapMoves)
        {
            writer.Write(value);
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
                plan.Ends[exit] = reader.ReadInt16();
                plan.Benchmark[exit] = reader.ReadSingle();
            }

            candidate.Plans[at] = plan;
        }

        candidate.HighRatio = reader.ReadSingle();
        candidate.SectorRank = reader.ReadSByte();

        for (var at = 0; at < candidate.TurnVolume.Length; at++)
        {
            candidate.TurnVolume[at] = reader.ReadSingle();
        }

        for (var at = 0; at < candidate.RsiLow.Length; at++)
        {
            candidate.RsiLow[at] = reader.ReadSingle();
        }

        candidate.RsiUpMask = reader.ReadInt16();
        candidate.SurpriseSessions = reader.ReadInt16();
        candidate.SurprisePercent = reader.ReadSingle();
        candidate.Tightness = reader.ReadSingle();

        for (var at = 0; at < candidate.PullbackSessions.Length; at++)
        {
            candidate.PullbackSessions[at] = reader.ReadInt16();
        }

        for (var at = 0; at < candidate.GapMoves.Length; at++)
        {
            candidate.GapMoves[at] = reader.ReadSingle();
        }

        return candidate;
    }
}

// Pass three: for each member-session some setting could list, the bands the level builder would have drawn that
// night, each support's setup band, each trigger's arrival over the sessions before, each plan's outcomes, and
// the readings the seven conditions take.
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
        var grid = SweepGrid.Extended;
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

                candidate.Plans[at] = Outcomes(plan, after, close, volatility, seed, series, bar);
                kept = true;
            }

            if (!kept)
            {
                continue;
            }

            candidate.Earnings = SessionsToEarnings(series.Name.Prints, calendar, session);
            Conditions(candidate, series, bar, cross);
            found.Add(candidate);
        }

        return found;
    }

    // The readings the seven conditions take of one candidate, each as the data stood on the session.
    static void Conditions(SweepCandidate candidate, SweepSeries series, int bar, SweepColumns.Session cross)
    {
        var highs = SweepAxes.ReferenceHighs.Count;
        var close = Statistic.FromPrice(series.Bars[bar].Close);

        candidate.HighRatio = double.IsNaN(series.High252[bar]) || series.High252[bar] <= 0 ? float.NaN : (float)(close / series.High252[bar]);
        candidate.SectorRank = series.Name.Sector is { } sector && cross.SectorRanks.TryGetValue(sector, out var rank) ? (sbyte)Math.Min(rank, sbyte.MaxValue) : (sbyte)-1;
        candidate.Tightness = (float)series.Tightness[bar];

        for (var back = 0; back < SweepCandidate.Backs; back++)
        {
            if (bar - back < 0)
            {
                break;
            }

            candidate.TurnVolume[back] = (float)series.VolumeRatio[bar - back];

            if (series.RsiUp[bar - back])
            {
                candidate.RsiUpMask |= (short)(1 << back);
            }
        }

        for (var high = 0; high < highs; high++)
        {
            candidate.RsiLow[high] = (float)series.RsiLow[(bar * highs) + high];
            candidate.PullbackSessions[high] = (short)Math.Min(series.SinceHigh[(bar * highs) + high], short.MaxValue);
            candidate.GapMoves[high] = (float)series.GapDown[(bar * highs) + high];
        }

        var newest = series.NewestSurprise[bar];

        if (newest >= 0)
        {
            candidate.SurpriseSessions = (short)Math.Min(bar - series.SurpriseBar[newest], short.MaxValue);
            candidate.SurprisePercent = (float)series.Name.Surprises[newest].Percent;
        }
    }

    // The level builder's bands on one of a name's bars, over the window ending on it, the averages it holds
    // and the swings confirmed by it inside the year the night holds, and none where the window is short or the
    // bar holds no typical move.
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

        return SessionReplay.BandsOver(window, indicators, SwingsHeld(series, bar));
    }

    // The swings the night's store held on a bar's session: confirmed by it, and made inside the year of bars
    // the store keeps.
    public static Swing[] SwingsHeld(SweepSeries series, int bar)
    {
        var oldest = series.Bars[series.WindowStart[bar]].Session;

        return [.. series.Swings.AsSpan(0, series.Confirmed[bar]).ToArray().Where(swing => swing.SessionDate >= oldest)];
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
        var lows = SwingsHeld(series, bar)
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

    // Each exit's outcome, scored as the live scorer scores a setup, the calibrated bar its paths set, and the
    // sessions the trade blocks the stock for.
    static SweepPlanOutcomes Outcomes(SweepPlan plan, IReadOnlyList<ReturnBar> after, decimal close, double? volatility, int seed, SweepSeries series, int bar)
    {
        var outcomes = new SweepPlanOutcomes { RewardToRisk = Statistic.FromRatio(plan.RewardToRisk), StopMoves = plan.StopMoves };
        var bars = series.Bars;

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
            outcomes.Ends[exit] = (short)ForwardReturnSeries.SetupSessionCap;

            if (result.ResolvedOn is { } ended)
            {
                for (var at = 0; at < after.Count; at++)
                {
                    if (after[at].SessionDate == ended)
                    {
                        outcomes.Ends[exit] = (short)Math.Max(1, series.SessionAt[bar + at + 1] - series.SessionAt[bar]);
                        break;
                    }
                }
            }

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
