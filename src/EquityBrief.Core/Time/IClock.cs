namespace EquityBrief.Core.Time;

// The one place the machine clock is read and the one place a session date is
// derived from an instant. Everything else takes an IClock, so replaying a
// fixture and running a night are the same code path.
//
// The derivations are written once here rather than in each implementation, so
// a test that exercises them through a fixed clock is exercising the same code
// a live run takes.
// see: Queued work runs off-peak, and every schedule is written in UTC
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    TimeZoneInfo SessionZone { get; }

    TimeSpan SessionOffsetAt(DateTimeOffset instant) => SessionZone.GetUtcOffset(instant);

    // The trading date an instant falls on, which is the date in the session
    // zone and not the date in UTC. After the close in New York the two differ
    // for the rest of the evening, which is exactly when the nightly run works.
    DateOnly SessionDateAt(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, SessionZone).DateTime);

    DateOnly UtcDateAt(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);
}
