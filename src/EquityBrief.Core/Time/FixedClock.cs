namespace EquityBrief.Core.Time;

// A clock that does not move. It resolves its zone the same way SystemClock
// does and derives dates through the same code, so a test written against it
// is testing what a night will do rather than a second implementation.
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset instant, TimeZoneInfo sessionZone)
    {
        UtcNow = instant.ToUniversalTime();
        SessionZone = sessionZone;
    }

    public static FixedClock At(DateTimeOffset instant, string sessionZoneIdentifier) =>
        new(instant, SessionZones.Resolve(sessionZoneIdentifier));

    public DateTimeOffset UtcNow { get; }

    public TimeZoneInfo SessionZone { get; }
}
