using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Worker.Improving;

// The Fundamentals family's freeze on an index: its live rule at the setting given, one of the history test's grid, and
// each setting of the grid one dial from it as a version beside it, all registered at one instant, so the family's
// correction for luck counts these rules alone; or why none can be written: an index the family carries no rule for, a
// rule of the family already standing on the index, a setting stating other parameters than the rule reads, or one
// outside the grid.
// see: Each index's Fundamentals rule is frozen on the operator's answers of 2026-10-28 with its one-step neighbours as versions
// see: A Fundamentals rule is registered as the family on its index and kept by the family's own step alone
public static class ImprovingRules
{
    // The words each rule's registration states for its rule and its test.
    public const string RuleWords =
        "The Fundamentals family on its index at every setting stated, its trades kept by the family's own step over the " +
        "index's own members: each member's scorecard as the night's last scoring stored it, a newly read filing's score " +
        "against the one its filing before held through the core's one scorecard, its sales growth's change as the ledger " +
        "read it and the flag that stood the night before from its results release; a member bought at the next open where " +
        "its newly read filing clears the stated bar and each check the setting asks holds, held while its score stays at " +
        "the stated floor and sold at the close its score falls below it, at the close of its last stated session or at its " +
        "last close as a member";

    public const string Test =
        "From the freeze, each rule's record is its trades' result less each one's own round trip at the published table " +
        "less the return of the equal-weighted members answered on its night over the same sessions, in percentage points " +
        "of the buy, read a month at a time as the history test reads its book, and the monthly run tests each version " +
        "against the live rule year by year as it tests every family's: 0.05 shared among the rules the family registers " +
        "on its index.";

    public static (IReadOnlyList<Registration>? Registrations, string? Refusal) Freeze(
        string index,
        IReadOnlyDictionary<string, double> live,
        IReadOnlyList<RegisterRow> register,
        DateTimeOffset at)
    {
        if (CandidateEvaluators.All.OfType<ImprovingCandidate>().FirstOrDefault(evaluator => evaluator.Index == index) is not { } carried)
        {
            return (null, $"no Fundamentals rule is carried for an index coded '{index}'; the indices are {string.Join(", ", CandidateEvaluators.All.OfType<ImprovingCandidate>().Select(evaluator => evaluator.Index))}.");
        }

        var standing = CandidateFamily.In(CandidateFamily.Standing(register, at), carried.Family);

        if (standing.Count > 0)
        {
            return (null, FormattableString.Invariant($"the {carried.Family} holds {standing.Count} rule(s) standing; the family is frozen again once they are retired."));
        }

        if (!live.Keys.Order(StringComparer.Ordinal).SequenceEqual(carried.Parameters.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return (null, $"the live rule states [{string.Join(", ", live.Keys.Order(StringComparer.Ordinal))}] and the Fundamentals rule reads [{string.Join(", ", carried.Parameters.Order(StringComparer.Ordinal))}].");
        }

        var setting = ImprovingCandidate.SettingsOf(live);

        if (!ImprovingGrid.Settings.Contains(setting) || !ImprovingCandidate.ParametersOf(setting).All(pair => live[pair.Key] == pair.Value))
        {
            return (null, "the live rule states a setting the history test's grid does not hold, which no answer read.");
        }

        ImprovingSweepSetting[] versions = [.. ImprovingGrid.Settings.Where(one => one.Changes(setting) == 1)];

        if (versions.Length + 1 > CandidateFamily.Maximum)
        {
            return (null, FormattableString.Invariant($"the live rule and its {versions.Length} neighbours would hold {versions.Length + 1} rules, past the {CandidateFamily.Maximum} a family keeps."));
        }

        var named = OwnStepCandidate.NameOf(index) + " Fundamentals rule at ";

        return (
            [
                new Registration(FamilyRecords.LivePrefix + named + Short(setting), RuleWords, Test, carried.Name, ImprovingCandidate.ParametersOf(setting)),
                .. versions.Select(version => new Registration("the " + named + Short(version), RuleWords, Test, carried.Name, ImprovingCandidate.ParametersOf(version))),
            ],
            null);
    }

    // A setting in the few words a rule's name carries, each of the six dials.
    public static string Short(ImprovingSweepSetting setting) => string.Create(
        CultureInfo.InvariantCulture,
        $"buy at {setting.BuyInNine} in 9, {(setting.FlagFirst ? "a flag first" : "no flag first")}, the rise {(setting.Rise ? "asked" : "not asked")}, the growth {(setting.Faster ? "asked" : "not asked")}, held at {setting.HoldInNine}, {setting.Sessions} sessions");
}
