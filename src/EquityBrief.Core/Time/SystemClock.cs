namespace EquityBrief.Core.Time;

// The only class in the system that reads the machine clock. clock-usage
// asserts that over every source file, and asserts that this one does read it,
// because a system that reads no clock anywhere would pass the first half.
public sealed class SystemClock : IClock
{
    public SystemClock(string sessionZoneIdentifier)
        : this(SessionZones.ResolveSessionZone(sessionZoneIdentifier))
    {
    }

    public SystemClock(TimeZoneInfo sessionZone) => SessionZone = sessionZone;

    public static SystemClock ForUnitedStatesSessions() => new(SessionZones.UnitedStates);

    public TimeZoneInfo SessionZone { get; }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
