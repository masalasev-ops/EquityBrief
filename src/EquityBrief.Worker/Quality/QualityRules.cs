using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Worker.Quality;

// Quality at a fair price's freeze on an index: its live rule at the setting given, one of the history test's grid, and
// each setting of the grid one dial from it as a version beside it, all registered at one instant, so the family's
// correction for luck counts these rules alone; or why none can be written: an index the family carries no rule for, a
// rule of the family already standing on the index, a setting stating other parameters than the rule reads, or one
// outside the grid.
// see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
// see: A quality rule is registered as the family on its index and kept by the family's own step alone
public static class QualityRules
{
    // The words each rule's registration states for its rule and its test.
    public const string RuleWords =
        "Quality at a fair price on its index at every setting stated, its holdings kept by the family's own step over the " +
        "index's own members: each member's scorecard as the night scored it and its price over earnings against its own " +
        "last 12 quarters as the ledger read it, the score's floor read as a share of the checks that applied, the valuation " +
        "and the trend as stated, the stated book chosen down the ranking on the first night of each month and on its own " +
        "first night, each holding bought at the next open and sold at a reshuffle where it no longer qualifies, and where " +
        "stated also on a filing dropping its score";

    public const string Test =
        "From the freeze, each rule's record is its holdings' result less each one's own round trip at the published table " +
        "less the return of the equal-weighted members it was chosen beside over the same sessions, in percentage points of " +
        "the buy, read a month at a time as the history test reads its book, and the monthly run tests each version against " +
        "the live rule year by year as it tests every family's: 0.05 shared among the rules the family registers on its index.";

    public static (IReadOnlyList<Registration>? Registrations, string? Refusal) Freeze(
        string index,
        IReadOnlyDictionary<string, double> live,
        IReadOnlyList<RegisterRow> register,
        DateTimeOffset at)
    {
        if (CandidateEvaluators.All.OfType<QualityCandidate>().FirstOrDefault(evaluator => evaluator.Index == index) is not { } carried)
        {
            return (null, $"no quality rule is carried for an index coded '{index}'; the indices are {string.Join(", ", CandidateEvaluators.All.OfType<QualityCandidate>().Select(evaluator => evaluator.Index))}.");
        }

        var standing = CandidateFamily.In(CandidateFamily.Standing(register, at), carried.Family);

        if (standing.Count > 0)
        {
            return (null, FormattableString.Invariant($"the {carried.Family} holds {standing.Count} rule(s) standing; the family is frozen again once they are retired."));
        }

        if (!live.Keys.Order(StringComparer.Ordinal).SequenceEqual(carried.Parameters.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return (null, $"the live rule states [{string.Join(", ", live.Keys.Order(StringComparer.Ordinal))}] and the quality rule reads [{string.Join(", ", carried.Parameters.Order(StringComparer.Ordinal))}].");
        }

        var setting = QualityCandidate.SettingsOf(live);

        if (!QualityRule.Grid.Contains(setting) || !QualityCandidate.ParametersOf(setting).All(pair => live[pair.Key] == pair.Value))
        {
            return (null, "the live rule states a setting the history test's grid does not hold, which no answer read.");
        }

        QualitySettings[] versions = [.. QualityRule.Grid.Where(one => one.Changes(setting) == 1)];

        if (versions.Length + 1 > CandidateFamily.Maximum)
        {
            return (null, FormattableString.Invariant($"the live rule and its {versions.Length} neighbours would hold {versions.Length + 1} rules, past the {CandidateFamily.Maximum} a family keeps."));
        }

        var named = OwnStepCandidate.NameOf(index) + " quality rule at ";

        return (
            [
                new Registration(FamilyRecords.LivePrefix + named + Short(setting), RuleWords, Test, carried.Name, QualityCandidate.ParametersOf(setting)),
                .. versions.Select(version => new Registration("the " + named + Short(version), RuleWords, Test, carried.Name, QualityCandidate.ParametersOf(version))),
            ],
            null);
    }

    // A setting in the few words a rule's name carries, each of the five dials.
    public static string Short(QualitySettings setting) => string.Create(
        CultureInfo.InvariantCulture,
        $"a score of {setting.ScoreFloorInNine} in 9, {ValuationShort(setting.Valuation)}, {(setting.Trend ? "above its 200-day average" : "the trend off")}, {setting.Holdings} held, sold {(setting.SellOnFiling ? "at reshuffles and on a filing" : "at reshuffles")}{(setting.SoldUnderAverage ? " and under its 200-day average" : string.Empty)}");

    static string ValuationShort(QualityValuation valuation) => valuation switch
    {
        QualityValuation.Off => "its price not read",
        QualityValuation.AtOrBelowMedian => "at or below its own median",
        _ => "in its cheapest third",
    };
}
