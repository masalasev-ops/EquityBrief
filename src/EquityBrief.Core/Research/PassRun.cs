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

    // The passes that ran to their end, each measured from the instant its run is named for to the
    // end of its last stage: the population the queue page's estimate is the median of and the
    // drain's bound is the longest of. A pass the runner stopped before its last stage wrote no
    // row here and is not one of them.
    public const string FinishedPasses = @"
        SELECT run_id, ended_at FROM run_log
        WHERE stage = 'research' AND outcome = 'ok' AND run_id LIKE 'research-%' AND ended_at IS NOT NULL;
    ";

    // How long a finished pass took, from the instant its run is named for to the end of its
    // row, or none where either is not an instant.
    public static TimeSpan? Took(string runId, string endedAt) =>
        StartedAt(runId) is { } started
        && DateTimeOffset.TryParseExact(
            endedAt,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var ended)
            ? ended - started
            : null;
}
