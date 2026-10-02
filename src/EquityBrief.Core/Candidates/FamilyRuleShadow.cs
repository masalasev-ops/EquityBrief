using System.Text.Json;
using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The new families' shadow for one night: the family candidates standing when the night started, read once,
// each member's inputs evaluated by every one of them at its own settings, and the counts the family
// evaluator's row states. A missing or moved evaluator is a fault, and a member the night holds no bar for,
// or holds across a gap, is a counted skip, by the rules the swing family's shadow applies to its own.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
public sealed class FamilyRuleShadow
{
    readonly IReadOnlyList<RegisterRow> standing;
    readonly Dictionary<string, string> faults = new(StringComparer.Ordinal);
    readonly Dictionary<ShadowSkipCause, int> withheldBy = [];

    FamilyRuleShadow(IReadOnlyList<RegisterRow> standing) => this.standing = standing;

    // The family candidates standing at the night's start, from the register as it stands.
    public static FamilyRuleShadow For(IReadOnlyList<RegisterRow> register, DateTimeOffset nightStartedAt) =>
        new([.. ShadowColumn.StandingAt(register, nightStartedAt).Where(row => CandidateEvaluators.Find(row.Evaluator) is FamilyRuleEvaluator)]);

    // A night evaluating no family candidate, as a store holding none hands the stage.
    public static FamilyRuleShadow None { get; } = new([]);

    public IReadOnlyList<RegisterRow> Standing => standing;

    // The widest window a standing drift candidate reads, the live rule's at least, so the stage reads the
    // typical moves and the volumes of every session a reaction one of them admits can follow.
    public int DriftWindowReach =>
        ShadowColumn.ForTheFamily(standing, DriftRule.Name)
            .Where(row => CandidateEvaluators.Find(row.Evaluator) is { } evaluator && evaluator.Version == row.EvaluatorVersion)
            .Select(row => DriftCandidate.WindowOf(CandidateEvaluator.Read(row.Parameters)))
            .Append(DriftRule.WindowSessions)
            .Max();

    public int Evaluated { get; private set; }

    public IReadOnlyList<string> Faults => [.. faults.Keys];

    // One family's verdicts over one member, as its row stores them, and none where no candidate of that
    // family stands.
    public string? Evaluate(string family, FamilyMember member, NameWithheld? withheld)
    {
        var own = ShadowColumn.ForTheFamily(standing, family);

        if (own.Count == 0)
        {
            return null;
        }

        var shadow = ShadowColumn.EvaluateFamily(own, member, withheld);

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

    // The verdicts a stored row's shadow holds, each candidate's name, whether it fired and its values.
    public static IReadOnlyList<ShadowOutcome> Read(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return [];
        }

        using var document = JsonDocument.Parse(stored);

        return
        [
            .. document.RootElement.GetProperty("candidates").EnumerateArray().Select(candidate => new ShadowOutcome(
                candidate.GetProperty("candidate").GetString()!,
                candidate.GetProperty("fired").GetBoolean(),
                candidate.GetProperty("values").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString() ?? string.Empty, StringComparer.Ordinal))),
        ];
    }

    // A failure where a candidate went unevaluated, since a hole in a candidate's record is one the
    // correction will later divide by.
    public string Said =>
        standing.Count == 0
            ? "; no family candidate stands registered, so nothing was evaluated in shadow"
            : FormattableString.Invariant($"; {standing.Count} family candidate(s) registered, {Evaluated} shadow evaluation(s) written, ")
                + FormattableString.Invariant($"{withheldBy.Values.Sum()} skipped on a member without the readings: {withheldBy.GetValueOrDefault(ShadowSkipCause.Stale)} stale, {withheldBy.GetValueOrDefault(ShadowSkipCause.Gapped)} gapped")
                + (faults.Count == 0
                    ? string.Empty
                    : FormattableString.Invariant($"; FAILURE: {faults.Count} registered candidate(s) skipped on every member, the code carrying no evaluator by its name or a moved one: ")
                        + string.Join("; ", faults.Select(entry => $"'{entry.Key}' {entry.Value}")));

    // Why a member is handed no verdict at all: no bar stored for the night, or a gap across its series as the
    // swing filter's row excluded it for.
    public static NameWithheld? Withheld(bool stale, bool gapped) =>
        stale
            ? new NameWithheld(ShadowSkipCause.Stale, "no bar is stored for the night's session")
            : gapped
                ? new NameWithheld(ShadowSkipCause.Gapped, "the stored series has a gap, so nothing is computed across it")
                : null;
}
