namespace EquityBrief.Core.Time;

// Exchange session zones, resolved from IANA identifiers and never Windows
// ones. A Windows identifier resolves on Windows and fails on macOS, which is a
// fault neither of the two machines would show until the other one ran.
//
// An exchange session zone is always an area and a location, so a slashless
// identifier is refused deliberately. That refuses UTC and the handful of other
// IANA identifiers carrying no separator along with the Windows ones, which is
// correct here and would be wrong for a general IANA resolver. Every zone this
// system resolves is an exchange's, and nothing asks for the general case.
// see: Nothing is written against one operating system
public static class SessionZones
{
    public const string UnitedStates = "America/New_York";

    // The day an instant falls on in New York, the day a page dates a fetch by: an answer stored at 01:23 UTC on the
    // tenth was asked on the ninth there.
    // see: An analyst figure is dated by the New York day it was fetched on, and its revisions by that day against the newest report
    public static DateOnly NewYorkDay(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, ResolveSessionZone(UnitedStates)).DateTime);

    public static TimeZoneInfo ResolveSessionZone(string identifier)
    {
        // An exchange session zone is area over location. A Windows identifier,
        // such as the one for the eastern United States, is a plain phrase with
        // no separator, so this is enough to tell them apart and refuse the
        // second. A slashless IANA identifier such as UTC is refused with them,
        // on purpose: no exchange session sits in one.
        if (!identifier.Contains('/'))
        {
            throw new ArgumentException(
                $"'{identifier}' is not an exchange session zone. An IANA area/location " +
                "identifier is required, because a Windows one resolves on Windows and fails " +
                "everywhere else. A slashless identifier is refused whether or not it is an " +
                "IANA one, because no exchange session sits in a zone without a location.",
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
