using EquityBrief.Core.Indicators;

namespace EquityBrief.Core.Candidates;

// A candidate condition: the close has come down into the nearest buying zone
// since the previous session, and that zone is no wider than a registered
// multiple of the name's typical daily move.
//
// One candidate rather than two. Arrival and width registered apart would be two
// rows in the family the level is divided across, and the divisor is a count of
// what was tried.
// see: A price that has come down into a narrow buying zone since the previous session is a candidate condition
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed class ArrivedAndNarrow : CandidateEvaluator
{
    public const string EvaluatorName = "arrived-and-narrow";

    // How wide the zone may be, in the name's typical daily moves. Proposed:
    // no study read sets it, and settling it is a new registration.
    // see: Nothing a candidate or a version is registered with changes while it runs, and a proposed number is settled only by a new registration
    public const double ProposedWidth = 1;

    public const string Width = "width";

    public override string Name => EvaluatorName;

    public override string Version => "2bb1c01b42c7";

    public override IReadOnlyList<string> Reads =>
        [NightValues.Close, NightValues.PreviousClose, NightValues.Zones, IndicatorSeries.Atr14];

    public override IReadOnlyList<string> Parameters => [Width];

    public override CandidateVerdict Evaluate(CandidateNight night, IReadOnlyDictionary<string, double> parameters)
    {
        var close = Required(night, NightValues.Close);
        var previous = Required(night, NightValues.PreviousClose);
        var zones = Required(night, NightValues.Zones);
        var typicalMove = Required(night, IndicatorSeries.Atr14);
        var width = parameters[Width];

        // The nearest zone's edges, which a name whose plan holds no zone has
        // none of. The count above is what makes that absence an answer: a name
        // with no buying zone has not come down into one.
        var low = Optional(night, NightValues.ZoneLow);
        var high = Optional(night, NightValues.ZoneHigh);

        // Arrival is a fact about two sessions and not one. A close that has sat
        // inside the zone all week is standing in the floor rather than arriving
        // at it, and a condition keyed on tonight alone would call each of those
        // sessions a new arrival. The lower bound is what tells an arrival from a
        // close that went straight through: a close below the low edge fell
        // through the zone and did not arrive in it.
        var arrived = zones >= 1
            && low is { } lowEdge
            && high is { } highEdge
            && previous > highEdge
            && close >= lowEdge
            && close <= highEdge;

        var narrow = low is { } inner && high is { } outer && outer - inner <= width * typicalMove;

        return new CandidateVerdict(
            arrived && narrow,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NightValues.Close] = Figure(close),
                [NightValues.PreviousClose] = Figure(previous),
                [NightValues.Zones] = Figure(zones),
                [NightValues.ZoneLow] = Figure(low),
                [NightValues.ZoneHigh] = Figure(high),
                [IndicatorSeries.Atr14] = Figure(typicalMove),
                [Width] = Figure(width),

                // Recorded and tested nowhere: a level's prior bounces and its
                // age bear on whether it holds, so a verdict that kept neither
                // could not be read against that afterwards.
                [NightValues.ZoneStrength] = Figure(Optional(night, NightValues.ZoneStrength)),
                [NightValues.ZoneNewestMemberSessions] = Figure(Optional(night, NightValues.ZoneNewestMemberSessions)),
            });
    }
}
