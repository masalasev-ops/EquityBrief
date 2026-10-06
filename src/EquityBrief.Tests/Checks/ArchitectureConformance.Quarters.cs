using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

public partial class ArchitectureConformance
{
    // The claims the fundamentals item lands, 35 of the 39 its planning pass predicted: the quarter fetcher
    // and the fundamental reader with their catalogue and matrix rows, their three stores and two night steps,
    // the list's three new parts, what the numbers say as its five, Past picks' two, section 17's ten rows,
    // section 18's five and the fixture's row. The other four were the run page's Fundamentals region, which
    // the operator had taken off the page the day it landed, the quarters step's line standing in its place. Section 4's
    // pattern table is placed rather than counted, and the list's order is a part it already had, reworded.
    // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
    //
    // A property rather than a field: the lists it is spread into are fields of this class declared in other
    // files, and the order fields of one class initialise in across its files is not one to rely on.
    internal static string[] FundamentalsClaims =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Fundamental reader"),
        CheckReach.Key(Scope.MatrixTable, "Fundamental reader"),
        CheckReach.Key(Scope.CatalogueTable, "Quarter fetcher"),
        CheckReach.Key(Scope.MatrixTable, "Quarter fetcher"),
        CheckReach.Key(Scope.StoresTable, "Reported quarters"),
        CheckReach.Key(Scope.StoresTable, "Quarter asks"),
        CheckReach.Key(Scope.StoresTable, "Fundamental readings"),
        CheckReach.Key(NightlyRunSteps.Heading, ReadingsStep),
        CheckReach.Key(NightlyRunSteps.Heading, QuartersStep),
        .. Reading.ReadSurface.FundamentalsScreenClaims,
        CheckReach.Key(Scope.LimitsTable, "Quarters step"),
        CheckReach.Key(Scope.LimitsTable, "Quarters fill"),
        CheckReach.Key(Scope.LimitsTable, "Quarter prices"),
        CheckReach.Key(Scope.LimitsTable, "Trajectory quarters"),
        CheckReach.Key(Scope.LimitsTable, "Business state"),
        CheckReach.Key(Scope.LimitsTable, "Estimate record"),
        CheckReach.Key(Scope.LimitsTable, "Met tolerance"),
        CheckReach.Key(Scope.LimitsTable, "Earnings quality"),
        CheckReach.Key(Scope.LimitsTable, "Valuation position"),
        CheckReach.Key(Scope.LimitsTable, "Weighted calls a quarters ask"),
        CheckReach.Key(Scope.FailureTable, "A member the provider returns no quarter for"),
        CheckReach.Key(Scope.FailureTable, "A member's new quarter is not yet posted when it is asked"),
        CheckReach.Key(Scope.FailureTable, "The provider refuses a quarters ask"),
        CheckReach.Key(Scope.FailureTable, "The quarters step reaches its limit or the day's allowance"),
        CheckReach.Key(Scope.FailureTable, "A night that stored no readings of the reported quarters"),
        CheckReach.Key(Scope.FixtureTable, "reported quarters"),
    ];

    // Section 14's two steps, word for word, which the night's order and the claims both key on.
    internal const string ReadingsStep =
        "Read the reported quarters of every member, from the quarters fetched on the nights before this one: the trajectory, sales and the operating margin against a year earlier over the two newest quarters; the record against the analysts' estimates; earnings quality, operating cash flow against net income over four quarters; and the valuation position, tonight's multiple in the range of the quarters' own; and the state they give, improving, steady, deteriorating, not enough quarters or no fundamentals yet, read from the trajectory alone, a member holding none written as any other (see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone). Then store every member's readings of the three indices and the switches beside them, each through the one function the sweeps read it with (see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing).";

    internal const string QuartersStep =
        "Ask the provider for the reported quarters of the members due, one request for a member's fundamentals and one for three years of its closes where the answer is stored: every member once at the start, at most 260 a night; a member on the first night after it reports; a member whose new quarter the answer does not yet carry on each of the five nights after and weekly after that; and a member joining the index on its first night; bounded by its own limit and by the day's allowance and never by the night's deadline (see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted).";
}
