using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Families;

// One setup family as every surface names it: the word the store keeps, the card's heading and the line
// above it, its rule in one sentence, and the horizon its trades are scored under with that horizon's cap.
public sealed record SetupFamily(string Name, string Label, string Heading, string Eyebrow, string Rule, string Horizon, int CapSessions);

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
        ForwardReturnSeries.SetupSessionCap);

    // The page's order, which is the order a stock qualifying under two families is listed in and the
    // order the night's reports are asked for in.
    public static IReadOnlyList<SetupFamily> InPageOrder { get; } = [Pullbacks];

    public static SetupFamily? Named(string name) =>
        InPageOrder.FirstOrDefault(family => string.Equals(family.Name, name, StringComparison.Ordinal));

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
        + string.Concat(InPageOrder.Select(one => $" WHEN '{one.Name}' THEN " + (one.Name == Pullback ? PullbackHorizonIn(settings) : $"'{one.Horizon}'")))
        + " END";

    // The sessions a family's trade is given, over a column holding its family.
    public static string CapIn(string family) =>
        "CASE " + family + string.Concat(InPageOrder.Select(one => FormattableString.Invariant($" WHEN '{one.Name}' THEN {one.CapSessions}"))) + FormattableString.Invariant($" ELSE {ForwardReturnSeries.SetupSessionCap} END");

    // What a family's card says of a rule no freeze has registered yet.
    // see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    public const string Provisional = "provisional: not yet frozen; its record starts at the freeze";
}
