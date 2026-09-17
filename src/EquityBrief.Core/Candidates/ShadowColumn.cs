namespace EquityBrief.Core.Candidates;

// One candidate's shadow result on one name-night: the candidate, whether it
// fired, and the values that made it true or false.
public sealed record ShadowOutcome(string Candidate, bool Fired, IReadOnlyDictionary<string, string> Values);

// A candidate the night could not evaluate, and why.
//
// Kept apart from the outcomes rather than written as an outcome that did not
// fire, because those are opposite statements. A candidate that did not fire is
// a measurement; a candidate nothing evaluated is a hole in one, and folding the
// second into the first is how a record of having skipped a night stops
// existing.
public sealed record ShadowSkip(string Candidate, string Reason, ShadowSkipCause Cause)
{
    // A fault in the code rather than a name-night without the readings, and the one kind that fails the stage.
    // see: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip
    public bool IsFault => Cause is ShadowSkipCause.NoEvaluator or ShadowSkipCause.VersionMoved;
}

public enum ShadowSkipCause
{
    NoEvaluator,
    VersionMoved,
    Stale,
    Gapped,
    NotAvailable,
}

// Why a night hands a name no readings at all, with the sentence its row carries.
public sealed record NameWithheld(ShadowSkipCause Cause, string Reason);

public sealed record ShadowResult(IReadOnlyList<ShadowOutcome> Outcomes, IReadOnlyList<ShadowSkip> Skipped);

// What the night evaluates in shadow, and what it refuses to.
//
// The arithmetic sits here rather than in the builder for the reason the reason
// outcomes do: a night's stage reads a store and writes a row, and the rule it
// applies is a thing a test can put constructed input to without a store at all.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public static class ShadowColumn
{
    // The candidates a night evaluates: those standing registered at the instant
    // the night started, and no others.
    //
    // The night's start rather than now, and that is the whole rule. A candidate
    // registered while the night was running would be evaluated on a name-night
    // it was not registered before, which is a candidate scored on evidence that
    // was already in, and the register cannot afterwards say which names were
    // reached before it landed and which after. The instant is the one the night
    // took before its first step and handed to the stage, never the stage's own
    // start, which comes minutes later.
    public static IReadOnlyList<RegisterRow> StandingAt(IReadOnlyList<RegisterRow> rows, DateTimeOffset nightStartedAt) =>
        CandidateFamily.StandingBefore(rows, nightStartedAt);

    // Every standing candidate evaluated over one name-night, or skipped with a
    // reason. Nothing is filtered by whether a live reason fired: a listings row
    // exists for every name in the index every night, and a shadow candidate has
    // to be evaluated on the nights it would have fired, most of which are nights
    // no live reason surfaced that name.
    // see: The base rate is over every name-night, and never over the listed ones
    public static ShadowResult Evaluate(IReadOnlyList<RegisterRow> standing, CandidateNight night, NameWithheld? withheld = null)
    {
        var outcomes = new List<ShadowOutcome>();
        var skipped = new List<ShadowSkip>();

        foreach (var row in standing)
        {
            var evaluator = CandidateEvaluators.Find(row.Evaluator);

            if (evaluator is null)
            {
                skipped.Add(new ShadowSkip(
                    row.Candidate,
                    $"the code carries no evaluator named '{row.Evaluator}'",
                    ShadowSkipCause.NoEvaluator));

                continue;
            }

            // The version the row names against the version the code carries. A
            // candidate whose evaluator has moved on is not evaluated under the
            // moved code, because a shadow score written under a rule the
            // register does not name is worse than no score: it reads as evidence
            // about the registered condition and is evidence about a different
            // one.
            // see: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through
            if (evaluator.Version != row.EvaluatorVersion)
            {
                skipped.Add(new ShadowSkip(
                    row.Candidate,
                    $"registered under {row.Evaluator} at {row.EvaluatorVersion} and the code carries " +
                    $"{evaluator.Version}, so a score would be about a rule the register does not name",
                    ShadowSkipCause.VersionMoved));

                continue;
            }

            // After the two faults, so a moved evaluator is named on a stale or gapped name's row too.
            if (withheld is not null)
            {
                skipped.Add(new ShadowSkip(row.Candidate, withheld.Reason, withheld.Cause));

                continue;
            }

            if (evaluator.Reads.FirstOrDefault(key => !night.Values.ContainsKey(key)) is { } missing)
            {
                skipped.Add(new ShadowSkip(
                    row.Candidate,
                    $"the night computed no '{missing}' for {night.Ticker}",
                    ShadowSkipCause.NotAvailable));

                continue;
            }

            var verdict = evaluator.Evaluate(night, CandidateEvaluator.Read(row.Parameters));

            outcomes.Add(new ShadowOutcome(row.Candidate, verdict.Fired, verdict.Values));
        }

        return new ShadowResult(outcomes, skipped);
    }
}
