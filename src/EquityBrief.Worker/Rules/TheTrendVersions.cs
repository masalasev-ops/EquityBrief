using EquityBrief.Core.Ladders;
using EquityBrief.Core.Rules;

namespace EquityBrief.Worker.Rules;

// One version of the trend rule as the command opens it: the name the store will
// carry, what the version does in words, and the parameters its replay runs at.
public sealed record TrendVersion(string Version, string Reads, TrendRuleSet Rules);

// The three versions of the trend rule phase 10 opens, written down rather than
// typed at the command line.
//
// In code for the reason the three candidates are: a version is a statement about
// what was tried, and parameters retyped by hand at a prompt are a rule nobody can
// check afterwards against the replay that ran. The command names one of these and
// the open reads its numbers from here.
//
// Each is opened on its own rather than all at once. A version's window opens
// when it opens and its scores count from the night after, so three opened a day
// apart are three windows measuring three different stretches of nights, which is
// what the version scorer already records and what the candidates' one instant
// exists to avoid for a level divided across a family. A version's level is not
// divided across the others.
// see: The trend rule is a fifth ladder rule a version replays, and none of its three versions is live
// see: A version's score counts only for a session after the New York date its window opened on
public static class TheTrendVersions
{
    public const string BelowBothAverages = "below both averages";

    public const string BelowBothUnderACross = "below both under a cross";

    public const string TheNewLabelHoldsTwoNights = "the new label holds two nights";

    public static IReadOnlyList<TrendVersion> All { get; } =
    [
        new(
            BelowBothAverages,
            "a close below both the 50-day and the 200-day average is a downtrend, whatever the swings say",
            new TrendRuleSet(DowntrendFromAverages: TrendSeries.FromAveragesBelowBoth)),
        new(
            BelowBothUnderACross,
            "the same reading, only where the 50-day average is below the 200-day",
            new TrendRuleSet(DowntrendFromAverages: TrendSeries.FromAveragesBelowBothUnderACross)),
        new(
            TheNewLabelHoldsTwoNights,
            "entering a downtrend removes buying zones the first night the label reads downtrend, and leaving it " +
            "restores them only once the new label has held two nights, the night being scored among them",
            new TrendRuleSet(NightsTheNewLabelHolds: 2)),
    ];

    public static TrendVersion? Named(string version) =>
        All.FirstOrDefault(one => string.Equals(one.Version, version, StringComparison.Ordinal));
}
