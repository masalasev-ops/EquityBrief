using System.Globalization;
using EquityBrief.Core.Levels;

namespace EquityBrief.Core.Ladders;

// A plan for a stock with fewer sessions than its 200-day average needs, worked as a page is read and never stored: the
// ladder's own rule over the levels its sessions give, read as a stock with no trend to follow, since the night's trend
// label needs the averages it lacks. No rule, gate or list reads it, and the night's stored plan stands as it was.
// see: A stock with too few sessions for its averages draws its plan from its levels when a page is read and no rule reads it
public static class ShortHistoryPlan
{
    public const int SessionsTheAverageNeeds = 200;

    public static Ladder Worked(IReadOnlyList<Level> bands, decimal close, decimal typicalMove, IReadOnlyList<LadderBar> recent) =>
        LadderSeries.For(bands, close, typicalMove, recent, TrendState.Range);

    public static string Why(DateOnly first, int sessions) =>
        Started(first, sessions) + string.Create(CultureInfo.InvariantCulture, $". The plan below uses the levels its {sessions} days show, without the average.");

    public static string NoLevels(DateOnly first, int sessions) =>
        Started(first, sessions) + ", and too few for any support or resistance level, so no plan can be drawn";

    public static string NoZone(DateOnly first, int sessions, string why) =>
        Started(first, sessions) + string.Create(CultureInfo.InvariantCulture, $", and on the levels its {sessions} days show, {why}");

    static string Started(DateOnly first, int sessions) =>
        string.Create(CultureInfo.InvariantCulture, $"This stock started trading on {first:d MMM yyyy}, so it has {sessions} of the {SessionsTheAverageNeeds} trading days a 200-day average needs");
}
