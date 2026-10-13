using System.Globalization;

namespace EquityBrief.Core.Time;

// The time the night starts, read off the nights the run log holds, since nothing the store holds states the schedule,
// which lives outside the application, nor says which night the scheduler started: the minute of the day in UTC that
// the most of the newest seven nights' first tries started at, the newest among a tie, so a night run by hand at
// another time moves nothing.
// see: The date line names tonight's night by the start the most of the newest seven nights share
public static class NightTime
{
    public const int NightsRead = 7;

    const string Prefix = "night-";
    const string Stamp = "yyyyMMdd'T'HHmmss'Z'";

    // The usual start, none where the run log holds no night's first try.
    public static TimeOnly? Usual(IEnumerable<string> runIds)
    {
        var starts = runIds.Select(StartOf).OfType<DateTimeOffset>().Distinct().OrderByDescending(start => start).Take(NightsRead).ToArray();

        return starts.Length == 0
            ? null
            : starts
                .GroupBy(start => new TimeOnly(start.UtcDateTime.Hour, start.UtcDateTime.Minute))
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Max())
                .First()
                .Key;
    }

    // A night's first try's start, read off its run id, and none for a run id that is not a night's own: a later try, a
    // request the night queued or a run of another kind.
    public static DateTimeOffset? StartOf(string runId) =>
        runId.StartsWith(Prefix, StringComparison.Ordinal)
        && runId.Length == Prefix.Length + Stamp.Length - 4
        && DateTimeOffset.TryParseExact(runId[Prefix.Length..], Stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start)
            ? start
            : null;

    // The night of a session's start at the usual minute: that minute in UTC on the session's day, or on the day after
    // where that falls before the session's close at four in the afternoon in the session's zone.
    public static DateTimeOffset On(DateOnly session, TimeOnly usual, TimeZoneInfo zone)
    {
        var close = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(session.ToDateTime(new TimeOnly(16, 0)), zone), TimeSpan.Zero);
        var at = new DateTimeOffset(session.ToDateTime(usual), TimeSpan.Zero);

        return at < close ? at.AddDays(1) : at;
    }
}
