using System.Globalization;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Candidates;

// The swing filter as a registered candidate: every threshold of the filter as a parameter, the trade
// gate's input as 0 for the ladder's first tranche and 1 for the swing trade's own plan, and whether the
// market gate is read, 1 or 0. A member fires where every gate read passes and no exclusion applies.
//
// One evaluator for the whole family. The live filter and each variant are registrations of it with
// every threshold stated, so a variant is a whole rule and runs on unchanged when the live settings move.
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
// see: The swing filter opens loose on the swing trade's own plan, and each of its five variants moves one setting to its other side
public sealed class SwingFilterRule : GateEvaluator
{
    public const string EvaluatorName = "swing-filter";

    public const string TradeParameter = "trade";

    public const string MarketGateParameter = "marketGate";

    public override string Name => EvaluatorName;

    public override string Version => "d1936df599dd";

    // The settings' own names as a version stores them, then the trade gate's input and the market gate.
    public override IReadOnlyList<string> Parameters { get; } =
    [
        "breadthFloor",
        "strengthFloor",
        "depthLow",
        "depthHigh",
        "dryUpCeiling",
        "tightnessCeiling",
        "breakoutVolumeMultiple",
        "rewardToRiskFloor",
        "stopLow",
        "stopHigh",
        "earningsWindowSessions",
        "arrivalSessions",
        TradeParameter,
        MarketGateParameter,
    ];

    // The parameters a registration states for settings, the market gate read or not.
    public static IReadOnlyDictionary<string, double> ParametersOf(FilterSettings settings, bool marketGate = true) =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["breadthFloor"] = settings.BreadthFloor,
            ["strengthFloor"] = settings.StrengthFloor,
            ["depthLow"] = settings.DepthLow,
            ["depthHigh"] = settings.DepthHigh,
            ["dryUpCeiling"] = settings.DryUpCeiling,
            ["tightnessCeiling"] = settings.TightnessCeiling,
            ["breakoutVolumeMultiple"] = settings.BreakoutVolumeMultiple,
            ["rewardToRiskFloor"] = settings.RewardToRiskFloor,
            ["stopLow"] = settings.StopLow,
            ["stopHigh"] = settings.StopHigh,
            ["earningsWindowSessions"] = settings.EarningsWindowSessions,
            ["arrivalSessions"] = settings.ArrivalSessions,
            [TradeParameter] = settings.Trade == TradeInput.Swing ? 1 : 0,
            [MarketGateParameter] = marketGate ? 1 : 0,
        };

    // The settings a registration's parameters state.
    public static FilterSettings SettingsOf(IReadOnlyDictionary<string, double> parameters) =>
        new(
            parameters["breadthFloor"],
            parameters["strengthFloor"],
            parameters["depthLow"],
            parameters["depthHigh"],
            parameters["dryUpCeiling"],
            parameters["tightnessCeiling"],
            parameters["breakoutVolumeMultiple"],
            parameters["rewardToRiskFloor"],
            parameters["stopLow"],
            parameters["stopHigh"],
            (int)parameters["earningsWindowSessions"],
            (int)parameters["arrivalSessions"],
            parameters[TradeParameter] == 1 ? TradeInput.Swing : TradeInput.Ladder);

    public override int ArrivalSessions(IReadOnlyDictionary<string, double> parameters) => (int)parameters["arrivalSessions"];

    public override CandidateVerdict EvaluateGates(GateInputs inputs, IReadOnlyDictionary<string, double> parameters)
    {
        var result = SwingGates.Evaluate(inputs, SettingsOf(parameters));
        var marketRead = parameters[MarketGateParameter] == 1;

        // Every gate read passing and no exclusion: the market gate is left out where the rule does not read it.
        var read = result.Gates.Where(gate => marketRead || gate.Name != SwingGates.Market).ToArray();
        var fired = read.All(gate => gate.Passed) && result.Exclusions.Count == 0;

        var values = result.Gates.ToDictionary(gate => gate.Name, gate => gate.Passed ? "passed" : "failed", StringComparer.Ordinal);

        values["exclusions"] = result.Exclusions.Count == 0 ? "none" : string.Join(", ", result.Exclusions);
        values["market read"] = marketRead ? "yes" : "no";
        values["arrived"] = result.Gates.Single(gate => gate.Name == SwingGates.Trigger).Values.GetValueOrDefault(SwingGates.ArrivedValue, "none");

        return new CandidateVerdict(fired, values);
    }
}
