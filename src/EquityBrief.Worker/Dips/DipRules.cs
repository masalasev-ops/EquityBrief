using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Dips;

// The short-term dip's freeze on an index: its live rule at the setting given, one of its sweep's grid, and each version
// given beside it, each one of the live rule's one-step neighbours on the grid or its market variant, the same rule with
// its market setting off, all registered at one instant, so the family's correction for luck counts these rules alone;
// or why none can be written: an index the family carries no rule for, a rule of the family already standing on it, a
// setting stating other parameters than the rule reads or one off the grid, a version neither a neighbour nor the market
// variant or given twice, or more rules than a family keeps.
// see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
// see: A dip rule is registered as the family on its index and kept by the dip's own step alone
public static class DipRules
{
    // The words each rule's registration states for its rule and its test.
    public const string RuleWords =
        "The short-term dip on its index at every setting stated, its trades kept by the dip's own step over the index's own " +
        "members: each member's stored year read at the setting's drop, trend, report gate, stop, exit, time limit, market " +
        "setting and drop volume, its list drawn last among the swing families, the lowest 2-session RSI first and five a " +
        "night, each pick bought at the next open and sold as its exit, its stop or its time limit says";

    public const string Test =
        "From the freeze, each rule's record is its trades' results at their fills less each one's round trip at the published " +
        "table, in multiples of the risk the listing close set, and the monthly run tests each version against the live rule " +
        "year by year as it tests every family's: 0.05 shared among the rules the family registers on its index.";

    // Each dial of the sweep's grid on an index in the order the rule reads its parameters, with its levels in the
    // grid's own order, the market's its places among the index's levels and the volume's not read or the second stage's.
    public static IReadOnlyList<(string Parameter, IReadOnlyList<double> Levels)> Dials(string index) =>
    [
        (DipCandidate.DropParameter, IndexSweepRunner.DipDrops),
        (DipCandidate.TrendParameter, IndexSweepRunner.DipTrends),
        (DipCandidate.SellParameter, IndexSweepRunner.DipExits),
        (DipCandidate.LimitParameter, [.. IndexSweepRunner.DipLimits.Select<int, double>(limit => limit)]),
        (DipCandidate.StopParameter, IndexSweepRunner.DipStops),
        (DipCandidate.MarketParameter, [.. Enumerable.Range(0, IndexSweepRunner.DipMarketLevels(index).Count).Select<int, double>(place => place)]),
        (DipCandidate.VolumeParameter, [0, IndexSweepRunner.DipDropVolume]),
    ];

    public static (IReadOnlyList<Registration>? Registrations, string? Refusal) Freeze(
        string index,
        IReadOnlyDictionary<string, double> live,
        IReadOnlyList<IReadOnlyDictionary<string, double>> versions,
        IReadOnlyList<RegisterRow> register,
        DateTimeOffset at)
    {
        if (CandidateEvaluators.All.OfType<DipCandidate>().FirstOrDefault(evaluator => evaluator.Index == index) is not { } carried)
        {
            return (null, $"no dip rule is carried for an index coded '{index}'; the indices are {string.Join(", ", CandidateEvaluators.All.OfType<DipCandidate>().Select(evaluator => evaluator.Index))}.");
        }

        var standing = CandidateFamily.In(CandidateFamily.Standing(register, at), carried.Family);

        if (standing.Count > 0)
        {
            return (null, FormattableString.Invariant($"the {carried.Family} holds {standing.Count} rule(s) standing; the family is frozen again once they are retired."));
        }

        var dials = Dials(index);

        foreach (var setting in versions.Prepend(live))
        {
            if (!setting.Keys.Order(StringComparer.Ordinal).SequenceEqual(carried.Parameters.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                return (null, $"a setting states [{string.Join(", ", setting.Keys.Order(StringComparer.Ordinal))}] and the dip rule reads [{string.Join(", ", carried.Parameters.Order(StringComparer.Ordinal))}].");
            }

            if (dials.FirstOrDefault(dial => !dial.Levels.Contains(setting[dial.Parameter])) is { Parameter: { } off })
            {
                return (null, FormattableString.Invariant($"a setting states {off}={setting[off]}, which the sweep's grid does not hold on the {OwnStepCandidate.NameOf(index)}."));
            }
        }

        foreach (var version in versions)
        {
            var differing = dials.Where(dial => version[dial.Parameter] != live[dial.Parameter]).ToArray();
            var neighbour = differing is [var dial] && Math.Abs(IndexOf(dial.Levels, version[dial.Parameter]) - IndexOf(dial.Levels, live[dial.Parameter])) == 1;
            var marketVariant = differing is [{ Parameter: DipCandidate.MarketParameter }] && version[DipCandidate.MarketParameter] == 0;

            if (!neighbour && !marketVariant)
            {
                return (null, $"the version {Stated(version)} is neither one of the live rule's one-step neighbours on the sweep's grid nor its market variant.");
            }
        }

        if (versions.Select(Stated).Distinct(StringComparer.Ordinal).Count() < versions.Count)
        {
            return (null, "a version is given twice.");
        }

        if (versions.Count + 1 > CandidateFamily.Maximum)
        {
            return (null, FormattableString.Invariant($"the live rule and its {versions.Count} versions would hold {versions.Count + 1} rules, past the {CandidateFamily.Maximum} a family keeps."));
        }

        var named = OwnStepCandidate.NameOf(index) + " dip rule at ";

        return (
            [
                new Registration(FamilyRecords.LivePrefix + named + Short(carried.SettingOf(live), index), RuleWords, Test, carried.Name, live),
                .. versions.Select(version => new Registration("the " + named + Short(carried.SettingOf(version), index), RuleWords, Test, carried.Name, version)),
            ],
            null);
    }

    static int IndexOf(IReadOnlyList<double> levels, double level) => levels.ToList().IndexOf(level);

    static string Stated(IReadOnlyDictionary<string, double> setting) =>
        string.Join(",", setting.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));

    // A setting in the few words a rule's name carries, each of its dials, its market setting in its index's words.
    public static string Short(DipSetting setting, string index) => string.Join(
        ", ",
        [
            setting.FallMoves is { } fall ? FormattableString.Invariant($"a fall of {fall:0.##} moves") : FormattableString.Invariant($"the RSI under {setting.RsiUnder:0.##}"),
            .. setting.VolumeUnder is { } volume ? [FormattableString.Invariant($"its volume under {volume:0.##} times")] : Array.Empty<string>(),
            setting.FiftyOverLong ? "the 50-day above the 200-day" : "above its 200-day",
            setting.ExitRsiAbove is { } rsi ? FormattableString.Invariant($"sold on the RSI over {rsi}") : setting.ExitOnUpClose ? "sold on an up close" : FormattableString.Invariant($"sold over its {setting.ExitAverage}-day average"),
            FormattableString.Invariant($"{setting.TimeLimit} days at most"),
            FormattableString.Invariant($"its stop {setting.StopMoves:0.##} moves under"),
            setting.Market == DipRule.MarketOff ? "the market setting off" : DipRule.MarketWordsOn(index, setting.Market),
        ]);
}
