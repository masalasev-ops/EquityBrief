using System.Globalization;

namespace EquityBrief.Core.Research;

// The run a research pass writes its rows under: what the worker names it, and what the read
// surface finds it by while the pass is still running.
//
// It is stated here rather than in either of them because both need it and neither may reach
// the other: the pass's own row, which carries the name it was for, is written last, so a page
// watching a pass it started has nothing but the run's name to find its rows by until it ends.
// see: A pass the page starts is watched until it ends and the page redraws as each section lands
public static class PassRun
{
    public const string Prefix = "research-";

    // One pass of one name, started at one instant. The instant is what separates a second
    // pass for a name from the first, and the name is what the surface matches on.
    public static string IdFor(DateTimeOffset startedAt, string ticker) =>
        FormattableString.Invariant($"{Prefix}{startedAt:yyyyMMddTHHmmssZ}-{ticker}");

    // The pattern a store matches a name's runs with. A ticker cannot hold the separator, so
    // a run for one name is never matched by another's pattern.
    public static string Like(string ticker) => Prefix + "%-" + ticker;

    // Whether a run is one of this name's passes, which is what the pattern above selects,
    // read here as well so the two cannot come to disagree.
    public static bool IsFor(string runId, string ticker) =>
        runId.StartsWith(Prefix, StringComparison.Ordinal)
        && runId.EndsWith("-" + ticker, StringComparison.Ordinal);

    // The instant a run's name carries, for a reader that has the run and not the row.
    public static DateTimeOffset? StartedAt(string runId) =>
        runId.StartsWith(Prefix, StringComparison.Ordinal)
        && runId.IndexOf('-', Prefix.Length) is var end and > 0
        && DateTimeOffset.TryParseExact(
            runId[Prefix.Length..end],
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var started)
            ? started
            : null;
}
