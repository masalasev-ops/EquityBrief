using System.Globalization;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Candidates;

// The swing filter as a registered candidate: every threshold of the filter as a parameter, the trade
// gate's input as 0 for the ladder's first tranche, 1 for the swing trade at the nearest bands and 2 for
// the swing trade clear of the noise, whether the market gate is read, 1 or 0, whether a member whose
// reported quarters read deteriorating is left off, 1 or 0, and whether a member's analysts' estimates have
// to have been raised, 1 or 0. A member fires where every gate read passes and no exclusion applies, where
// the registration leaves a deteriorating business off, where its state reads anything else or nothing, and
// where the registration reads estimates, where the night read them raised; its verdict names the plan its
// trade gate read, which is the plan its setup is scored on, the state it read and the estimate it read.
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

    // The sector leaders' reading in place of the trend and strength gate: the top sectors a member's has to
    // be among and the share of its sector it has to be inside, one in so many rounded up, both nought where
    // the trend and strength gate is read as the filter reads it.
    // see: The sector leaders are a variant of the pullback's starting point and not a family of their own
    public const string LeaderSectorsParameter = "leaderSectors";

    public const string LeaderShareOfParameter = "leaderShareOf";

    // The most a night the rule's list keeps of the members it fires on, the first in the list's own order once
    // its own open trades have kept a stock off, and nought for every one. A member's verdict is its own and
    // the night's order is the list's, so it is read where the rule's record is read and moves no verdict.
    // see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
    public const string BestOfParameter = "bestOf";

    // Whether a member fires only where its analysts raised their estimate for its current fiscal year over the
    // thirty days before the night, 1 or 0, read off the answer the night asks for a member the rule passes on
    // everything else.
    // see: The pullback's sector leaders' rule is retired and the analysts' revisions variant registered in its place
    // see: A member's estimates are raised where its current fiscal year's consensus earnings estimate stands above its level 30 days before
    public const string RaisedEstimatesParameter = "raisedEstimates";

    // The verdict's value naming the estimate the rule read, where it reads one.
    public const string EstimatesValue = "estimates";

    // The verdict's value naming where the member stood in its sector, where the rule reads leadership.
    public const string LeadershipValue = "leadership";

    // The verdict's value naming the plan the trade gate read, as a version's settings name it.
    public const string PlanValue = "plan";

    // The verdict's value naming the state the member's reported quarters gave it, as the night stored it.
    public const string StateValue = "business state";

    public const string StateNotRead = "not read";

    public override string Name => EvaluatorName;

    public override string Version => "b4425b7cbb05";

    // The settings' own names as a version stores them, then the trade gate's input, the market gate, the
    // deteriorating business, sector leadership, the most a night the list keeps and the raised estimates.
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
        LeaderSectorsParameter,
        LeaderShareOfParameter,
        BestOfParameter,
        RaisedEstimatesParameter,
    ];

    // The parameters a registration states for settings, the market gate read or not, a deteriorating
    // business left off or not, sector leadership read in place of the trend and strength gate or not, the
    // most a night its list keeps, nought for every member it fires on, and raised estimates read or not.
    public static IReadOnlyDictionary<string, double> ParametersOf(FilterSettings settings, bool marketGate = true, bool skipDeteriorating = false, Families.LeaderSettings? leadership = null, int bestOf = 0, bool raisedEstimates = false) =>
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
            [LeaderSectorsParameter] = leadership?.TopSectors ?? 0,
            [LeaderShareOfParameter] = leadership?.ShareOf ?? 0,
            [BestOfParameter] = bestOf,
            [RaisedEstimatesParameter] = raisedEstimates ? 1 : 0,
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
        var leaderSectors = (int)parameters.GetValueOrDefault(LeaderSectorsParameter);
        var leaderShareOf = (int)parameters.GetValueOrDefault(LeaderShareOfParameter);
        var readsLeadership = leaderSectors > 0 && leaderShareOf > 0;
        var readsEstimates = parameters.GetValueOrDefault(RaisedEstimatesParameter) == 1;
        var deteriorating = string.Equals(inputs.FundamentalState, FundamentalState.Deteriorating, StringComparison.Ordinal);
        var leads = !readsLeadership || Leads(inputs.Leadership, leaderSectors, leaderShareOf);

        // Every gate read passing and no exclusion, then the estimates where the rule reads them, a member whose
        // estimate the night did not read firing on none, as a gate fails on an absent value.
        var fired = FiresBeforeEstimates(result, marketRead, readsLeadership, skipsDeteriorating && deteriorating, leads)
            && (!readsEstimates || inputs.Estimates?.Raised == true);

        var values = result.Gates.ToDictionary(gate => gate.Name, gate => gate.Passed ? "passed" : "failed", StringComparer.Ordinal);

        values["exclusions"] = result.Exclusions.Count == 0 ? "none" : string.Join(", ", result.Exclusions);
        values["market read"] = marketRead ? "yes" : "no";
        values["arrived"] = result.Gates.Single(gate => gate.Name == SwingGates.Trigger).Values.GetValueOrDefault(SwingGates.ArrivedValue, "none");
        values[PlanValue] = FilterSettings.Word(settings.Trade);
        values[StateValue] = inputs.FundamentalState ?? StateNotRead;
        values["skips deteriorating"] = skipsDeteriorating ? "yes" : "no";

        if (readsLeadership)
        {
            values[LeadershipValue] = inputs.Leadership is { Sector.Rank: { } rank, Place: { } place } standing
                ? FormattableString.Invariant($"sector {rank} of the top {leaderSectors}, place {place} of {standing.Sector!.Counted} against {Families.LeaderRule.Cut(standing.Sector.Counted, leaderShareOf)}{(leads ? ", leading" : ", not leading")}")
                : "not ranked";
        }

        if (readsEstimates)
        {
            values[EstimatesValue] = Words(inputs.Estimates);
        }

        return new CandidateVerdict(fired, values);
    }

    // Every gate a rule reads passing, no exclusion, no deteriorating business it leaves off and its sector's lead
    // where it reads one: what a member has to pass before the estimates a rule reads are asked for.
    static bool FiresBeforeEstimates(GateResult result, bool marketRead, bool readsLeadership, bool leftOff, bool leads) =>
        result.Gates
            .Where(gate => marketRead || gate.Name != SwingGates.Market)
            .Where(gate => !readsLeadership || gate.Name != SwingGates.Trend)
            .All(gate => gate.Passed)
        && result.Exclusions.Count == 0
        && !leftOff
        && leads;

    // Whether a rule reading analysts' estimates passes a member on everything else, which is the member the night asks
    // the provider's estimates for; no rule reading none asks for any.
    // see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night
    public static bool AsksForEstimates(GateInputs inputs, IReadOnlyDictionary<string, double> parameters)
    {
        if (parameters.GetValueOrDefault(RaisedEstimatesParameter) != 1)
        {
            return false;
        }

        var leaderSectors = (int)parameters.GetValueOrDefault(LeaderSectorsParameter);
        var leaderShareOf = (int)parameters.GetValueOrDefault(LeaderShareOfParameter);
        var readsLeadership = leaderSectors > 0 && leaderShareOf > 0;

        return FiresBeforeEstimates(
            SwingGates.Evaluate(inputs, SettingsOf(parameters)),
            parameters[MarketGateParameter] == 1,
            readsLeadership,
            parameters.GetValueOrDefault(SkipDeterioratingParameter) == 1 && string.Equals(inputs.FundamentalState, FundamentalState.Deteriorating, StringComparison.Ordinal),
            !readsLeadership || Leads(inputs.Leadership, leaderSectors, leaderShareOf));
    }

    // The estimate a verdict records: raised or not with both figures and the fiscal year, or why none was read.
    public static string Words(EstimateReading? estimates) => estimates switch
    {
        null => "not read: the night asked for none",
        { Raised: { } raised, Current: { } now, DaysAgo: { } then } => FormattableString.Invariant(
            $"{(raised ? "raised" : "not raised")}: {now} now against {then} {EstimateReading.Days} days before, for the year to {estimates.YearEnd:yyyy-MM-dd}"),
        _ => "not read: " + (estimates.NotRead ?? "the answer files no figure"),
    };

    // A member inside the top sectors and the share of its own the rule reads, by the night's standings; a
    // member in no ranked sector or holding no place leads none.
    public static bool Leads(Families.LeaderStanding? standing, int topSectors, int shareOf) =>
        standing is { Sector: { Rank: { } rank } sector, Place: { } place }
        && rank <= topSectors
        && place <= Families.LeaderRule.Cut(sector.Counted, shareOf);
}
