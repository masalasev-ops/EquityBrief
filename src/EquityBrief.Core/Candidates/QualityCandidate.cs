using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// Quality at a fair price on one index as a registered candidate: the family's rule at the settings a registration states,
// each of its history test's five dials a parameter. The score's floor is a count in nine; the valuation is 0 where its
// price over earnings is not read, 1 at or below its own median and 2 in the cheapest third of its own range; the trend,
// 1 where the close must stand above its 200-day average; the book, the holdings kept; and the sale, 1 where a holding is
// also sold on a filing dropping its score. The family's own step keeps each rule's holdings under its name.
// see: A quality rule is registered as the family on its index and kept by the family's own step alone
public sealed class QualityCandidate(string index) : OwnStepCandidate(index)
{
    public const string FloorParameter = "floor";
    public const string ValuationParameter = "valuation";
    public const string TrendParameter = "trend";
    public const string BookParameter = "book";
    public const string SaleParameter = "sale";

    public override string Version => "cd9612244d07";

    public override string SetupFamily => QualityRule.Name;

    public override IReadOnlyList<string> Parameters { get; } = [FloorParameter, ValuationParameter, TrendParameter, BookParameter, SaleParameter];

    // Every file the family's step runs through keeping a rule's holdings, its rule first: the step itself, the scorecard
    // and the valuation's place it reads as the night stored them, the pricing of a buy at the open and of the round trip,
    // the trend's average, the code that reads a registration and names the rules standing, and the money and statistic
    // crossings and the store. None of the sources the swing families share, which the step never reads, so a change to
    // them moves no quality version.
    // see: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through but the catalogue
    public override IReadOnlyList<string> OwnSources { get; } =
    [
        "src/EquityBrief.Core/Families/QualityRule.cs",
        "src/EquityBrief.Core/Candidates/OwnStepCandidate.cs",
        "src/EquityBrief.Worker/Quality/QualityFamily.cs",
        "src/EquityBrief.Core/Ledger/FundamentalScorecard.cs",
        "src/EquityBrief.Core/Ledger/LedgerReadings.cs",
        "src/EquityBrief.Core/Ledger/TradeFills.cs",
        "src/EquityBrief.Core/Readings/TradeCost.cs",
        "src/EquityBrief.Core/Indicators/IndicatorSeries.cs",
        "src/EquityBrief.Worker/Indicators/IndicatorEngine.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluator.cs",
        "src/EquityBrief.Core/Candidates/CandidateFamily.cs",
        "src/EquityBrief.Core/Candidates/FamilyRecords.cs",
        "src/EquityBrief.Core/Candidates/ShadowColumn.cs",
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Data/StoreConnection.cs",
        .. EquityBrief.Core.Loop.RuleHooks.Sources,
    ];

    // The parameters a registration states for settings, and the settings a registration's parameters state.
    public static IReadOnlyDictionary<string, double> ParametersOf(QualitySettings settings)
    {
        var parameters = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [FloorParameter] = settings.ScoreFloorInNine,
            [ValuationParameter] = settings.Valuation switch { QualityValuation.Off => 0, QualityValuation.AtOrBelowMedian => 1, _ => 2 },
            [TrendParameter] = settings.Trend ? 1 : 0,
            [BookParameter] = settings.Holdings,
            [SaleParameter] = settings.SellOnFiling ? 1 : 0,
        };

        if (settings.SoldUnderAverage)
        {
            parameters[AverageParameter] = 1;
        }

        return parameters;
    }

    public static QualitySettings SettingsOf(IReadOnlyDictionary<string, double> parameters) => new(
        (int)parameters[FloorParameter],
        parameters[ValuationParameter] switch { 0 => QualityValuation.Off, 1 => QualityValuation.AtOrBelowMedian, _ => QualityValuation.CheapestThird },
        parameters[TrendParameter] == 1,
        (int)parameters[BookParameter],
        parameters[SaleParameter] == 1,
        parameters.GetValueOrDefault(AverageParameter) == 1);

    // From 21.16 a book's holding also sold at a close under its 200-day average, stated as 1 and otherwise not stated, so
    // a registration from before it reads as one with the sale off; the Fundamentals family reads it under the same name.
    // see: Every change the engines propose is one its family's night reads, so each is put to the operator
    public const string AverageParameter = "average";

    // The parameters a book's registration may state beside the ones it must.
    public static IReadOnlyList<string> OptionalOf(CandidateEvaluator? evaluator) =>
        evaluator is QualityCandidate or ImprovingCandidate ? [AverageParameter] : [];
}
