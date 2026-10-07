using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Candidates;

// The new families' freezes, written down rather than typed at the command line: the breakouts' and the
// earnings drift's live rule at the starting point its sweep proposed and the operator approved, the
// proposal's one-step neighbours on its sweep's grid as its variants, read off the grid so they are the ones
// its report listed, the two variants the operator added, the breakout's provisional setting and the
// drift's proposal with its stop no closer than one typical move, and the market switch the operator added to
// each from the ideas' run on the frozen families, the breakout listing only on nights the index closes above its
// 200-session average and the drift only on nights the VIX closes under its close ten sessions before, each the
// live rule with that switch and nothing else moved. Each is judged by the test its sweep's report fixed before
// the freeze. The sector leaders are no family and froze as the pullback's variant. The sector heavyweights freeze
// at their sweep's proposal with its three one-step neighbours that met the floors, each kept in a book of its own.
// see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, each family registered again whole and its records replayed
public static class TheSetupFamilies
{
    // How a registered rule's market switches are read, which both families' words end on.
    const string SwitchWords =
        "; where a market switch is stated, only on nights the index closes above the average of the stated count of " +
        "its closes ending on the night, or the VIX closes under its close the stated count of sessions before, each " +
        "read on the store's sessions and closed on a night it cannot be read";

    public const string BreakoutWords =
        "the breakout family's gates at every setting stated: a close above the highest high of the stated sessions before " +
        "it, volume at least the stated multiple of its fifty-session average, the mean daily range of the twenty sessions " +
        "before tonight no wider than the stated share of the twenty before them, and the stop the stated typical moves " +
        "beneath the close, trailing the highest close since and never lowered, with no target and the family's cap; a " +
        "member firing where every gate passes and no exclusion applies, the rule's own list at most five a night by " +
        "volume against its average, with one open trade a stock" + SwitchWords;

    public const string DriftWords =
        "the earnings drift family's gates at every setting stated: a print reacting inside the stated sessions, a surprise " +
        "above zero, the reaction session's close up the stated typical moves on the stated multiple of its volume's " +
        "average and tonight's close above that session's low, bought at the close with the stop at that low, or the " +
        "stated floor's typical moves beneath where one is stated and the low sits nearer, and the target the nearer of the " +
        "lowest band two typical moves above and the stated multiple of the risk, with the family's cap; a member firing " +
        "where every gate passes and no exclusion applies, the rule's own list at most five a night by the surprise, with " +
        "one open trade a stock" + SwitchWords;

    public const string HeavyweightWords =
        "the sector heavyweights' rule at every setting stated: on the first session of each month, or of each week where " +
        "stated, or the first after it whose stored year holds the closes the setting's readings need, each sector's " +
        "stated count of largest companies by value as it stood, one listing a company and every " +
        "company where the count is nought; a company's lead its return over the stated look-back less its sector's, the " +
        "sector's its fund's over the fund's own sessions where stated and its members' mean otherwise; the stated count " +
        "of leaders a sector, the largest leads above nothing whose close sits above its 50-day average and that above its " +
        "200-day and, where stated, whose beta over 251 daily returns against the index is at least one, bought at that " +
        "close; a holding sold at its last close as a member and, as stated, at a rebalance where the rule would not buy " +
        "it, at a close under its 200-day average, or at either; one holding a stock, kept in a book of its own";

    // The test the heavyweights' records are read by, a holding counted in the block it ends in, as the operator ruled
    // its scoring.
    // see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
    public const string HeavyweightTest =
        "From the freeze, each rule's record is its holdings' edge, the percent return less its size cut's over the same " +
        "sessions, over blocks of 63 sessions, a holding counted in the block it ends in, and a checkpoint passes it where " +
        "the sign-flip test over its whole blocks falls under its level: 0.05 shared among the rules the family registers, " +
        "its own and its variants, the test the register's candidates are judged by.";

    // The switch each family's last variant holds, the setting the ideas' run read it at: the index's 200-session
    // average for the breakout, and the VIX's close ten sessions before for the drift.
    public static MarketSwitches BreakoutSwitch { get; } = new(SweepIdeas.SlowAverage, 0);

    public static MarketSwitches DriftSwitch { get; } = new(0, SweepIdeas.Lookback);

    public static IReadOnlyList<string> Names { get; } = [Core.Families.BreakoutRule.Name, Core.Families.DriftRule.Name, Core.Families.HeavyweightRule.Name];

