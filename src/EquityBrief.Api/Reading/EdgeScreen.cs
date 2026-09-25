using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The Calibration region's edge half as the run page reads it: the swing family's records off their own
// setups, and the near misses off the rows the open filter version stored. It reads what the nights stored
// and recounts nothing, so a setting moved since changes none of its figures.
// owes: The swing family's first look
// see: A gate's near misses are the setups it alone rejected, each group read against its own break-even and null and withheld below the block floor
public static class EdgeScreen
{
    public static EdgeView Edge(
        IReadOnlyList<CandidateRow> register,
        IReadOnlyList<CandidateNightRow> nights,
        IReadOnlyList<CandidateSetupRow> setups,
        DateOnly night,
        DateTimeOffset at)
    {
        var rows = register
            .Select(row => new RegisterRow(
                row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence))
            .ToArray();

        var first = nights
            .GroupBy(row => row.Candidate, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(row => row.SessionDate), StringComparer.Ordinal);

        var fired = setups
            .GroupBy(row => row.Candidate, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CandidateSetup>)
                [
                    .. group.Select(row => new CandidateSetup(
                        row.SessionDate, row.Outcome ?? string.Empty, row.Null, row.NullAtSensitivity,
                        row.BreakEven, row.ReturnPct, row.PlannedRisk, row.OnEarnings)),
                ],
                StringComparer.Ordinal);

        return new EdgeView(night, EdgeClock.Candidates(rows, fired, first, night, at), EdgeClock.FirstLookSessions, EdgeClock.EarliestPromotionSessions);
    }

    // The near misses over the rows the open filter version stored, and none where no version is open.
    public static NearMissView NearMisses(string? version, IReadOnlyList<NearMissRow> rows, DateOnly night) =>
        new(night, version, rows.Count == 0 ? null : rows.Min(row => row.Session), version is null ? [] : EdgeClock.NearMisses(rows, night));
}
