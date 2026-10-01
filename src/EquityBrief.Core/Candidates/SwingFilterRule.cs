using System.Globalization;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Candidates;

// The swing filter as a registered candidate: every threshold of the filter as a parameter, the trade
// gate's input as 0 for the ladder's first tranche, 1 for the swing trade at the nearest bands and 2 for
// the swing trade clear of the noise, whether the market gate is read, 1 or 0, and whether a member whose
// reported quarters read deteriorating is left off, 1 or 0. A member fires where every gate read passes
// and no exclusion applies, and, where the registration leaves a deteriorating business off, where its
// state reads anything else or nothing; its verdict names the plan its trade gate read, which is the
// plan its setup is scored on, and the state it read.
//
// One evaluator for the whole family. The live filter and each variant are registrations of it with
// every threshold stated, so a variant is a whole rule and runs on unchanged when the live settings move.
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
// see: The swing filter's trade gate reads section 10's plan for the swing trade, and the plan at the nearest bands is the variant in the reward to risk variant's place
// see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
// see: The seventh swing family candidate leaves off a member whose reported quarters read deteriorating, and no live rule removes a stock for its state
public sealed class SwingFilterRule : GateEvaluator
{
    public const string EvaluatorName = "swing-filter";

    public const string TradeParameter = "trade";

    public const string MarketGateParameter = "marketGate";

    // Whether a member whose reported quarters read deteriorating is left off, 1 or 0.
    public const string SkipDeterioratingParameter = "skipDeteriorating";

    // The verdict's value naming the plan the trade gate read, as a version's settings name it.
    public const string PlanValue = "plan";

    // The verdict's value naming the state the member's reported quarters gave it, as the night stored it.
    public const string StateValue = "business state";

    public const string StateNotRead = "not read";

    public override string Name => EvaluatorName;

    public override string Version => "d30b185d9565";

    // The settings' own names as a version stores them, then the trade gate's input, the market gate and
    // the deteriorating business.
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
        SkipDeterioratingParameter,
    ];

    // The parameters a registration states for settings, the market gate read or not, and a deteriorating
    // business left off or not.
    public static IReadOnlyDictionary<string, double> ParametersOf(FilterSettings settings, bool marketGate = true, bool skipDeteriorating = false) =>
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
            [SkipDeterioratingParameter] = skipDeteriorating ? 1 : 0,
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
        var skipsDeteriorating = parameters.GetValueOrDefault(SkipDeterioratingParameter) == 1;

        // Every gate read passing and no exclusion: the market gate is left out where the rule does not read it,
        // and a member whose state reads deteriorating is left off where the rule leaves one off, every other
        // state and none firing as the gates say.
        var read = result.Gates.Where(gate => marketRead || gate.Name != SwingGates.Market).ToArray();
        var deteriorating = string.Equals(inputs.FundamentalState, FundamentalState.Deteriorating, StringComparison.Ordinal);
        var fired = read.All(gate => gate.Passed) && result.Exclusions.Count == 0 && !(skipsDeteriorating && deteriorating);

        var values = result.Gates.ToDictionary(gate => gate.Name, gate => gate.Passed ? "passed" : "failed", StringComparer.Ordinal);

        values["exclusions"] = result.Exclusions.Count == 0 ? "none" : string.Join(", ", result.Exclusions);
        values["market read"] = marketRead ? "yes" : "no";
        values["arrived"] = result.Gates.Single(gate => gate.Name == SwingGates.Trigger).Values.GetValueOrDefault(SwingGates.ArrivedValue, "none");
        values[PlanValue] = FilterSettings.Word(settings.Trade);
        values[StateValue] = inputs.FundamentalState ?? StateNotRead;
        values["skips deteriorating"] = skipsDeteriorating ? "yes" : "no";

        return new CandidateVerdict(fired, values);
    }
}