    // A family's registrations at its freeze, the live rule first, or none for a name no freeze is written for.
    public static IReadOnlyList<Registration>? For(string family) => family switch
    {
        Core.Families.BreakoutRule.Name => Breakouts,
        Core.Families.DriftRule.Name => Drifts,
        Core.Families.HeavyweightRule.Name => Heavyweights,
        _ => null,
    };

    public static IReadOnlyList<Registration> Breakouts { get; } = BreakoutsAtTheFreeze();

    public static IReadOnlyList<Registration> Drifts { get; } = DriftsAtTheFreeze();

    // The sector heavyweights at their sweep's proposal and its three one-step neighbours that met the floors, as the
    // operator ruled on 2026-10-04: either exit, the members' mean in the fund's place, and every company in the size
    // cut's.
    // see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants
    public static IReadOnlyList<Registration> Heavyweights { get; } = HeavyweightsAtTheFreeze();

    static IReadOnlyList<Registration> HeavyweightsAtTheFreeze()
    {
        var live = Core.Families.HeavyweightRule.Live;

        Registration Of(HeavyweightSettings settings, bool isLive) =>
            new(Named(isLive, Words(settings)), HeavyweightWords, HeavyweightTest, SectorHeavyweightCandidate.EvaluatorName, SectorHeavyweightCandidate.ParametersOf(settings));

        return
        [
            Of(live, true),
            Of(live with { SoldUnderAverage = true }, false),
            Of(live with { FundReturn = false }, false),
            Of(live with { Largest = Core.Families.HeavyweightRule.EveryCompany }, false),
        ];
    }

    static IReadOnlyList<Registration> BreakoutsAtTheFreeze()
    {
        var grid = BreakoutSweep.Grid;
        var live = Core.Families.BreakoutRule.Live;
        var variants = Neighbours(grid, [live.HighSessions, live.VolumeMultiple, live.RangeCeiling, live.StopMoves])
            .Select(values => new BreakoutSettings((int)values[0], values[1], values[2], values[3]))
            .Append(Core.Families.BreakoutRule.Provisional);

        Registration Of(BreakoutSettings settings, bool isLive, MarketSwitches switches) =>
            new(Named(isLive, Words(settings) + switches.Words), BreakoutWords, FamilySweepReport.Test, BreakoutCandidate.EvaluatorName, BreakoutCandidate.ParametersOf(settings, switches));

        return [Of(live, true, MarketSwitches.None), .. variants.Select(settings => Of(settings, false, MarketSwitches.None)), Of(live, false, BreakoutSwitch)];
    }

    static IReadOnlyList<Registration> DriftsAtTheFreeze()
    {
        var grid = DriftSweep.Grid;
        var live = Core.Families.DriftRule.Live;
        var variants = Neighbours(grid, [live.WindowSessions, live.ReactionMoves, live.VolumeMultiple, live.TargetRiskMultiple])
            .Select(values => new DriftSettings((int)values[0], values[1], values[2], values[3]))
            .Append(live with { StopFloorMoves = Core.Families.DriftRule.VariantStopFloorMoves });

        Registration Of(DriftSettings settings, bool isLive, MarketSwitches switches) =>
            new(Named(isLive, Words(settings) + switches.Words), DriftWords, FamilySweepReport.Test, DriftCandidate.EvaluatorName, DriftCandidate.ParametersOf(settings, switches));

        return [Of(live, true, MarketSwitches.None), .. variants.Select(settings => Of(settings, false, MarketSwitches.None)), Of(live, false, DriftSwitch)];
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

    public static string Words(HeavyweightSettings settings) =>
        "sector heavyweights rule at "
        + (settings.Largest == Core.Families.HeavyweightRule.EveryCompany ? "every company" : FormattableString.Invariant($"the {settings.Largest} largest"))
        + FormattableString.Invariant($", {settings.LookBack} sessions against the sector's {(settings.FundReturn ? "fund" : "members")}, {settings.Leaders} {(settings.Leaders == 1 ? "leader" : "leaders")}")
        + (settings.HighBeta ? ", a beta of at least 1" : string.Empty)
        + (settings.Weekly ? ", weekly" : ", monthly")
        + (settings.SoldOnLeading && settings.SoldUnderAverage
            ? ", sold on no longer leading or a close under the 200-day average"
            : settings.SoldOnLeading ? ", sold on no longer leading" : ", sold on a close under the 200-day average");

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
