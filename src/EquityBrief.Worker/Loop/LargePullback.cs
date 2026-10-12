using System.Globalization;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// The S&P 500's pullback as the tester walks it: the swing filter's open version read by value, each candidate of the
// ideas' run placed on a grid holding one level a dial at the version's own settings rather than on the sweep's grid,
// so a setting the sweep never tried, a shape move's hundredth among them, is read as exactly as one it did, and a
// setting on the sweep's grid keeps the listings the ideas' run keeps there.
// see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
public sealed class LargePullback(IReadOnlyList<SweepSeries> series, IReadOnlyList<SweepCandidate> candidates)
{
    // The place of the live design's reference high among the highs a candidate's readings are kept for.
    static readonly int LiveHigh = SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh);

    public IReadOnlyList<SweepSeries> Series => series;

    // A grid of one level a dial, each the filter's own setting: the trigger's arrival window as the freshness, the
    // stop's bounds as its one option, and no band strength asked, as the filter asks none.
    public static SweepGrid PointOf(FilterSettings settings) =>
        new(
            [settings.StrengthFloor],
            [settings.DepthLow],
            [settings.DepthHigh],
            [settings.DryUpCeiling],
            [settings.ArrivalSessions],
            [settings.RewardToRiskFloor],
            [(settings.StopLow, settings.StopHigh)],
            [settings.BreadthFloor],
            [0]);

    // The setting a grid of one level a dial is read at.
    public static DialSetting AtThePoint { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    // The listings the filter at its settings keeps over the history, in the ideas' run's order, each as the ideas'
    // run lists one; the earnings window the filter's own.
    public List<IdeaListing> Listings(FilterSettings settings)
    {
        var listings = new List<IdeaListing>();

        foreach (var pick in SweepStages.Picks(candidates, SweepDesign.Live with { EarningsWindow = settings.EarningsWindowSessions }, PointOf(settings)))
        {
            if (!pick.Corner.Passes(AtThePoint))
            {
                continue;
            }

            var candidate = candidates[pick.Index];
            var bar = Array.BinarySearch(series[pick.Name].SessionAt, pick.Session);

            listings.Add(new IdeaListing(
                pick.Index,
                pick.Name,
                bar,
                pick.Session,
                pick.Year,
                pick.Plan.RewardToRisk,
                candidate.Strength[(int)SweepDesign.Live.Strength],
                candidate.Band[(int)SweepDesign.Live.Support],
                pick.Plan.StopMoves,
                pick.Plan,
                SweepIdeas.RsiFall(series[pick.Name].Rsi, bar, candidate.PullbackSessions[LiveHigh])));
        }

        return listings;
    }

    // The filter at a version in words, as the tester names the rule today.
    public static string Words(string version, FilterSettings settings) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the S&P 500's swing filter at version {version}: a strength place at least {settings.StrengthFloor:0.##}, a pullback of {settings.DepthLow:0.##} to {settings.DepthHigh:0.##} typical moves, its volume at most {settings.DryUpCeiling:0.##} of its average, a reward to risk at least {settings.RewardToRiskFloor:0.##}, the stop {settings.StopLow:0.##} to {settings.StopHigh:0.##} typical moves under the buy, breadth at least {settings.BreadthFloor:0.##}, five a night, held on closes to {SweepIdeas.Cap} sessions");
}
