using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The sector heavyweights on the S&P 400 or the S&P 600 as a registered candidate, in either design its index's sweeps
// read: design (a), each sector's largest members of the index leading their sector's members, at the size cut, the
// look-back, the leaders a sector, a leader's beta and the exits its sweep's grid holds, a size cut of nought standing
// for every company; or design (b), the index's members following the S&P 500's leading industries, at the window the
// lead is read over, the industries read, nought for every one leading, and the members bought in each. Each states its
// quality and its dollar volume floor beside them, and a design's own dials are nought on the other's registration. The
// index families' step keeps its book.
// see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
public sealed class IndexHeavyweightCandidate(string index) : IndexRuleCandidate(index)
{
    public const string DesignParameter = "design";
    public const string LargestParameter = "largest";
    public const string LookBackParameter = "lookBack";
    public const string LeadersParameter = "leaders";
    public const string BetaParameter = "highBeta";
    public const string LeadingExitParameter = "soldOnLeading";
    public const string AverageExitParameter = "soldUnderAverage";
    public const string WindowParameter = "window";
    public const string IndustriesParameter = "industries";
    public const string MembersParameter = "members";

    // The two designs as a registration states them.
    public const int DesignA = 0;
    public const int DesignB = 1;

    public override string Version => "34fc5ff8ccd6";

    public override string SetupFamily => HeavyweightRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
    [
        DesignParameter, LargestParameter, LookBackParameter, LeadersParameter, BetaParameter, LeadingExitParameter,
        AverageExitParameter, WindowParameter, IndustriesParameter, MembersParameter, QualityParameter, FloorsParameter,
    ];

    public override IReadOnlyList<string> OwnSources { get; } =
        IndexSourcesWith(
            "src/EquityBrief.Worker/Sweep/HeavyweightSweep.cs",
            "src/EquityBrief.Core/Families/HeavyweightRule.cs",
            "src/EquityBrief.Core/Families/CompanyValue.cs",
            "src/EquityBrief.Core/Families/CompanyRank.cs",
            "src/EquityBrief.Core/Families/GicsSectors.cs");
}
