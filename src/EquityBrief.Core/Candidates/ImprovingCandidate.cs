using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The Fundamentals family on one index as a registered candidate: the family's rule at the settings a registration
// states, each of its history test's six dials a parameter. The score a newly read filing must lift a stock to and the
// one a trade is held at are counts in nine; the flag is 1 where a flag must have stood on the night before; the rise is 1
// where the score must stand above the filing before's; faster is 1 where the sales growth must be faster than the
// quarter before's; and the limit is the most sessions a trade is held. The family's own step keeps each rule's trades
// under its name.
// see: A Fundamentals rule is registered as the family on its index and kept by the family's own step alone
public sealed class ImprovingCandidate(string index) : OwnStepCandidate(index)
{
    public const string BuyParameter = "buy";
    public const string FlagParameter = "flag";
    public const string RiseParameter = "rise";
    public const string FasterParameter = "faster";
    public const string HoldParameter = "hold";
    public const string LimitParameter = "limit";

    public override string Version => "b1bb17189a77";

    public override string SetupFamily => ImprovingRule.Name;

    public override IReadOnlyList<string> Parameters { get; } = [BuyParameter, FlagParameter, RiseParameter, FasterParameter, HoldParameter, LimitParameter];

    // Every file the family's step runs through keeping a rule's trades, its rule first: the grid its settings are read
    // off, the step itself, the scorecard it reads as the night stored it and works for the filing before over the
    // filer's facts as first filed, the ledger's readings, the one reader of the release readings with the answers and
    // the availability it reads, the exchange's sessions a flag is counted over, the pricing of a buy at the open and of
    // the round trip, the clock, the code that reads a registration and names the rules standing, and the money and
    // statistic crossings and the store. None of the sources the swing families share, which the step never reads, so a
    // change to them moves no Fundamentals version.
    // see: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through but the catalogue
    public override IReadOnlyList<string> OwnSources { get; } =
    [
        "src/EquityBrief.Core/Families/ImprovingRule.cs",
        "src/EquityBrief.Core/Families/ImprovingGrid.cs",
        "src/EquityBrief.Core/Candidates/OwnStepCandidate.cs",
        "src/EquityBrief.Worker/Improving/ImprovingFamily.cs",
        "src/EquityBrief.Core/Ledger/FundamentalScorecard.cs",
        "src/EquityBrief.Core/Ledger/FiledFacts.cs",
        "src/EquityBrief.Core/Families/FirstFiledRevenue.cs",
        "src/EquityBrief.Core/Ledger/ValuationReading.cs",
        "src/EquityBrief.Core/Ledger/LedgerReadings.cs",
        "src/EquityBrief.Core/Ledger/ScorecardText.cs",
        "src/EquityBrief.Core/Ledger/TradeFills.cs",
        "src/EquityBrief.Core/Readings/TradeCost.cs",
        "src/EquityBrief.Core/Bars/ExchangeClosures.cs",
        "src/EquityBrief.Core/Time/IClock.cs",
        "src/EquityBrief.Data/ReleaseReadings.cs",
        "src/EquityBrief.Core/Releases/ReleaseAnswers.cs",
        "src/EquityBrief.Core/Releases/ReleaseAvailability.cs",
        "src/EquityBrief.Core/Providers/IFiledRevenueFeed.cs",
        "src/EquityBrief.Core/Providers/SecEdgarArchive.cs",
        "src/EquityBrief.Core/Providers/RetryPolicy.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluator.cs",
        "src/EquityBrief.Core/Candidates/CandidateFamily.cs",
        "src/EquityBrief.Core/Candidates/FamilyRecords.cs",
        "src/EquityBrief.Core/Candidates/ShadowColumn.cs",
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Data/StoreConnection.cs",
        .. EquityBrief.Core.Loop.RuleHooks.Sources,
    ];

    // The parameters a registration states for a setting, and the setting a registration's parameters state.
    public static IReadOnlyDictionary<string, double> ParametersOf(ImprovingSweepSetting setting) =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [BuyParameter] = setting.BuyInNine,
            [FlagParameter] = setting.FlagFirst ? 1 : 0,
            [RiseParameter] = setting.Rise ? 1 : 0,
            [FasterParameter] = setting.Faster ? 1 : 0,
            [HoldParameter] = setting.HoldInNine,
            [LimitParameter] = setting.Sessions,
        };

    public static ImprovingSweepSetting SettingsOf(IReadOnlyDictionary<string, double> parameters) => new(
        (int)parameters[BuyParameter],
        parameters[FlagParameter] == 1,
        parameters[RiseParameter] == 1,
        parameters[FasterParameter] == 1,
        (int)parameters[HoldParameter],
        (int)parameters[LimitParameter]);
}
