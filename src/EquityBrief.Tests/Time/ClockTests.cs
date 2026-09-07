using EquityBrief.Core.Time;

namespace EquityBrief.Tests.Time;

// 0.3's done condition. Every one of these calls the clock's own public
// surface, because a green suite is not evidence the path ran.
public class ClockTests
{
    [Fact]
    public void AnIdentifierThatDoesNotResolveFailsLoudly()
    {
        var refusal = Assert.Throws<TimeZoneNotFoundException>(
            () => SessionZones.ResolveSessionZone("Mars/Olympus_Mons"));

        // The message names the setting that causes this in practice, because
        // when InvariantGlobalization is true every lookup fails at once and
        // the cause is not otherwise visible from the failure.
        Assert.Contains("InvariantGlobalization", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowsIdentifierIsRefused()
    {
        // It would resolve on Windows and fail on macOS, so accepting one here
        // would hide the fault on the machine that could see it.
        var refusal = Assert.Throws<ArgumentException>(
            () => SessionZones.ResolveSessionZone("Eastern Standard Time"));

        Assert.Contains("IANA", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlashlessIdentifierIsRefusedEvenWhenItIsAnIanaOne()
    {
        // UTC is a real IANA identifier and is refused with the Windows ones,
        // deliberately. What this method resolves is an exchange session zone,
        // and no exchange session sits in a zone without a location. The
        // refusal is the claim being narrowed to what the code does rather than
        // the code widened for a case nothing asks for.
        var refusal = Assert.Throws<ArgumentException>(
            () => SessionZones.ResolveSessionZone("UTC"));

        Assert.Contains("not an exchange session zone", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSessionZoneIsBehindUtcByAtMostADay()
    {
        IClock clock = SystemClock.ForUnitedStatesSessions();
        var instant = clock.UtcNow;

        // Both bounds are read from the clock: the session date and the UTC
        // date of one instant it gave us, neither taken from the machine.
        var sessionDate = clock.SessionDateAt(instant);
        var utcDate = clock.UtcDateAt(instant);

        Assert.InRange(utcDate.DayNumber - sessionDate.DayNumber, 0, 1);
        Assert.InRange(clock.SessionOffsetAt(instant), -TimeSpan.FromDays(1), TimeSpan.Zero);
    }

    [Fact]
    public void TheSessionDateIsTheDateInTheSessionZoneAndNotInUtc()
    {
        // Half past one in the morning UTC is still the previous evening in New
        // York, which is when the nightly run works. A clock that returned the
        // UTC date would pass every test above and be wrong every night.
        IClock clock = FixedClock.At(
            new DateTimeOffset(2026, 3, 10, 1, 30, 0, TimeSpan.Zero),
            SessionZones.UnitedStates);

        Assert.Equal(new DateOnly(2026, 3, 9), clock.SessionDateAt(clock.UtcNow));
        Assert.Equal(new DateOnly(2026, 3, 10), clock.UtcDateAt(clock.UtcNow));
    }

    [Fact]
    public void TheOffsetMovesWithDaylightSaving()
    {
        // The proof that a real timezone database is being read rather than a
        // fixed offset. This is the assertion InvariantGlobalization breaks.
        var zone = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);

        IClock winter = new FixedClock(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), zone);
        IClock summer = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero), zone);

        Assert.Equal(TimeSpan.FromHours(-5), winter.SessionOffsetAt(winter.UtcNow));
        Assert.Equal(TimeSpan.FromHours(-4), summer.SessionOffsetAt(summer.UtcNow));
    }

    [Fact]
    public void TheClockKeepsTheInstantItWasGiven()
    {
        var instant = new DateTimeOffset(2026, 6, 1, 20, 15, 0, TimeSpan.FromHours(-4));
        IClock clock = new FixedClock(instant, SessionZones.ResolveSessionZone(SessionZones.UnitedStates));

        Assert.Equal(instant.ToUniversalTime(), clock.UtcNow);
        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
    }
}
