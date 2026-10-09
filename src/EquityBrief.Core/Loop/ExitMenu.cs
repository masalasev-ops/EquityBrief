using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// The kinds of exit the menu holds: the stop raised to the buy once a close reaches a number of risks, a trail
// following the highest close once a close reaches a number of risks, a fixed target, a time exit for a trade that
// has not moved, and half the trade sold at the target with the rest trailed.
public enum ExitKind
{
    BreakEven,
    Trail,
    Target,
    Time,
    HalfTarget,
}

// One exit of the menu: its number, its kind and its figures, and its words. K is risks for a break-even and a
// target and typical moves for a trail; J is the risks a trail waits for; N the sessions a time exit reads at and X
// the risks a trade must be up by then.
public sealed record ExitChoice(int Number, ExitKind Kind, double K, double J, int N, double X)
{
    public string Words => Kind switch
    {
        ExitKind.BreakEven => FormattableString.Invariant($"the stop raised to the buy once a close is {K:0.##} risks up"),
        ExitKind.Trail => J > 0
            ? FormattableString.Invariant($"a trail {K:0.##} typical moves under the highest close once a close is {J:0.##} risks up, with no target")
            : FormattableString.Invariant($"a trail {K:0.##} typical moves under the highest close from the buy, with no target"),
        ExitKind.Target => FormattableString.Invariant($"a target at {K:0.##} risks"),
        ExitKind.Time => X > 0
            ? FormattableString.Invariant($"sold at the close {N} sessions in unless it is {X:0.##} risks up")
            : FormattableString.Invariant($"sold at the close {N} sessions in unless it is above the buy"),
        _ => FormattableString.Invariant($"half sold at the target and the rest trailed {K:0.##} typical moves under the highest close"),
    };
}

// The menu of exits the trade autopsy proposes from, each replayed exactly on closes from a setup's anchor, as the
// families' own walks are: each session's close read from the session after the buy, a close under the stop selling
// at that close, and the cap's close ending a trade nothing else ended. Each exit changes only what it names and keeps
// the rest of the plan: its stop, its cap, and its target or its trail.
// see: Every engine's settings hooks land together and all default off, so the families' pins move once
public static class ExitMenu
{
    public static IReadOnlyList<double> BreakEvenRisks { get; } = [0.5, 1, 1.5, 2];

    public static IReadOnlyList<double> TrailMoves { get; } = [1.5, 2, 3];

    public static IReadOnlyList<double> TrailAfterRisks { get; } = [0, 1, 2];

    public static IReadOnlyList<double> TargetRisks { get; } = [1.5, 2, 2.5, 3, 4];

    public static IReadOnlyList<int> TimeSessions { get; } = [10, 20, 40];

    public static IReadOnlyList<double> TimeRisks { get; } = [0, 0.5];

    public static IReadOnlyList<double> HalfTrailMoves { get; } = [2, 3];

    // A trailing plan names no target, so its half is sold at this many risks.
    public const double HalfTargetWhereTheTrailHasNone = 2;

    // The swing families' menu, numbered from 1; 0 is a rule's own exit.
    public static IReadOnlyList<ExitChoice> Swing { get; } = Build();

    static List<ExitChoice> Build()
    {
        var menu = new List<ExitChoice>();

        void Add(ExitKind kind, double k = 0, double j = 0, int n = 0, double x = 0) => menu.Add(new ExitChoice(menu.Count + 1, kind, k, j, n, x));

        foreach (var k in BreakEvenRisks)
        {
            Add(ExitKind.BreakEven, k);
        }

        foreach (var j in TrailAfterRisks)
        {
            foreach (var k in TrailMoves)
            {
                Add(ExitKind.Trail, k, j);
            }
        }

        foreach (var k in TargetRisks)
        {
            Add(ExitKind.Target, k);
        }

        foreach (var n in TimeSessions)
        {
            foreach (var x in TimeRisks)
            {
                Add(ExitKind.Time, n: n, x: x);
            }
        }

        foreach (var k in HalfTrailMoves)
        {
            Add(ExitKind.HalfTarget, k);
        }

        return menu;
    }

    // The exit a number names, none for a rule's own.
    public static ExitChoice? Of(int number) => number <= 0 ? null : Swing.FirstOrDefault(choice => choice.Number == number);

    // The same plan entered at a session's close on every member under an exit of the menu: each member's stop its own
    // typical moves under its close, its target or its trail the plan's multiple of its own risk, and the average of
    // the results its closes reach the end of, with how many that is.
    public static (double Average, int Members) Benchmark(
        IReadOnlyList<double[]> closes,
        IReadOnlyList<int> names,
        IReadOnlyList<int> bars,
        Func<int, int, double> moveOf,
        double riskMoves,
        double? rewardToRisk,
        double? trailRisks,
        int cap,
        ExitChoice? exit)
    {
        var sum = 0.0;
        var count = 0;

        for (var at = 0; at < names.Count; at++)
        {
            var (name, bar) = (names[at], bars[at]);
            var move = moveOf(name, bar);
            var entry = closes[name][bar];
            var risk = riskMoves * move;

            if (!(risk > 0) || entry - risk <= 0)
            {
                continue;
            }

            var anchor = new SetupAnchor(DateOnly.MinValue, entry, entry - risk, rewardToRisk is { } reward ? entry + (reward * risk) : null, trailRisks is { } trail ? trail * risk : null, cap, riskMoves);

            if (Replay(closes[name], bar, anchor, move, exit).Result is { } result)
            {
                sum += result;
                count++;
            }
        }

        return (count > 0 ? sum / count : double.NaN, count);
    }

