using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Families;

// One setup family as every surface names it: the word the store keeps, the card's heading and the line
// above it, its rule in one sentence, the horizon its trades are scored under with that horizon's cap, and
// whether its trade is sold on a trailing stop with no target, or is the pullback's own plan as the swing
// filter stored it, which is scored on the swing filter's row and under the horizon that row's plan names.
public sealed record SetupFamily(string Name, string Label, string Heading, string Eyebrow, string Rule, string Horizon, int CapSessions, bool Trails = false, bool OnThePullbacksPlan = false);

// A family's words alone, for the one family no horizon, cap or list of the night's buy points describes.
public sealed record FamilyWords(string Name, string Label, string Heading, string Eyebrow, string Rule);

// The setup families tonight's page is drawn from, in the page's order, and the numbers they share.
//
// A family is a rule of its own: it reads every member every night, lists the names it passes in its own
// order, and keeps its own record. The families share the night's market check, the open trade rule and
// the page, and nothing here decides what a family's rule is.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public static class SetupFamilies
{
    public const string Pullback = "pullback";

    // The most names one family lists on a night, the first of them in its own order.
    public const int ListedANight = 5;

    // The most reports the night asks for, the first names down its page.
    // see: The six reports a night are taken in turn across the three indices, one at a time in the page's order
    public const int ReportsANight = 6;

    // The pullback, today's swing filter as it stands: its names are the ones the filter passed, improving
    // businesses first and then the filter's own order, on the plan clear of the noise.
    public static SetupFamily Pullbacks { get; } = new(
        Pullback,
        "Pullback",
        RuleWording.SetupFamilies.PullbacksHeading,
        RuleWording.SetupFamilies.PullbacksEyebrow,
        RuleWording.SetupFamilies.PullbacksRule,
        ForwardReturnSeries.Clear,
        ForwardReturnSeries.SetupSessionCap,
        OnThePullbacksPlan: true);

    // The breakout, at the settings its freeze registered.
    // see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target
    // see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
    public static SetupFamily Breakouts { get; } = new(
        BreakoutRule.Name,
        "Breakout",
        RuleWording.SetupFamilies.BreakoutsHeading,
        RuleWording.SetupFamilies.BreakoutsEyebrow,
        RuleWording.SetupFamilies.BreakoutsRule,
        BreakoutRule.Horizon,
        BreakoutRule.CapSessions,
        Trails: true);

    // The earnings drift, at the settings its freeze registered.
    // see: The earnings drift buys a beat with a strong reaction within five sessions, stopped under the reaction session's low
    // see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
    public static SetupFamily EarningsDrift { get; } = new(
        DriftRule.Name,
        "Earnings drift",
        "Earnings drift",
        RuleWording.SetupFamilies.EarningsDriftEyebrow,
        RuleWording.SetupFamilies.EarningsDriftRule,
        DriftRule.Horizon,
        DriftRule.CapSessions);

    // The sector leader, a family until the freeze of 2026-10-02 and a variant of the pullback since, named
    // still for the picks it listed before, which are scored on the pullback's plan.
    // see: The sector leaders are a variant of the pullback's starting point and not a family of their own
    public static SetupFamily SectorLeaders { get; } = new(
        LeaderRule.Name,
        "Sector leader",
        "Sector leaders",
        RuleWording.SetupFamilies.SectorLeadersEyebrow,
        RuleWording.SetupFamilies.SectorLeadersRule,
        ForwardReturnSeries.Clear,
        ForwardReturnSeries.SetupSessionCap,
        OnThePullbacksPlan: true);

    // The page's order, which is the order a stock qualifying under two families is listed in and the
    // order the night's reports are asked for in.
    public static IReadOnlyList<SetupFamily> InPageOrder { get; } = [Pullbacks, Breakouts, EarningsDrift];

    // The fundamentals-first family, read on every index by the index families' step, the S&P 500's included, so no S&P
    // 500 family's own code moves: an improving business in an uptrend bought at a pullback's buy point, on the
    // pullback's plan.
    // see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
    public static SetupFamily FundamentalsFirst { get; } = new(
        FundamentalsRule.Name,
        "Fundamentals first",
        RuleWording.SetupFamilies.FundamentalsFirstHeading,
        RuleWording.SetupFamilies.FundamentalsFirstEyebrow,
        RuleWording.SetupFamilies.FundamentalsFirstRule,
        ForwardReturnSeries.Clear,
        Sweep.SweepDesign.Live.Hold);

    // The families every index's page draws in its order: the page's own and, after them, the fundamentals-first family.
    public static IReadOnlyList<SetupFamily> OnEveryIndex { get; } = [.. InPageOrder, FundamentalsFirst];

    // The short-term dip, read on every index by a step of its own after every other family's and scored on rows of its
    // own: in no list a pinned step reads, its card drawn after the fundamentals-first family's on every index, and on no
    // page that waits for its freeze, paper trading and the Loop among them, until its freeze.
    // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
    public static SetupFamily Dips { get; } = new(
        DipRule.Name,
        "Short-term dip",
        RuleWording.SetupFamilies.DipsHeading,
        RuleWording.SetupFamilies.DipsEyebrow,
        DipRule.Provisional.Words,
        DipRule.Name,
        DipRule.Provisional.TimeLimit);

    // The sector heavyweights, the page's fourth card: a rotation held while it leads, with a book of its own in place
    // of a list of the night's buy points, so it is drawn after the swing families and stands in none of their
    // orders, horizons or caps. A stock it holds is free for any swing family, each card keeping its own one trade a
    // stock, and the night asks for a report on what it buys after the swing families' picks.
    // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year and the pulled history before it hold the closes their readings need
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    public static FamilyWords SectorHeavyweights { get; } = new(
        HeavyweightRule.Name,
        "Sector heavyweight",
        "Sector heavyweights",
        RuleWording.SetupFamilies.SectorHeavyweightsEyebrow,
        RuleWording.SetupFamilies.SectorHeavyweightsRule);

    // The one order every page lists the families in, Tonight's: the three swing families in the page's order, the
    // fundamentals-first family after them and the sector heavyweights last, by the word the store keeps each under.
    public static IReadOnlyList<string> Order { get; } = [.. OnEveryIndex.Select(family => family.Name), HeavyweightRule.Name];

    // The order every card and row is drawn in, Tonight's: the one order with the short-term dip after the
    // fundamentals-first family and before the sector heavyweights.
    public static IReadOnlyList<string> PageOrder { get; } = [.. OnEveryIndex.Select(family => family.Name), DipRule.Name, HeavyweightRule.Name];

    // Where a family stands in the page's order, counted from one, and past every family for a word it does not hold.
    public static int OrderOf(string name)
    {
        for (var at = 0; at < PageOrder.Count; at++)
        {
            if (string.Equals(PageOrder[at], name, StringComparison.Ordinal))
            {
                return at + 1;
            }
        }

        return PageOrder.Count + 1;
    }

    // The families the page drew once and draws no longer, kept so a pick one listed is still named and scored.
    public static IReadOnlyList<SetupFamily> Former { get; } = [SectorLeaders];

    // The families whose trades are scored from their own stored rows under a horizon of their own. A
    // family on the pullback's plan is scored on the swing filter's row, which holds that plan.
    public static IReadOnlyList<SetupFamily> ScoredOnTheirOwnRows { get; } = [.. InPageOrder.Where(family => !family.OnThePullbacksPlan)];

    // The families whose answers the family evaluator stores, every one but the pullback, whose answers
    // are the swing filter's own rows.
    public static IReadOnlyList<SetupFamily> Evaluated { get; } = [.. InPageOrder.Where(family => family.Name != Pullback)];

    public static SetupFamily? Named(string name) =>
        OnEveryIndex.Append(Dips).Concat(Former).FirstOrDefault(family => string.Equals(family.Name, name, StringComparison.Ordinal));

    // Where a family stands in the page's order, counted from one, and past every family for a name the
    // page does not draw.
    public static int PlaceOf(string name)
    {
        for (var at = 0; at < InPageOrder.Count; at++)
        {
            if (string.Equals(InPageOrder[at].Name, name, StringComparison.Ordinal))
            {
                return at + 1;
            }
        }

        return InPageOrder.Count + 1;
    }

    // The horizon a pullback's trade is scored under, over a column holding the settings of the filter
    // version its night ran on: the plan that version's trade gate read, as Past picks has always chosen it.
    // see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
    public static string PullbackHorizonIn(string settings) =>
        $"CASE json_extract({settings}, '$.trade') WHEN '{Filter.FilterSettings.ClearWord}' THEN '{ForwardReturnSeries.Clear}' ELSE '{ForwardReturnSeries.Swing}' END";

    // The horizon a listed trade is scored under, over a column holding its family and one holding its
    // night's filter settings, written from the families above so a query and the page read one rule.
    public static string HorizonIn(string family, string settings) =>
        "CASE " + family
        + string.Concat(InPageOrder.Concat(Former).Select(one => $" WHEN '{one.Name}' THEN " + (one.OnThePullbacksPlan ? PullbackHorizonIn(settings) : $"'{one.Horizon}'")))
        + " END";

    // The horizon a listed pick is scored under, over its family, its night's filter settings and the exit of the menu
    // its pick names: a pullback pick an approved setting named an exit for under the menu's own horizon, every other as
    // its family's.
    // see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
    public static string PickHorizonIn(string family, string settings, string exit) =>
        $"CASE WHEN {exit} IS NOT NULL AND {family} = '{Pullback}' THEN '{ForwardReturnSeries.MenuExit}' ELSE {HorizonIn(family, settings)} END";

    // The sessions a family's trade is given, over a column holding its family.
    public static string CapIn(string family) =>
        "CASE " + family + string.Concat(InPageOrder.Concat(Former).Select(one => FormattableString.Invariant($" WHEN '{one.Name}' THEN {one.CapSessions}"))) + FormattableString.Invariant($" ELSE {ForwardReturnSeries.SetupSessionCap} END");

    // What a family's record says of a rule no freeze has registered yet.
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    public const string Provisional = RuleWording.SetupFamilies.Provisional;

    // The status line a card draws for a rule no freeze has registered yet, beside "Live rule since" for one a freeze has.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    public const string ProvisionalStatus = RuleWording.SetupFamilies.ProvisionalStatus;
}
