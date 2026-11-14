using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The sector heavyweights as a registered candidate: the heavyweights' rule at the settings a registration states, each
// of them a parameter, its holdings kept in a book of its own by the book's stage. The size cut is the count of each
// sector's largest companies, nought for every one; the look-back and the leaders are counts; the fund, the beta, the
// week and the two exits are each 1 or 0.
// see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
// see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants
public sealed class SectorHeavyweightCandidate : BookEvaluator
{
    public const string EvaluatorName = "heavyweight";

    public const string LargestParameter = "largest";
    public const string LookBackParameter = "lookBack";
    public const string LeadersParameter = "leaders";
    public const string FundParameter = "fundReturn";
    public const string BetaParameter = "highBeta";
    public const string WeeklyParameter = "weekly";
    public const string LeadingExitParameter = "soldOnLeading";
    public const string AverageExitParameter = "soldUnderAverage";

    public override string Name => EvaluatorName;

    public override string Version => "413adb9d4ce2";

    public override string Family => HeavyweightRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
        [LargestParameter, LookBackParameter, LeadersParameter, FundParameter, BetaParameter, WeeklyParameter, LeadingExitParameter, AverageExitParameter];

    // Every file the book's evaluation runs through, the rule's first: the readings it values, ranks and places a
    // member by, the stage that keeps the book, the shared evaluator code that reads a registration, the money and
    // statistic crossings and the store, and the indicator arithmetic whose averages its trend gate reads. None of the
    // sources the listings' candidates share, which no book reads, so a change to them moves no heavyweights version. From
    // 23.10's second part the hooks a book reads, its conditions and its score over the catalogue's readings, and not the
    // plan's walk, since a book states no plan.
    // see: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through but the catalogue
    // see: The S&P 500's swing filter reads the hooks its night keeps a pick under, and its heavyweight book a book's
    public override IReadOnlyList<string> OwnSources { get; } =
    [
        "src/EquityBrief.Core/Families/HeavyweightRule.cs",
        "src/EquityBrief.Core/Families/CompanyValue.cs",
        "src/EquityBrief.Core/Families/CompanyRank.cs",
        "src/EquityBrief.Core/Families/GicsSectors.cs",
        "src/EquityBrief.Worker/Families/HeavyweightBook.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluator.cs",
        "src/EquityBrief.Core/Candidates/CandidateFamily.cs",
        "src/EquityBrief.Core/Candidates/FamilyRecords.cs",
        "src/EquityBrief.Core/Bars/ExchangeClosures.cs",
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Data/StoreConnection.cs",
        "src/EquityBrief.Core/Indicators/IndicatorSeries.cs",
        "src/EquityBrief.Worker/Indicators/IndicatorEngine.cs",
        "src/EquityBrief.Core/Ledger/LedgerReadings.cs",
        .. EquityBrief.Core.Loop.RuleHooks.Sources.Except(EquityBrief.Core.Loop.RuleHooks.PlanWalkSources, StringComparer.Ordinal),

        // From the operator's order of 2026-11-14 the stop and the take-profit each holding is bought with: the price it
        // turned down at before, read off its swings over its stored and pulled history, and its typical daily move.
        // see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
        "src/EquityBrief.Worker/Loop/PlanTrades.cs",
        "src/EquityBrief.Core/Swings/SwingSeries.cs",
        "src/EquityBrief.Core/Levels/LevelSeries.cs",
        "src/EquityBrief.Core/Ladders/LadderSeries.cs",
        "src/EquityBrief.Core/Prices/PriceForm.cs",
        "src/EquityBrief.Core/Sweep/SweepReadings.cs",
        "src/EquityBrief.Worker/Filter/SessionReplay.cs",
    ];

    // The parameters a registration states for settings, and the settings a registration's parameters state.
    public static IReadOnlyDictionary<string, double> ParametersOf(HeavyweightSettings settings) =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [LargestParameter] = settings.Largest == HeavyweightRule.EveryCompany ? 0 : settings.Largest,
            [LookBackParameter] = settings.LookBack,
            [LeadersParameter] = settings.Leaders,
            [FundParameter] = settings.FundReturn ? 1 : 0,
            [BetaParameter] = settings.HighBeta ? 1 : 0,
            [WeeklyParameter] = settings.Weekly ? 1 : 0,
            [LeadingExitParameter] = settings.SoldOnLeading ? 1 : 0,
            [AverageExitParameter] = settings.SoldUnderAverage ? 1 : 0,
        };

    public override HeavyweightSettings SettingsOf(IReadOnlyDictionary<string, double> parameters)
    {
        var settings = new HeavyweightSettings(
            parameters[LargestParameter] == 0 ? HeavyweightRule.EveryCompany : (int)parameters[LargestParameter],
            (int)parameters[LookBackParameter],
            (int)parameters[LeadersParameter],
            parameters[BetaParameter] == 1,
            parameters[FundParameter] == 1,
            parameters[WeeklyParameter] == 1,
            parameters[LeadingExitParameter] == 1,
            parameters[AverageExitParameter] == 1);

        return settings.Largest < 1 || settings.LookBack < 1 || settings.Leaders < 1 || !(settings.SoldOnLeading || settings.SoldUnderAverage)
            ? throw new InvalidOperationException(
                "A sector heavyweights registration reads a size cut, a look-back and leaders of at least one, and sells on no longer leading, on a close under the average or on both.")
            : settings;
    }
}
