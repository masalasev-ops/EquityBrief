namespace EquityBrief.Core.Candidates;

// A candidate condition: the name's volume ratio, tonight's volume over its
// fifty-day average, above a registered multiple of the night's own median ratio
// and above that multiple on its own.
//
// Both halves rather than the median alone. On a quiet night the median sits
// below 1, and a condition keyed on it alone fires on names trading under twice
// their own average, which is not unusual volume in any sense the live reason
// means.
// see: Unusual volume measured against the night's own median is a candidate condition
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed class VolumeAgainstTheNight : CandidateEvaluator
{
    public const string EvaluatorName = "volume-against-the-night";

    // The multiple both halves are taken at. Proposed: no study read sets it,
    // and settling it is a new registration.
    // see: Nothing a candidate or a version is registered with changes while it runs, and a proposed number is settled only by a new registration
    public const double ProposedMultiple = 2;

    public const string Multiple = "multiple";

    public override string Name => EvaluatorName;

    public override string Version => "75271ad2ada6";

    public override IReadOnlyList<string> Reads => [NightValues.VolumeRatio, NightValues.NightMedianRatio];

    public override IReadOnlyList<string> Parameters => [Multiple];

    public override CandidateVerdict Evaluate(CandidateNight night, IReadOnlyDictionary<string, double> parameters)
    {
        var ratio = Required(night, NightValues.VolumeRatio);
        var median = Required(night, NightValues.NightMedianRatio);
        var multiple = parameters[Multiple];

        return new CandidateVerdict(
            ratio > multiple * median && ratio > multiple,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NightValues.VolumeRatio] = Figure(ratio),
                [NightValues.NightMedianRatio] = Figure(median),
                [Multiple] = Figure(multiple),

                // Recorded and tested nowhere: a day is classed as high volume
                // elsewhere by its rank inside the name's own window, and a
                // verdict that kept only the ratio could not be read that way
                // afterwards.
                [NightValues.VolumeRank] = Figure(Optional(night, NightValues.VolumeRank)),
            });
    }
}
