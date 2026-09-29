using EquityBrief.Core.Filter;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// "Close to a buy point" as the read surface draws it: a stored gate row read as the near-miss rule reads a
// result, and the list's rows put in that rule's order.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
public static class CloseScreen
{
    // A stored gate row as the near-miss rule reads it.
    public static NearMissResult Of(GateResultRow gate) =>
        new(
            gate.Ticker,
            gate.Market,
            gate.Trend,
            gate.Setup,
            gate.Trigger,
            gate.Trade,
            gate.Exclusions.Count,
            gate.Passed,
            gate.Gates,
            gate.Strength,
            gate.BandStrength,
            gate.SwingEntry,
            gate.SwingStop,
            gate.SwingTarget,
            gate.ClearStop,
            gate.ClearTarget);

    public static bool MissedOne(GateResultRow gate) => NearMiss.MissedOne(Of(gate));

    public static MissedGate? MissOf(GateResultRow gate, FilterSettings settings, int? firedSessionsBefore) =>
        NearMiss.MissOf(Of(gate), settings, firedSessionsBefore);

    // Close to a buy point, nearest first, a tie going by the first list's own order, each figure read off
    // the row's own stored result.
    public static IReadOnlyList<ListingCell> Ordered(IEnumerable<ListingCell> rows, IReadOnlyDictionary<string, GateResultRow> gates, Func<string, int> statePlace) =>
        NearMiss.Ordered(
            rows.Where(row => row.Missed is not null && gates.ContainsKey(row.Ticker)),
            row => row.Ticker,
            row => row.Missed!.Distance,
            row => statePlace(row.Ticker),
            row => NearMiss.RewardToRisk(gates[row.Ticker].Gates),
            row => gates[row.Ticker].Strength,
            row => gates[row.Ticker].BandStrength);
}
