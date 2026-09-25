using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Candidates;

// The swing family's shadow for one night: the gate candidates standing when the night started, read
// once, each member's gate inputs evaluated by them, and the counts the stage's row states. A missing or
// moved evaluator is a fault, and a member the night holds no bar for, or holds across a gap, is a
// counted skip, by the rules the listings stage applies to its own.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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

    public string Evaluate(GateInputs inputs, bool stale, DateOnly? gap)
    {
        var withheld = stale
            ? new NameWithheld(ShadowSkipCause.Stale, "no bar is stored for the night's session")
            : gap is { } at
                ? new NameWithheld(ShadowSkipCause.Gapped, "the stored series has a gap at " + at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", so nothing is computed across it")
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
            ? "; no swing family candidate stands registered, so nothing was evaluated in shadow"
            : FormattableString.Invariant($"; {standing.Count} swing family candidate(s) registered, {Evaluated} shadow evaluation(s) written, ")
                + FormattableString.Invariant($"{withheldBy.Values.Sum()} skipped on a member without the readings: {withheldBy.GetValueOrDefault(ShadowSkipCause.Stale)} stale, {withheldBy.GetValueOrDefault(ShadowSkipCause.Gapped)} gapped")
                + (faults.Count == 0
                    ? string.Empty
                    : FormattableString.Invariant($"; FAILURE: {faults.Count} registered candidate(s) skipped on every member, the code carrying no evaluator by its name or a moved one: ")
                        + string.Join("; ", faults.Select(entry => $"'{entry.Key}' {entry.Value}")));
}
