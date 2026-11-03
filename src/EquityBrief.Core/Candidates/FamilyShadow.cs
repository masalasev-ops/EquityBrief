using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Candidates;

// The swing family's shadow for one night: the gate candidates standing when the night started, read
// once, each member's gate inputs evaluated by them, and the counts the stage's row states. A missing or
// moved evaluator is a fault, and a member the night holds no bar for, or holds across a gap, is a
// counted skip, by the rules the listings stage applies to its own.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
public sealed class FamilyShadow : IMemberShadow
{
    readonly IReadOnlyList<RegisterRow> standing;
    readonly Dictionary<string, string> faults = new(StringComparer.Ordinal);
    readonly Dictionary<ShadowSkipCause, int> withheldBy = [];

    FamilyShadow(IReadOnlyList<RegisterRow> standing) => this.standing = standing;

    // The family standing at the night's start, from the register as it stands.
    public static FamilyShadow For(IReadOnlyList<RegisterRow> register, DateTimeOffset nightStartedAt) =>
        new(ShadowColumn.ForTheFilter(ShadowColumn.StandingAt(register, nightStartedAt)));

    public int Standing => standing.Count;

    public int ArrivalReach => ShadowColumn.ArrivalReach(standing);

    public int Evaluated { get; private set; }

    public IReadOnlyList<string> Faults => [.. faults.Keys];

    // A standing rule reading analysts' estimates at the version it was registered under that passes the member on
    // everything else, and none for a member the night holds no bar for or holds across a gap, which every rule skips.
    // see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night
    public bool AsksForEstimates(GateInputs inputs, bool stale, DateOnly? gap) =>
        !stale
        && gap is null
        && standing.Any(row => CandidateEvaluators.Find(row.Evaluator) is SwingFilterRule rule
            && rule.Version == row.EvaluatorVersion
            && SwingFilterRule.AsksForEstimates(inputs, CandidateEvaluator.Read(row.Parameters)));

    // From 21.4 whether a standing rule at the version it was registered under reads the business's state off the SEC's
    // facts as first filed, which the night then reads once for every member through the ledger's catalogue.
    // see: The seventh swing family candidate leaves off a member whose business the SEC's facts as first filed read deteriorating through the ledger's catalogue, and the nine are registered again at 21.4 with the eight unchanged versions keeping their records
    public bool ReadsFiledState =>
        standing.Any(row => CandidateEvaluators.Find(row.Evaluator) is SwingFilterRule rule
            && rule.Version == row.EvaluatorVersion
            && CandidateEvaluator.Read(row.Parameters).GetValueOrDefault(SwingFilterRule.FiledStateParameter) == 1);

    public string Evaluate(GateInputs inputs, bool stale, DateOnly? gap)
    {
        var withheld = stale
            ? new NameWithheld(ShadowSkipCause.Stale, Families.RuleWording.Shadow.NoBar)
            : gap is { } at
                ? new NameWithheld(ShadowSkipCause.Gapped, Families.RuleWording.Shadow.GappedAt(at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                : null;

        var shadow = ShadowColumn.EvaluateGates(standing, inputs, withheld);

        Evaluated += shadow.Outcomes.Count;

        foreach (var skip in shadow.Skipped)
        {
            if (skip.IsFault)
            {
                faults.TryAdd(skip.Candidate, skip.Reason);
            }
            else
            {
                withheldBy[skip.Cause] = withheldBy.GetValueOrDefault(skip.Cause) + 1;
            }
        }

        return JsonSerializer.Serialize(new
        {
            candidates = shadow.Outcomes.Select(outcome => new { candidate = outcome.Candidate, fired = outcome.Fired, values = outcome.Values }).ToArray(),
            skipped = shadow.Skipped.Select(skip => new { candidate = skip.Candidate, reason = skip.Reason }).ToArray(),
        });
    }

    // A failure where a candidate went unevaluated, since a hole in a candidate's record is one the
    // correction will later divide by.
    public string Said =>
        standing.Count == 0
            ? Families.RuleWording.Shadow.NoSwingCandidate
            : Families.RuleWording.Shadow.SwingWritten(standing.Count, Evaluated)
                + Families.RuleWording.Shadow.Skipped(withheldBy.Values.Sum(), withheldBy.GetValueOrDefault(ShadowSkipCause.Stale), withheldBy.GetValueOrDefault(ShadowSkipCause.Gapped))
                + (faults.Count == 0
                    ? string.Empty
                    : Families.RuleWording.Shadow.FailedOnEveryMember(faults.Count)
                        + string.Join("; ", faults.Select(entry => $"'{entry.Key}' {entry.Value}")));
}