    // A setup's path under an exit, or under its own where none is given; the typical move a trail is counted in.
    public static SetupOutcome Replay(ReadOnlySpan<double> closes, int from, SetupAnchor anchor, double move, ExitChoice? choice)
    {
        if (choice is null)
        {
            return SetupReplay.Replay(closes, from, anchor);
        }

        var risk = anchor.Risk;

        if (!(risk > 0) || (choice.Kind is ExitKind.Trail or ExitKind.HalfTarget && !(move > 0)))
        {
            return new SetupOutcome(null, 0, SetupEnds.None);
        }

        return choice.Kind switch
        {
            ExitKind.BreakEven => Walk(closes, from, anchor, move, breakEvenAt: anchor.Entry + (choice.K * risk)),
            ExitKind.Trail => Walk(closes, from, anchor with { Target = null, Trail = choice.K * move }, move, trailFrom: anchor.Entry + (choice.J * risk)),
            ExitKind.Target => Walk(closes, from, anchor with { Target = anchor.Entry + (choice.K * risk), Trail = null }, move),
            ExitKind.Time => Walk(closes, from, anchor, move, timeAt: choice.N, timeFloor: anchor.Entry + (choice.X * risk)),
            _ => Half(closes, from, anchor, choice.K * move),
        };
    }

    // The plan's own walk with the menu's one change: the stop raised to the buy from the session after a close
    // reaches a level, a trail that follows the highest close only once a close has reached a level, or a sale at a
    // session's close where the close is under a floor.
    static SetupOutcome Walk(
        ReadOnlySpan<double> closes,
        int from,
        SetupAnchor anchor,
        double move,
        double breakEvenAt = double.PositiveInfinity,
        double trailFrom = double.NegativeInfinity,
        int timeAt = 0,
        double timeFloor = double.NegativeInfinity)
    {
        var (entry, risk) = (anchor.Entry, anchor.Risk);
        var floor = anchor.Stop;
        var trailing = anchor.Trails && anchor.Entry >= trailFrom;

        if (anchor.Trails ? !(anchor.Trail > 0) : !(anchor.Target > entry))
        {
            return new SetupOutcome(null, 0, SetupEnds.None);
        }

        for (var session = 1; session <= anchor.Cap; session++)
        {
            var at = from + session;

            if (at >= closes.Length)
            {
                return new SetupOutcome(null, Math.Max(0, closes.Length - 1 - from), SetupEnds.Open);
            }

            var close = closes[at];

            if (close < floor)
            {
                return new SetupOutcome((close - entry) / risk, session, floor > anchor.Stop && trailing ? SetupEnds.Trail : SetupEnds.Stop);
            }

            if (!anchor.Trails && close >= anchor.Target!.Value)
            {
                return new SetupOutcome((close - entry) / risk, session, SetupEnds.Target);
            }

            if (session == timeAt && close < timeFloor)
            {
                return new SetupOutcome((close - entry) / risk, session, SetupEnds.Time);
            }

            if (session == anchor.Cap)
            {
                return new SetupOutcome((close - entry) / risk, session, SetupEnds.Cap);
            }

            // The trail follows each close since the buy, as the families' walks do, once a close has reached the
            // level it waits for.
            if (anchor.Trails && !trailing && close >= trailFrom)
            {
                trailing = true;

                for (var earlier = 1; earlier < session; earlier++)
                {
                    floor = Math.Max(floor, closes[from + earlier] - anchor.Trail!.Value);
                }
            }

            if (trailing)
            {
                floor = Math.Max(floor, close - anchor.Trail!.Value);
            }

            if (close >= breakEvenAt)
            {
                floor = Math.Max(floor, entry);
            }
        }

        return new SetupOutcome(null, anchor.Cap, SetupEnds.Open);
    }

    // Half sold at the plan's target, or at two risks where the plan trails, at the first close reaching it; before it
    // the plan's own exit holds, and after it the rest is trailed under the highest close since the buy as well, never
    // lowered. A close under the stop before the target sells the whole, and the cap's close what is left.
    static SetupOutcome Half(ReadOnlySpan<double> closes, int from, SetupAnchor anchor, double trail)
    {
        var (entry, risk) = (anchor.Entry, anchor.Risk);
        var target = anchor.Target ?? entry + (HalfTargetWhereTheTrailHasNone * risk);
        var floor = anchor.Stop;
        var highest = double.NegativeInfinity;
        double? half = null;

        for (var session = 1; session <= anchor.Cap; session++)
        {
            var at = from + session;

            if (at >= closes.Length)
            {
                return new SetupOutcome(null, Math.Max(0, closes.Length - 1 - from), SetupEnds.Open);
            }

            var close = closes[at];
            var result = (close - entry) / risk;

            if (close < floor)
            {
                return new SetupOutcome(half is { } sold ? (sold + result) / 2 : result, session, half is null ? SetupEnds.Stop : SetupEnds.Trail);
            }

            if (half is null && close >= target)
            {
                half = result;
            }

            if (session == anchor.Cap)
            {
                return new SetupOutcome(half is { } sold ? (sold + result) / 2 : result, session, SetupEnds.Cap);
            }

            highest = Math.Max(highest, close);

            if (anchor.Trails)
            {
                floor = Math.Max(floor, close - anchor.Trail!.Value);
            }

            if (half is not null)
            {
                floor = Math.Max(floor, highest - trail);
            }
        }

        return new SetupOutcome(null, anchor.Cap, SetupEnds.Open);
    }
}
