using System.Globalization;

namespace EquityBrief.Core.Filter;

// One member's stored answers on one night, as the shape proposer recounts them: the readings each
// threshold is compared with, the two band tests and the trigger's arrival inside its window, which no
// threshold moves, the trade read both ways, and whether an exclusion removed it.
public sealed record StoredMember(
    string Ticker,
    string? TrendState,
    double? Strength,
    double? Depth,
    double? DryUp,
    double? Tightness,
    double? VolumeMultiple,
    bool PullbackBand,
    bool BreakoutBand,
    bool? Arrived,
    double? LadderRewardToRisk,
    double? LadderStopMoves,
    double? SwingRewardToRisk,
    double? SwingStopMoves,
    bool Excluded);

public sealed record StoredNight(DateOnly Session, IReadOnlyList<StoredMember> Members);

// One gate's lever in a proposal: the setting it moves, the value it holds and the value proposed,
// none where no value in the range brings the gate inside its band or the gate has no threshold, and
// the gate's median count over the ordinary nights under each.
public sealed record Lever(
    string Gate,
    string? Setting,
    double? Current,
    double? Proposed,
    double? MedianNow,
    double? MedianProposed,
    int Low,
    int High);

// A shape proposal: the settings it proposes, each gate's lever, the list's median under the settings
// held and under the ones proposed, and the gates no threshold brings inside their bands.
public sealed record ShapeProposal(
    FilterSettings Settings,
    IReadOnlyList<Lever> Levers,
    double? ListNow,
    double? ListProposed,
    IReadOnlyList<string> Findings);

// The shape proposer's arithmetic. At the trigger it recounts every ordinary night's stored members under
// other settings, which it may do because it moves counts and reads no outcome, and it moves one setting
// per gate, in the funnel's order, to the value nearest the one held that puts the gate's median count
// inside its band, holding every gate before it at what it has just proposed. A gate no value in its range
// brings inside its band, and the trigger, which has no threshold, are named as findings about the gate
// rather than forced. It proposes and never applies.
// see: The shape proposer moves one setting a gate, nearest first, and never applies what it proposes
public static class ShapeProposals
{
    public const string StrengthFloor = "strengthFloor";
    public const string DryUpCeiling = "dryUpCeiling";
    public const string RewardToRiskFloor = "rewardToRiskFloor";

    // Each gate's lever: the setting it moves and the range it is tried over, in steps, in the funnel's
    // order; the trigger has none.
    public static IReadOnlyList<(string Gate, string? Setting, int From, int To, int Hundredths)> LeverRanges { get; } =
    [
        (SwingGates.Trend, StrengthFloor, 40, 95, 1),
        (SwingGates.Setup, DryUpCeiling, 50, 200, 5),
        (SwingGates.Trigger, null, 0, 0, 0),
        (SwingGates.Trade, RewardToRiskFloor, 100, 400, 10),
    ];

    // A night's counts under the settings given, the market held open: through each of the four gates
    // after it, and the list.
    public static (int[] Through, int Listed) Count(StoredNight night, FilterSettings settings)
    {
        var through = new int[4];
        var listed = 0;

        foreach (var member in night.Members)
        {
            var trend = member.TrendState == SwingGates.Uptrend && member.Strength is { } strength && strength >= settings.StrengthFloor;
            var pullback = member.Depth is { } depth && depth >= settings.DepthLow && depth <= settings.DepthHigh
                && member.DryUp is { } dryUp && dryUp < settings.DryUpCeiling
                && member.PullbackBand;
            var breakout = member.Tightness is { } tightness && tightness < settings.TightnessCeiling
                && member.VolumeMultiple is { } multiple && multiple >= settings.BreakoutVolumeMultiple
                && member.BreakoutBand;
            var setup = pullback || breakout;
            var trigger = (!pullback && breakout) || member.Arrived == true;

            var (ratio, moves) = settings.Trade == TradeInput.Ladder
                ? (member.LadderRewardToRisk, member.LadderStopMoves)
                : (member.SwingRewardToRisk, member.SwingStopMoves);
            var trade = ratio is { } rewardToRisk && rewardToRisk >= settings.RewardToRiskFloor
                && moves is { } stop && stop >= settings.StopLow && stop <= settings.StopHigh;

            bool[] passes = [trend, setup, trigger, trade];

            for (var at = 0; at < passes.Length; at++)
            {
                if (passes.Take(at + 1).All(pass => pass))
                {
                    through[at]++;
                }
            }

            listed += passes.All(pass => pass) && !member.Excluded ? 1 : 0;
        }

        return (through, listed);
    }

    public static ShapeProposal Propose(IReadOnlyList<StoredNight> ordinary, FilterSettings current)
    {
        double? Median(FilterSettings settings, int gate) =>
            SwingReadings.Median([.. ordinary.Select(night => Count(night, settings).Through[gate] * 1.0)]);

        double? ListMedian(FilterSettings settings) =>
            SwingReadings.Median([.. ordinary.Select(night => Count(night, settings).Listed * 1.0)]);

        var proposed = current;
        var levers = new List<Lever>();
        var findings = new List<string>();

        for (var gate = 0; gate < LeverRanges.Count; gate++)
        {
            var (name, setting, from, to, hundredths) = LeverRanges[gate];
            var (low, high) = (ShapeClock.GateBands[gate].Low, ShapeClock.GateBands[gate].High);
            var now = Median(proposed, gate);

            bool Inside(double? median) => median is { } value && value >= low && value <= high;

            if (setting is null)
            {
                levers.Add(new Lever(name, null, null, null, now, now, low, high));

                if (!Inside(now))
                {
                    findings.Add(FormattableString.Invariant($"the {name} has no threshold, and its median of {Say(now)} sits outside {low} to {high}"));
                }

                continue;
            }

            var held = Read(proposed, setting);

            if (Inside(now))
            {
                levers.Add(new Lever(name, setting, held, held, now, now, low, high));
                continue;
            }

            var tried = Enumerable.Range(0, ((to - from) / hundredths) + 1)
                .Select(step => (from + (step * hundredths)) / 100.0)
                .OrderBy(value => Math.Abs(value - held))
                .ThenBy(value => value)
                .FirstOrDefault(value => Inside(Median(With(proposed, setting, value), gate)), double.NaN);

            if (double.IsNaN(tried))
            {
                levers.Add(new Lever(name, setting, held, null, now, now, low, high));
                findings.Add(FormattableString.Invariant($"no {setting} between {from / 100.0:0.00} and {to / 100.0:0.00} brings the {name}'s median inside {low} to {high}, and it stays {Say(now)}"));
                continue;
            }

            proposed = With(proposed, setting, tried);
            levers.Add(new Lever(name, setting, held, tried, now, Median(proposed, gate), low, high));
        }

        return new ShapeProposal(proposed, levers, ListMedian(current), ListMedian(proposed), findings);
    }

    static double Read(FilterSettings settings, string setting) => setting switch
    {
        StrengthFloor => settings.StrengthFloor,
        DryUpCeiling => settings.DryUpCeiling,
        RewardToRiskFloor => settings.RewardToRiskFloor,
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "no such lever"),
    };

    static FilterSettings With(FilterSettings settings, string setting, double value) => setting switch
    {
        StrengthFloor => settings with { StrengthFloor = value },
        DryUpCeiling => settings with { DryUpCeiling = value },
        RewardToRiskFloor => settings with { RewardToRiskFloor = value },
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "no such lever"),
    };

    static string Say(double? median) => median is { } value ? value.ToString("0.#", CultureInfo.InvariantCulture) : "none";
}
