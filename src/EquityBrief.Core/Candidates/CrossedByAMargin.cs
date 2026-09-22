using EquityBrief.Core.Indicators;

namespace EquityBrief.Core.Candidates;

// A candidate condition: the close crossed one of the name's bands since the
// previous session and finished at least a registered margin of the name's
// typical daily move past the edge it crossed, the high edge on a rise and the
// low edge on a fall.
//
// The margin is what tells a crossing from a close that finished against the
// edge it touched. A rule with no margin counts a close a few cents past a level
// as having gone through it, and a level a price is sitting on has decided
// nothing yet.
// see: A crossing is a candidate condition only where the close sits half a typical move past the edge it crossed
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed class CrossedByAMargin : CandidateEvaluator
{
    public const string EvaluatorName = "crossed-by-a-margin";

    // How far past the crossed edge the close must sit, in the name's typical
    // daily moves. Settled by the operator on the per-night counts rather than
    // proposed, so changing it is a new registration and not an edit here.
    // see: Nothing a candidate or a version is registered with changes while it runs, and a proposed number is settled only by a new registration
    public const double SettledMargin = 0.5;

    public const string Margin = "margin";

    public override string Name => EvaluatorName;

    public override string Version => "179d86c01fe7";

    public override IReadOnlyList<string> Reads => [NightValues.Crossings, IndicatorSeries.Atr14];

    public override IReadOnlyList<string> Parameters => [Margin];

    public override CandidateVerdict Evaluate(CandidateNight night, IReadOnlyDictionary<string, double> parameters)
    {
        var crossings = Required(night, NightValues.Crossings);
        var typicalMove = Required(night, IndicatorSeries.Atr14);
        var margin = parameters[Margin];

        // How far past the crossed edge the close finished, which a night that
        // crossed nothing has none of. The count above is what makes that
        // absence an answer rather than a value the night could not compute.
        var past = Optional(night, NightValues.CrossingDistance);

        return new CandidateVerdict(
            crossings >= 1 && past is { } distance && distance >= margin * typicalMove,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NightValues.Crossings] = Figure(crossings),
                [NightValues.CrossingDistance] = Figure(past),
                [IndicatorSeries.Atr14] = Figure(typicalMove),
                [Margin] = Figure(margin),
            });
    }
}
