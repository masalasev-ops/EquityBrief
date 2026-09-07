namespace EquityBrief.Core.Time;

// Session zones, resolved from IANA identifiers and never Windows ones. A
// Windows identifier resolves on Windows and fails on macOS, which is a fault
// neither of the two machines would show until the other one ran.
// see: Nothing is written against one operating system
public static class SessionZones
{
    public const string UnitedStates = "America/New_York";

    public static TimeZoneInfo Resolve(string identifier)
    {
        // An IANA identifier is area over location. A Windows identifier, such
        // as the one for the eastern United States, is a plain phrase with no
        // separator, so this is enough to tell them apart and refuse the second.
        if (!identifier.Contains('/'))
        {
            throw new ArgumentException(
                $"'{identifier}' is not an IANA identifier. An area/location identifier is " +
                "required, because a Windows one resolves on Windows and fails everywhere else.",
                nameof(identifier));
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(identifier);
        }
        catch (TimeZoneNotFoundException notFound)
        {
            throw new TimeZoneNotFoundException(
                $"The session zone '{identifier}' did not resolve. The usual cause is " +
                "InvariantGlobalization being true, which removes the timezone database and " +
                "makes every IANA lookup fail at once rather than one at a time.",
                notFound);
        }
    }
}
