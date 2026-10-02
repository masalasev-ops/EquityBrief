using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Candidates;

// The new families' freezes, written down rather than typed at the command line: the breakouts' and the
// earnings drift's live rule at the starting point its sweep proposed and the operator approved, the
// proposal's one-step neighbours on its sweep's grid as its variants, read off the grid so they are the ones
// its report listed, and the two variants the operator added, the breakout's provisional setting and the
// drift's proposal with its stop no closer than one typical move. Each is judged by the test its sweep's
// report fixed before the freeze. The sector leaders are no family and freeze as the pullback's variant.
// see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
public static class TheSetupFamilies
{
    public const string BreakoutWords =
        "the breakout family's gates at every setting stated: a close above the highest high of the stated sessions before " +
        "it, volume at least the stated multiple of its fifty-session average, the mean daily range of the twenty sessions " +
        "before tonight no wider than the stated share of the twenty before them, and the stop the stated typical moves " +
        "beneath the close, trailing the highest close since and never lowered, with no target and the family's cap; a " +
        "member firing where every gate passes and no exclusion applies, the rule's own list at most five a night by " +
        "volume against its average, with one open trade a stock";

    public const string DriftWords =
        "the earnings drift family's gates at every setting stated: a print reacting inside the stated sessions, a surprise " +
        "above zero, the reaction session's close up the stated typical moves on the stated multiple of its volume's " +
        "average and tonight's close above that session's low, bought at the close with the stop at that low, or the " +
        "stated floor's typical moves beneath where one is stated and the low sits nearer, and the target the nearer of the " +
        "lowest band two typical moves above and the stated multiple of the risk, with the family's cap; a member firing " +
        "where every gate passes and no exclusion applies, the rule's own list at most five a night by the surprise, with " +
        "one open trade a stock";

    public static IReadOnlyList<string> Names { get; } = [Core.Families.BreakoutRule.Name, Core.Families.DriftRule.Name];

    // A family's registrations at its freeze, the live rule first, or none for a name no freeze is written for.
    public static IReadOnlyList<Registration>? For(string family) => family switch
    {
        Core.Families.BreakoutRule.Name => Breakouts,
        Core.Families.DriftRule.Name => Drifts,
        _ => null,
    };

    public static IReadOnlyList<Registration> Breakouts { get; } = BreakoutsAtTheFreeze();

    public static IReadOnlyList<Registration> Drifts { get; } = DriftsAtTheFreeze();

    static IReadOnlyList<Registration> BreakoutsAtTheFreeze()
    {
        var grid = BreakoutSweep.Grid;
        var live = Core.Families.BreakoutRule.Live;
        var variants = Neighbours(grid, [live.HighSessions, live.VolumeMultiple, live.RangeCeiling, live.StopMoves])
            .Select(values => new BreakoutSettings((int)values[0], values[1], values[2], values[3]))
            .Append(Core.Families.BreakoutRule.Provisional);

        Registration Of(BreakoutSettings settings, bool isLive) =>
            new(Named(isLive, Words(settings)), BreakoutWords, FamilySweepReport.Test, BreakoutCandidate.EvaluatorName, BreakoutCandidate.ParametersOf(settings));

        return [Of(live, true), .. variants.Select(settings => Of(settings, false))];
    }

    static IReadOnlyList<Registration> DriftsAtTheFreeze()
    {
        var grid = DriftSweep.Grid;
        var live = Core.Families.DriftRule.Live;
        var variants = Neighbours(grid, [live.WindowSessions, live.ReactionMoves, live.VolumeMultiple, live.TargetRiskMultiple])
            .Select(values => new DriftSettings((int)values[0], values[1], values[2], values[3]))
            .Append(live with { StopFloorMoves = Core.Families.DriftRule.VariantStopFloorMoves });

        Registration Of(DriftSettings settings, bool isLive) =>
            new(Named(isLive, Words(settings)), DriftWords, FamilySweepReport.Test, DriftCandidate.EvaluatorName, DriftCandidate.ParametersOf(settings));

        return [Of(live, true), .. variants.Select(settings => Of(settings, false))];
    }

    // The grid's settings one step along one dial from the live values, each as its dials' values, the live
    // values being the grid's own setting the sweep proposed.
    static IEnumerable<double[]> Neighbours(FamilyGrid grid, double[] live)
    {
        var at = grid.Settings.Single(setting => Enumerable.Range(0, grid.Dials.Count).All(dial => grid.Value(setting, dial) == live[dial]));

        return grid.Neighbours(at)
            .Select(setting => Enumerable.Range(0, grid.Dials.Count).Select(dial => grid.Value(setting, dial)).ToArray())
            .Where(values => values.All(double.IsFinite));
    }

    static string Named(bool isLive, string words) => (isLive ? FamilyRecords.LivePrefix : "the ") + words;

    public static string Words(BreakoutSettings settings) =>
        FormattableString.Invariant($"breakout rule at a {settings.HighSessions}-session high, {Number(settings.VolumeMultiple)} times the volume, ranges at {Number(settings.RangeCeiling)} and the stop {Number(settings.StopMoves)} typical moves beneath");

    public static string Words(DriftSettings settings) =>
        FormattableString.Invariant($"drift rule within {settings.WindowSessions} sessions, up {Number(settings.ReactionMoves)} typical moves on {Number(settings.VolumeMultiple)} times the volume, the target at {Number(settings.TargetRiskMultiple)} times the risk")
        + (settings.StopFloorMoves > 0 ? FormattableString.Invariant($", the stop at least {Number(settings.StopFloorMoves)} typical move beneath") : string.Empty);

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
