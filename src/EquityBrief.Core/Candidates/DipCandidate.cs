using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The short-term dip on one index as a registered candidate: the family's rule at the setting a registration states,
// each dial of its sweep's grid a parameter, as the sweep's keys state it. The drop is the 2-session RSI's floor above
// nothing and a fall in typical moves written less than nothing; the trend 200 for the close above its 200-session
// average and 50 for that with the 50-session average above it; the exit 5 for the 5-session average, 65 for the RSI
// back above 65 and 1 for the first up close; the time limit and the stop as stated; the market setting its place among
// the index's levels as the sweep's market dial orders them, nought for none; and the volume the multiple of its
// 50-session average the drop's mean volume stands under, nought where it is not read. The dip's own step keeps each
// rule's trades under its name.
// see: A dip rule is registered as the family on its index and kept by the dip's own step alone
public sealed class DipCandidate(string index) : OwnStepCandidate(index)
{
    public const string DropParameter = "drop";
    public const string TrendParameter = "trend";
    public const string LimitParameter = "limit";
    public const string StopParameter = "stop";
    public const string MarketParameter = "market";
    public const string VolumeParameter = "volume";

    // The level a trade is sold at, named sell from 21.4 so the dip's rule can state the hooks' exit beside its own
    // dials; a registration from before that names it exit, and is read under either name.
    public const string SellParameter = "sell";

    public const string SellParameterBefore = "exit";

    public override string Version => "c29d7e670cd3";

    public override string SetupFamily => DipRule.Name;

    public override IReadOnlyList<string> Parameters { get; } = [DropParameter, TrendParameter, SellParameter, LimitParameter, StopParameter, MarketParameter, VolumeParameter];

    // The sell dial a registration states, under its name or the name it carried before 21.4.
    public static double SellOf(IReadOnlyDictionary<string, double> parameters) =>
        parameters.TryGetValue(SellParameter, out var sell) ? sell : parameters[SellParameterBefore];

    // The hooks a registration states beside the dip's own dials, read as a swing rule's are: a registration from
    // before 21.4, which names no sell dial, names the dial exit, so its exit is the dial and no hook.
    public static Loop.RuleHooks HooksOf(IReadOnlyDictionary<string, double> parameters)
    {
        var dials = parameters.ContainsKey(SellParameter) ? Dials : [.. Dials, SellParameterBefore];

        return Loop.RuleHooks.Of(parameters.Where(pair => !dials.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    // A setting stated in the form from before 21.4, its sell dial named exit and no sell dial beside it, read with the
    // dial under its name, so a remedy or a form written then registers the same rule; a setting naming both leaves its
    // exit to the hooks.
    public static IReadOnlyDictionary<string, double> Renamed(IReadOnlyDictionary<string, double> parameters)
    {
        if (parameters.ContainsKey(SellParameter) || !parameters.ContainsKey(SellParameterBefore))
        {
            return parameters;
        }

        var renamed = parameters.Where(pair => pair.Key != SellParameterBefore).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        renamed[SellParameter] = parameters[SellParameterBefore];

        return renamed;
    }

    static readonly string[] Dials = [DropParameter, TrendParameter, SellParameter, LimitParameter, StopParameter, MarketParameter, VolumeParameter];

    // Every file the dip's step runs through keeping a rule's trades, its rule first, as the compiled code reaches them:
    // the step itself and the index night's reader it reads each member's year through, the other families' open trades'
    // readers it gives way to, the walk's fill and round trip, the code that reads a registration and names the rules
    // standing, and the money and statistic crossings and the store. None of the sources the swing families share but
    // those the index night's reader runs through, so a change to the swing filter moves no dip version.
    // see: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through but the catalogue
    public override IReadOnlyList<string> OwnSources { get; } =
    [
        "src/EquityBrief.Core/Families/DipRule.cs",
        "src/EquityBrief.Core/Bars/ExchangeClosures.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluator.cs",
        "src/EquityBrief.Core/Candidates/CandidateFamily.cs",
        "src/EquityBrief.Core/Candidates/FamilyRecords.cs",
        "src/EquityBrief.Core/Candidates/OwnStepCandidate.cs",
        "src/EquityBrief.Core/Candidates/ShadowColumn.cs",
        "src/EquityBrief.Core/Cards/RuleRows.cs",
        "src/EquityBrief.Core/Families/FamilyList.cs",
        "src/EquityBrief.Core/Families/FamilyRule.cs",
        "src/EquityBrief.Core/Families/SetupFamilies.cs",
        "src/EquityBrief.Core/Filter/OpenTrades.cs",
        "src/EquityBrief.Core/Filter/SwingGates.cs",
        "src/EquityBrief.Core/Filter/SwingReadings.cs",
        "src/EquityBrief.Core/Indicators/IndicatorSeries.cs",
        "src/EquityBrief.Core/Ladders/TrendSeries.cs",
        "src/EquityBrief.Core/Ledger/LedgerReadings.cs",
        "src/EquityBrief.Core/Ledger/SetupAnchor.cs",
        "src/EquityBrief.Core/Ledger/TradeFills.cs",
        "src/EquityBrief.Core/Prices/PriceForm.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Core/Readings/MemberReadings.cs",
        "src/EquityBrief.Core/Readings/TradeCost.cs",
        "src/EquityBrief.Core/Sweep/FamilyWalks.cs",
        "src/EquityBrief.Core/Sweep/SweepAxes.cs",
        "src/EquityBrief.Core/Sweep/SweepReadings.cs",
        "src/EquityBrief.Core/Sweep/SweepWindows.cs",
        "src/EquityBrief.Core/Swings/SwingSeries.cs",
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Data/StoreConnection.cs",
        "src/EquityBrief.Worker/Dips/DipFamily.cs",
        "src/EquityBrief.Worker/Families/FamilyLister.cs",
        "src/EquityBrief.Worker/Indices/IndexFamilies.cs",
        "src/EquityBrief.Worker/Indices/IndexNightRead.cs",
        "src/EquityBrief.Worker/Sweep/DriftSweep.cs",
        "src/EquityBrief.Worker/Sweep/FamilySweep.cs",
        "src/EquityBrief.Worker/Sweep/SweepBenchmark.cs",
        "src/EquityBrief.Worker/Sweep/SweepColumns.cs",
        "src/EquityBrief.Worker/Sweep/SweepHistory.cs",
        "src/EquityBrief.Worker/Sweep/SweepIdeas.cs",
        .. EquityBrief.Core.Loop.RuleHooks.Sources,
        "src/EquityBrief.Core/Loop/LooseSetup.cs",
    ];

    // The setting a registration's parameters state on the evaluator's index, its market setting the level its place
    // names among the index's.
    public DipSetting SettingOf(IReadOnlyDictionary<string, double> parameters) => DipRule.SettingOf(
        parameters[DropParameter],
        parameters[TrendParameter],
        (int)SellOf(parameters),
        (int)parameters[LimitParameter],
        parameters[StopParameter],
        DipRule.MarketLevels(Index)[(int)parameters[MarketParameter]].Key,
        parameters[VolumeParameter]);
}
