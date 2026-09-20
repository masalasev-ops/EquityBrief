using System.Globalization;

namespace EquityBrief.Core.Candidates;

// A candidate condition: the relative strength index at or below a registered
// level.
//
// The momentum panel is context a reader weighs and no component decides
// anything from it, which is the decision 8.0 took. That decision names this as
// the way the panel could be given a job: as a candidate condition like any
// other, registered before it is scored and scored in shadow before it is shown.
// So this is the shape of that, and nothing here reaches a reason, a band, a plan
// or a gate. At 8.3 it is registered and evaluated by nothing; the shadow column
// at 8.4 is what runs it.
// see: The momentum panel is context a reader weighs, and nothing computes with it
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed class MomentumIndexReading : CandidateEvaluator
{
    public const string EvaluatorName = "momentum-index-reading";

    public const string Reading = "rsi14";

    public const string Level = "level";

    public override string Name => EvaluatorName;

    public override string Version => "79ae94cd071d";

    public override IReadOnlyList<string> Reads => [Reading];

    public override IReadOnlyList<string> Parameters => [Level];

    public override CandidateVerdict Evaluate(CandidateNight night, IReadOnlyDictionary<string, double> parameters)
    {
        var reading = Required(night, Reading);
        var level = parameters[Level];

        // At or below, inclusive, and the boundary is stated rather than left to
        // the operator to find out from a night: a condition registered as "below
        // 30" that fires at exactly 30 is a different condition from the one that
        // was registered, and the register is the thing that has to settle which.
        return new CandidateVerdict(
            reading <= level,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Reading] = reading.ToString("0.####", CultureInfo.InvariantCulture),
                [Level] = level.ToString("0.####", CultureInfo.InvariantCulture),
            });
    }
}
