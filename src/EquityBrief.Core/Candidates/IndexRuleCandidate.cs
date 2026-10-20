using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Candidates;

// A rule of the S&P 400's or the S&P 600's swing families as a registered candidate: the family's rule on its own index
// at the settings a registration states, each a parameter, the levels its index's sweep read beside the family's own
// dials among them. The index families' step evaluates it over its own index's members alone. Its family is the family
// on its index, so the S&P 500's family evaluator, which reads its own families by name, never reads it, and its
// correction for luck counts its own index's rules of the family alone, at most nine. On an S&P 500 member it never
// fires. Its version pins its own source, every file its evaluation on the night runs through, and the sources every
// family rule's evaluator pins.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
public abstract class IndexRuleCandidate(string index) : FamilyRuleEvaluator
{
    // The levels every swing family's rule on an index states beside its family's own dials, each nought where the
    // level is off: the dollar volume floor's multiple, the lowest close, the quality, the sessions held where the
    // family's own cap is not, the window the industry's fall is read over, the index fund against SPY's window, the
    // credit fund above its average and rising, the breadth the market check reads, the index's own, the S&P 500's in
    // its place, or none, and from 20.11 the floor that breadth is read against, nought where the check reads none.
    public const string FloorsParameter = "floors";
    public const string LowestCloseParameter = "lowestClose";
    public const string QualityParameter = "quality";
    public const string HoldParameter = "hold";
    public const string IndustryFallParameter = "industryFall";
    public const string FundOverSpyParameter = "fundOverSpy";
    public const string CreditAverageParameter = "creditAverage";
    public const string CreditChangeParameter = "creditChange";
    public const string LargeBreadthParameter = "largeBreadth";
    public const string BreadthFloorParameter = "breadthFloor";

    // The floors the index's own breadth is read against, the four its sweep on its own history read; the S&P 500's
    // breadth is read at its own floor, the one its filter reads, which the third of them equals.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    public static IReadOnlyList<double> BreadthFloors { get; } = [0.35, 0.40, 0.45, 0.50];

    public const double LargeBreadthFloor = 0.45;

    // The breadth level's third value, a rule reading no market check at all, and the short words naming it, the level
    // each index's own search reads as its market check off, read on the night as every member above its average.
    // see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
    public const double NoMarketCheck = 2;

    public const string MarketCheckOff = "market check off";

    public static IReadOnlyList<string> Levels { get; } =
    [
        FloorsParameter,
        LowestCloseParameter,
        QualityParameter,
        HoldParameter,
        IndustryFallParameter,
        FundOverSpyParameter,
        CreditAverageParameter,
        CreditChangeParameter,
        LargeBreadthParameter,
        BreadthFloorParameter,
    ];

    // The index the rule reads, by its code in the store.
    public string Index { get; } = index;

    // The setup family the rule is a rule of, by the word the store keeps it under.
    public abstract string SetupFamily { get; }

    public override string Name => SetupFamily + "-" + WordOf(Index);

    public override string Family => FamilyOn(SetupFamily, Index);

    // The family on an index, which the register groups the index's rules of the family under.
    public static string FamilyOn(string family, string index) => family + " on the " + NameOf(index);

    // The name a reader knows each index by, and the word the evaluator's name carries.
    public static string NameOf(string index) => index == FundHoldings.MidCapIndex ? "S&P 400" : "S&P 600";

    public static string WordOf(string index) => index == FundHoldings.MidCapIndex ? "400" : "600";

    // An index rule is never evaluated over an S&P 500 member, and fires on none.
    public override FamilyResult EvaluateMember(FamilyMember member, IReadOnlyDictionary<string, double> parameters) =>
        new(member.Breakout.Ticker, Family, [], null, null, null, null, []);

    public override double? TypicalMoveOf(FamilyMember member) => null;

    // An index rule's own sources: its family's own, then the files every index rule's evaluation on the night runs
    // through, then the sources every family rule's evaluator pins.
    protected static IReadOnlyList<string> IndexSourcesWith(params string[] family) =>
    [
        .. family,
        "src/EquityBrief.Core/Candidates/IndexRuleCandidate.cs",
        "src/EquityBrief.Worker/Indices/IndexRules.cs",
        "src/EquityBrief.Worker/Indices/IndexNightRead.cs",
        "src/EquityBrief.Worker/Indices/IndexFamilies.cs",
        "src/EquityBrief.Worker/Sweep/SweepColumns.cs",
        "src/EquityBrief.Worker/Sweep/SweepBenchmark.cs",
        "src/EquityBrief.Worker/Sweep/FamilySweep.cs",
        "src/EquityBrief.Core/Readings/MemberReadings.cs",
        "src/EquityBrief.Core/Readings/IndexSwitches.cs",
        .. SourcesWith("src/EquityBrief.Core/Families/FamilyRule.cs").Skip(1),
        .. EquityBrief.Core.Loop.RuleHooks.Sources,
    ];
}
