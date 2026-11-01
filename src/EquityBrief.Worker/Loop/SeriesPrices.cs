using System.Runtime.CompilerServices;
using EquityBrief.Core.Prices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// From 21.18 each history series' opens and highs as statistics, read once for a run's history, which the target
// standing as a sell order walks beside the closes.
// see: The target as a sell order standing from the buy is an exit of the menu, and its variant registers where its family has room
public static class SeriesPrices
{
    static readonly ConditionalWeakTable<IReadOnlyList<SweepSeries>, double[][]> OpensRead = new();

    static readonly ConditionalWeakTable<IReadOnlyList<SweepSeries>, double[][]> HighsRead = new();

    public static double[][] Opens(IReadOnlyList<SweepSeries> series) =>
        OpensRead.GetValue(series, read => [.. read.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Open)).ToArray())]);

    public static double[][] Highs(IReadOnlyList<SweepSeries> series) =>
        HighsRead.GetValue(series, read => [.. read.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.High)).ToArray())]);
}
