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
    // see: The night asks for a report on the first six names its page draws
    public const int ReportsANight = 6;

    // The pullback, today's swing filter as it stands: its names are the ones the filter passed, improving
    // businesses first and then the filter's own order, on the plan clear of the noise.
    public static SetupFamily Pullbacks { get; } = new(
        Pullback,
        "Pullback",
        "Pullbacks to support",
        "Pullback in an uptrend",
        "A strong stock in an uptrend dips to a support band and turns back up. Stop below the band, target at the next band above.",
        ForwardReturnSeries.Clear,
        ForwardReturnSeries.SetupSessionCap,
        OnThePullbacksPlan: true);

    // The breakout, at the settings its freeze registered.
    // see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target
    // see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
    public static SetupFamily Breakouts { get; } = new(
        BreakoutRule.Name,
        "Breakout",
        "Breakouts to a new high",
        "Breakout from a base",
        "A stock closes above its highest price of the past six months on heavy volume, after its daily ranges narrowed. Stop one and a half typical moves below, raised as the price climbs and never lowered; no target.",
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
        "After a strong report",
        "A company beats its estimate and the stock closes up at least half a typical day's move on twice its usual volume. Bought within three sessions while it holds above that day's low. Stop at that day's low, target at the next band above or 2.5 times the risk, whichever is nearer.",
        DriftRule.Horizon,
        DriftRule.CapSessions);

    // The sector leader, a family until the freeze of 2026-10-02 and a variant of the pullback since, named
    // still for the picks it listed before, which are scored on the pullback's plan.
    // see: The sector leaders are a variant of the pullback's starting point and not a family of their own
    public static SetupFamily SectorLeaders { get; } = new(
        LeaderRule.Name,
        "Sector leader",
        "Sector leaders",
        "Strongest stocks of the strongest sectors",
        "A stock in the top quarter of one of the three strongest sectors, at a pullback's buy point. Stop below the band, target at the next band above, as a pullback's.",
        ForwardReturnSeries.Clear,
        ForwardReturnSeries.SetupSessionCap,
        OnThePullbacksPlan: true);

    // The page's order, which is the order a stock qualifying under two families is listed in and the
    // order the night's reports are asked for in.
    public static IReadOnlyList<SetupFamily> InPageOrder { get; } = [Pullbacks, Breakouts, EarningsDrift];

    // The sector heavyweights, the page's fourth card: a rotation held while it leads, with a book of its own in place
    // of a list of the night's buy points, so it is drawn after the swing families and stands in none of their
    // orders, horizons or caps. A stock it holds is free for any swing family, each card keeping its own one trade a
    // stock, and the night asks for a report on what it buys after the swing families' picks.
    // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    public static FamilyWords SectorHeavyweights { get; } = new(
        HeavyweightRule.Name,
        "Sector heavyweight",
        "Sector heavyweights",
        "Largest companies leading their sectors",
        "On each month's first session, among each sector's five largest companies, the one whose six-month return beats the average of its sector's members by the most, where it beats it at all and its close is above its 50-day average and that above its 200-day, bought at that close. Held while it leads: sold at a month's first close where the rule would no longer buy it, or at any close under its 200-day average.");

    // The families the page drew once and draws no longer, kept so a pick one listed is still named and scored.
    public static IReadOnlyList<SetupFamily> Former { get; } = [SectorLeaders];

    // The families whose trades are scored from their own stored rows under a horizon of their own. A
    // family on the pullback's plan is scored on the swing filter's row, which holds that plan.
    public static IReadOnlyList<SetupFamily> ScoredOnTheirOwnRows { get; } = [.. InPageOrder.Where(family => !family.OnThePullbacksPlan)];

    // The families whose answers the family evaluator stores, every one but the pullback, whose answers
    // are the swing filter's own rows.
    public static IReadOnlyList<SetupFamily> Evaluated { get; } = [.. InPageOrder.Where(family => family.Name != Pullback)];

    public static SetupFamily? Named(string name) =>
        InPageOrder.Concat(Former).FirstOrDefault(family => string.Equals(family.Name, name, StringComparison.Ordinal));

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

    // The sessions a family's trade is given, over a column holding its family.
    public static string CapIn(string family) =>
        "CASE " + family + string.Concat(InPageOrder.Concat(Former).Select(one => FormattableString.Invariant($" WHEN '{one.Name}' THEN {one.CapSessions}"))) + FormattableString.Invariant($" ELSE {ForwardReturnSeries.SetupSessionCap} END");

    // What a family's card says of a rule no freeze has registered yet.
    // see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    public const string Provisional = "provisional: not yet frozen; its record starts at the freeze";
}
