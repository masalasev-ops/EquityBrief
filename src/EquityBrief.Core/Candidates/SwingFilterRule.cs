using System.Globalization;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Candidates;

// The swing filter as a registered candidate: every threshold of the filter as a parameter, the trade
// gate's input as 0 for the ladder's first tranche, 1 for the swing trade at the nearest bands and 2 for
// the swing trade clear of the noise, and whether the market gate is read, 1 or 0. A member fires where
// every gate read passes and no exclusion applies, and its verdict names the plan its trade gate read,
// which is the plan its setup is scored on.
//
// One evaluator for the whole family. The live filter and each variant are registrations of it with
// every threshold stated, so a variant is a whole rule and runs on unchanged when the live settings move.
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
// see: The swing filter's trade gate reads section 10's plan for the swing trade, and the plan at the nearest bands is the variant in the reward to risk variant's place
// see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
public sealed class SwingFilterRule : GateEvaluator
{
    public const string EvaluatorName = "swing-filter";

    public const string TradeParameter = "trade";

    public const string MarketGateParameter = "marketGate";

    // The verdict's value naming the plan the trade gate read, as a version's settings name it.
    public const string PlanValue = "plan";

    public override string Name => EvaluatorName;

    public override string Version => "abcf6df67b7d";

    // The settings' own names as a version stores them, then the trade gate's input and the market gate.
    public override IReadOnlyList<string> Parameters { get; } =
    [
        "breadthFloor",
        "strengthFloor",
        "depthLow",
        "depthHigh",
        "dryUpCeiling",
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
            ["rewardToRiskFloor"] = settings.RewardToRiskFloor,
            ["stopLow"] = settings.StopLow,
            ["stopHigh"] = settings.StopHigh,
            ["earningsWindowSessions"] = settings.EarningsWindowSessions,
            ["arrivalSessions"] = settings.ArrivalSessions,
            [TradeParameter] = settings.Trade switch
            {
                TradeInput.Ladder => 0,
                TradeInput.Swing => 1,
                _ => 2,
            },
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
            parameters["rewardToRiskFloor"],
            parameters["stopLow"],
            parameters["stopHigh"],
            (int)parameters["earningsWindowSessions"],
            (int)parameters["arrivalSessions"],
            parameters[TradeParameter] switch
            {
                0 => TradeInput.Ladder,
                1 => TradeInput.Swing,
                2 => TradeInput.Clear,
                var other => throw new InvalidOperationException(FormattableString.Invariant($"A swing filter registration's trade parameter is {other}, which names no plan: 0 is the ladder's first tranche, 1 the swing trade at the nearest bands and 2 the swing trade clear of the noise.")),
            });

    public override int ArrivalSessions(IReadOnlyDictionary<string, double> parameters) => (int)parameters["arrivalSessions"];

    public override CandidateVerdict EvaluateGates(GateInputs inputs, IReadOnlyDictionary<string, double> parameters)
    {
        var settings = SettingsOf(parameters);
        var result = SwingGates.Evaluate(inputs, settings);
        var marketRead = parameters[MarketGateParameter] == 1;

        // Every gate read passing and no exclusion: the market gate is left out where the rule does not read it.
        var read = result.Gates.Where(gate => marketRead || gate.Name != SwingGates.Market).ToArray();
        var fired = read.All(gate => gate.Passed) && result.Exclusions.Count == 0;

        var values = result.Gates.ToDictionary(gate => gate.Name, gate => gate.Passed ? "passed" : "failed", StringComparer.Ordinal);

        values["exclusions"] = result.Exclusions.Count == 0 ? "none" : string.Join(", ", result.Exclusions);
        values["market read"] = marketRead ? "yes" : "no";
        values["arrived"] = result.Gates.Single(gate => gate.Name == SwingGates.Trigger).Values.GetValueOrDefault(SwingGates.ArrivedValue, "none");
        values[PlanValue] = FilterSettings.Word(settings.Trade);

        return new CandidateVerdict(fired, values);
    }
}
