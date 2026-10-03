using System.Globalization;

namespace EquityBrief.Core.Research;

// A drain that stopped on an error outside a pass: the run its one row is written under, named for the instant the
// drain started, and the stage and outcome that row carries, which the queue page and the run page read it by.
// see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
public static class DrainStops
{
    public const string Prefix = "drain-";

    public const string Stage = "drain";

    public const string Failed = "failed";

    public static string RunFor(DateTimeOffset startedAt) =>
        Prefix + startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // Whether a run log row is a drain's stop, read by its run, its stage and its outcome together.
    public static bool IsStop(string runId, string stage, string outcome) =>
        runId.StartsWith(Prefix, StringComparison.Ordinal)
        && string.Equals(stage, Stage, StringComparison.Ordinal)
        && string.Equals(outcome, Failed, StringComparison.Ordinal);
}

// A drain's stop as the read surface hands it on: when the drain started and stopped, and the error it stopped on.
public sealed record DrainStop(DateTimeOffset StartedAt, DateTimeOffset EndedAt, string Error);
