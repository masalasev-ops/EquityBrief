using System.Globalization;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// One finished trade's path as the autopsy reads it: its closes from the buy's session to the one it ended on, its buy,
// its risk and how it ended.
public sealed record TradePath(IReadOnlyList<double> Closes, double Entry, double Risk, string End);

// One figure the autopsy states of a family's finished trades: its name, its value, the trades it was read over and
// its words.
public sealed record AutopsyFigure(string Figure, double? Value, int Trades, string Words);

// The trade autopsy: what a family's finished trades did on their way to their end, read off their closes. How far each
// went for and against before it ended, the best and the worst close in risks; the sessions to the best close; and of
// the trades its stop ended, how many had closed a risk up first, and of those its target ended, how far under the buy
// they fell first. Each figure is a median or a share over the trades it is read over, and none where none is.
// see: The trade autopsy proposes exits of a fixed menu, each tested as the procedure that chose it
public static class PathAutopsy
{
    public const string Best = "best";

    public const string Worst = "worst";

    public const string ToBest = "sessions to best";

    public const string StoppedUp = "stopped once a risk up";

    public const string TargetWorst = "target hits' worst";

    public static IReadOnlyList<AutopsyFigure> Read(IReadOnlyList<TradePath> paths)
    {
        var read = paths.Where(path => path.Risk > 0 && path.Closes.Count > 1).ToArray();
        var best = read.Select(path => (path.Closes.Skip(1).Max() - path.Entry) / path.Risk).ToArray();
        var worst = read.Select(path => (path.Closes.Skip(1).Min() - path.Entry) / path.Risk).ToArray();
        var toBest = read.Select(path => 1.0 + path.Closes.Skip(1).Select((close, at) => (close, at)).MaxBy(pair => pair.close).at).ToArray();
        var stopped = read.Where(path => path.End == SetupEnds.Stop).ToArray();
        var stoppedUp = stopped.Count(path => path.Closes.Skip(1).SkipLast(1).Any(close => close >= path.Entry + path.Risk));
        var targets = read.Where(path => path.End == SetupEnds.Target).ToArray();
        var targetWorst = targets.Select(path => (path.Closes.Skip(1).Min() - path.Entry) / path.Risk).ToArray();

        return
        [
            new(Best, Median(best), read.Length, read.Length == 0 ? "no trade has finished" : Invariant($"the median trade's best close was {Median(best):0.00} risks above its buy before it ended")),
            new(Worst, Median(worst), read.Length, read.Length == 0 ? "no trade has finished" : Invariant($"its worst close {Math.Abs(Median(worst) ?? 0):0.00} risks {(Median(worst) < 0 ? "under" : "over")} its buy")),
            new(ToBest, Median(toBest), read.Length, read.Length == 0 ? "no trade has finished" : Invariant($"its best close came a median {Median(toBest):0.#} sessions after the buy")),
            new(StoppedUp, stopped.Length == 0 ? null : 1.0 * stoppedUp / stopped.Length, stopped.Length, stopped.Length == 0 ? "no trade ended at its stop" : Invariant($"{stoppedUp} of the {stopped.Length} trades the stop ended had first closed a risk up")),
            new(TargetWorst, Median(targetWorst), targets.Length, targets.Length == 0 ? "no trade ended at its target" : Invariant($"the trades the target ended fell a median {Math.Abs(Median(targetWorst) ?? 0):0.00} risks {(Median(targetWorst) < 0 ? "under" : "over")} the buy first")),
        ];
    }

    // The middle of a set, the mean of the two middle values for an even count; none for an empty set.
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    // The finding a proposal states: the autopsy's figures in words and the exit's edge on the learning years against
    // the rule's own.
    public static string Finding(IReadOnlyList<AutopsyFigure> figures, ExitChoice exit, double ownEdge, double edge) =>
        string.Join("; ", figures.Where(figure => figure.Figure is Best or StoppedUp).Select(figure => figure.Words))
        + Invariant($"; {exit.Words} read {edge:+0.000;-0.000} risks a trade on the years it learned on against the rule's own {ownEdge:+0.000;-0.000}");

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
