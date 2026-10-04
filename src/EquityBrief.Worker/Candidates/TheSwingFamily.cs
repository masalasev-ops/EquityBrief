using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;

namespace EquityBrief.Worker.Candidates;

// The swing family, written down rather than typed at the command line: the live filter at the open
// version's settings and eight variants, each the same whole rule with one thing moved, the sixth leaving
// off a member whose reported quarters read deteriorating, the seventh firing only where the member's
// analysts raised their estimate for its current fiscal year over the thirty days before the night, in the
// place the sector leaders' rule held until it was retired, and the eighth keeping the night's best three in
// the list's own order, all run by the one evaluator and each named for the version it was defined against,
// since a variant defined against another version's settings is another rule. Each rule's record keeps one
// open trade a stock of its own. And the words each retirement of phase 10's three carries.
// see: The swing filter's trade gate reads section 10's plan for the swing trade, and the plan at the nearest bands is the variant in the reward to risk variant's place
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
// see: The three phase 10 candidates are retired when the swing family registers, and each retirement says no result of theirs was read
// see: The seventh swing family candidate leaves off a member whose reported quarters read deteriorating, and no live rule removes a stock for its state
// see: The sector leaders are a variant of the pullback's starting point and not a family of their own
// see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
// see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
// see: The pullback's sector leaders' rule is retired and the analysts' revisions variant registered in its place
public static class TheSwingFamily
{
    // The words every retirement at the family's registration carries.
    public const string NothingRead = "no result of this candidate was read before it was retired";

    public const string Evidence =
        "retired when the swing family registered, since from phase 12 no reason chooses tonight's list and the question " +
        "this candidate was registered to answer no longer exists; " + NothingRead;

    public const string Rule =
        "the swing filter's gates, its trigger's arrival inside its window and its exclusions at every setting stated, " +
        "a member firing where every gate read passes and no exclusion applies, and its record keeping one open trade " +
        "a stock of its own, free the night after the trade ends";

    // The deteriorating business's rule: the same, with a member whose reported quarters read deteriorating left off.
    public const string RuleSkippingDeteriorating =
        Rule + ", and a member whose reported quarters read deteriorating on the night left off, every other state and none firing as the gates say";

    // The sector leaders' rule: the same, with sector leadership read in place of the trend and strength gate.
    public const string RuleReadingLeadership =
        Rule + ", with the trend and strength gate read as sector leadership: the member's sector among the stated top of the sectors " +
        "ranked by their members' median long return and the member inside the stated share of its sector by its own";

    // The analysts' revisions rule: the same, firing only where the member's estimates were raised.
    public const string RuleReadingEstimates =
        Rule + ", firing only where the provider's consensus estimate of the member's earnings a share for its current fiscal " +
        "year, as the answer the night asks for a member the rule passes on everything else files it, stands above the same " +
        "estimate thirty days before, a member whose answer files neither or that the provider did not serve firing on none";

    // The night's best three's rule: the same, with only the first three of the members it fires on a night kept.
    public const string RuleKeepingTheBest =
        Rule + ", and of the members it fires on a night only the first three kept, in the list's own order of reward " +
        "to risk on the plan its trade gate reads, strength and the setup band's strength, each higher first, then the " +
        "ticker, once its own open trades have kept a stock off; a member past the three is no trade";

    public const string Test =
        "a sign-flip test over blocks of 63 exchange sessions, read at 8, 12 and 16 non-empty whole blocks, against each " +
        "setup's own calibrated bar, at 0.05 over the distinct trials spent across the looks";

    // The one thing each variant moves: the plan the trade gate reads, and the other side of each live
    // setting a variant moves to.
    public const TradeInput VariantTrade = TradeInput.Swing;

    public const double VariantDepthLow = 1;

    public const double VariantDepthHigh = 3;

    public const double VariantStrength = 2.0 / 3.0;

    public const int VariantArrival = 1;

    // How many a night the best three's list keeps, which the ideas' run tried as its idea c.
    public const int VariantBestOf = 3;

    public const string NearestBandsName = "the swing filter on the plan at the nearest bands";

    public const string DepthName = "the swing filter at a pullback of 1 to 3 typical moves";

    public const string MarketOffName = "the swing filter with the market gate off";

    public const string StrengthName = "the swing filter with strength in the top third";

    public const string ArrivalName = "the swing filter with one-session arrival";

    public const string DeterioratingName = "the swing filter leaving off a deteriorating business";

    // The sector leaders' name, in the operator's words, which the register keeps for the rule it retired.
    public const string LeadersName = "the pullback in the top 3 sectors, top quarter of each";

    // The analysts' revisions rule's name, in the operator's words.
    public const string RevisionsName = "the swing filter with estimates raised over the last 30 days";

    // The best three's name, in the operator's words.
    public const string BestThreeName = "the night's best three, in the list's own order";

    // A variant's name against the version it was defined against.
    public static string Variant(string name, string version) => FormattableString.Invariant($"{name}, from version {version}");

    // The rows the family's registration writes at one instant: phase 10's three retired and the nine registered.
    public static int RowsAtOnce => Retires.Count + For("0", FilterSettings.Proposed).Count;

    // The nine, the live filter first at the open version's settings.
    public static IReadOnlyList<Registration> For(string version, FilterSettings live) =>
    [
        new(SwingFamily.LiveCandidate(version), Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live)),
        new(Variant(NearestBandsName, version), Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { Trade = VariantTrade })),
        new(Variant(DepthName, version), Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { DepthLow = VariantDepthLow, DepthHigh = VariantDepthHigh })),
        new(Variant(MarketOffName, version), Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live, marketGate: false)),
        new(Variant(StrengthName, version), Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { StrengthFloor = VariantStrength })),
        new(Variant(ArrivalName, version), Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { ArrivalSessions = VariantArrival })),
        new(Variant(DeterioratingName, version), RuleSkippingDeteriorating, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live, skipDeteriorating: true)),
        new(Variant(RevisionsName, version), RuleReadingEstimates, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live, raisedEstimates: true)),
        new(Variant(BestThreeName, version), RuleKeepingTheBest, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live, bestOf: VariantBestOf)),
    ];

    // The sector leaders' rule as the family registered it under a version, which the family registered again with
    // the revisions rule in its place retires.
    public static Registration Leaders(string version, FilterSettings live) =>
        new(Variant(LeadersName, version), RuleReadingLeadership, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live, leadership: Core.Families.LeaderRule.Live));

    // The candidates the family's registration retires: phase 10's three.
    public static IReadOnlyList<string> Retires => [.. TheThreeCandidates.All.Select(one => one.Candidate)];
}
